using System.Text.Json;
using Polhem.JsonRpc;
using Polhem.JsonRpc.AotSmoke;
using Polhem.JsonRpc.Client;

// Exercises the client the way a mobile app would, with a source-generated serializer context, against a fake server.
var options = new JsonRpcClientOptions
{
    SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = SmokeJsonContext.Default },
};
using var http = new HttpClient(new FakeServerHandler()) { BaseAddress = new Uri("http://smoke.test/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http), options);

var failures = new List<string>();

var sum = await rpc.InvokeAsync<int>("math.add", new AddArgs(2, 3));
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
var first = batch.Add<int>("math.add", new AddArgs(1, 1));
var second = batch.Add<int>("math.add", new AddArgs(2, 2));
batch.AddNotification("math.add", new AddArgs(0, 0));
await batch.SendAsync();
if (await first != 2 || await second != 4) { failures.Add("the batch returned the wrong results"); }

foreach (var failure in failures) { Console.Error.WriteLine($"FAIL: {failure}"); }
Console.WriteLine(failures.Count == 0 ? "AOT smoke test passed." : "AOT smoke test failed.");
return failures.Count == 0 ? 0 : 1;
