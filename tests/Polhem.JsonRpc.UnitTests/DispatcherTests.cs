using System.ComponentModel;
using System.Reflection;
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
    [DisplayName("The dispatcher requires an object factory")]
    public void Constructor_NoObjectFactory_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JsonRpcDispatcher(new JsonRpcServerOptions()));
    }

    [Fact]
    [DisplayName("ProgId.Action creates the object for the ProgId and calls the action on it")]
    public async Task DispatchAsync_ProgIdAction_CallsAction()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Subtract", """{"minuend": 5, "subtrahend": 3}""");

        Assert.Equal(2, response.Result!.Value.GetProperty("difference").GetInt32());
    }

    [Theory]
    [DisplayName("A name the factory or the object does not know is answered with -32601")]
    [InlineData("Unknown.Subtract")]
    [InlineData("Spec.Missing")]
    public async Task DispatchAsync_UnknownProgIdOrAction_ReturnsMethodNotFound(string method)
    {
        var response = await CallAsync(DispatcherFixture.Create(), method, """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Theory]
    [DisplayName("Names are matched case-sensitively, as in the Polhem framework")]
    [InlineData("Spec.subtract")]
    [InlineData("spec.Subtract")]
    public async Task DispatchAsync_DifferentCase_ReturnsMethodNotFound(string method)
    {
        var response = await CallAsync(DispatcherFixture.Create(), method, """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Theory]
    [DisplayName("Malformed or too long names, and methods that are static, overloaded, accessors, generic or declared by object, are not resolved")]
    [InlineData("Spec")]
    [InlineData(".Subtract")]
    [InlineData("Spec.")]
    [InlineData("Spec.Sub-tract")]
    [InlineData("Spec.Subtract.Extra")]
    [InlineData("Spec.Static")]
    [InlineData("Spec.Twice")]
    [InlineData("Spec.ToString")]
    [InlineData("Spec.Equals")]
    [InlineData("Spec.set_Label")]
    [InlineData("Spec.Generic")]
    [InlineData("Spec.LongActionNameThatFillsEveryOneOfTheSixtyFourCharactersAllowed_XY")]
    public async Task DispatchAsync_UnresolvableName_ReturnsMethodNotFound(string method)
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, method, """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Theory]
    [DisplayName("A record's Equals is not an action, even under a policy that admits every method")]
    [InlineData("Record.Equals")]
    [InlineData("DerivedRecord.Equals")]
    public async Task DispatchAsync_RecordEquals_ReturnsMethodNotFound(string method)
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, method, """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Theory]
    [DisplayName("A public method inherited from a base type is an action, as with Polhem's Type.GetMethod")]
    [InlineData("Record.Subtract")]
    [InlineData("DerivedRecord.Subtract")]
    public async Task DispatchAsync_InheritedMethod_CallsAction(string method)
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, method, """{"minuend": 5, "subtrahend": 3}""");

        Assert.Equal(2, response.Result!.Value.GetProperty("difference").GetInt32());
    }

    [Fact]
    [DisplayName("A request without params is answered with -32602, because the method has a parameter to bind")]
    public async Task DispatchMessageAsync_NoParams_ReturnsInvalidParams()
    {
        using var answer = await DispatcherFixture.RunAsync(DispatcherFixture.Create(),
            """{"jsonrpc": "2.0", "method": "Spec.Subtract", "id": 1}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, answer!.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    [DisplayName("An action name of exactly 64 characters is resolved")]
    public async Task DispatchAsync_ActionNameAtLengthLimit_CallsAction()
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, "Spec.LongActionNameThatFillsEveryOneOfTheSixtyFourCharactersAllowed_X",
            """{"minuend": 5, "subtrahend": 3}""");

        Assert.Equal(2, response.Result!.Value.GetProperty("difference").GetInt32());
    }

    [Theory]
    [DisplayName("A ProgId of exactly 64 characters is resolved, and one of 65 is not a method name")]
    [InlineData(TestObjectFactory.LongProgId, false)]
    [InlineData(TestObjectFactory.LongProgId + "X", true)]
    public async Task DispatchAsync_ProgIdAtLengthLimit_IsResolvedUpTo64(string progId, bool refused)
    {
        var response = await CallAsync(DispatcherFixture.Create(), progId + ".Subtract", """{"minuend": 5, "subtrahend": 3}""");

        if (refused)
        {
            Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
        }
        else
        {
            Assert.Equal(2, response.Result!.Value.GetProperty("difference").GetInt32());
        }
    }

    [Fact]
    [DisplayName("Names the caller makes up are looked up but never cached, so they cannot grow the dispatcher's memory")]
    public async Task DispatchAsync_ManyUnknownActions_CacheHoldsOneEntryPerType()
    {
        var dispatcher = DispatcherFixture.Create();

        for (var i = 0; i < 1000; i++)
        {
            await CallAsync(dispatcher, $"Spec.Unknown{i}", "{}");
        }
        await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""");

        // White-box on purpose: the size of the cache is the property, and nothing public reveals it.
        var cache = (System.Collections.ICollection)typeof(JsonRpcDispatcher)
            .GetField("_actions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dispatcher)!;
        Assert.Single(cache.Cast<object>());
    }

    [Fact]
    [DisplayName("A public method outside the {Action}Request/{Action}Response convention is not callable")]
    public async Task DispatchAsync_UnconventionalMethod_ReturnsMethodNotFound()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Echo", """{"text": "x"}""");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, response.Error!.Code);
    }

    [Fact]
    [DisplayName("A replaced method policy decides which methods are callable")]
    public async Task DispatchAsync_CustomPolicy_AdmitsUnconventionalMethod()
    {
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        var response = await CallAsync(dispatcher, "Spec.Echo", """{"text": "x"}""");

        Assert.Equal("x", response.Result!.Value.GetString());
    }

    [Theory]
    [DisplayName("Naming convention: the parameter is {Action}Request, the result {Action}Response, also inside a task")]
    [InlineData(nameof(SpecTarget.Subtract), true)]
    [InlineData(nameof(SpecTarget.Upper), true)]
    [InlineData(nameof(SpecTarget.Length), true)]
    [InlineData(nameof(SpecTarget.Echo), false)]
    [InlineData(nameof(SpecTarget.Reject), true)]
    [InlineData(nameof(SpecTarget.Mismatch), false)]
    public void NamingConventionPolicy_IsCallable_FollowsConvention(string methodName, bool expected)
    {
        var method = typeof(SpecTarget).GetMethod(methodName, [typeof(SpecTarget).GetMethod(methodName)!.GetParameters()[0].ParameterType])!;

        Assert.Equal(expected, new JsonRpcNamingConventionPolicy().IsCallable(method));
    }

    [Theory]
    [DisplayName("Task and ValueTask results are awaited and serialized")]
    [InlineData("Spec.Upper", """{"text":"ABC"}""")]
    [InlineData("Spec.Length", """{"length":3}""")]
    public async Task DispatchAsync_AsyncMethods_ReturnResult(string method, string expected)
    {
        var response = await CallAsync(DispatcherFixture.Create(), method, """{"text": "abc"}""");

        Assert.Equal(expected, response.Result!.Value.GetRawText());
    }

    [Fact]
    [DisplayName("Parameters that do not fit the request type are answered with -32602")]
    public async Task DispatchAsync_ParamsOfWrongShape_ReturnsInvalidParams()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Subtract", """{"minuend": "not a number"}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
    }

    [Fact]
    [DisplayName("An unexpected exception is answered with -32603 and a fixed message that does not leak its text")]
    public async Task DispatchAsync_UnexpectedException_DoesNotLeakMessage()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Fail", """{"text": "pwd=1"}""");

        Assert.Equal(JsonRpcErrorCodes.InternalError, response.Error!.Code);
        Assert.Equal("Internal error", response.Error.Message);
        Assert.Null(response.Error.Data);
    }

    [Fact]
    [DisplayName("IncludeExceptionDetails puts the exception message in the error data")]
    public async Task DispatchAsync_IncludeExceptionDetails_SendsMessageAsData()
    {
        var dispatcher = DispatcherFixture.Create(o => o.IncludeExceptionDetails = true);

        var response = await CallAsync(dispatcher, "Spec.Fail", """{"text": "x"}""");

        Assert.Equal("Secret connection string: x", response.Error!.Data!.Value.GetString());
    }

    [Fact]
    [DisplayName("JsonRpcErrorException thrown by a method reaches the caller as it is")]
    public async Task DispatchAsync_JsonRpcErrorException_IsSentAsIs()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Reject", """{"text": "nope"}""");

        Assert.Equal(-32050, response.Error!.Code);
        Assert.Equal("Rejected: nope", response.Error.Message);
    }

    [Fact]
    [DisplayName("The exception mapper turns an exception into a chosen error")]
    public async Task DispatchAsync_ExceptionMapper_MapsException()
    {
        var dispatcher = DispatcherFixture.Create(o => o.ExceptionMapper = (ex, _) =>
            ex is InvalidOperationException ? new JsonRpcError(-32010, "Mapped") : null);

        var response = await CallAsync(dispatcher, "Spec.Fail", """{"text": "x"}""");

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

        await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(["a:before", "b:before", "b:after", "a:after"], log);
    }

    [Fact]
    [DisplayName("Filters see the resolved object and method, and do not run for a method that is not found")]
    public async Task DispatchAsync_Filters_RunAfterResolution()
    {
        var seen = new List<string>();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new MethodRecordingFilter(seen)));

        await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""");
        await CallAsync(dispatcher, "Spec.Missing", """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(["Spec.Subtract:SpecTarget"], seen);
    }

    [Fact]
    [DisplayName("A filter that throws JsonRpcErrorException rejects the call before the method runs")]
    public async Task DispatchAsync_FilterThrows_RejectsCall()
    {
        var marker = Guid.NewGuid().ToString();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new RejectingFilter()));

        var response = await CallAsync(dispatcher, "Spec.Update", $$"""{"text": "{{marker}}"}""");

        Assert.Equal(-32001, response.Error!.Code);
        Assert.DoesNotContain(marker, SpecTarget.Updates);
    }

    [Fact]
    [DisplayName("Filters can rewrite raw params and results: a compress-and-encrypt round trip")]
    public async Task DispatchAsync_PayloadFilter_RewritesParamsAndResult()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new SealedPayloadFilter()));
        var sealedParams = SealedPayload.Seal(DispatcherFixture.Element("""{"minuend": 10, "subtrahend": 4}"""));

        var response = await CallAsync(dispatcher, "Spec.Subtract", sealedParams.GetRawText());

        Assert.Equal(6, SealedPayload.Open(response.Result!.Value).GetProperty("difference").GetInt32());
    }

    [Fact]
    [DisplayName("A filter can answer from the returned object, so a result the default options cannot write never reaches them")]
    public async Task DispatchAsync_FilterWritesResultFromReturnValue_SkipsDefaultSerialization()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new DescribeFilter()));

        var response = await CallAsync(dispatcher, "Spec.Describe", """{"text": "x"}""");

        Assert.Equal("String:x", response.Result!.Value.GetString());
    }

    [Fact]
    [DisplayName("Without such a filter, a result the default options cannot write is an internal error")]
    public async Task DispatchAsync_UnserializableResultWithoutFilter_ReturnsInternalError()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Describe", """{"text": "x"}""");

        Assert.Equal(JsonRpcErrorCodes.InternalError, response.Error!.Code);
    }

    [Fact]
    [DisplayName("The filter sees the transport kind and items the transport set")]
    public async Task DispatchAsync_Filter_SeesTransportInfo()
    {
        var seen = new List<string>();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new TransportRecordingFilter(seen)));
        var transport = new JsonRpcTransportInfo(JsonRpcTransportKind.InProcess, items: new Dictionary<string, object?> { ["token"] = "t-1" });

        await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""", transport);

        Assert.Equal(["InProcess:t-1"], seen);
    }

    [Theory]
    [DisplayName("The object is released after the call, whether it succeeded or failed")]
    [InlineData("Disposable.Update")]
    [InlineData("Disposable.Fail")]
    public async Task DispatchAsync_Object_IsReleased(string method)
    {
        var marker = Guid.NewGuid().ToString();
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        await CallAsync(dispatcher, method, $$"""{"text": "{{marker}}"}""");

        Assert.Contains(marker, DisposableTarget.Disposed);
    }

    [Fact]
    [DisplayName("A batch larger than MaxBatchSize is answered with a single -32600")]
    public async Task DispatchMessageAsync_BatchTooLarge_ReturnsSingleInvalidRequest()
    {
        var dispatcher = DispatcherFixture.Create(o => o.MaxBatchSize = 2);
        const string Call = """{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 1, "subtrahend": 1}, "id": 1}""";

        using var answer = await DispatcherFixture.RunAsync(dispatcher, $"[{Call},{Call},{Call}]");

        Assert.Equal(JsonValueKind.Object, answer!.RootElement.ValueKind);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, answer.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Theory]
    [DisplayName("DispatchBatchAsync answers an empty batch or one larger than MaxBatchSize with a single -32600")]
    [InlineData(0)]
    [InlineData(3)]
    public async Task DispatchBatchAsync_EmptyOrTooLarge_ReturnsSingleInvalidRequest(int count)
    {
        var dispatcher = DispatcherFixture.Create(o => o.MaxBatchSize = 2);
        var requests = Enumerable.Range(1, count)
            .Select(i => new JsonRpcRequest("Spec.Subtract", DispatcherFixture.Element("""{"minuend": 1, "subtrahend": 1}"""), JsonRpcId.FromNumber(i)))
            .ToList();

        var responses = await dispatcher.DispatchBatchAsync(requests, DispatcherFixture.Http());

        var response = Assert.Single(responses);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, response.Error!.Code);
        Assert.Equal(JsonRpcId.Null, response.Id);
    }

    [Fact]
    [DisplayName("A message nested deeper than the JSON reader allows is answered with -32700")]
    public async Task DispatchMessageAsync_NestedTooDeep_ReturnsParseError()
    {
        var nested = new string('[', 70) + new string(']', 70);
        var json = $$"""{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": {{nested}}}, "id": 1}""";

        using var answer = await DispatcherFixture.RunAsync(DispatcherFixture.Create(), json);

        Assert.Equal(JsonRpcErrorCodes.ParseError, answer!.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("id").ValueKind);
    }

    [Fact]
    [DisplayName("A call the caller cancelled propagates the cancellation instead of answering -32603")]
    public async Task DispatchAsync_CallerCancelled_Throws()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new CancellationObservingFilter()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var request = new JsonRpcRequest("Spec.Subtract", DispatcherFixture.Element("""{"minuend": 1, "subtrahend": 1}"""), JsonRpcId.FromNumber(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(request, DispatcherFixture.Http(), cancelled.Token));
    }

    [Fact]
    [DisplayName("A cancellation the caller did not ask for is an internal error like any other exception")]
    public async Task DispatchAsync_CancellationNotRequested_ReturnsInternalError()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new CancellationObservingFilter()));

        var response = await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.InternalError, response.Error!.Code);
    }

    [Fact]
    [DisplayName("A transport that leaves the kind at its default is HTTP, never in-process")]
    public void TransportKind_Default_IsNotInProcess()
    {
        Assert.Equal(JsonRpcTransportKind.Http, default);
        Assert.NotEqual(JsonRpcTransportKind.InProcess, new JsonRpcTransportInfo(default).Kind);
    }

    [Fact]
    [DisplayName("InProcessTransport marks its calls as in-process")]
    public async Task InProcessTransport_SendAsync_MarksInProcess()
    {
        var seen = new List<JsonRpcTransportKind>();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new KindRecordingFilter(seen)));
        var request = new JsonRpcRequest("Spec.Subtract", DispatcherFixture.Element("""{"minuend": 1, "subtrahend": 1}"""), JsonRpcId.FromNumber(1));

        await new InProcessTransport(dispatcher).SendAsync(request);

        Assert.Equal([JsonRpcTransportKind.InProcess], seen);
    }

    [Fact]
    [DisplayName("A release that throws answers a call that succeeded with -32603, keeps the error of one that failed, and leaves the rest of the batch answered")]
    public async Task DispatchMessageAsync_ReleaseThrows_AnswersEachCall()
    {
        var dispatcher = DispatcherFixture.Create(o => o.ObjectFactory = new ThrowingReleaseFactory());
        const string Batch = """
            [{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 3, "subtrahend": 1}, "id": 1},
             {"jsonrpc": "2.0", "method": "Spec.Reject", "params": {"text": "x"}, "id": 2}]
            """;

        using var answer = await DispatcherFixture.RunAsync(dispatcher, Batch);

        var codes = answer!.RootElement.EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetInt32(), r => r.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonRpcErrorCodes.InternalError, codes[1]);
        Assert.Equal(-32050, codes[2]);
    }

    [Fact]
    [DisplayName("An exception mapper that throws falls back to the default -32603 and leaves the rest of the batch answered")]
    public async Task DispatchMessageAsync_ExceptionMapperThrows_FallsBackToInternalError()
    {
        var dispatcher = DispatcherFixture.Create(o => o.ExceptionMapper = (_, _) => throw new InvalidOperationException("Mapper bug"));
        const string Batch = """
            [{"jsonrpc": "2.0", "method": "Spec.Fail", "params": {"text": "x"}, "id": 1},
             {"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 3, "subtrahend": 1}, "id": 2}]
            """;

        using var answer = await DispatcherFixture.RunAsync(dispatcher, Batch);

        var responses = answer!.RootElement.EnumerateArray().ToDictionary(r => r.GetProperty("id").GetInt32());
        Assert.Equal(JsonRpcErrorCodes.InternalError, responses[1].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("Internal error", responses[1].GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(2, responses[2].GetProperty("result").GetProperty("difference").GetInt32());
    }

    [Theory]
    [DisplayName("A batch whose caller cancels stops before its next call")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchBatch_CancelledMidway_StopsBeforeNextCall(bool asMessage)
    {
        using var cancellation = new CancellationTokenSource();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new CancelAfterCallFilter(cancellation)));
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();
        var requests = new[] { first, second }
            .Select((text, i) => new JsonRpcRequest("Spec.Update", DispatcherFixture.Element($$"""{"text": "{{text}}"}"""), JsonRpcId.FromNumber(i)))
            .ToList();

        Task run = asMessage
            ? dispatcher.DispatchMessageAsync(JsonRpcSerializer.SerializeRequests(requests), DispatcherFixture.Http(), cancellation.Token)
            : dispatcher.DispatchBatchAsync(requests, DispatcherFixture.Http(), cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Contains(first, SpecTarget.Updates);
        Assert.DoesNotContain(second, SpecTarget.Updates);
    }

    private sealed class AllowAllPolicy : IJsonRpcMethodPolicy
    {
        public bool IsCallable(MethodInfo method) => true;
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

    private sealed class MethodRecordingFilter(List<string> seen) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            var method = context.Method!;
            seen.Add($"{method.ProgId}.{method.Action}:{method.Instance.GetType().Name}");
            return next(context);
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
            context.Result = SealedPayload.Seal(context.GetResult()!.Value);
        }
    }

    private sealed class DescribeFilter : IJsonRpcFilter
    {
        public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            await next(context);
            var response = (DescribeResponse)context.ReturnValue!;
            context.Result = JsonSerializer.SerializeToElement($"{response.Kind.Name}:{response.Text}");
        }
    }

    private sealed class ThrowingReleaseFactory : IJsonRpcObjectFactory
    {
        private readonly TestObjectFactory _inner = new();

        public object? CreateObject(string progId, JsonRpcRequestContext context) => _inner.CreateObject(progId, context);

        public ValueTask ReleaseObjectAsync(object instance, JsonRpcRequestContext context) =>
            throw new InvalidOperationException("Release failed");
    }

    // Lets the call run, then cancels, as a client that disconnects during a batch.
    private sealed class CancelAfterCallFilter(CancellationTokenSource cancellation) : IJsonRpcFilter
    {
        public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            await next(context);
            await cancellation.CancelAsync();
        }
    }

    // Throws when the token is cancelled; with a live token it throws a cancellation the caller never asked for.
    private sealed class CancellationObservingFilter : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }
    }

    private sealed class KindRecordingFilter(List<JsonRpcTransportKind> seen) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            seen.Add(context.Transport.Kind);
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
