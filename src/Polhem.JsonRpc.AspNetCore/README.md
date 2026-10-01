# Polhem.JsonRpc.AspNetCore

A JSON-RPC 2.0 endpoint for ASP.NET Core, on System.Text.Json.

```csharp
builder.Services.AddJsonRpcServer(options => options.AddTarget<Calculator>("math"));
app.MapJsonRpc("/api");

public sealed class Calculator
{
    [JsonRpcMethod]
    public int Add(AddArgs args) => args.A + args.B;   // answers "math.add"
}
```

A method is callable when it is a public instance method with one parameter and carries `[JsonRpcMethod]`. Filters
(`IJsonRpcFilter`) run around every call, for authorization or for rewriting parameters and results. An MVC
controller can serve the endpoint too, through `JsonRpcHttpHandler.HandleAsync(HttpContext)`.

Documentation and samples: https://github.com/polhem-dev/polhem-jsonrpc
