# QuickStart.Server

[English](README.md) | **繁體中文**

以 ASP.NET Core minimal API 架設的 JSON-RPC 伺服器，在 `http://localhost:5080/api` 回應 `math.add`、
`math.divide` 與 `math.log`。

```bash
dotnet run --project samples/QuickStart.Server
```

## 關鍵的幾行

`Program.cs` 註冊 target 並對應端點：

```csharp
builder.Services.AddJsonRpcServer(options => options.AddTarget<Calculator>("math"));
app.MapJsonRpc("/api");
```

`Calculator.cs` 是 target。公開的實例方法、只有一個參數、並標上 `[JsonRpcMethod]`，就能被呼叫。
`math.add` 會呼叫 `Add`：名稱比對不分大小寫。

```csharp
[JsonRpcMethod]
public int Add(AddArgs args) => args.A + args.B;
```

- `params` 會反序列化成方法的參數（`AddArgs`），回傳值成為 `result`。
- target 可以在建構子注入服務：`Calculator` 注入了 `ILogger`。
- 要回傳特定錯誤就丟 `JsonRpcErrorException`，像 `Divide` 那樣。其他例外一律回 `-32603 Internal error`，
  不會帶出例外訊息。

用 [QuickStart.Client](../QuickStart.Client/README.zh-TW.md) 呼叫它。
