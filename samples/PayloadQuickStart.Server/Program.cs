using Polhem.JsonRpc;
using Polhem.JsonRpc.AspNetCore;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;
using PayloadQuickStart.Server;

var builder = WebApplication.CreateBuilder(args);

// The client must use the same options: the envelope does not name the compressor, the encryptor or the frame.
// No contract type is registered: a request decodes into the parameter type of the method it calls.
var payload = new PayloadOptions { RequireFrame = true };
var policy = new DemoKeyPolicy(DemoKeyPolicy.ReadKey(builder.Configuration["PayloadDemoKey"]));

builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
builder.Services.AddJsonRpcServer(options =>
{
    // Set before UsePayload, which keeps it answering first and maps an InvalidPayloadException to -32602 after it.
    options.ExceptionMapper = (exception, _) => exception is ReplayRejectedException
        ? new JsonRpcError(-32005, "Replay rejected")
        : null;
    options.UsePayload(payload, policy);
});

var app = builder.Build();
app.MapJsonRpc("/api");
await app.RunAsync();
