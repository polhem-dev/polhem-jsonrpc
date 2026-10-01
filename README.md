# Polhem.JsonRpc

**English** | [繁體中文](README.zh-TW.md)

[![Build CI](https://github.com/polhem-dev/polhem-jsonrpc/actions/workflows/build-ci.yml/badge.svg)](https://github.com/polhem-dev/polhem-jsonrpc/actions/workflows/build-ci.yml)

JSON-RPC 2.0 for .NET, built on System.Text.Json: a transport-independent server, an ASP.NET Core endpoint and a
client, published as separate NuGet packages so an application takes only the part it needs.

> **Status: under development.** No package has been released yet, and the API may still change.

## Packages

| Package | What it contains | Depends on |
|---------|------------------|------------|
| `Polhem.JsonRpc` | Message types, error codes and the transport abstraction shared by the server and the client | .NET only |
| `Polhem.JsonRpc.Server` | The dispatcher: method resolution, parameter binding, filters and an in-process transport | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.AspNetCore` | The ASP.NET Core endpoint (`MapJsonRpc`) and the HTTP request handler | `Polhem.JsonRpc.Server`, ASP.NET Core |
| `Polhem.JsonRpc.Client` | `JsonRpcConnector` with an HTTP transport, batches, notifications and request interceptors | `Polhem.JsonRpc` |

A server application references `Polhem.JsonRpc.AspNetCore` (or `Polhem.JsonRpc.Server` for a host that is not
ASP.NET Core). A client application references `Polhem.JsonRpc.Client` only.

The packages do not depend on the [Polhem framework](https://github.com/polhem-dev/polhem). Polhem uses them for its
API, and adds its own payload encryption, compression and authorization on top through the filters and interceptors.

## Quick start

Server (ASP.NET Core):

```csharp
builder.Services.AddJsonRpcServer(options => options.AddTarget<Calculator>("math"));
app.MapJsonRpc("/api");

public sealed class Calculator
{
    [JsonRpcMethod]
    public int Add(AddArgs args) => args.A + args.B;   // answers "math.add"
}
```

Client:

```csharp
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http));
var sum = await rpc.InvokeAsync<int>("math.add", new AddArgs(1, 2));
```

## Samples

| Sample | What it shows |
|--------|---------------|
| [QuickStart.Server](samples/QuickStart.Server/README.md) | A server on ASP.NET Core minimal APIs |
| [QuickStart.Client](samples/QuickStart.Client/README.md) | Calls, errors, notifications and batches from a console application |
| [ApiKey](samples/ApiKey/README.md) | A server filter that checks an API key, and the client handler that sends it |

Payload encryption and compression are not part of the packages. Use HTTPS and HTTP compression, or rewrite
parameters and results in a server filter and a client interceptor.

## Design

The reasons behind the package split and the main design choices are recorded in
[ADR-001](maintainers/adr/adr-001-package-split-and-design.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
