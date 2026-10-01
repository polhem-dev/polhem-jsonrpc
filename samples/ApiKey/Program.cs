using Polhem.JsonRpc;
using Polhem.JsonRpc.AspNetCore;
using Polhem.JsonRpc.Client;
using ApiKey;

// A demo value. A real deployment reads its keys from configuration or a secret store.
const string DemoKey = "demo-key";

// Server: a filter rejects calls without the right X-Api-Key header.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddJsonRpcServer(options =>
{
    options.AddTarget<Greeter>("greeter");
    options.Filters.Add(new ApiKeyFilter(DemoKey));
});
var app = builder.Build();
app.MapJsonRpc("/api");
await app.StartAsync();
var endpoint = new Uri($"{app.Urls.First()}/api");

// Client with the key: a handler on the HttpClient adds the header to every request.
using var withKey = new HttpClient(new ApiKeyHandler(DemoKey) { InnerHandler = new HttpClientHandler() }) { BaseAddress = endpoint };
var rpc = new JsonRpcConnector(new HttpTransport(withKey));
Console.WriteLine(await rpc.InvokeAsync<string>("greeter.hello", new HelloArgs("Ada")));

// Client without the key: the filter answers with an error.
using var withoutKey = new HttpClient { BaseAddress = endpoint };
try
{
    await new JsonRpcConnector(new HttpTransport(withoutKey)).InvokeAsync<string>("greeter.hello", new HelloArgs("Eve"));
}
catch (JsonRpcErrorException ex)
{
    Console.WriteLine($"Without the key: error {ex.Code}: {ex.Message}");
}

await app.StopAsync();
