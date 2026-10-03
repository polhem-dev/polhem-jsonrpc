# PayloadQuickStart.Server

**English** | [繁體中文](README.zh-TW.md)

A JSON-RPC server whose parameters and results travel encrypted, with replay protection: the optional
`Polhem.JsonRpc.Payload.Server` package on top of [QuickStart.Server](../QuickStart.Server/README.md). It answers
`Calculator.Add` at `http://localhost:5081/api`.

Both ends need the same 64-byte demo key, from which each client's own key is derived. Generate one and export it in
each terminal:

```bash
export PayloadDemoKey=$(openssl rand -base64 64 | tr -d '\n')
dotnet run --project samples/PayloadQuickStart.Server
```

## The lines that matter

`Program.cs` adds the payload filter to the server with the options both ends share:

```csharp
var payload = new PayloadOptions { RequireFrame = true };
builder.Services.AddJsonRpcServer(options =>
{
    options.ExceptionMapper = (exception, _) => exception is ReplayRejectedException
        ? new JsonRpcError(-32005, "Replay rejected")
        : null;
    options.UsePayload(payload, policy);
});
```

The exception mapper answers a replayed call with `-32005`, a code this sample chooses; without it a replay is answered
`-32603`, like every other payload failure except a malformed envelope or a call below the minimum format, which
`UsePayload` answers with `-32602`. The
mapper is set before `UsePayload`, so it keeps answering first.

The filter asks the application what only it knows (`DemoKeyPolicy.cs`): the key of a call, the scope a sequence
number must be unique in, whether the method rejects a repeated one, and the lowest format it accepts.

```csharp
public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context) => /* derived from the demo key and X-Client-Id */;
public string? GetReplayScope(JsonRpcRequestContext context) => /* the X-Client-Id header */;
public bool RequiresUniqueSequence(JsonRpcRequestContext context) => true;
public PayloadFormat GetMinimumFormat(JsonRpcRequestContext context) => PayloadFormat.Encrypted;
```

- Only an encrypted call carries a frame the caller cannot forge: a plain call has none, and anybody can write the frame
  of an encoded one. Requiring `Encrypted` is what makes the replay check impossible to skip. A call in a lower format is
  answered `-32602 Invalid params` before its body is read.

- The method itself (`Calculator.cs`) is the same as without the envelope: the filter opens the request before the
  call and seals the result after it, in the format and codec the request used.
- The server decides the type a request decodes into, the parameter type of the method, and the `type` the client
  writes is only checked against it. So no contract type is registered: the methods the server exposes are the
  allow-list.
- The replay scope must be something the key authenticates, or a captured call replayed under a new scope is accepted
  again. The `X-Client-Id` header is not authenticated by itself, so the key is derived from it
  (`HMACSHA512(demoKey, clientId)`): a call replayed under another client id fails its HMAC.
- Deriving keys from one demo key keeps the sample short. A real application gives each session its own key, agreed at
  sign-in, and uses the session as the replay scope. How keys are agreed is outside the payload packages.
- `MemoryPayloadReplayStore` remembers sequence numbers in the process. Several server instances need a shared
  `IPayloadReplayStore`.

Call it with [PayloadQuickStart.Client](../PayloadQuickStart.Client/README.md). The format is described in
[ADR-002](../../maintainers/adr/adr-002-payload-packages.md).
