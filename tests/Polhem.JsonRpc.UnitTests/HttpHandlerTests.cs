using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Polhem.JsonRpc.AspNetCore;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public sealed class HttpHandlerTests : IAsyncLifetime
{
    private const string Subtract = """{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 3, "subtrahend": 1}, "id": 1}""";

    private WebApplication? _app;
    private HttpClient? _client;
    private readonly List<JsonRpcTransportKind> _seenKinds = [];

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddJsonRpcServer(
            options =>
            {
                options.ObjectFactory = new TestObjectFactory();
                options.Filters.Add(new KindRecordingFilter(_seenKinds));
            },
            http =>
            {
                http.MaxRequestBodySize = 1024;
                http.StatusCodeSelector = response => response.Error?.Code == JsonRpcErrorCodes.MethodNotFound ? 404 : 200;
            });
        _app = builder.Build();
        _app.MapJsonRpc("/api");
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null) { await _app.DisposeAsync(); }
    }

    private Task<HttpResponseMessage> PostAsync(string body, string contentType = "application/json") =>
        _client!.PostAsync("/api", new StringContent(body, Encoding.UTF8, contentType));

    [Fact]
    [DisplayName("HTTP: a call is answered with 200 and the JSON-RPC response")]
    public async Task Post_Call_Returns200WithResponse()
    {
        using var response = await PostAsync(Subtract);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("result").GetProperty("difference").GetInt32());
    }

    [Fact]
    [DisplayName("HTTP: a notification is answered with 204 and no body")]
    public async Task Post_Notification_Returns204()
    {
        using var response = await PostAsync("""{"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "http"}}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    [DisplayName("HTTP: a body that is not application/json is answered with 415")]
    public async Task Post_WrongContentType_Returns415()
    {
        using var response = await PostAsync(Subtract, "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    [DisplayName("HTTP: a body larger than MaxRequestBodySize is answered with 413")]
    public async Task Post_TooLarge_Returns413()
    {
        var large = $$"""{"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "{{new string('x', 2048)}}"}, "id": 1}""";

        using var response = await PostAsync(large);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    [DisplayName("HTTP: StatusCodeSelector chooses the status of a single response")]
    public async Task Post_StatusCodeSelector_ChoosesStatus()
    {
        using var response = await PostAsync("""{"jsonrpc": "2.0", "method": "Nothing.Here", "id": 1}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [DisplayName("HTTP: a call is marked as HTTP even when a header claims otherwise")]
    public async Task Post_HeaderClaimsInProcess_StillMarkedHttp()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api")
        {
            Content = new StringContent(Subtract, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transport", "InProcess");
        request.Headers.Add("Kind", "InProcess");

        using var response = await _client!.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(_seenKinds, kind => Assert.Equal(JsonRpcTransportKind.Http, kind));
        Assert.NotEmpty(_seenKinds);
    }

    [Fact]
    [DisplayName("HTTP: AddJsonRpcServer adds to the options a framework registered first, after its filters")]
    public async Task AddJsonRpcServer_RegisteredOptions_AreSharedAndExtended()
    {
        var order = new List<string>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var frameworkOptions = new JsonRpcServerOptions { ObjectFactory = new TestObjectFactory() };
        frameworkOptions.Filters.Add(new NamedFilter("framework", order));
        builder.Services.AddSingleton(frameworkOptions);
        builder.Services.AddJsonRpcServer(options => options.Filters.Add(new NamedFilter("application", order)));
        await using var app = builder.Build();
        app.MapJsonRpc("/api");
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.PostAsync("/api", new StringContent(Subtract, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["framework", "application"], order);
    }

    [Fact]
    [DisplayName("HTTP: AddJsonRpcServer applies configureHttp to HTTP options a framework registered first")]
    public void AddJsonRpcServer_RegisteredHttpOptions_AreSharedAndExtended()
    {
        var services = new ServiceCollection();
        var frameworkHttp = new JsonRpcHttpOptions();
        frameworkHttp.WriteOptions.OmitNullId = true;
        services.AddSingleton(frameworkHttp);

        services.AddJsonRpcServer(configureHttp: http => http.MaxRequestBodySize = 123);
        using var provider = services.BuildServiceProvider();

        var shared = provider.GetRequiredService<JsonRpcHttpOptions>();
        Assert.Same(frameworkHttp, shared);
        Assert.True(shared.WriteOptions.OmitNullId);
        Assert.Equal(123, shared.MaxRequestBodySize);
    }

    private sealed class NamedFilter(string name, List<string> order) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            order.Add(name);
            return next(context);
        }
    }

    private sealed class KindRecordingFilter(List<JsonRpcTransportKind> kinds) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            lock (kinds) { kinds.Add(context.Transport.Kind); }
            return next(context);
        }
    }
}
