using Polhem.JsonRpc;
using Polhem.JsonRpc.Client;
using QuickStart.Client;

using var http = new HttpClient { BaseAddress = new Uri(args.FirstOrDefault() ?? "http://localhost:5080/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http));

// A call with a result.
var sum = await rpc.InvokeAsync<int>("math.add", new AddArgs(1, 2));
Console.WriteLine($"1 + 2 = {sum}");

// An error from the server arrives as JsonRpcErrorException.
try
{
    await rpc.InvokeAsync<double>("math.divide", new DivideArgs(1, 0));
}
catch (JsonRpcErrorException ex)
{
    Console.WriteLine($"Error {ex.Code}: {ex.Message}");
}

// A notification: the server runs it and does not answer.
await rpc.NotifyAsync("math.log", new LogArgs("Hello from the client"));

// A batch: several calls in one message.
var batch = rpc.CreateBatch();
var first = batch.Add<int>("math.add", new AddArgs(2, 3));
var second = batch.Add<int>("math.add", new AddArgs(4, 5));
await batch.SendAsync();
Console.WriteLine($"Batch: {await first}, {await second}");
