# Changelog

**English** | [繁體中文](CHANGELOG.zh-TW.md)

Notable changes to the Polhem.JsonRpc packages. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `PayloadOptions.TimeProvider`: the clock frames are stamped with and their timestamps checked against. It defaults to
  the system clock, and `PayloadFilter` passes it to the in-memory replay store it creates.
- `JsonRpcSerializer.ReadRequests(utf8Json, maxBatchSize)`: refuses a batch larger than the limit before reading any of
  its entries.
- `IPayloadServerPolicy.GetMinimumFormat`: the lowest format a method accepts. A call in a lower format is answered
  `-32602` before a key is asked for or its body is read. The default accepts every format, as before.
- `GzipPayloadCompressor.MaxCompressionRatio` and a constructor that sets it.

### Changed

- `JsonRpcDispatcher` resolves the methods of an object type once, the first time the type is used, and asks the method
  policy about each of them then, rather than one method name at a time.
- `PayloadParameterBinder` answers a plain value that is not an object, or that System.Text.Json cannot create, with
  `-32602 Invalid params` instead of `-32603 Internal error`, as the dispatcher's own binder does.
- `UsePayload` answers `InvalidPayloadException` (a malformed envelope) with `-32602 Invalid params` instead of
  `-32603 Internal error`. An `ExceptionMapper` set before `UsePayload` still answers first; every other payload failure
  stays `-32603`, so the answer does not say which check failed.
- `GzipPayloadCompressor` also refuses a body that decompresses to more than 100 times its compressed size
  (`DefaultMaxCompressionRatio`), unless it decompresses to 1 MiB or less. An encoded body is decompressed without a
  key, so the size limit alone let a request make the server decompress far more than it sent.
- `JsonRpcConnector` reads its `JsonRpcClientOptions` once, when it is created, interceptors included. Before, only
  `SerializerOptions` was read then, and the rest on every call, so adding an interceptor while calls ran could throw.
- `JsonRpcConnector` refuses a response that carries the id of another request, and an `IdGenerator` that returns no
  id. A batch refuses a call whose id is null or already in the batch.
- The PayloadQuickStart sample derives each client's key from the demo key and its `X-Client-Id`, so a call replayed
  under another client id no longer starts over in a fresh replay scope. It also requires encrypted calls.

### Fixed

- A method name the caller made up no longer stays in the dispatcher's method cache, so names that resolve to nothing
  cannot grow its memory. The lookup runs before any filter, so before authentication.
- A batch larger than `MaxBatchSize` is refused before a request object is built for each of its entries.
- An exception from `IJsonRpcObjectFactory.ReleaseObjectAsync` or from `ExceptionMapper` no longer escapes the
  dispatcher: the call is answered `-32603` and the rest of the batch is answered as usual. A call that already failed
  keeps its own error.
- A batch whose caller cancels stops before its next call instead of running every remaining call.
- `MemoryPayloadReplayStore` could accept a replayed sequence number when a call reached an idle scope while a sweep
  was removing it.
- When the server answers a whole batch with one error (too large, for instance), each call of the batch now fails with
  that error, instead of "The server did not answer this call of the batch".
- A batch whose `IdGenerator` repeated an id left a task that never completed.
- The non-generic `InvokeAsync` and `InvokeAsync<JsonElement>` no longer need `JsonElement` in a source-generated
  serializer context, so they work under Native AOT; the non-generic one does not read the result at all.
- A trimmed application, such as an iOS head, no longer gets warning IL2072 from `PayloadParameterBinder` or
  `DefaultParameterBinder`. `Polhem.JsonRpc.Server`, `Polhem.JsonRpc.AspNetCore` and `Polhem.JsonRpc.Payload.Server`
  now run the trim analyzer, though they still do not claim to support trimming.

## [1.0.0] - 2026-10-03

### Removed

- The options for an older wire format: `JsonRpcServerOptions.InternalErrorCode`, `JsonRpcRequestContext.ResponseMembers`,
  `JsonRpcResponse.AdditionalMembers`, and `JsonRpcWriteOptions` with `OmitNullId` (and the `options` parameters that
  took it, and `JsonRpcHttpOptions.WriteOptions`). Responses are always written as the specification defines them, and
  unknown response members are ignored when read ([ADR-001](maintainers/adr/adr-001-package-split-and-design.md),
  decision 6).

### Added

- `Polhem.JsonRpc.Payload` (optional): the payload envelope around `params` and `result` (`format`, `value`, `type`,
  `codec`), the JSON codec, gzip with a limit on the decompressed size, AES-256-CBC with HMAC-SHA256, the 17-byte
  replay frame, and `PayloadProcessor`, which wraps parameters and unwraps results per call. A reader that decides the
  type itself (a server from the method's parameter, a client through `Unwrap<T>`) only checks the envelope's `type`
  against it, so contract types are registered only for a client that resolves results by name. It supports trimming
  and Native AOT. The wire format is the one the Polhem framework and polhem-connector-js already speak
  ([ADR-002](maintainers/adr/adr-002-payload-packages.md)).
- `Polhem.JsonRpc.Payload.Server` (optional): `PayloadFilter`, which opens the envelope of a request, checks the frame's
  timestamp and sequence number, and answers in the same format and codec; `IPayloadServerPolicy` for the key, the
  replay scope and the type to decode into; `MemoryPayloadReplayStore`; and `JsonRpcServerOptions.UsePayload`.
- The PayloadQuickStart samples: an encrypted call and a replayed call refused, end to end.

## [0.1.0] - 2026-10-02

### Added

- `Polhem.JsonRpc`: request, response, error and id types, the standard error codes, `JsonRpcSerializer` (reads and
  writes messages without reflection) and `IJsonRpcTransport`.
- `Polhem.JsonRpc.Server`: `JsonRpcDispatcher` with batches and notifications. A method name `ProgId.Action` calls
  the action on the object the application's `IJsonRpcObjectFactory` creates for the ProgId; an action is callable
  when its parameter is named `{Action}Request` and its result `{Action}Response`. Filters, exception mapping and
  `InProcessTransport`. A method's return value is serialized after the filters have run, so a filter can write the
  result in its own form from `ReturnValue`.
- `Polhem.JsonRpc.AspNetCore`: `AddJsonRpcServer`, `MapJsonRpc` and `JsonRpcHttpHandler`. `AddJsonRpcServer` builds
  on server and HTTP options registered before it, so a framework can set them up and an application add to them.
- Options for a host that keeps an older wire format: `InternalErrorCode`, `ResponseMembers`, `OmitNullId` and
  `StatusCodeSelector`.
- `Polhem.JsonRpc.Client`: `JsonRpcConnector` with calls, notifications and batches, `HttpTransport` and
  interceptors. It supports trimming and Native AOT.

[Unreleased]: https://github.com/polhem-dev/polhem-jsonrpc/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/polhem-dev/polhem-jsonrpc/compare/v0.1.0...v1.0.0
[0.1.0]: https://github.com/polhem-dev/polhem-jsonrpc/releases/tag/v0.1.0
