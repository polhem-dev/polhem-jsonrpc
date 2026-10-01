using Polhem.JsonRpc;
using Polhem.JsonRpc.Client;
using QuickStart.Client;

using var http = new HttpClient { BaseAddress = new Uri(args.FirstOrDefault() ?? "http://localhost:5080/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http));

// A call with a result.
var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest(1, 2));
Console.WriteLine($"1 + 2 = {added!.Sum}");

// An error from the server arrives as JsonRpcErrorException.
try
{
    await rpc.InvokeAsync<DivideResponse>("Calculator.Divide", new DivideRequest(1, 0));
}
catch (JsonRpcErrorException ex)
{
    Console.WriteLine($"Error {ex.Code}: {ex.Message}");
}

// A notification: the server runs it and does not answer.
await rpc.NotifyAsync("Calculator.Log", new LogRequest("Hello from the client"));

// A batch: several calls in one message.
var batch = rpc.CreateBatch();
var first = batch.Add<AddResponse>("Calculator.Add", new AddRequest(2, 3));
var second = batch.Add<AddResponse>("Calculator.Add", new AddRequest(4, 5));
await batch.SendAsync();
Console.WriteLine($"Batch: {(await first)!.Sum}, {(await second)!.Sum}");
