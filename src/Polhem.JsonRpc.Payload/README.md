# Polhem.JsonRpc.Payload

An optional payload envelope for Polhem.JsonRpc: the `params` of a request and the `result` of a response travel as
plain JSON, as an encoded body (a codec, then gzip), or as an encrypted one (AES-256-CBC with HMAC-SHA256), with an
optional frame that guards against replay. It is the wire format of the Polhem framework and of its TypeScript client.
It supports trimming and Native AOT, so it runs on iOS, Android and WebAssembly.

```csharp
var options = new PayloadOptions
{
    RequireFrame = true,
    TypeResolver = new PayloadTypeRegistry().Register<AddRequest>().Register<AddResponse>(),
};
var payload = new PayloadProcessor(options);

var parameters = payload.Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: sessionKey, sequence: next);
var result = await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
var added = (AddResponse)payload.Unwrap(result, sessionKey)!;
```

The format, the codec, the key and the sequence number are chosen per call. A client accepts only the result types
registered with its `TypeResolver`. Under Native AOT, give `PayloadOptions.SerializerOptions` and the JSON codec options
whose `TypeInfoResolver` is a source-generated `JsonSerializerContext`.

Documentation and samples: https://github.com/polhem-dev/polhem-jsonrpc
