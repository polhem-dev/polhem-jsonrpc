# ADR-001: Package split and core design of Polhem.JsonRpc

## Status

**Accepted (2026-10-01)**

## Context

The Polhem framework (`polhem-dev/polhem`) uses JSON-RPC 2.0 as its API model, but its implementation is part of the
framework itself: the request and response types and the dispatcher live in `Polhem.Api.Core`, the HTTP endpoint in
`Polhem.Api.AspNetCore` and the client in `Polhem.Api.Client`. Only the outer shell (`jsonrpc`, `method`, `id`,
`error`) is generic. The rest is tied to Polhem: the payload envelope and its codec negotiation, compression and
encryption, replay protection, sessions, access control attributes and the `progId.action` method naming. An
application that wants JSON-RPC without the rest of Polhem cannot use any of it.

Measured against the JSON-RPC 2.0 specification, the Polhem implementation also lacks batches and notifications,
accepts only string ids, and never checks the `jsonrpc` member.

### Existing .NET libraries (surveyed 2026-09-30)

No library combined all of: System.Text.Json without a Newtonsoft.Json dependency, an HTTP request/response server, a
matching client, trimming and Native AOT support where it matters, and active maintenance.

| Library | Why it was not adopted |
|---------|------------------------|
| StreamJsonRpc (Microsoft) | Built for full-duplex streams, not HTTP request/response; one package with a hard dependency on Newtonsoft.Json |
| ModelContextProtocol SDK | Clean System.Text.Json and AOT support, but its JSON-RPC layer serves the MCP protocol only |
| Tochka.JsonRpc | Closest in scope (Common, Server, Client), but tied to ASP.NET Core MVC and not AOT-compatible |
| AustinHarris.JsonRpc 2.0 | Rewritten on 2026-09-28: transport-independent and fast, but server only, and AOT support is still on its roadmap |
| EdjCase.JsonRpc, OmniSharp.Extensions.JsonRpc, CXuesong.JsonRpc, Anemonis.JsonRpc | Inactive, unlisted, or a client that still depends on Newtonsoft.Json |

Most of these libraries split into several packages: a common package, a server, a client, and a separate ASP.NET
Core package (Tochka, Odachi, the MCP SDK, AustinHarris). StreamJsonRpc is the exception, and its single package is the
reason every consumer receives Newtonsoft.Json and MessagePack.

## Decision

### 1. Four packages, one direction of dependency

