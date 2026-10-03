# Changelog

**English** | [繁體中文](CHANGELOG.zh-TW.md)

Notable changes to the Polhem.JsonRpc packages. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `PayloadOptions.TimeProvider`: the clock frames are stamped with and their timestamps checked against. It defaults to
  the system clock, and `PayloadFilter` passes it to the in-memory replay store it creates.

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
