using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Polhem.JsonRpc.AspNetCore;
using Polhem.JsonRpc.Client;
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

    [Fact(DisplayName = "HTTP: a call is answered with 200 and the JSON-RPC response")]
    public async Task Post_Call_Returns200WithResponse()
    {
        using var response = await PostAsync(Subtract);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("result").GetProperty("difference").GetInt32());
    }

    [Fact(DisplayName = "HTTP: a notification is answered with 204 and no body")]
    public async Task Post_Notification_Returns204()
    {
        using var response = await PostAsync("""{"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "http"}}""");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "HTTP: a body that is not application/json is answered with 415")]
    public async Task Post_WrongContentType_Returns415()
    {
        using var response = await PostAsync(Subtract, "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact(DisplayName = "HTTP: a body larger than MaxRequestBodySize is answered with 413")]
    public async Task Post_TooLarge_Returns413()
    {
        var large = $$"""{"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "{{new string('x', 2048)}}"}, "id": 1}""";

        using var response = await PostAsync(large);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact(DisplayName = "HTTP: a body without Content-Length is cut off at MaxRequestBodySize and answered with 413")]
    public async Task Post_TooLargeWithoutContentLength_Returns413()
    {
        var large = $$"""{"jsonrpc": "2.0", "method": "Spec.Update", "params": {"text": "{{new string('x', 2048)}}"}, "id": 1}""";
        using var content = new StringContent(large, Encoding.UTF8, "application/json");
        content.Headers.ContentLength = null;

        using var response = await _client!.PostAsync("/api", content);

        Assert.Null(content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact(DisplayName = "HTTP: a Content-Length the body does not match reserves no more memory than the bytes that arrive")]
    public async Task HandleAsync_DeclaredLengthAboveBody_AllocatesByBytesReceived()
    {
        var handler = new JsonRpcHttpHandler(DispatcherFixture.Create(), new JsonRpcHttpOptions());
        var body = Encoding.UTF8.GetBytes(Subtract);
        await handler.HandleAsync(Context(body, declaredLength: body.Length));

        var before = GC.GetAllocatedBytesForCurrentThread();
        await handler.HandleAsync(Context(body, declaredLength: 4 * 1024 * 1024));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1024 * 1024, $"{allocated} bytes allocated for a {body.Length}-byte body.");

        static DefaultHttpContext Context(byte[] body, long declaredLength)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = declaredLength;
            context.Request.Body = new MemoryStream(body);
            context.Response.Body = new MemoryStream();
            return context;
        }
    }

    [Fact(DisplayName = "HTTP: StatusCodeSelector chooses the status of a single response")]
    public async Task Post_StatusCodeSelector_ChoosesStatus()
    {
        using var response = await PostAsync("""{"jsonrpc": "2.0", "method": "Nothing.Here", "id": 1}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "HTTP: a call is marked as HTTP even when a header claims otherwise")]
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

    [Fact(DisplayName = "HTTP: header names are looked up ignoring case, and the values of a repeated header are joined with commas")]
    public async Task Post_RepeatedHeader_JoinedAndCaseInsensitive()
    {
        string? token = null;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddJsonRpcServer(options =>
        {
            options.ObjectFactory = new TestObjectFactory();
            options.Filters.Add(new HeaderReadingFilter("x-token", value => token = value));
        });
        await using var app = builder.Build();
        app.MapJsonRpc("/api");
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api")
        {
            Content = new StringContent(Subtract, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Token", ["first", "second"]);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("first,second", token);
    }

    [Fact(DisplayName = "HTTP: the call sees a cancellation token that the client's disconnect cancels")]
    public async Task Post_Call_SeesRequestAbortedToken()
    {
        bool? cancellable = null;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddJsonRpcServer(options =>
        {
            options.ObjectFactory = new TestObjectFactory();
            options.Filters.Add(new TokenReadingFilter(token => cancellable = token.CanBeCanceled));
        });
        await using var app = builder.Build();
        app.MapJsonRpc("/api");
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.PostAsync("/api", new StringContent(Subtract, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(cancellable);
    }

    [Fact(DisplayName = "HTTP: AddJsonRpcServer adds to the options a framework registered first, after its filters")]
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

    [Fact(DisplayName = "HTTP: two service providers built from the same services each use their own object factory, and the shared options are not changed")]
    public async Task AddJsonRpcServer_TwoProviders_EachUsesItsOwnFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJsonRpcObjectFactory, TestObjectFactory>();
        services.AddJsonRpcServer();
        var first = services.BuildServiceProvider();
        await using var second = services.BuildServiceProvider();
        var firstDispatcher = first.GetRequiredService<JsonRpcDispatcher>();
        var secondDispatcher = second.GetRequiredService<JsonRpcDispatcher>();

        await first.DisposeAsync();
        var result = await new JsonRpcConnector(new InProcessTransport(secondDispatcher)).InvokeAsync<JsonElement>(
            "Spec.Subtract", DispatcherFixture.Element("""{"minuend": 3, "subtrahend": 1}"""));

        Assert.NotSame(firstDispatcher, secondDispatcher);
        Assert.Null(second.GetRequiredService<JsonRpcServerOptions>().ObjectFactory);
        Assert.Equal(2, result.GetProperty("difference").GetInt32());
    }

    [Fact(DisplayName = "HTTP: AddJsonRpcServer applies configureHttp to HTTP options a framework registered first")]
    public void AddJsonRpcServer_RegisteredHttpOptions_AreSharedAndExtended()
    {
        var services = new ServiceCollection();
        var frameworkHttp = new JsonRpcHttpOptions { StatusCodeSelector = _ => 299 };
        services.AddSingleton(frameworkHttp);

        services.AddJsonRpcServer(configureHttp: http => http.MaxRequestBodySize = 123);
        using var provider = services.BuildServiceProvider();

        var shared = provider.GetRequiredService<JsonRpcHttpOptions>();
        Assert.Same(frameworkHttp, shared);
        Assert.NotNull(shared.StatusCodeSelector);
        Assert.Equal(123, shared.MaxRequestBodySize);
    }

    private sealed class HeaderReadingFilter(string name, Action<string?> read) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            read(context.Transport.Headers.GetValueOrDefault(name));
            return next(context);
        }
    }

    private sealed class TokenReadingFilter(Action<CancellationToken> read) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            read(context.CancellationToken);
            return next(context);
        }
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
