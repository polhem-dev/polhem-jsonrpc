# PayloadQuickStart.Client

**English** | [繁體中文](README.zh-TW.md)

A console application that calls [PayloadQuickStart.Server](../PayloadQuickStart.Server/README.md) with encrypted
parameters, then sends a sequence number the server has already seen to show it refused. Start the server first, then,
with the same `PayloadDemoKey`:

```bash
dotnet run --project samples/PayloadQuickStart.Client
```

## The lines that matter

```csharp
var key = HMACSHA512.HashData(Convert.FromBase64String(base64Key), Encoding.UTF8.GetBytes(clientId));
var payload = new PayloadProcessor(new PayloadOptions { RequireFrame = true });
var rpc = new PayloadConnector(connector, payload, new PayloadConnectorOptions { KeyProvider = () => key });

var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
```

- The key is derived from the demo key and the client id the client sends in `X-Client-Id`, exactly as the server
  derives it, so a call cannot be replayed under another client id.
- `PayloadConnector` (package `Polhem.JsonRpc.Payload.Client`) is called like `JsonRpcConnector`. For each call it
  serializes, compresses, frames and encrypts the parameters, and reverses it for the result.
- The method name and the direction are covered by the HMAC, so an encrypted request cannot be sent to another method,
  nor a result sent back as a request.
- Calls are encrypted unless the options or the call name another format. Each call takes the next sequence number of
  the connector; a second connector that numbers from 1 again under the same client id is answered with an error
  (`-32005` in this sample). `PayloadConnectorOptions.SequenceGenerator` keeps the numbers going across connectors.
- The result is decoded into the type the caller asks for, and the `type` the server wrote is only checked against it,
  so no contract type is registered. `InvokeAsync<object>` resolves the type from that name instead, and accepts only
  types registered with `PayloadOptions.TypeResolver`.
- Both payload packages run with Native AOT. There, give the options a source-generated `JsonSerializerContext`.
