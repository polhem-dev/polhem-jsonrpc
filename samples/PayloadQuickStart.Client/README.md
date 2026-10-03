# PayloadQuickStart.Client

**English** | [繁體中文](README.zh-TW.md)

A console application that calls [PayloadQuickStart.Server](../PayloadQuickStart.Server/README.md) with encrypted
parameters, then replays one call to show it refused. Start the server first, then, with the same `PayloadDemoKey`:

```bash
dotnet run --project samples/PayloadQuickStart.Client
```

## The lines that matter

```csharp
var payload = new PayloadProcessor(new PayloadOptions
{
    RequireFrame = true,
    TypeResolver = new PayloadTypeRegistry().Register<AddRequest>().Register<AddResponse>(),
});

var parameters = payload.Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: key, sequence: 1);
var result = await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
var added = (AddResponse)payload.Unwrap(result, key)!;
```

- `Wrap` serializes, compresses, frames and encrypts the request; `Unwrap` reverses it for the result. The connector
  sends and receives plain `JsonElement`s, so the core client package is unchanged.
- The format, the key and the sequence number are chosen per call. Each call takes the next sequence number; sending
  the same parameters again is answered with an error (`-32005` in this sample).
- A result is decoded only into a type registered with the `TypeResolver`.
- `Polhem.JsonRpc.Payload` runs with Native AOT. There, give the options a source-generated `JsonSerializerContext`.
