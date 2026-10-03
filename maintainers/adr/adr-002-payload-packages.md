# ADR-002: Optional payload packages for encoding, encryption and replay protection

## Status

**Accepted (2026-10-03)**

Amends decision 5 of [ADR-001](adr-001-package-split-and-design.md): compression and encryption are still not part of
the four core packages, but they are offered as two optional packages.

## Context

ADR-001 left compression, encryption and replay protection to the application, to be written as server filters and
client interceptors. The Polhem framework did exactly that when it moved onto these packages: it wraps every `params`
and `result` in its own payload envelope, and the code that reads and writes that envelope sits in Polhem.

That code is not specific to Polhem. The envelope, its compression and encryption, and the frame that guards against
replay are a protocol of their own, and an application that wants an encrypted JSON-RPC API needs all of it. Two
clients already speak it: the Polhem framework's .NET client and the TypeScript client `polhem-connector-js`. Writing it
again for each application, from the specification of the bytes, is the error-prone part.

What is specific to Polhem is the policy around it: where the session key comes from, which methods must be encrypted,
which methods reject a repeated sequence number, which types a `type` member may name, and MessagePack with Polhem's own
formatters as the default codec.

The payload code is extracted as it is, not designed anew. Polhem's wire format is the specification, and the extraction
must leave it byte for byte unchanged.

## Decision

### 1. Two packages

