namespace Polhem.JsonRpc.ReadmeSnippets.QuickStart
{
    public static class Snippets
    {
        public static async Task Client()
        {
            #region readme: samples/QuickStart.Client/README.md#1
            using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080/api") };
            var rpc = new JsonRpcConnector(new HttpTransport(http));

            var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });   // a call
            await rpc.NotifyAsync("Calculator.Log", new LogRequest { Message = "Hello" });        // a notification: no answer

            var batch = rpc.CreateBatch();                                         // a batch: one message
            var first = batch.Add<AddResponse>("Calculator.Add", new AddRequest { A = 2, B = 3 });
            await batch.SendAsync();
            Console.WriteLine((await first)!.Sum);
            #endregion
        }

        public static void Server(WebApplicationBuilder builder, WebApplication app)
        {
            #region readme: samples/QuickStart.Server/README.md#2
            builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
            builder.Services.AddJsonRpcServer();
            app.MapJsonRpc("/api");
            #endregion
        }
    }

    public sealed class AppObjectFactory : IJsonRpcObjectFactory
    {
        #region readme: samples/QuickStart.Server/README.md#1
        public object? CreateObject(string progId, JsonRpcRequestContext context) => progId switch
        {
            "Calculator" => new Calculator(),
            _ => null,
        };
        #endregion
    }

    public sealed class Calculator
    {
        #region readme: samples/QuickStart.Server/README.md#3
        public AddResponse Add(AddRequest request) => new() { Sum = request.A + request.B };
        #endregion
    }
}

namespace Polhem.JsonRpc.ReadmeSnippets.PayloadQuickStart
{
    public static class Snippets
    {
        public static async Task Client(string base64Key, string clientId, JsonRpcConnector rpc)
        {
            #region readme: samples/PayloadQuickStart.Client/README.md#1
            var key = HMACSHA512.HashData(Convert.FromBase64String(base64Key), Encoding.UTF8.GetBytes(clientId));
            var payload = new PayloadProcessor(new PayloadOptions { RequireFrame = true });

            var parameters = payload.WrapRequest("Calculator.Add", new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: key, sequence: 1);
            var result = await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
            var added = payload.UnwrapResult<AddResponse>("Calculator.Add", result, key)!;
            #endregion
        }

        public static void Server(WebApplicationBuilder builder, IPayloadServerPolicy policy)
        {
            #region readme: samples/PayloadQuickStart.Server/README.md#1
            var payload = new PayloadOptions { RequireFrame = true };
            builder.Services.AddJsonRpcServer(options =>
            {
                options.ExceptionMapper = (exception, _) => exception is ReplayRejectedException
                    ? new JsonRpcError(-32005, "Replay rejected")
                    : null;
                options.UsePayload(payload, policy);
            });
            #endregion
        }
    }

    public sealed class DemoKeyPolicy : IPayloadServerPolicy
    {
        // The README shows the members with comments where the bodies go; these bodies stand in for them.
        #region readme: samples/PayloadQuickStart.Server/README.md#2
        public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context) => ValueTask.FromResult<byte[]?>(null);
        public string? GetReplayScope(JsonRpcRequestContext context) => null;
        public bool RequiresUniqueSequence(JsonRpcRequestContext context) => true;
        public PayloadFormat GetMinimumFormat(JsonRpcRequestContext context) => PayloadFormat.Encrypted;
        #endregion
    }
}
