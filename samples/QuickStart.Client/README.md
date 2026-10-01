# QuickStart.Client

**English** | [繁體中文](README.zh-TW.md)

A console application that calls [QuickStart.Server](../QuickStart.Server/README.md): a call, an error, a notification
and a batch. Start the server first, then:

```bash
dotnet run --project samples/QuickStart.Client
```

## The lines that matter

```csharp
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http));

var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest(1, 2));   // a call
await rpc.NotifyAsync("Calculator.Log", new LogRequest("Hello"));        // a notification: no answer

var batch = rpc.CreateBatch();                                         // a batch: one message
var first = batch.Add<AddResponse>("Calculator.Add", new AddRequest(2, 3));
await batch.SendAsync();
Console.WriteLine((await first)!.Sum);
```

An error answer is thrown as `JsonRpcErrorException`, with the server's code and message.

The client package also runs with Native AOT, on iOS, Android and WebAssembly. There, set
`JsonRpcClientOptions.SerializerOptions` to options whose `TypeInfoResolver` is a source-generated
`JsonSerializerContext` covering the parameter and result types.
