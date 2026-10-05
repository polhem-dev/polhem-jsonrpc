namespace Polhem.JsonRpc.ReadmeSnippets.Root
{
    public static class Snippets
    {
        public static void Server(WebApplicationBuilder builder, WebApplication app)
        {
            #region readme: README.md#1
            builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
            builder.Services.AddJsonRpcServer();
            app.MapJsonRpc("/api");
            #endregion
        }

        public static async Task Client()
        {
            #region readme: README.md#2
            using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080/api") };
            var rpc = new JsonRpcConnector(new HttpTransport(http));
            var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
            #endregion
        }

        public static void Filter(WebApplicationBuilder builder, string apiKey)
        {
            #region readme: README.md#3
            builder.Services.AddJsonRpcServer(options => options.Filters.Add(new ApiKeyFilter(apiKey)));
            #endregion
        }

        public static void Handler(string apiKey, Uri endpoint)
        {
            #region readme: README.md#4
            using var http = new HttpClient(new ApiKeyHandler(apiKey) { InnerHandler = new HttpClientHandler() }) { BaseAddress = endpoint };
            #endregion
        }

        public static async Task Payload(WebApplicationBuilder builder, PayloadOptions payloadOptions,
            JsonRpcConnector connector, AddRequest request, byte[] sessionKey)
        {
            #region readme: README.md#5
            // Server: the application supplies the key and the replay rules.
            builder.Services.AddJsonRpcServer(options => options.UsePayload(payloadOptions, new MyPayloadPolicy()));

            // Client: called like JsonRpcConnector; each call is sealed and its result opened.
            var rpc = new PayloadConnector(connector, new PayloadProcessor(payloadOptions), new PayloadConnectorOptions { KeyProvider = () => sessionKey });
            var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", request);
            #endregion
        }
    }

    #region readme: README.md#1
    // "Calculator.Add": the ProgId "Calculator" names the object, the action "Add" the method.
    public sealed class AppObjectFactory : IJsonRpcObjectFactory
    {
        public object? CreateObject(string progId, JsonRpcRequestContext context) => progId switch
        {
            "Calculator" => new Calculator(),
            _ => null,
        };
    }

    public sealed class Calculator
    {
        // Callable because the parameter is AddRequest and the result AddResponse.
        public AddResponse Add(AddRequest request) => new() { Sum = request.A + request.B };
    }
    #endregion

    #region readme: README.md#3
    public sealed class ApiKeyFilter(string expectedKey) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            context.Transport.Headers.TryGetValue("X-Api-Key", out var key);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key ?? ""), Encoding.UTF8.GetBytes(expectedKey)))
            {
                throw new JsonRpcErrorException(-32001, "Unauthorized");
            }
            return next(context);
        }
    }
    #endregion

    #region readme: README.md#4
    public sealed class ApiKeyHandler(string key) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("X-Api-Key", key);
            return base.SendAsync(request, cancellationToken);
        }
    }
    #endregion
}
