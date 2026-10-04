namespace Polhem.JsonRpc.ReadmeSnippets.AspNetCore
{
    public static class Snippets
    {
        public static void Server(WebApplicationBuilder builder, WebApplication app)
        {
            #region readme: src/Polhem.JsonRpc.AspNetCore/README.md#1
            builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
            builder.Services.AddJsonRpcServer();
            app.MapJsonRpc("/api");
            #endregion
        }
    }

    #region readme: src/Polhem.JsonRpc.AspNetCore/README.md#1
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
}

namespace Polhem.JsonRpc.ReadmeSnippets.Client
{
    public static class Snippets
    {
        public static async Task Call()
        {
            #region readme: src/Polhem.JsonRpc.Client/README.md#1
            using var http = new HttpClient { BaseAddress = new Uri("https://example.com/api") };
            var rpc = new JsonRpcConnector(new HttpTransport(http));

            var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
            #endregion
        }
    }
}

namespace Polhem.JsonRpc.ReadmeSnippets.Server
{
    public static class Snippets
    {
        public static void Create()
        {
            #region readme: src/Polhem.JsonRpc.Server/README.md#1
            var dispatcher = new JsonRpcDispatcher(new JsonRpcServerOptions { ObjectFactory = new AppObjectFactory() });
            #endregion
        }
    }

    #region readme: src/Polhem.JsonRpc.Server/README.md#1
    // "Calculator.Add": the factory creates the object for the ProgId "Calculator", and Add is called on it.
    public sealed class AppObjectFactory : IJsonRpcObjectFactory
    {
        public object? CreateObject(string progId, JsonRpcRequestContext context) =>
            progId == "Calculator" ? new Calculator() : null;
    }

    public sealed class Calculator
    {
        public AddResponse Add(AddRequest request) => new() { Sum = request.A + request.B };   // {Action}Request -> {Action}Response
    }
    #endregion
}

namespace Polhem.JsonRpc.ReadmeSnippets.Payload
{
    public static class Snippets
    {
        public static async Task Call(JsonRpcConnector rpc, byte[] sessionKey, long next)
        {
            #region readme: src/Polhem.JsonRpc.Payload/README.md#1
            var payload = new PayloadProcessor(new PayloadOptions { RequireFrame = true });

            var parameters = payload.WrapRequest("Calculator.Add", new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: sessionKey, sequence: next);
            var result = await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
            var added = payload.UnwrapResult<AddResponse>("Calculator.Add", result, sessionKey)!;
            #endregion
        }
    }
}

namespace Polhem.JsonRpc.ReadmeSnippets.PayloadServer
{
    public static class Snippets
    {
        public static void Server(WebApplicationBuilder builder, PayloadOptions payloadOptions)
        {
            #region readme: src/Polhem.JsonRpc.Payload.Server/README.md#1
            builder.Services.AddJsonRpcServer(options =>
            {
                options.ObjectFactory = new MyObjectFactory();
                options.Filters.Add(new MyAccessFilter());          // runs before the body is decrypted
                options.UsePayload(payloadOptions, new MyPayloadPolicy());
            });
            #endregion
        }
    }
}
