# Security

**English** | [繁體中文](security.zh-TW.md)

What the Polhem.JsonRpc packages do against hostile callers and hostile networks, what they leave to the application,
and the limits that are known and accepted. Each section links the design record that explains it; those records name
the tests that pin each rule. Defaults are not repeated here: each option's documentation states its own.

Everything up to the method's own code runs on bytes the caller controls, often before the caller is authenticated.
The protections below are ordered by the point of the request at which they apply.

## Which methods can be called

The server registers nothing: it finds a method from its name, so what it refuses to find is the first line of defence
([ADR-001](../maintainers/adr/adr-001-package-split-and-design.md), decisions 2 and 3; the rules are in the
[README](../README.md#how-a-method-is-found)).

| Protection | Where |
|------------|-------|
| Only a public, non-generic instance method with exactly one parameter can be an action. Static methods, property accessors and methods declared by `object` are not. | `JsonRpcMethod.IsResolvableAction` |
| A name that matches more than one candidate is ambiguous and answered as not found; the server does not pick one. | `JsonRpcDispatcher` |
| By default a method is callable only when its types follow `{Action}Request` → `{Action}Response`, so a public helper that does not follow it is answered as if it did not exist. | `JsonRpcServerOptions.MethodPolicy` |
| The ProgId and the action are limited in characters and length before anything is looked up. | `JsonRpcMethodName` |
| The method policy runs before every filter, so a payload is decrypted only for a call that is allowed to happen. | ADR-001 decision 2, ADR-002 decision 4 |
| The transport kind (HTTP, in-process, custom) is set by the transport. Headers and `params` cannot claim another. | `JsonRpcTransportInfo`, ADR-001 decision 4 |

## Resource limits

| Protection | Where |
|------------|-------|
| The HTTP body is limited, with or without `Content-Length`, including chunked bodies. | `JsonRpcHttpOptions.MaxRequestBodySize` |
| A batch is limited in the number of requests it holds. | `JsonRpcServerOptions.MaxBatchSize` |
| Gzip output is limited per body. | `GzipPayloadCompressor` |
| All bodies of one message share one decompression budget, so a batch cannot multiply the gzip limit. A body that fails to decompress uses up what is left of it. | `PayloadOptions.MaxDecompressedBytesPerMessage` |
| The in-memory replay store forgets a scope that stays idle, and a sequence number cannot jump forward without bound. | `MemoryPayloadReplayStore` |

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
| A result must come back in the format of its request, `null` results included. A client that sent an encrypted request refuses a plain or encoded answer, so a result cannot be downgraded on the way back. | ADR-003 decision 6 |
| A method can require a lowest format, and lower ones are refused before a key is asked for. | `IPayloadServerPolicy.GetMinimumFormat` |
| The encryptor that does not encrypt is refused unless explicitly allowed for development. | `PayloadOptions.AllowNoEncryption` |

## Replayed calls

| Protection | Where |
|------------|-------|
| With frames required, every encoded and encrypted payload carries a timestamp and a sequence number; a timestamp outside the tolerance is refused. | `PayloadOptions.RequireFrame`, `PayloadOptions.FrameTimestampTolerance` |
| A method that needs it refuses a sequence number already seen in its scope. | `IPayloadServerPolicy.RequiresUniqueSequence`, `IPayloadServerPolicy.GetReplayScope`, `IPayloadReplayStore` |
| Such a method accepts encrypted calls only. A plain call has no frame, and the frame of an encoded call is not authenticated, so neither can prove its sequence number new. | ADR-002 decision 4 (amended for 1.1.0) |
| Whether frames are required is a server setting. A request cannot switch them off. | ADR-002 decision 7 |
| The store keeps a scope at least twice as long as the timestamp tolerance, so a frame old enough to have been forgotten is already refused by its timestamp. | `MemoryPayloadReplayStore` |

## Types, codecs and parameters

| Protection | Where |
|------------|-------|
| No type is loaded from a name received on the wire. Where the reader decides the type (the server from the method's parameter, a client through `UnwrapResult<T>`), the name is only compared. | ADR-002 decision 5 |
| Where a name must be resolved, it goes through an allow-list the application owns, which screens the whole assembly-qualified name, generic arguments included. | `PayloadTypeRegistry`, `PayloadOptions.TypeResolver` |
| The built-in `json` codec cannot be replaced by registering another codec under its name, and codec names are restricted in characters and length. | `PayloadOptions.RegisterCodec` |
| `params` must be a JSON object. Positional parameters and a missing value are answered with `-32602 Invalid params`. | ADR-001 decision 2 |

## Errors and information leaks

| Protection | Where |
|------------|-------|
| An unexpected exception is answered with `-32603 Internal error`, without its message, type or stack trace. Only `JsonRpcErrorException`, or an `ExceptionMapper` the application sets, chooses what the caller sees. | `JsonRpcDispatcher`, `JsonRpcServerOptions.ExceptionMapper` |
| Exception details are included only when `IncludeExceptionDetails` is set, which is meant for development. | `JsonRpcServerOptions.IncludeExceptionDetails` |
| A call the client aborted is cancelled, not reported as an internal error. | `JsonRpcDispatcher`, `JsonRpcHttpHandler` |

## Deployment

| Protection | Where |
|------------|-------|
| The dispatcher refuses to start when a loaded assembly was compiled against `Polhem.JsonRpc.Server` 1.0, whose transport kinds have other values. | `JsonRpcServerOptions.AllowCodeCompiledAgainst10` |
| The packages depend on nothing but .NET, and ASP.NET Core for the endpoint; the build refuses another package reference. | ADR-001 decision 1 |

## What the application is responsible for

- **HTTPS.** The payload packages protect `params` and `result`; the rest of the request, the method name included, is
  readable without HTTPS.
- **Authentication and authorization**, in the object factory, the method policy or a filter placed before the payload
  filter.
- **Keys**: how they are agreed, stored and rotated. `IPayloadServerPolicy.GetKeyAsync` returns the key of each call.
- **The replay scope** must come from a session authenticated before the payload filter, and cover every holder of the
  key. When several sessions share one key, a sequence number cannot tell them apart, so a unique sequence keeps out
  only callers without the key (`IPayloadServerPolicy.GetReplayScope`).
- **Leaving `IncludeExceptionDetails` and `AllowNoEncryption` off** in production.

## Known limits

- **Error responses are not authenticated.** A JSON-RPC error carries no payload, so an attacker on the network can
  replace a result with an error. It cannot forge the result of an encrypted call (ADR-003 decision 6).
- **The `format`, `type` and `codec` members and the request `id` are outside the HMAC** (ADR-003 decision 5).
  Changing `format` or `codec` makes the body fail to decode or authenticate. A client that opens a result without
  naming its type resolves it from `type`, among the types its `PayloadTypeRegistry` allows; name the type with
  `UnwrapResult<T>` to rule that out.
- **A result can be swapped with another result of the same method** on its way to the client, because the `id` is not
  bound. Binding it would make every client track it; it was left out.
- **The replay check runs after decompression.** Replaying a captured call costs the server the same work as the
  original call, once per replay; it is not amplified.
- **Encoded payloads are not authenticated.** Encoding is compression, not protection; only encrypted payloads are.
