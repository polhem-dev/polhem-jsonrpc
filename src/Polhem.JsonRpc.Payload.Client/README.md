# Polhem.JsonRpc.Payload.Client

The client half of Polhem.JsonRpc.Payload: `PayloadConnector` seals the parameters of each call into the payload
envelope (the codec, gzip, the replay-protection frame and the encryption) and opens its result, so an encrypted call is
written exactly like a plain one with `JsonRpcConnector`.
It supports trimming and Native AOT, so it runs on iOS, Android and WebAssembly.

```csharp
var payload = new PayloadProcessor(new PayloadOptions { RequireFrame = true });
var rpc = new PayloadConnector(connector, payload, new PayloadConnectorOptions { KeyProvider = () => sessionKey });

var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
var plain = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 3, B = 4 }, PayloadFormat.Plain, CancellationToken.None);
```

Calls are encrypted unless `PayloadConnectorOptions.Format` or the call names another format. The key is asked for on
every encrypted call, so a key that changes after a sign-in is used from the next call; an encrypted call without a key
fails, and is never sent in a lower format. Each call takes the next sequence number of the connector, or the number
`SequenceGenerator` returns when the numbers must outlive it. The result is opened into the type the call asks for;
`InvokeAsync<object>` resolves the type the envelope names, which must be registered with `PayloadOptions.TypeResolver`.

Documentation and samples: https://github.com/polhem-dev/polhem-jsonrpc
