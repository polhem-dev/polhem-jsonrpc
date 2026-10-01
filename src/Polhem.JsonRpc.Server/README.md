# Polhem.JsonRpc.Server

A transport-independent JSON-RPC 2.0 dispatcher on System.Text.Json: method resolution by convention, parameter
binding, filters, batches and notifications, and an in-process transport.

Most applications reference [Polhem.JsonRpc.AspNetCore](https://www.nuget.org/packages/Polhem.JsonRpc.AspNetCore)
instead, which hosts this dispatcher on an HTTP endpoint. Reference this package directly for another host.

```csharp
var options = new JsonRpcServerOptions().AddTarget<Calculator>("math");
var dispatcher = new JsonRpcDispatcher(options);

public sealed class Calculator
{
    [JsonRpcMethod]
    public int Add(AddArgs args) => args.A + args.B;   // answers "math.add"
}
```

The dispatcher resolves methods by reflection, so it does not support Native AOT.

Documentation and samples: https://github.com/polhem-dev/polhem-jsonrpc
