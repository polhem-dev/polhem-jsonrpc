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
    public AddResponse Add(AddRequest request) => new() { Sum = request.A + request.B };
}
```

Client:

```csharp
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http));
var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
```

## Samples

| Sample | What it shows |
|--------|---------------|
| [QuickStart.Server](samples/QuickStart.Server/README.md) | A server on ASP.NET Core minimal APIs |
| [QuickStart.Client](samples/QuickStart.Client/README.md) | Calls, errors, notifications and batches from a console application |
| [QuickStart.Contracts](samples/QuickStart.Contracts) | The request and response classes both sides share |

## Extension points

A **filter** runs around every call on the server. It can reject the call by throwing `JsonRpcErrorException`,
rewrite `context.Request.Params` before `next`, and rewrite `context.Result` after it:

```csharp
public sealed class ApiKeyFilter(string expectedKey) : IJsonRpcFilter
{
    public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
    {
        context.Transport.Headers.TryGetValue("X-Api-Key", out var key);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key ?? ""), Encoding.UTF8.GetBytes(expectedKey)))
        {
            throw new JsonRpcErrorException(-32001, "Unauthorized");
        }
        return next(context);
    }
}

builder.Services.AddJsonRpcServer(options => options.Filters.Add(new ApiKeyFilter(apiKey)));
```

On the client, HTTP headers belong to the `HttpClient`: add them with a `DelegatingHandler`. To rewrite parameters
and results, for example to encrypt them, add an `IJsonRpcClientInterceptor` to `JsonRpcClientOptions.Interceptors`.

```csharp
public sealed class ApiKeyHandler(string key) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Api-Key", key);
        return base.SendAsync(request, cancellationToken);
    }
}

using var http = new HttpClient(new ApiKeyHandler(apiKey) { InnerHandler = new HttpClientHandler() }) { BaseAddress = endpoint };
```

Payload encryption and compression are not part of the packages. Use HTTPS and HTTP compression, or rewrite
parameters and results in a filter and an interceptor as above.

## Design

The reasons behind the package split and the main design choices are recorded in
[ADR-001](maintainers/adr/adr-001-package-split-and-design.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
