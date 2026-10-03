namespace Polhem.JsonRpc.UnitTests;

public partial class DispatcherTests
{
    [Fact(DisplayName = "A request without params is answered with -32602, because the method has a parameter to bind")]
    public async Task DispatchMessageAsync_NoParams_ReturnsInvalidParams()
    {
        using var answer = await DispatcherFixture.RunAsync(DispatcherFixture.Create(),
            """{"jsonrpc": "2.0", "method": "Spec.Subtract", "id": 1}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, answer!.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Theory(DisplayName = "Task and ValueTask results are awaited and serialized")]
    [InlineData("Spec.Upper", """{"text":"ABC"}""")]
    [InlineData("Spec.Length", """{"length":3}""")]
    public async Task DispatchAsync_AsyncMethods_ReturnResult(string method, string expected)
    {
        var response = await CallAsync(DispatcherFixture.Create(), method, """{"text": "abc"}""");

        Assert.Equal(expected, response.Result!.Value.GetRawText());
    }

    [Fact(DisplayName = "Parameters that do not fit the request type are answered with -32602")]
    public async Task DispatchAsync_ParamsOfWrongShape_ReturnsInvalidParams()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Subtract", """{"minuend": "not a number"}""");

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response.Error!.Code);
    }

    [Fact(DisplayName = "An unexpected exception is answered with -32603 and a fixed message that does not leak its text")]
    public async Task DispatchAsync_UnexpectedException_DoesNotLeakMessage()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Fail", """{"text": "pwd=1"}""");

        Assert.Equal(JsonRpcErrorCodes.InternalError, response.Error!.Code);
        Assert.Equal("Internal error", response.Error.Message);
        Assert.Null(response.Error.Data);
    }

    [Fact(DisplayName = "IncludeExceptionDetails puts the exception message in the error data")]
    public async Task DispatchAsync_IncludeExceptionDetails_SendsMessageAsData()
    {
        var dispatcher = DispatcherFixture.Create(o => o.IncludeExceptionDetails = true);

        var response = await CallAsync(dispatcher, "Spec.Fail", """{"text": "x"}""");

        Assert.Equal("Secret connection string: x", response.Error!.Data!.Value.GetString());
    }

    [Fact(DisplayName = "JsonRpcErrorException thrown by a method reaches the caller as it is")]
    public async Task DispatchAsync_JsonRpcErrorException_IsSentAsIs()
    {
        var response = await CallAsync(DispatcherFixture.Create(), "Spec.Reject", """{"text": "nope"}""");

        Assert.Equal(-32050, response.Error!.Code);
        Assert.Equal("Rejected: nope", response.Error.Message);
    }

    [Fact(DisplayName = "The exception mapper turns an exception into a chosen error")]
    public async Task DispatchAsync_ExceptionMapper_MapsException()
    {
        var dispatcher = DispatcherFixture.Create(o => o.ExceptionMapper = (ex, _) =>
            ex is InvalidOperationException ? new JsonRpcError(-32010, "Mapped") : null);

        var response = await CallAsync(dispatcher, "Spec.Fail", """{"text": "x"}""");

        Assert.Equal(-32010, response.Error!.Code);
    }

    [Theory(DisplayName = "The object is released after the call, whether it succeeded or failed")]
    [InlineData("Disposable.Update")]
    [InlineData("Disposable.Fail")]
    public async Task DispatchAsync_Object_IsReleased(string method)
    {
        var marker = Guid.NewGuid().ToString();
        var dispatcher = DispatcherFixture.Create(o => o.MethodPolicy = new AllowAllPolicy());

        await CallAsync(dispatcher, method, $$"""{"text": "{{marker}}"}""");

        Assert.Contains(marker, DisposableTarget.Disposed);
    }

    [Fact(DisplayName = "A release that throws answers a call that succeeded with -32603, keeps the error of one that failed, and leaves the rest of the batch answered")]
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

    [Fact(DisplayName = "An exception mapper that throws falls back to the default -32603 and leaves the rest of the batch answered")]
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
}
