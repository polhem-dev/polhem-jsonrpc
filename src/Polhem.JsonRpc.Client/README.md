# Polhem.JsonRpc.Client

A JSON-RPC 2.0 client on System.Text.Json: calls, notifications and batches over HTTP, with interceptors for
rewriting parameters and results. It supports trimming and Native AOT, so it runs on iOS, Android and WebAssembly.

```csharp
using var http = new HttpClient { BaseAddress = new Uri("https://example.com/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http));

var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
```

An error answer is thrown as `JsonRpcErrorException`. Under Native AOT, set `JsonRpcClientOptions.SerializerOptions`
to options whose `TypeInfoResolver` is a source-generated `JsonSerializerContext`.

Documentation and samples: https://github.com/polhem-dev/polhem-jsonrpc
