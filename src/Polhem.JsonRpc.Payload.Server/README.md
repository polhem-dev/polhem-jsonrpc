# Polhem.JsonRpc.Payload.Server

The server half of Polhem.JsonRpc.Payload: a dispatcher filter that opens the payload envelope of each request
(decryption, decompression, the codec), checks its replay-protection frame, and answers in the same format and codec.

```csharp
builder.Services.AddJsonRpcServer(options =>
{
    options.ObjectFactory = new MyObjectFactory();
    options.Filters.Add(new MyAccessFilter());          // runs before the body is decrypted
    options.UsePayload(payloadOptions, new MyPayloadPolicy());
});
```

`IPayloadServerPolicy` is where the application answers what the package cannot know: the key of an encrypted call,
the scope a sequence number must be unique in, which methods reject a repeated one, the lowest format a method accepts
(only `Encrypted` makes the replay checks impossible to skip), and the type to decode into. The
type is always decided by the server; the `type` member of a request is only checked against it, so the contract types
are not registered with `PayloadOptions.TypeResolver`.
Where sequence numbers are checked (`PayloadOptions.RequireFrame` on and a replay scope for the caller), a method that
rejects a repeated one also refuses plain and encoded calls, because only an encrypted frame proves its number new.
`UsePayload` answers `-32602 Invalid params` for a malformed envelope, a call below the method's minimum format, and a
plain or encoded call to such a method; every other payload failure is `-32603`, unless
the application's own `ExceptionMapper`, set before `UsePayload`, answers it first.
Decompression is bounded per body by the compressor and per message, the call alone or a whole batch, by
`PayloadOptions.MaxDecompressedBytesPerMessage`, because an encoded body is decompressed before any key is asked for.
`MemoryPayloadReplayStore` remembers sequence numbers in the process; a deployment with several instances needs a
shared `IPayloadReplayStore`.

Documentation and samples: https://github.com/polhem-dev/polhem-jsonrpc