| Package | Contents | References |
|---------|----------|------------|
| `Polhem.JsonRpc` | Request, response, error and id types; error codes; `JsonRpcSerializer`, which reads and writes messages; `IJsonRpcTransport` | .NET only |
| `Polhem.JsonRpc.Server` | The dispatcher, method resolution, parameter binding, filters, error mapping, the in-process transport | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.AspNetCore` | `JsonRpcHttpHandler` and `MapJsonRpc` | `Polhem.JsonRpc.Server`, ASP.NET Core |
| `Polhem.JsonRpc.Client` | `JsonRpcConnector`, the HTTP transport, interceptors, error-to-exception mapping | `Polhem.JsonRpc` |

- The shared types exist once, in `Polhem.JsonRpc`. If the server and the client each carried a copy, an application
  that references both (an in-process call, for example) would see two types with the same name.
- `IJsonRpcTransport` lives in the shared package, so the server can provide an in-process transport without
  referencing the client.
- ASP.NET Core is a package of its own, so the server can run in a host that is not ASP.NET Core. The HTTP handling is
  not trivial (media type, size limit, cancellation, no response body for notifications, the HTTP status of each error
  kind), and every application that hosts the server over HTTP would otherwise write it again.
- The packages reference nothing but .NET, and ASP.NET Core for `Polhem.JsonRpc.AspNetCore`. There is no MessagePack
  and no Newtonsoft.Json. The packages do not reference Polhem; Polhem references them.

### 2. Methods are resolved by convention

The default resolver follows the convention Polhem already uses:

- A method name `target.action` asks an `IJsonRpcTargetFactory` for the object named `target`, then calls its method
  `action`.
- The method must be a public, non-generic instance method with exactly one parameter. A method that returns a
  `Task` is awaited; a synchronous method is called as it is.
- `params` must be an object, and it is deserialized into that one parameter. An array (positional parameters) is
  answered with `-32602 Invalid params`, which the specification allows a server to do.
- Target and method names are compared case-insensitively, so the JSON-RPC convention `math.add` finds the C#
  method `Add`. Two methods whose names differ only in case make the name ambiguous, and it is not resolved.
- The resolver is replaceable, for a host that wants method names without a dot.

### 3. A method is callable only when it is marked

Every public one-parameter method of an object the factory returns would otherwise be reachable from the network,
including one added later for internal use. The default method policy therefore admits only methods marked with
`[JsonRpcMethod]`: a method left unmarked cannot be called, which is the safe way to fail
(`DispatcherTests.DispatchAsync_UnmarkedMethod_ReturnsMethodNotFound`). The policy is replaceable;
Polhem replaces it with one that reads its own `[ApiAccessControl]` attribute.

### 4. The transport identity is set by the transport, never by the request

The request context carries headers, the client address, the cancellation token, the service provider, a bag for
per-request items, and the identity of the transport that delivered the request. The in-process transport marks its
requests as in-process; the HTTP handler marks every request as HTTP. The identity is never read from a header, from
`params` or from any other part of the request, because a host may grant in-process calls more than remote ones
(`HttpHandlerTests.Post_HeaderClaimsInProcess_StillMarkedHttp`).

### 5. Compression and encryption are not part of the packages

Filters on the server and interceptors on the client can read and rewrite the raw JSON of `params` and `result` before
binding and after serialization. A host that encrypts or compresses its payloads does it there. A host that does not
needs nothing beyond HTTP: response compression on the server, automatic decompression in `HttpClient`, and HTTPS.

### 6. Defaults follow the specification; deviations are explicit options

The internal error code is `-32603`, and a response carries only `jsonrpc`, `result` or `error`, and `id`. Two options
let a host keep an older wire format: a different internal error code, and additional response members.

### 7. AOT is promised for the shared package and the client

`Polhem.JsonRpc` and `Polhem.JsonRpc.Client` are meant to run on iOS, Android and WebAssembly. The envelope is read
and written by hand with `JsonDocument` and `Utf8JsonWriter`, which needs no reflection; parameters and results go
through the `JsonSerializerOptions` the application supplies, which under Native AOT carry a source-generated
`JsonSerializerContext`. Both projects are marked `IsAotCompatible`, so the trim and AOT analyzers fail their build
on an incompatible call, and the `aot` job of `build-ci.yml` publishes `tests/Polhem.JsonRpc.AotSmoke` with Native
AOT and runs it. `Polhem.JsonRpc.Server` resolves
methods and binds parameters by reflection (decision 2), so it does not claim AOT compatibility, and the APIs that
reflect are annotated with `RequiresUnreferencedCode` and `RequiresDynamicCode`.

### 8. Target framework

`net10.0`, the current long-term support release. .NET 8 reaches the end of support in November 2026 and .NET 9 has
already reached it, so an older target would add a test matrix for a short time.

## Consequences

- An application can use JSON-RPC 2.0 on ASP.NET Core with a single package reference, without taking Polhem.
- Polhem's own JSON-RPC types move to these packages. What stays in Polhem is what is Polhem's: the payload envelope,
  codec negotiation, compression, encryption, replay protection, sessions and access control, implemented as filters,
  interceptors, a target factory and a method policy.
- A change to the shape of a request, a response, an error or an error code reaches the Polhem framework and the
  TypeScript client `polhem-connector-js`, which speak the same wire format.
- The server cannot be published with Native AOT while it resolves methods by reflection. A source generator for method
  registration can be added later without breaking the reflection path.

## Alternatives considered

- **Adopt an existing library.** Rejected for the reasons in the table above: each one misses at least one of
  System.Text.Json without Newtonsoft.Json, HTTP request/response, a matching client, and AOT support for the client.
- **One package.** Rejected: every client, including mobile ones, would take ASP.NET Core and the reflection-based
  dispatcher.
- **Three packages, with the HTTP handling in Polhem's own ASP.NET Core package.** Rejected: an application that does not
  use Polhem would have no HTTP endpoint and would write the handling itself.
- **Explicit method registration (`AddMethod<TParams, TResult>`) as the default.** It would make the server AOT-safe,
  but it is not the convention Polhem's business objects follow, and Polhem is the first consumer.
