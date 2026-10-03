using System.Net;
using System.Text;
using System.Text.Json;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public class ClientTests
{
    private static JsonRpcConnector InProcess(JsonRpcClientOptions? options = null, Action<JsonRpcServerOptions>? configureServer = null) =>
        new(new InProcessTransport(DispatcherFixture.Create(configureServer)), options);

    [Fact(DisplayName = "Client: InvokeAsync returns the result")]
    public async Task InvokeAsync_Call_ReturnsResult()
    {
        var result = await InProcess().InvokeAsync<SubtractResponse>("Spec.Subtract", new SubtractRequest(10, 3));

        Assert.Equal(7, result!.Difference);
    }

    [Fact(DisplayName = "Client: an error response is thrown as JsonRpcErrorException")]
    public async Task InvokeAsync_ErrorResponse_ThrowsJsonRpcErrorException()
    {
        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => InProcess().InvokeAsync<RejectResponse>("Spec.Reject", new RejectRequest("x")));

        Assert.Equal(-32050, ex.Code);
    }

    [Fact(DisplayName = "Client: ErrorMapper chooses the exception to throw")]
    public async Task InvokeAsync_ErrorMapper_ThrowsMappedException()
    {
        var options = new JsonRpcClientOptions { ErrorMapper = error => error.Code == -32050 ? new UnauthorizedAccessException(error.Message) : null };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => InProcess(options).InvokeAsync<RejectResponse>("Spec.Reject", new RejectRequest("x")));
    }

    [Fact(DisplayName = "Client: the non-generic InvokeAsync waits for a method that returns nothing")]
    public async Task InvokeAsync_VoidMethod_Completes()
    {
        var marker = Guid.NewGuid().ToString();

        await InProcess().InvokeAsync("Spec.Update", new UpdateRequest(marker), CancellationToken.None);

        Assert.Contains(marker, SpecTarget.Updates);
    }

    [Fact(DisplayName = "Client: NotifyAsync runs the method and receives nothing")]
    public async Task NotifyAsync_Notification_RunsMethod()
    {
        var marker = Guid.NewGuid().ToString();

        await InProcess().NotifyAsync("Spec.Update", new UpdateRequest(marker));

        Assert.Contains(marker, SpecTarget.Updates);
    }

    [Fact(DisplayName = "Client: a batch completes each call's task, failures included")]
    public async Task Batch_MixedCalls_CompletesEachTask()
    {
        var batch = InProcess().CreateBatch();
        var ok = batch.Add<SubtractResponse>("Spec.Subtract", new SubtractRequest(5, 1));
        var failed = batch.Add<RejectResponse>("Spec.Reject", new RejectRequest("x"));
        batch.AddNotification("Spec.Update", new UpdateRequest("batch"));

        await batch.SendAsync();

        Assert.Equal(4, (await ok)!.Difference);
        await Assert.ThrowsAsync<JsonRpcErrorException>(() => failed);
    }

    [Fact(DisplayName = "Client: a batch cannot be sent twice")]
    public async Task Batch_SentTwice_Throws()
    {
        var batch = InProcess().CreateBatch();
        await batch.SendAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => batch.SendAsync());
    }

    [Fact(DisplayName = "Client: interceptors can rewrite raw params and results: a compress-and-encrypt round trip")]
    public async Task InvokeAsync_Interceptor_RewritesParamsAndResult()
    {
        var options = new JsonRpcClientOptions();
        options.Interceptors.Add(new SealingInterceptor());
        var connector = InProcess(options, server => server.Filters.Add(new OpeningFilter()));

        var result = await connector.InvokeAsync<SubtractResponse>("Spec.Subtract", new SubtractRequest(9, 2));

        Assert.Equal(7, result!.Difference);
    }

    [Fact(DisplayName = "Client: IdGenerator chooses the request ids")]
    public async Task InvokeAsync_IdGenerator_UsesGeneratedId()
    {
        var transport = new RecordingTransport();
        var connector = new JsonRpcConnector(transport, new JsonRpcClientOptions { IdGenerator = () => "custom-id" });

        await connector.InvokeAsync<JsonElement>("Any.Method", null);

        Assert.Equal(JsonRpcId.FromString("custom-id"), transport.LastRequest!.Id);
    }

    [Fact(DisplayName = "Client: Timeout cancels a call that takes too long")]
    public async Task InvokeAsync_Timeout_Cancels()
    {
        var connector = new JsonRpcConnector(new HangingTransport(), new JsonRpcClientOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connector.InvokeAsync<int>("Any.Method", null));
    }

    [Fact(DisplayName = "Client: parameters that are not an object or array are rejected")]
    public async Task InvokeAsync_ScalarParameters_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => InProcess().InvokeAsync<SubtractResponse>("Spec.Subtract", 42));
    }

    [Fact(DisplayName = "Client: a response carrying the id of another request is refused")]
    public async Task InvokeAsync_ResponseWithAnotherId_Throws()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, """{"jsonrpc": "2.0", "result": 1, "id": 999}""")) { BaseAddress = new Uri("http://test/api") };

        await Assert.ThrowsAsync<JsonException>(() => new JsonRpcConnector(new HttpTransport(http)).InvokeAsync<int>("Any.Method", null));
    }

    [Fact(DisplayName = "Client: an IdGenerator that returns no id is refused, because the call would become a notification")]
    public async Task InvokeAsync_IdGeneratorReturnsNone_Throws()
    {
        var transport = new RecordingTransport();
        var connector = new JsonRpcConnector(transport, new JsonRpcClientOptions { IdGenerator = () => JsonRpcId.None });

        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.InvokeAsync<int>("Any.Method", null));
        Assert.Null(transport.LastRequest);
    }

    [Theory(DisplayName = "Client: a batch refuses a call whose id is null or already in the batch, so no task is left waiting")]
    [InlineData(true)]
    [InlineData(false)]
    public void Batch_IdNotUnique_AddThrows(bool nullId)
    {
        var options = new JsonRpcClientOptions { IdGenerator = () => nullId ? JsonRpcId.Null : JsonRpcId.FromString("same") };
        var batch = InProcess(options).CreateBatch();

        if (!nullId) { batch.Add<SubtractResponse>("Spec.Subtract", new SubtractRequest(1, 1)); }

        // Add throws before it returns a task, so the call is wrapped as an action rather than awaited.
        Assert.Throws<InvalidOperationException>(() => { _ = batch.Add<SubtractResponse>("Spec.Subtract", new SubtractRequest(2, 1)); });
    }

    [Fact(DisplayName = "Client: when the server refuses the whole batch with one error, each call fails with that error")]
    public async Task Batch_RefusedAsAWhole_EachCallFailsWithServerError()
    {
        var batch = InProcess(configureServer: server => server.MaxBatchSize = 1).CreateBatch();
        var first = batch.Add<SubtractResponse>("Spec.Subtract", new SubtractRequest(5, 1));
        var second = batch.Add<SubtractResponse>("Spec.Subtract", new SubtractRequest(6, 1));

        await batch.SendAsync();

        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, (await Assert.ThrowsAsync<JsonRpcErrorException>(() => first)).Code);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, (await Assert.ThrowsAsync<JsonRpcErrorException>(() => second)).Code);
    }

    [Fact(DisplayName = "Client: the options are read when the connector is created; an interceptor added later does not run")]
    public async Task Connector_OptionsChangedAfterCreation_AreNotSeen()
    {
        var options = new JsonRpcClientOptions();
        var connector = InProcess(options);
        var late = new CountingInterceptor();

        options.Interceptors.Add(late);
        options.ErrorMapper = _ => new UnauthorizedAccessException();
        await connector.InvokeAsync<SubtractResponse>("Spec.Subtract", new SubtractRequest(1, 1));

        Assert.Equal(0, late.Requests);
        await Assert.ThrowsAsync<JsonRpcErrorException>(() => connector.InvokeAsync<RejectResponse>("Spec.Reject", new RejectRequest("x")));
    }

    [Fact(DisplayName = "Client: the non-generic InvokeAsync and InvokeAsync<JsonElement> need no JsonElement in a source-generated context")]
    public async Task InvokeAsync_JsonElementWithSourceGeneratedContext_NeedsNoMetadata()
    {
        var options = new JsonRpcClientOptions
        {
            SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = ClientTestJsonContext.Default },
        };
        var connector = InProcess(options);

        await connector.InvokeAsync("Spec.Subtract", new SubtractRequest(5, 3), CancellationToken.None);
        var element = await connector.InvokeAsync<JsonElement>("Spec.Subtract", new SubtractRequest(5, 3));
        var nullable = await connector.InvokeAsync<JsonElement?>("Spec.Subtract", new SubtractRequest(5, 3));

        Assert.Equal(2, element.GetProperty("difference").GetInt32());
        Assert.Equal(2, nullable!.Value.GetProperty("difference").GetInt32());
    }

    [Fact(DisplayName = "HTTP transport: an error object in a 4xx body is read as a JSON-RPC error")]
    public async Task HttpTransport_ErrorBodyWith401_IsRead()
    {
        const string Body = """{"jsonrpc": "2.0", "error": {"code": -32001, "message": "Unauthorized"}, "id": 1}""";
        using var http = new HttpClient(new StubHandler(HttpStatusCode.Unauthorized, Body)) { BaseAddress = new Uri("http://test/api") };

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => new JsonRpcConnector(new HttpTransport(http)).InvokeAsync<int>("Any.Method", null));

        Assert.Equal(-32001, ex.Code);
    }

    [Fact(DisplayName = "HTTP transport: a non-JSON-RPC body with an error status raises HttpRequestException")]
    public async Task HttpTransport_HtmlWith500_ThrowsHttpRequestException()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "<html>oops</html>")) { BaseAddress = new Uri("http://test/api") };

        await Assert.ThrowsAsync<HttpRequestException>(() => new JsonRpcConnector(new HttpTransport(http)).InvokeAsync<int>("Any.Method", null));
    }

    [Fact(DisplayName = "HTTP transport: a response from an older server without id and result reads as an empty success")]
    public async Task HttpTransport_LenientResponse_ReadsAsSuccess()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, """{"jsonrpc": "2.0", "method": "A.B"}""")) { BaseAddress = new Uri("http://test/api") };

        var result = await new JsonRpcConnector(new HttpTransport(http)).InvokeAsync<string>("A.B", null);

        Assert.Null(result);
    }

    private sealed class SealingInterceptor : IJsonRpcClientInterceptor
    {
        public ValueTask OnRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            request.Params = SealedPayload.Seal(request.Params!.Value);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnResponseAsync(JsonRpcRequest request, JsonRpcResponse response, CancellationToken cancellationToken)
        {
            response.Result = SealedPayload.Open(response.Result!.Value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingInterceptor : IJsonRpcClientInterceptor
    {
        public int Requests { get; private set; }

        public ValueTask OnRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            Requests++;
            return ValueTask.CompletedTask;
        }

        public ValueTask OnResponseAsync(JsonRpcRequest request, JsonRpcResponse response, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class OpeningFilter : IJsonRpcFilter
    {
        public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            context.Request.Params = SealedPayload.Open(context.Request.Params!.Value);
            await next(context);
            context.Result = SealedPayload.Seal(context.GetResult()!.Value);
        }
    }

    private sealed class RecordingTransport : IJsonRpcTransport
    {
        public JsonRpcRequest? LastRequest { get; private set; }

        public Task<JsonRpcResponse?> SendAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<JsonRpcResponse?>(JsonRpcResponse.Success(request.Id, null));
        }

        public Task<IReadOnlyList<JsonRpcResponse>> SendBatchAsync(IReadOnlyList<JsonRpcRequest> requests, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JsonRpcResponse>>([]);
    }

    private sealed class HangingTransport : IJsonRpcTransport
    {
        public async Task<JsonRpcResponse?> SendAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }

        public Task<IReadOnlyList<JsonRpcResponse>> SendBatchAsync(IReadOnlyList<JsonRpcRequest> requests, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JsonRpcResponse>>([]);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
