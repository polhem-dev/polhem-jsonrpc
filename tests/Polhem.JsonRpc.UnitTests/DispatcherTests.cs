using System.ComponentModel;
using System.Text.Json;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public class DispatcherTests
{
    private static async Task<JsonRpcResponse> CallAsync(JsonRpcDispatcher dispatcher, string method, string paramsJson, JsonRpcTransportInfo? transport = null)
    {
        var request = new JsonRpcRequest(method, DispatcherFixture.Element(paramsJson), JsonRpcId.FromNumber(1));
        var response = await dispatcher.DispatchAsync(request, transport ?? DispatcherFixture.Http());
        return response!;
    }

    [Fact]
    [DisplayName("A method without [JsonRpcMethod] is answered as if it did not exist")]
    public async Task DispatchAsync_UnmarkedMethod_ReturnsMethodNotFound()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "spec.unmarked", """{"text": "x"}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Fact]
    [DisplayName("A replaced method policy decides which methods are callable")]
    public async Task DispatchAsync_CustomPolicy_AdmitsUnmarkedMethod()
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, "spec.unmarked", """{"text": "x"}""");

        Assert.Equal("x", response.Result!.Value.GetString());
    }

    [Theory]
    [DisplayName("Static methods, overloaded names, rpc.* names and malformed names are not resolved")]
    [InlineData("spec.staticMethod")]
    [InlineData("spec.twice")]
    [InlineData("rpc.subtract")]
    [InlineData("spec")]
    [InlineData(".subtract")]
    [InlineData("spec.")]
    [InlineData("spec.sub-tract")]
    [InlineData("spec.toString")]
    public async Task DispatchAsync_UnresolvableName_ReturnsMethodNotFound(string method)
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, method, """{"text": "x"}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Theory]
    [DisplayName("Target and method names match case-insensitively")]
    [InlineData("spec.subtract")]
    [InlineData("Spec.Subtract")]
    [InlineData("SPEC.SUBTRACT")]
    public async Task DispatchAsync_NameInAnyCase_ResolvesMethod(string method)
    {
        var response = await CallAsync(DispatcherFixture.Create(), method, """{"minuend": 5, "subtrahend": 3}""");

        Assert.Equal(2, response.Result!.Value.GetInt32());
    }

    [Theory]
    [DisplayName("Task, ValueTask and void results are awaited and serialized")]
    [InlineData("spec.taskResult", "\"ABC\"")]
    [InlineData("spec.valueTaskResult", "3")]
    [InlineData("spec.noResult", "null")]
    [InlineData("spec.update", "null")]
    public async Task DispatchAsync_AsyncAndVoidMethods_ReturnResult(string method, string expected)
    {
        var response = await CallAsync(DispatcherFixture.Create(), method, """{"text": "abc"}""");

        Assert.True(response.IsSuccess);
        Assert.Equal(expected, JsonSerializer.Serialize(response.Result));
    }

    [Fact]
    [DisplayName("Parameters that do not fit the method are answered with -32602")]
    public async Task DispatchAsync_ParamsOfWrongShape_ReturnsInvalidParams()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "spec.subtract", """{"minuend": "not a number"}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
    }

    [Fact]
    [DisplayName("An unexpected exception is answered with -32603 and a fixed message that does not leak its text")]
    public async Task DispatchAsync_UnexpectedException_DoesNotLeakMessage()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "spec.fail", """{"text": "pwd=1"}""");

        Assert.Equal(JsonRpcErrorCodes.InternalError, response.Error!.Code);
        Assert.Equal("Internal error", response.Error.Message);
        Assert.Null(response.Error.Data);
    }

    [Fact]
    [DisplayName("IncludeExceptionDetails puts the exception message in the error data")]
    public async Task DispatchAsync_IncludeExceptionDetails_SendsMessageAsData()
    {
        var dispatcher = DispatcherFixture.Create(o => o.IncludeExceptionDetails = true);

        var response = await CallAsync(dispatcher, "spec.fail", """{"text": "x"}""");

        Assert.Equal("Secret connection string: x", response.Error!.Data!.Value.GetString());
    }

    [Fact]
    [DisplayName("InternalErrorCode changes the code of unexpected exceptions, for an older wire format")]
    public async Task DispatchAsync_CustomInternalErrorCode_IsUsed()
    {
        var dispatcher = DispatcherFixture.Create(o => o.InternalErrorCode = -32000);

        var response = await CallAsync(dispatcher, "spec.fail", """{"text": "x"}""");

        Assert.Equal(-32000, response.Error!.Code);
    }

    [Fact]
    [DisplayName("JsonRpcErrorException thrown by a method reaches the caller as it is")]
    public async Task DispatchAsync_JsonRpcErrorException_IsSentAsIs()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "spec.reject", """{"text": "nope"}""");

        Assert.Equal(-32050, response.Error!.Code);
        Assert.Equal("Rejected: nope", response.Error.Message);
    }

    [Fact]
    [DisplayName("The exception mapper turns an exception into a chosen error")]
    public async Task DispatchAsync_ExceptionMapper_MapsException()
    {
        var dispatcher = DispatcherFixture.Create(o => o.ExceptionMapper = (ex, _) =>
            ex is InvalidOperationException ? new JsonRpcError(-32010, "Mapped") : null);

        var response = await CallAsync(dispatcher, "spec.fail", """{"text": "x"}""");

        Assert.Equal(-32010, response.Error!.Code);
    }

    [Fact]
    [DisplayName("Filters run in order around the call")]
    public async Task DispatchAsync_Filters_RunInOrder()
    {
        var log = new List<string>();
        var dispatcher = DispatcherFixture.Create(o =>
        {
            o.Filters.Add(new RecordingFilter("a", log));
            o.Filters.Add(new RecordingFilter("b", log));
        });

        await CallAsync(dispatcher, "spec.subtract", """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(["a:before", "b:before", "b:after", "a:after"], log);
    }

    [Fact]
    [DisplayName("A filter that throws JsonRpcErrorException rejects the call before the method runs")]
    public async Task DispatchAsync_FilterThrows_RejectsCall()
    {
        var marker = Guid.NewGuid().ToString();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new RejectingFilter()));

        var response = await CallAsync(dispatcher, "spec.update", $$"""{"text": "{{marker}}"}""");

        Assert.Equal(-32001, response.Error!.Code);
        Assert.DoesNotContain(marker, SpecTarget.Updates);
    }

    [Fact]
    [DisplayName("Filters can rewrite raw params and results: a compress-and-encrypt round trip")]
    public async Task DispatchAsync_PayloadFilter_RewritesParamsAndResult()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new SealedPayloadFilter()));
        var sealedParams = SealedPayload.Seal(DispatcherFixture.Element("""{"minuend": 10, "subtrahend": 4}"""));

        var response = await CallAsync(dispatcher, "spec.subtract", sealedParams.GetRawText());

        Assert.Equal(6, SealedPayload.Open(response.Result!.Value).GetInt32());
    }

    [Fact]
    [DisplayName("Response members added by a filter are written next to the standard ones")]
    public async Task DispatchAsync_ResponseMembers_AreAdded()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new EchoMethodFilter()));

        var response = await CallAsync(dispatcher, "spec.subtract", """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal("spec.subtract", response.AdditionalMembers!["method"].GetString());
    }

    [Fact]
    [DisplayName("The filter sees the transport kind and items the transport set")]
    public async Task DispatchAsync_Filter_SeesTransportInfo()
    {
        var seen = new List<string>();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new TransportRecordingFilter(seen)));
        var transport = new JsonRpcTransportInfo(JsonRpcTransportKind.InProcess, items: new Dictionary<string, object?> { ["token"] = "t-1" });

        await CallAsync(dispatcher, "spec.subtract", """{"minuend": 1, "subtrahend": 1}""", transport);

        Assert.Equal(["InProcess:t-1"], seen);
    }

    [Fact]
    [DisplayName("A target the factory created itself is disposed after the call")]
    public async Task DispatchAsync_CreatedTarget_IsDisposed()
    {
        var marker = Guid.NewGuid().ToString();

        await CallAsync(DispatcherFixture.Create(), "disposable.ping", $$"""{"text": "{{marker}}"}""");

        Assert.Contains(marker, DisposableTarget.Disposed);
    }

    [Fact]
    [DisplayName("A batch larger than MaxBatchSize is answered with a single -32600")]
    public async Task DispatchMessageAsync_BatchTooLarge_ReturnsSingleInvalidRequest()
    {
        var dispatcher = DispatcherFixture.Create(o => o.MaxBatchSize = 2);
        const string Call = """{"jsonrpc": "2.0", "method": "spec.subtract", "params": {"minuend": 1, "subtrahend": 1}, "id": 1}""";

        using var answer = await DispatcherFixture.RunAsync(dispatcher, $"[{Call},{Call},{Call}]");

        Assert.Equal(JsonValueKind.Object, answer!.RootElement.ValueKind);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, answer.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    private sealed class AllowAllPolicy : IJsonRpcMethodPolicy
    {
        public bool IsCallable(System.Reflection.MethodInfo method) => true;
    }

    private sealed class RecordingFilter(string name, List<string> log) : IJsonRpcFilter
    {
        public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            log.Add($"{name}:before");
            await next(context);
            log.Add($"{name}:after");
        }
    }

    private sealed class RejectingFilter : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next) =>
            throw new JsonRpcErrorException(-32001, "Unauthorized");
    }

    private sealed class SealedPayloadFilter : IJsonRpcFilter
    {
        public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            context.Request.Params = SealedPayload.Open(context.Request.Params!.Value);
            await next(context);
            context.Result = SealedPayload.Seal(context.Result!.Value);
        }
    }

    private sealed class EchoMethodFilter : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            context.ResponseMembers["method"] = JsonSerializer.SerializeToElement(context.Request.Method);
            return next(context);
        }
    }

    private sealed class TransportRecordingFilter(List<string> seen) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            seen.Add($"{context.Transport.Kind}:{context.Items["token"]}");
            return next(context);
        }
    }
}
