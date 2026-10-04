# ADR-003: Bind the method and the direction into the payload HMAC

## Status

**Accepted (2026-10-05)**. Amends [ADR-002](adr-002-payload-packages.md), decision 2. A breaking change of the wire
format, released in 1.1.0 together with the Polhem framework and polhem-connector-js.

## Context

The HMAC of an encrypted payload (ADR-002, decision 2) covered the frame and the body, and nothing outside them. The
JSON-RPC `method`, the envelope's `type` and `codec`, and which way the payload travels were not covered. Two attacks
followed, both measured in the seventh health check with one key per session, the session as the replay scope and
frames required:

- **Another method.** The server remembered sequence numbers only for methods that require unique ones. A captured
  encrypted call to a method that does not (`Form.GetData`) was sent to one that does (`Form.Delete`), with `method`
  and `type` rewritten, and accepted once. In the Polhem framework both take `{ RowId }`, so a read became a delete.
- **A response sent back.** The server sealed every result with the call's key and sequence number 0. A result
  relabelled as the parameters of a call was accepted.

Without frames there is no replay protection at all, but the first attack still turns a captured read into a write: the
attacker cannot write an encrypted request, and rewriting `method` gives them one.

## Decision

1. **The HMAC also covers a binding.** It is computed over the IV length, the IV, the ciphertext length and the
   ciphertext, as before, followed by:
   - one byte for the direction: `0x01` for the parameters of a call, `0x02` for its result;
   - the JSON-RPC `method` in UTF-8, exactly as the request names it. A result is bound to the method of the request
     it answers.

   The binding is not written to the payload; the reader supplies it from the call it is reading. The bytes on the
   wire keep the layout of ADR-002 (`PayloadWireVectorTests.Wrap_EncryptedFramed_WritesPolhemLayout` recomputes the
   HMAC without this package's code).
2. **Every encrypted payload is bound, with or without frames.** `PayloadProcessor.WrapRequest`, `SealResponse`,
   `UnwrapResult` and the overloads of `OpenRequest` and `OpenResult` that take the method write and check the binding;
   the methods that take none refuse an encrypted payload (`PayloadBindingTests`). `PayloadFilter` opens a request with
   the request's method and seals its result as a response to it
   (`PayloadServerTests.Call_CapturedParametersSentToAnotherMethod_IsRefused`,
   `PayloadServerTests.Call_ResultSentBackAsParameters_IsRefused`).
3. **No compatibility mode.** A payload written before this decision fails its HMAC and is refused
   (`PayloadWireVectorTests.Open_PolhemEncryptedFramedEnvelope_RefusedWithoutBinding`). A reader that accepted both
   would be downgraded by any attacker who sends the old form.
4. **The encryptor authenticates associated data.** `IPayloadEncryptor` gains `Encrypt` and `Decrypt` overloads that
   take it. Their default implementations throw, so an encryptor written against 1.0 refuses to run rather than leave
   the binding unchecked (`PayloadBindingTests.WrapRequest_EncryptorWithoutAssociatedData_Throws`).
5. **What stays outside the HMAC:** the envelope's `format`, `type` and `codec`, and the request `id`. `type` is decided
   by the server from the method, which is bound; a different `codec` or `format` makes the body fail to decode or
   authenticate; binding the `id` would tie a result to one call, which nothing needs once the method and direction are
   bound.

## Consequences

- The Polhem framework moves its client to `WrapRequest` and `UnwrapResult`, and polhem-connector-js adds the binding
  to its AES-CBC-HMAC. All three are released together; an old client cannot talk to a new server, nor the reverse.
- A captured encrypted call opens only as the call it was written for. Replaying it unchanged is still a matter for the
  frame and the replay scope (ADR-002, decision 4).
- Alternatives considered:
  - **Remember every encrypted call's sequence number and refuse sequence number 0 for methods that require unique
    ones.** No wire change, and it stops both attacks while frames are on, but not the first one without frames.
  - **Put the method in the frame.** Inside the ciphertext, so covered, but only when frames are required.
