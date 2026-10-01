# Polhem.JsonRpc.AspNetCore

A JSON-RPC 2.0 endpoint for ASP.NET Core, on System.Text.Json.

```csharp
builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
builder.Services.AddJsonRpcServer();
app.MapJsonRpc("/api");

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
    public AddResponse Add(AddRequest request) => new(request.A + request.B);
}
```

A method name has the form `ProgId.Action`: the application's `IJsonRpcObjectFactory` creates the object for the
ProgId, and the action is a public instance method of it whose parameter is named `{Action}Request` and whose result
is named `{Action}Response`. Filters
(`IJsonRpcFilter`) run around every call, for authorization or for rewriting parameters and results. An MVC
controller can serve the endpoint too, through `JsonRpcHttpHandler.HandleAsync(HttpContext)`.

Documentation and samples: https://github.com/polhem-dev/polhem-jsonrpc
