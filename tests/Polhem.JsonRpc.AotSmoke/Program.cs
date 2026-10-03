using System.Text.Json;
using Polhem.JsonRpc;
using Polhem.JsonRpc.AotSmoke;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;

const string AddMethod = "math.add";

// Exercises the client the way a mobile app would, with a source-generated serializer context, against a fake server.
var options = new JsonRpcClientOptions
{
    SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = SmokeJsonContext.Default },
};
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
    var wrapped = processor.Wrap(new PayloadArgs("smoke", 7), format, key: key, sequence: 1);
    if (processor.Unwrap(wrapped, key) is not PayloadArgs { Name: "smoke", Count: 7 }) { failures.Add($"the {format} payload did not round-trip"); }
}
var plain = processor.Unwrap(processor.Wrap(new PayloadArgs("plain", 1), PayloadFormat.Plain));
if (plain is not JsonElement { ValueKind: JsonValueKind.Object }) { failures.Add("the plain payload did not round-trip"); }

foreach (var failure in failures) { await Console.Error.WriteLineAsync($"FAIL: {failure}"); }
await Console.Out.WriteLineAsync(failures.Count == 0 ? "AOT smoke test passed." : "AOT smoke test failed.");
return failures.Count == 0 ? 0 : 1;
