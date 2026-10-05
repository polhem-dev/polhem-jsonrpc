# ADR-004: A client package that seals and opens every call

## Status

**Accepted (2026-10-05)**. Amends [ADR-002](adr-002-payload-packages.md), decisions 1 and 3, and the alternative it
rejected as "not needed": a separate client package.

## Context

ADR-002 decision 3 left the client half of the payload to the caller: wrap the parameters with
`PayloadProcessor.WrapRequest`, call `JsonRpcConnector.InvokeAsync<JsonElement>`, and unwrap the result with
`UnwrapResult`, passing the method, the format, the key and the sequence number at each step. An encrypted call takes
three statements where a plain one takes one, the method is written three times, and the format twice. Each repetition
is a place to get it wrong: the result must be opened for the same method and in the format the call was actually sent
in, and a sequence number must never repeat within its replay scope.

The Polhem framework's `ApiConnector` does all of this itself, and every other client would write it again.
ADR-002 expected this and left room for a ready-made connector on top of the same API.

## Decision

1. **A third payload package, `Polhem.JsonRpc.Payload.Client`.** It references `Polhem.JsonRpc.Payload` and
   `Polhem.JsonRpc.Client`, and mirrors `Polhem.JsonRpc.Payload.Server`. Like the other client packages it is marked
   `IsAotCompatible` and is covered by `tests/Polhem.JsonRpc.AotSmoke`.
2. **`PayloadConnector` wraps a `JsonRpcConnector`; it does not extend it.** `JsonRpcConnector` stays sealed and gains no
   extension point. The connector has the same `InvokeAsync` signatures, so an encrypted call is written like a plain
   one, plus overloads that take the format of a single call.
3. **What differs per call is supplied once.** `PayloadConnectorOptions` holds the default format (encrypted), the codec,
   a function that returns the key, and an optional function that returns the next sequence number. The key is asked for
   on every encrypted call, so a key that changes after a sign-in is picked up. Without a sequence function the
   connector numbers its own calls from 1.
4. **No silent downgrade.** An encrypted call without a key fails before anything is sent. Lowering the format is a
   decision for the application, made by passing the format to the call.
5. **The result is opened for the same method and format.** `InvokeAsync<object>` opens it into the type the envelope
   names, through `PayloadOptions.TypeResolver`, as the untyped `UnwrapResult` does. The overloads that return nothing do
   not open the result, as `JsonRpcConnector` does not read it.

## Consequences

- An application that encrypts its calls references `Polhem.JsonRpc.Payload.Client` on the client instead of
  `Polhem.JsonRpc.Payload`, which it still gets transitively.
- The core packages do not change, and the wire format does not change: the connector writes exactly what
  `WrapRequest` writes.
- The wrap and unwrap API stays public for a client that seals by hand.
- A connector numbers its calls from 1. Two connectors under the same replay scope, or one created again after a
  restart, repeat numbers unless the application supplies the sequence function, as the Polhem framework does from its
  session.
- Batches and notifications are not sealed by the connector. They can still be sent with the wrap API through
  `JsonRpcConnector`.

## Alternatives considered

- **Let `JsonRpcConnector` seal and open itself.** Rejected: `Polhem.JsonRpc.Client` would reference the payload
  package, and every plain client, mobile ones included, would carry the cryptography (ADR-002 decision 1).
- **Unseal `JsonRpcConnector` and add protected hooks, so that a connector such as Polhem's `ApiConnector` can inherit
  it.** Rejected: the hooks are the per-call extension point ADR-002 decision 3 declined, and a subclass would expose
  `InvokeAsync`, `NotifyAsync` and `CreateBatch` publicly, letting a caller step around whatever the subclass enforces
  (in Polhem, the time-zone conversion and the guards around it).
- **Put the connector in `Polhem.JsonRpc.Payload`.** Rejected: the package would reference the client, and
  `Polhem.JsonRpc.Payload.Server` would carry the client to every server.
- **A client interceptor.** Still rejected for the reason in ADR-002: an interceptor has no per-call state.
