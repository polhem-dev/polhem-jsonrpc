using System.Text.Json;
using Polhem.JsonRpc;
using Polhem.JsonRpc.AotSmoke;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Client;

const string AddMethod = "math.add";

// Exercises the client the way a mobile app would, with a source-generated serializer context, against a fake server.
var counter = new CountingInterceptor();
var options = new JsonRpcClientOptions
{
    SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = SmokeJsonContext.Default },
};
options.Interceptors.Add(counter);
using var http = new HttpClient(new FakeServerHandler()) { BaseAddress = new Uri("https://smoke.test/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http), options);

var failures = new List<string>();

var sum = await rpc.InvokeAsync<int>(AddMethod, new AddArgs(2, 3));
if (sum != 5) { failures.Add($"math.add returned {sum}"); }

try
{
    await rpc.InvokeAsync<int>("math.fail", new AddArgs(0, 0));
    failures.Add("math.fail did not throw");
}
catch (JsonRpcErrorException ex) when (ex.Code == -32001)
{
    // Expected.
}

var batch = rpc.CreateBatch();
var first = batch.Add<int>(AddMethod, new AddArgs(1, 1));
var second = batch.Add<int>(AddMethod, new AddArgs(2, 2));
batch.AddNotification(AddMethod, new AddArgs(0, 0));
await batch.SendAsync();
if (await first != 2 || await second != 4) { failures.Add("the batch returned the wrong results"); }

// A result read as a JsonElement, or not read at all, needs no JsonElement in the application's context.
await rpc.InvokeAsync(AddMethod, new AddArgs(1, 2), CancellationToken.None);
var element = await rpc.InvokeAsync<JsonElement>(AddMethod, new AddArgs(3, 4));
if (element.ValueKind != JsonValueKind.Number || element.GetInt32() != 7) { failures.Add("the JsonElement result was wrong"); }

await rpc.NotifyAsync(AddMethod, new AddArgs(0, 0));

// A batch the server refuses as a whole fails each call with the server's error.
var refused = rpc.CreateBatch();
var refusedCall = refused.Add<int>("math.refuse", new AddArgs(0, 0));
await refused.SendAsync();
try
{
    await refusedCall;
    failures.Add("the refused batch call did not throw");
}
catch (JsonRpcErrorException ex) when (ex.Code == -32600)
{
    // Expected.
}

// Every request above passed through the interceptor: four calls, the three requests of the first batch, a
// notification and the call of the refused batch.
if (counter.Requests != 9) { failures.Add($"the interceptor saw {counter.Requests} requests"); }

// The payload envelope round-trips in every format with source-generated metadata only.
var payloadJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = SmokeJsonContext.Default };
var payloadOptions = new PayloadOptions
{
    SerializerOptions = payloadJson,
    JsonCodec = new JsonPayloadCodec(payloadJson),
    RequireFrame = true,
    TypeResolver = new PayloadTypeRegistry().Register<PayloadArgs>(),
};
var processor = new PayloadProcessor(payloadOptions);
var key = new byte[AesCbcHmacPayloadEncryptor.KeySize];
foreach (var format in new[] { PayloadFormat.Encoded, PayloadFormat.Encrypted })
{
    var wrapped = processor.SealResponse("payload.echo", new PayloadArgs("smoke", 7), format, key: key).ToElement();
    if (processor.UnwrapResult("payload.echo", format, wrapped, key) is not PayloadArgs { Name: "smoke", Count: 7 }) { failures.Add($"the {format} payload did not round-trip"); }
}
var plain = processor.Unwrap(processor.Wrap(new PayloadArgs("plain", 1), PayloadFormat.Plain));

// The documented payload flow: the connector sends what Wrap returns, a JsonElement the application's context does not
// list, as a single call and in a batch.
var wrappedArgs = processor.Wrap(new PayloadArgs("sent", 3), PayloadFormat.Encoded);
var echoed = await rpc.InvokeAsync<JsonElement>("payload.echo", wrappedArgs);
if (processor.Unwrap(echoed) is not PayloadArgs { Name: "sent", Count: 3 }) { failures.Add("the wrapped call did not round-trip"); }
var payloadBatch = rpc.CreateBatch();
var batchedEcho = payloadBatch.Add<JsonElement>("payload.echo", wrappedArgs);
await payloadBatch.SendAsync();
if (processor.Unwrap(await batchedEcho) is not PayloadArgs { Name: "sent", Count: 3 }) { failures.Add("the wrapped batch call did not round-trip"); }
if (plain is not JsonElement { ValueKind: JsonValueKind.Object }) { failures.Add("the plain payload did not round-trip"); }

// The payload connector seals and opens around the same connector, into the type the caller names and into the type
// the envelope names.
var payloadRpc = new PayloadConnector(rpc, processor, new PayloadConnectorOptions { Format = PayloadFormat.Encoded });
if (await payloadRpc.InvokeAsync<PayloadArgs>("payload.echo", new PayloadArgs("connector", 4)) is not { Name: "connector", Count: 4 }) { failures.Add("the payload connector call did not round-trip"); }
if (await payloadRpc.InvokeAsync<object>("payload.echo", new PayloadArgs("named", 5)) is not PayloadArgs { Name: "named", Count: 5 }) { failures.Add("the payload connector call into the named type did not round-trip"); }

// A reader that names the type needs nothing registered, in either an encoded or a plain envelope.
var unregistered = new PayloadProcessor(new PayloadOptions { SerializerOptions = payloadJson, JsonCodec = new JsonPayloadCodec(payloadJson) });
foreach (var format in new[] { PayloadFormat.Plain, PayloadFormat.Encrypted })
{
    var wrapped = unregistered.SealResponse("payload.echo", new PayloadArgs("typed", 2), format, key: key).ToElement();
    if (unregistered.UnwrapResult<PayloadArgs>("payload.echo", format, wrapped, key) is not { Name: "typed", Count: 2 }) { failures.Add($"the typed {format} payload did not round-trip"); }
}

foreach (var failure in failures) { await Console.Error.WriteLineAsync($"FAIL: {failure}"); }
await Console.Out.WriteLineAsync(failures.Count == 0 ? "AOT smoke test passed." : "AOT smoke test failed.");
return failures.Count == 0 ? 0 : 1;
