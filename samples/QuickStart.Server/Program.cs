using Polhem.JsonRpc.AspNetCore;
using QuickStart.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddJsonRpcServer(options => options.AddTarget<Calculator>("math"));

var app = builder.Build();
app.MapJsonRpc("/api");
app.Run();
