# PayloadQuickStart.Client

**English** | [繁體中文](README.zh-TW.md)

A console application that calls [PayloadQuickStart.Server](../PayloadQuickStart.Server/README.md) with encrypted
parameters, then replays one call to show it refused. Start the server first, then, with the same `PayloadDemoKey`:

```bash
dotnet run --project samples/PayloadQuickStart.Client
```

## The lines that matter

```csharp
var key = HMACSHA512.HashData(Convert.FromBase64String(base64Key), Encoding.UTF8.GetBytes(clientId));
var payload = new PayloadProcessor(new PayloadOptions { RequireFrame = true });

var parameters = payload.Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: key, sequence: 1);
var result = await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
var added = payload.Unwrap<AddResponse>(result, key)!;
```

- The key is derived from the demo key and the client id the client sends in `X-Client-Id`, exactly as the server
  derives it, so a call cannot be replayed under another client id.
- `Wrap` serializes, compresses, frames and encrypts the request; `Unwrap` reverses it for the result. The connector
  sends and receives plain `JsonElement`s, so the core client package is unchanged.
- The format, the key and the sequence number are chosen per call. Each call takes the next sequence number; sending
  the same parameters again is answered with an error (`-32005` in this sample).
- `Unwrap<AddResponse>` decodes the result into the type the caller expects, and only checks the `type` the server
  wrote against it, so no contract type is registered. `Unwrap` without a type argument resolves the type from that
  name instead, and accepts only types registered with `PayloadOptions.TypeResolver`.
- `Polhem.JsonRpc.Payload` runs with Native AOT. There, give the options a source-generated `JsonSerializerContext`.