| Package | Contents | References |
|---------|----------|------------|
| `Polhem.JsonRpc.Payload` | The envelope and its JSON reading and writing; the frame; the codec, compressor and encryptor interfaces and their built-in implementations; the wrap and unwrap API; `PayloadOptions` | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.Payload.Server` | The server filter, the hand-over of the decoded value to the parameter binder, the replay window and its store | `Polhem.JsonRpc.Payload`, `Polhem.JsonRpc.Server` |

The split follows the core packages. A mobile client references `Polhem.JsonRpc.Payload` only and is not handed the
reflection-based dispatcher of `Polhem.JsonRpc.Server`. `Polhem.JsonRpc.Payload` is marked `IsAotCompatible` and is
covered by `tests/Polhem.JsonRpc.AotSmoke`; `Polhem.JsonRpc.Payload.Server` makes no AOT claim, as `.Server` makes none
(ADR-001 decision 7).

Neither package is needed for plain JSON-RPC. The core packages do not reference them.

### 2. The wire format is Polhem's, unchanged

- **The envelope** is a JSON object with `format`, `value`, `type` and `codec`. `format` is a number: 0 for plain, 1 for
  encoded, 2 for encrypted. A plain `value` is ordinary JSON. An encoded or encrypted `value` is a Base64 string of the
  body bytes, and `type` names the type the body decodes into. `codec` names the codec of the body and is left out when
  blank, so that a payload on the default codec is written exactly as before codecs could be negotiated.
- **Encoding** runs serialize, compress, frame, encrypt, in that order; decoding runs the reverse. No step is skipped or
  reordered.
- **The frame** is 17 bytes put in front of the encoded body: a version byte (1), the timestamp in Unix milliseconds and
  a sequence number, both as big-endian 64-bit integers. It is put in front before encryption, so the HMAC covers it,
  and it never appears in the JSON, where it could be rewritten.
- **Encryption** is AES-256-CBC with PKCS#7 padding and HMAC-SHA256 (encrypt-then-MAC). The 64-byte key is the AES key
  followed by the HMAC key. The ciphertext is laid out as the IV length (32-bit little-endian), a random 16-byte IV, the
  ciphertext length (32-bit little-endian), the ciphertext, and the HMAC of everything before it. The HMAC is compared in
  constant time before anything is decrypted.
- **Compression** is gzip, with a limit on the decompressed size, because a small compressed body can expand without
  bound.

Each of these is pinned by vectors that the Polhem implementation produced before the extraction
(`tests/Polhem.JsonRpc.UnitTests/Payload/PayloadWireVectorTests.cs`); a vector that has to change means the wire changed.

### 3. The client wraps and unwraps; there is no interceptor

The format of a call (plain, encoded or encrypted), its codec, its key and its sequence number are chosen per call.
`IJsonRpcClientInterceptor` receives a request with no per-call state, so an interceptor cannot know them without a new
extension point in `Polhem.JsonRpc.Client`.

Instead the package offers a wrap and unwrap API: the caller turns its value into the `params` element before
`JsonRpcConnector.InvokeAsync` and turns the `result` element back into a value after it, passing the per-call choices
explicitly. This is the place the Polhem client already does it. `Polhem.JsonRpc.Client` does not change, and does not
reference the payload package.

A ready-made connector that does both can be added later on top of the same API without changing the core.

### 4. The server does it in a filter, and the application supplies the policy

The payload filter runs after the method policy and any access filter the application places before it (ADR-001
decision 2, step 5), so a body is only decrypted for a call that is allowed to happen. It reads the envelope, decrypts,
removes and checks the frame, decodes the body, and leaves the decoded value in the request items for the parameter
binder. After the call it writes the result back in the format and codec the request used.

What the filter cannot decide is asked of the application through interfaces:

- **the key** of the call, and the scope a sequence number is unique in (Polhem: the access token and its session key);
- **whether the method rejects a repeated sequence number** (Polhem: `ApiReplayProtection.UniqueSequence`);
- **the type to decode into.** The server decides it, from what the method takes; the `type` member of a request is only
  checked against it. A type is never loaded from a name the client sent.

The package exposes how it reads `format`, so an access filter that runs before it can apply its own rules (Polhem: a
method that requires encryption rejects a plain call) without parsing the envelope again.

### 5. The `type` member goes through an allow-list the application owns

A client unwrapping a response resolves the body's type from `type`. Resolving a type from a name received over the
network is the classic deserialization gadget, so the package resolves names only through an interface the application
implements, and its built-in implementation accepts only types registered with it by name. An allow-list screens the
whole assembly-qualified name, generic arguments included, not the part before the first comma.

Polhem implements the interface with its existing allow-list and assembly-qualified names, so the names on the wire do
not change.

### 6. The package carries its own AES-CBC-HMAC

The encryption is part of the wire format, and the package is where that format is defined, so the package carries its
own implementation of it. It does not reference Polhem; Polhem keeps the implementation it uses for its own settings
files. Polhem's test suite encrypts with each implementation and decrypts with the other, so a change that makes them
disagree turns its build red.

### 7. Configuration is an options instance

`PayloadOptions` holds the codecs and the default codec, the compressor, the encryptor, whether a frame is required,
the timestamp tolerance, and whether the unencrypted encryptor may be used (off by default; meant for development). On
the server it is registered in the service collection together with the filter; on the client the caller holds an
instance.

- **The default codec is a setting.** The package defaults to JSON. Polhem sets MessagePack, its compatibility constant
  for every client that predates codec negotiation.
- **Codec names** are lower-case letters, digits and hyphens, at most 32 characters. A built-in codec cannot be
  replaced by registering another under its name.
- **Whether a frame is required** is a deployment setting, never read from a request: a request able to declare "I carry
  no frame" would be a downgrade attack.

## Consequences

- An application can encrypt its JSON-RPC payloads with two package references and an implementation of the key
  interface, and talk to clients that already speak the format.
- The format is now defined in this repository. A change to it reaches the Polhem framework and `polhem-connector-js`,
  and is a breaking change of the protocol, not only of the package.
- With encryption on, `params` and `result` are no longer ordinary JSON-RPC parameters. A client in another language has
  to implement the envelope, the frame and the encryption described in decision 2.
- Polhem removes its own copy of this code and its static configuration (`ApiServiceOptions`) in its release 1.2.0; that
  decision and the migration of its hosts are recorded in Polhem's ADR-049.
- Two implementations of AES-CBC-HMAC exist, one here and one in Polhem. Only the cross test in Polhem keeps them in
  agreement.

## Alternatives considered

- **One package.** Rejected: a client would reference the reflection-based dispatcher.
- **A separate client package (`Polhem.JsonRpc.Payload.Client`).** Not needed while the client half is the wrap and
  unwrap API, which has no dependency beyond `Polhem.JsonRpc`.
- **An interceptor on the client.** Rejected for decision 3: it needs per-call state the core client does not carry,
  and adding it would change `Polhem.JsonRpc.Client` before 1.0.
- **Interfaces only, no built-in encryption.** Rejected: every application would write its own cryptography, which is
  what an optional package is meant to spare it.
- **Keep the payload code in Polhem.** No new packages. Rejected: an application that wants an encrypted API without
  Polhem would have to reimplement a format two clients already speak.
