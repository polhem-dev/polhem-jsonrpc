using Polhem.JsonRpc;
using Polhem.JsonRpc.AspNetCore;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;
using PayloadQuickStart.Server;
using QuickStart.Contracts;

var builder = WebApplication.CreateBuilder(args);

// The client must use the same options: the envelope does not name the compressor, the encryptor or the frame.
var payload = new PayloadOptions
{
    RequireFrame = true,
    TypeResolver = new PayloadTypeRegistry().Register<AddRequest>().Register<AddResponse>(),
};
var policy = new DemoKeyPolicy(DemoKeyPolicy.ReadKey(builder.Configuration["PayloadDemoKey"]));

builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
builder.Services.AddJsonRpcServer(options =>
{
    options.UsePayload(payload, policy);
    options.ExceptionMapper = (exception, _) => exception is ReplayRejectedException
        ? new JsonRpcError(-32005, "Replay rejected")
        : null;
});

var app = builder.Build();
app.MapJsonRpc("/api");
await app.RunAsync();
