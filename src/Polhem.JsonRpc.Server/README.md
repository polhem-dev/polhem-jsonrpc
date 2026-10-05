# Polhem.JsonRpc.Server

A transport-independent JSON-RPC 2.0 dispatcher on System.Text.Json: `ProgId.Action` methods on objects the
application creates, a naming convention for callable methods, parameter
binding, filters, batches and notifications, and an in-process transport.

Most applications reference [Polhem.JsonRpc.AspNetCore](https://www.nuget.org/packages/Polhem.JsonRpc.AspNetCore)
instead, which hosts this dispatcher on an HTTP endpoint. Reference this package directly for another host.

```csharp
var dispatcher = new JsonRpcDispatcher(new JsonRpcServerOptions { ObjectFactory = new AppObjectFactory() });

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
```

The dispatcher resolves methods by reflection, so it does not support Native AOT. Under Native AOT its constructor
throws `InvalidOperationException`, because it cannot check which loaded code was compiled against
`Polhem.JsonRpc.Server` 1.0.

Documentation and samples: https://github.com/polhem-dev/polhem-jsonrpc
