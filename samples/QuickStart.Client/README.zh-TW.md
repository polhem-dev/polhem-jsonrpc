# QuickStart.Client

[English](README.md) | **繁體中文**

呼叫 [QuickStart.Server](../QuickStart.Server/README.zh-TW.md) 的主控台程式：一般呼叫、錯誤、notification
與 batch。先啟動伺服器，再執行：

```bash
dotnet run --project samples/QuickStart.Client
```

## 關鍵的幾行

```csharp
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http));

var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest(1, 2));   // 一般呼叫
await rpc.NotifyAsync("Calculator.Log", new LogRequest("Hello"));        // notification：沒有回應

var batch = rpc.CreateBatch();                                         // batch：一次送出多個呼叫
var first = batch.Add<AddResponse>("Calculator.Add", new AddRequest(2, 3));
await batch.SendAsync();
Console.WriteLine((await first)!.Sum);
```

伺服器回傳的錯誤會以 `JsonRpcErrorException` 丟出，帶有伺服器給的錯誤碼與訊息。

Client 套件也能在 Native AOT、iOS、Android、WebAssembly 上執行。在這些環境要把
`JsonRpcClientOptions.SerializerOptions` 設成 `TypeInfoResolver` 為 source-generated `JsonSerializerContext`
的 options，且涵蓋所有參數與結果型別。
