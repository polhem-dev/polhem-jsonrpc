# Security

**English** | [繁體中文](security.zh-TW.md)

What the Polhem.JsonRpc packages do against hostile callers and hostile networks, what they leave to the application,
and the limits that are known and accepted. Where a design record explains a protection, the section or the row links
it. Defaults are not repeated here: each option's documentation states its own.

Everything up to the method's own code runs on bytes the caller controls, often before the caller is authenticated.

## Resource limits

| Protection | Where |
|------------|-------|
| The HTTP body is limited before it is parsed, with or without `Content-Length`, including chunked bodies. | `JsonRpcHttpOptions.MaxRequestBodySize` |
| A batch is limited in the number of requests it holds. | `JsonRpcServerOptions.MaxBatchSize` |
| Gzip output is limited per body. | `GzipPayloadCompressor` |
| All bodies of one message share one decompression budget, so a batch cannot multiply the gzip limit. A body that fails to decompress uses up what is left of it. A body sent uncompressed is not held to the gzip limit, but its length is taken from the budget. | `PayloadOptions.MaxDecompressedBytesPerMessage` |
| The in-memory replay store forgets a scope that stays idle, and a sequence number cannot jump forward without bound. | `MemoryPayloadReplayStore` |

## Which methods can be called

The server registers nothing: it finds a method from its name, so what it refuses to find is the first line of defence
([ADR-001](../maintainers/adr/adr-001-package-split-and-design.md), decisions 2 and 3; the rules are in the
[README](../README.md#how-a-method-is-found)).

| Protection | Where |
|------------|-------|
| Only a public, non-generic instance method with exactly one parameter can be an action. Static methods, property accessors and methods declared by `object` are not. | `JsonRpcMethod.IsResolvableAction` |
| A name that matches more than one candidate is ambiguous and answered as not found; the server does not pick one. | `JsonRpcDispatcher` |
| By default a method is callable only when its types follow `{Action}Request` → `{Action}Response`, so a public helper that does not follow it is answered as if it did not exist. A policy that throws makes the method not callable. | `JsonRpcServerOptions.MethodPolicy` |
| The ProgId and the action are limited in characters and length before anything is looked up. | `JsonRpcDispatcher` |
| The method policy runs before every filter, so a payload is decrypted only for a call that is allowed to happen. | ADR-001 decision 2, ADR-002 decision 4 |
| The built-in HTTP and in-process transports set the transport kind themselves; headers and `params` cannot claim another. | `JsonRpcTransportInfo`, ADR-001 decision 4 |

## Encrypted payloads

The optional payload packages carry `params` and `result` in an envelope that can be encrypted end to end
([ADR-002](../maintainers/adr/adr-002-payload-packages.md), decisions 2 and 6;
[ADR-003](../maintainers/adr/adr-003-bind-method-into-payload-hmac.md)).

| Protection | Where |
|------------|-------|
| AES-256-CBC with HMAC-SHA256, encrypt-then-MAC, with a 64-byte key. | `AesCbcHmacPayloadEncryptor` |
| The HMAC is compared in constant time and verified before anything is decrypted, so padding errors cannot be told apart from tampering. | `AesCbcHmacPayloadEncryptor` |
| A fresh random IV for every message, from `RandomNumberGenerator`. | `AesCbcHmacPayloadEncryptor` |
| Length fields are checked before the data is sliced, so a truncated message is refused rather than read out of range. | `AesCbcHmacPayloadEncryptor`, `PayloadFrame` |
| The HMAC covers the method name and the direction (request or response). A captured call cannot be sent to another method, and a response cannot be sent back as a request. | ADR-003 decisions 1 and 2 |
| A result comes back in the format of its request, `null` results included. A client that reads results with `UnwrapResult` or `UnwrapResult<T>` (or `OpenResult` given the request's format) refuses a result in another format, so an encrypted call cannot be answered with a plain result. The older `Unwrap` methods do not check this. | ADR-003 decision 6 |
| A method can require a lowest format, and lower ones are refused before a key is asked for. | `IPayloadServerPolicy.GetMinimumFormat` |
| An envelope in which `format`, `value`, `type` or `codec` appears twice is refused (`-32602` unless the application's `ExceptionMapper` maps it), so code that reads the format before the payload is opened sees the format it is opened in. | `PayloadEnvelope.Read`, `PayloadEnvelope.ReadFormat` |
| `NoPayloadEncryptor`, which does not encrypt, is refused unless allowed for development. | `PayloadOptions.AllowNoEncryption` |

## Replayed calls

Replay protection works only where both conditions hold: frames are required, and the policy gives the call a replay
scope. Without either, nothing is checked and a repeated call runs again (ADR-002 decision 4).

| Protection | Where |
|------------|-------|
| With frames required, every encoded and encrypted payload carries a timestamp and a sequence number. The server refuses a request whose timestamp is outside the tolerance. The timestamps of results are not checked. | `PayloadOptions.RequireFrame`, `PayloadOptions.FrameTimestampTolerance` |
| A method that requires unique sequence numbers refuses one already seen in the caller's scope. The built-in store also refuses one too far behind the highest seen, or too far ahead of it. | `IPayloadServerPolicy.RequiresUniqueSequence`, `IPayloadServerPolicy.GetReplayScope`, `MemoryPayloadReplayStore` |
| Such a method accepts encrypted calls only. A plain call has no frame, and the frame of an encoded call is not authenticated, so neither can prove its sequence number new. | ADR-002 decision 4 (amended for 1.1.0) |
| Whether frames are required is a server setting. A request cannot switch them off. | ADR-002 decision 7 |
| The store that `UsePayload` creates when it is given none keeps a scope twice as long as the timestamp tolerance, so a frame old enough to have been forgotten is already refused by its timestamp. A store you create yourself must be given such a lifetime. | `JsonRpcServerOptions.UsePayload`, `MemoryPayloadReplayStore` |

## Types, codecs and parameters

| Protection | Where |
|------------|-------|
| Where the reader decides the type (the server from the method's parameter, a client through `UnwrapResult<T>`), the `type` member is only compared with it; no type is loaded. | ADR-002 decision 5 |
| Where a name must be resolved, the built-in registry resolves only the names registered with it, compared exactly, and never loads a type by name. A resolver you supply instead must screen the whole assembly-qualified name, generic arguments included. | `PayloadTypeRegistry`, `PayloadOptions.TypeResolver` |
| The built-in `json` codec cannot be replaced by registering another codec under its name, and codec names, registered or received, are restricted in characters and length. | `PayloadOptions.RegisterCodec` |
| `params` must be a JSON object. Positional parameters and a missing value are answered with `-32602 Invalid params`. | ADR-001 decision 2 |

## Errors and information leaks

| Protection | Where |
|------------|-------|
| An unexpected exception is answered with `-32603 Internal error`, without its message, type or stack trace. Only `JsonRpcErrorException`, or an `ExceptionMapper` the application sets, chooses what the caller sees. | `JsonRpcDispatcher`, `JsonRpcServerOptions.ExceptionMapper` |
| Payload failures other than a malformed or too-low envelope (a failed HMAC, a replayed frame, a foreign type name, a missing key) all answer the same `-32603`, so the caller cannot tell which check failed, unless the application's `ExceptionMapper` maps them to errors of its own. | `JsonRpcServerOptions.UsePayload` |
| The exception's message is sent in `data` only when `IncludeExceptionDetails` is set. It can reveal which check failed, so it is for development only. | `JsonRpcServerOptions.IncludeExceptionDetails` |
| A call the client aborted is cancelled, not reported as an internal error. | `JsonRpcDispatcher`, `JsonRpcHttpHandler` |

## Deployment

| Protection | Where |
|------------|-------|
| The dispatcher refuses to start when an assembly loaded at that moment was compiled against `Polhem.JsonRpc.Server` 1.0, whose transport kinds have other values. An assembly loaded later is not seen. There is no exception to this check. | `JsonRpcDispatcher`, ADR-001 decision 4 |
| The packages depend on nothing but .NET, and ASP.NET Core for the endpoint. The repository's build fails on a package reference that would reach consumers. | ADR-001 decision 1 |

## What the application is responsible for

- **HTTPS.** The payload packages protect `params` and `result`; the rest of the request, the method name included, is
  readable without HTTPS.
- **Authentication and authorization**, in the object factory or in a filter placed before the payload filter. The
  method policy only decides which methods exist; it never sees the caller.
- **Keys**: how they are agreed, stored and rotated. `IPayloadServerPolicy.GetKeyAsync` returns the key of each call.
- **The replay scope** must come from a session authenticated before the payload filter, and cover every holder of the
  key. When several sessions share one key, a call captured in one session can be replayed in another by anybody who
  can present that session; give each session its own key and use the session as the scope
  (`IPayloadServerPolicy.GetReplayScope`).
- **More than one server process.** The in-memory replay store belongs to one process: another server, or the same one
  after a restart, accepts a call it has not seen within the timestamp tolerance. Supply a shared
  `IPayloadReplayStore` when that matters.
- **A custom transport** sets `JsonRpcTransportInfo.Kind` itself; its default is HTTP.
- **Header values.** Repeated headers of one name are joined with commas (`JsonRpcTransportInfo.Headers`); code that
  expects a single value, such as a token, refuses one that contains a comma.
- **Response size on the client.** The HTTP transport reads a whole response; bound it with
  `HttpClient.MaxResponseContentBufferSize`.
- **Leaving `IncludeExceptionDetails` and `AllowNoEncryption` off** in production.

## Known limits

- **Error responses are not authenticated.** A JSON-RPC error carries no payload, so an attacker on the network can
  replace a result with an error. It cannot forge the result of an encrypted call (ADR-003 decisions 5 and 6).
- **The `format`, `type` and `codec` members and the request `id` are outside the HMAC** (ADR-003 decision 5).
  With the built-in codecs and encryptor, changing `format` or `codec` makes the body fail to decode or authenticate;
  a sealed null result has no body to decode, so its `codec` is never resolved. A client that opens a result without
  naming its type resolves it from `type`, among the types its `PayloadTypeRegistry` allows; name the type with
  `UnwrapResult<T>` to rule that out.
- **A result can be swapped with, or replayed as, another result of the same method** on its way to the client,
  because the `id` is not bound. Binding it would make every client track it; it was left out.
- **The replay check runs after decryption and decompression.** A replayed call costs the server the key lookup,
  decryption and decompression of the original body, once per replay; the method does not run.
- **Encoded payloads are not authenticated.** Encoding is compression, not protection; only encrypted payloads are.
