# Changelog

**English** | [繁體中文](CHANGELOG.zh-TW.md)

Notable changes to Polhem.JsonRpc, Polhem.JsonRpc.Server, Polhem.JsonRpc.AspNetCore and Polhem.JsonRpc.Client. The
format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `Polhem.JsonRpc`: request, response, error and id types, the standard error codes, `JsonRpcSerializer` (reads and
  writes messages without reflection) and `IJsonRpcTransport`.
- `Polhem.JsonRpc.Server`: `JsonRpcDispatcher` with batches and notifications. A method name `ProgId.Action` calls
  the action on the object the application's `IJsonRpcObjectFactory` creates for the ProgId; an action is callable
  when its parameter is named `{Action}Request` and its result `{Action}Response`. Filters, exception mapping and
  `InProcessTransport`.
- `Polhem.JsonRpc.AspNetCore`: `AddJsonRpcServer`, `MapJsonRpc` and `JsonRpcHttpHandler`.
- `Polhem.JsonRpc.Client`: `JsonRpcConnector` with calls, notifications and batches, `HttpTransport` and
  interceptors. It supports trimming and Native AOT.
