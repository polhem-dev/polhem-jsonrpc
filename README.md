# Polhem.JsonRpc

**English** | [繁體中文](README.zh-TW.md)

[![Build CI](https://github.com/polhem-dev/polhem-jsonrpc/actions/workflows/build-ci.yml/badge.svg)](https://github.com/polhem-dev/polhem-jsonrpc/actions/workflows/build-ci.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=alert_status)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=bugs)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=vulnerabilities)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=code_smells)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=coverage)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)

JSON-RPC 2.0 for .NET, built on System.Text.Json: a transport-independent server, an ASP.NET Core endpoint and a
client, published as separate NuGet packages so an application takes only the part it needs.

The packages are published on [nuget.org](https://www.nuget.org/packages?q=Polhem.JsonRpc); the changes of each release
are in the [CHANGELOG](CHANGELOG.md).

## Packages

| Package | What it contains | Depends on |
|---------|------------------|------------|
| `Polhem.JsonRpc` | Message types, error codes and the transport abstraction shared by the server and the client | .NET only |
| `Polhem.JsonRpc.Server` | The dispatcher: method resolution, parameter binding, filters and an in-process transport | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.AspNetCore` | The ASP.NET Core endpoint (`MapJsonRpc`) and the HTTP request handler | `Polhem.JsonRpc.Server`, ASP.NET Core |
| `Polhem.JsonRpc.Client` | `JsonRpcConnector` with an HTTP transport, batches, notifications and request interceptors | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.Payload` | Optional: the payload envelope around `params` and `result`, with codecs, gzip, AES-CBC-HMAC encryption and a replay-protection frame | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.Payload.Client` | Optional: `PayloadConnector`, which seals the parameters of each call and opens its result | `Polhem.JsonRpc.Payload`, `Polhem.JsonRpc.Client` |
| `Polhem.JsonRpc.Payload.Server` | Optional: the server filter that opens the envelope and checks the frame | `Polhem.JsonRpc.Payload`, `Polhem.JsonRpc.Server` |

A server application references `Polhem.JsonRpc.AspNetCore` (or `Polhem.JsonRpc.Server` for a host that is not
ASP.NET Core). A client application references `Polhem.JsonRpc.Client` only. An application that encrypts its payloads
adds `Polhem.JsonRpc.Payload.Client` on the client and `Polhem.JsonRpc.Payload.Server` on the server.

The packages do not depend on the [Polhem framework](https://github.com/polhem-dev/polhem). Polhem uses them for its
API, with the payload packages for its encryption and compression and its own filters for authorization.

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

## How a method is found

A method name has the form `ProgId.Action`, for example `Calculator.Add`. No method is registered: the server finds
it by these rules.

| Step | Rule |
|------|------|
| Name | `ProgId.Action`. A ProgId holds letters, digits, underscores and hyphens; an action letters, digits and underscores; each at most 64 characters. The action is matched case-sensitively; the ProgId is passed to the factory as written. |
| Object | The application's `IJsonRpcObjectFactory.CreateObject(progId, context)` creates the object the call runs on. `null` means the ProgId is unknown. After the call the object is passed to `ReleaseObjectAsync`. |
| Action | A public, non-generic instance method of that object with exactly one parameter. A name with more than one such method is ambiguous and not found. |
| Convention | The method is callable only when its parameter type is named `{Action}Request` and its return type, or the result of the `Task<T>` / `ValueTask<T>` it returns, `{Action}Response`: `AddResponse Add(AddRequest request)`. Any other public method is answered as if it did not exist. Replace the rule with `JsonRpcServerOptions.MethodPolicy`. |
| Parameters | `params` must be a JSON object, deserialized into the request (camelCase names by default). An absent `params` or an array is answered with `-32602 Invalid params`. |
| Result | The response object becomes `result`. |
| Id | A string, an integer or `null`, echoed as it came. A number with a fraction or an exponent (`1.0`, `1e2`), or one beyond a 64-bit integer, is answered with `-32600 Invalid Request` and a `null` id, because the specification discourages them; send integers or strings. |
| Errors | An unknown name, object or action is answered with `-32601 Method not found`. Throw `JsonRpcErrorException` to answer with your own code and message; any other exception is answered with `-32603 Internal error`, without its message. |

## Design choices and when not to use it

The fixed shape (a `ProgId.Action` name, one request class in, one response class out) is deliberate: it lets an API
change without breaking the clients already deployed. Adding a member to a request or a response class does not
break a client built before it. A member the client does not send binds its default on the server, and a result
member the client does not know is skipped. Old and new front ends keep calling the same method, with no `v1` and
`v2` endpoints to maintain. This holds with the default System.Text.Json settings; setting
`JsonUnmappedMemberHandling.Disallow` or `RespectRequiredConstructorParameters` turns it off.

The same choice rules out protocols whose method names and parameters are defined by someone else. The packages do
not implement the Language Server Protocol, the Model Context Protocol or an Ethereum node's API, which use names like
`textDocument/didOpen` or `eth_call` and positional parameters; [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc)
or the [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) fit those. Nor do they offer a full-duplex
connection in which the server calls the client: a call is an HTTP request and its response.

## Samples

| Sample | What it shows |
|--------|---------------|
| [QuickStart.Server](samples/QuickStart.Server/README.md) | A server on ASP.NET Core minimal APIs |
| [QuickStart.Client](samples/QuickStart.Client/README.md) | Calls, errors, notifications and batches from a console application |
| [QuickStart.Contracts](samples/QuickStart.Contracts) | The request and response classes both sides share |
| [PayloadQuickStart.Server](samples/PayloadQuickStart.Server/README.md) | Encrypted parameters and results with replay protection, on the server |
| [PayloadQuickStart.Client](samples/PayloadQuickStart.Client/README.md) | The same from a console application with `PayloadConnector`, including a repeated sequence number refused |

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
and results on every call, add an `IJsonRpcClientInterceptor` to `JsonRpcClientOptions.Interceptors`.

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

## Encrypted payloads

HTTPS protects the connection. When parameters and results must also be protected end to end, or a call must not be
replayable, add the optional payload packages: `Polhem.JsonRpc.Payload.Server` on the server and
`Polhem.JsonRpc.Payload.Client` on the client. They carry `params` and `result` in an envelope that is plain JSON, encoded
(a codec, then gzip) or encrypted (AES-256-CBC with HMAC-SHA256), with an optional frame that rejects a repeated
sequence number.

```csharp
// Server: the application supplies the key and the replay rules.
builder.Services.AddJsonRpcServer(options => options.UsePayload(payloadOptions, new MyPayloadPolicy()));

// Client: called like JsonRpcConnector; each call is sealed and its result opened.
var rpc = new PayloadConnector(connector, new PayloadProcessor(payloadOptions), new PayloadConnectorOptions { KeyProvider = () => sessionKey });
var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", request);
```

Both ends share the same `PayloadOptions` settings; how the key is agreed is up to the application. A client that
seals by hand calls `PayloadProcessor.WrapRequest` and `UnwrapResult` around `JsonRpcConnector` instead. The
[PayloadQuickStart](samples/PayloadQuickStart.Server/README.md) samples run it end to end, and
[ADR-002](maintainers/adr/adr-002-payload-packages.md) describes the format, which other clients can implement.

## Security

What the packages do against hostile callers and networks, what is left to the application, and the known limits
are in [docs/security.md](docs/security.md).

## Design

The reasons behind the package split and the main design choices are recorded in
[ADR-001](maintainers/adr/adr-001-package-split-and-design.md), and those of the payload packages in
[ADR-002](maintainers/adr/adr-002-payload-packages.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
