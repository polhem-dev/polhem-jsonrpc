using Polhem.JsonRpc.AspNetCore;
using Polhem.JsonRpc.Server;
using QuickStart.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
builder.Services.AddJsonRpcServer();

var app = builder.Build();
app.MapJsonRpc("/api");
app.Run();
