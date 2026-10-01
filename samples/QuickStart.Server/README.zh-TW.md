# QuickStart.Server

[English](README.md) | **繁體中文**

以 ASP.NET Core minimal API 架設的 JSON-RPC 伺服器，在 `http://localhost:5080/api` 回應 `Calculator.Add`、
`Calculator.Divide` 與 `Calculator.Log`。

```bash
dotnet run --project samples/QuickStart.Server
```

## 關鍵的幾行

方法名的格式是 `ProgId.Action`。ProgId 指定物件，由應用程式自己的 factory 決定它對應哪個物件
（`AppObjectFactory.cs`）：

```csharp
public object? CreateObject(string progId, JsonRpcRequestContext context) => progId switch
{
    "Calculator" => new Calculator(),
    _ => null,
};
```

`Program.cs` 註冊 factory 並對應端點，不註冊任何方法：

```csharp
builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
builder.Services.AddJsonRpcServer();
app.MapJsonRpc("/api");
```

Action 是物件上的方法（`Calculator.cs`）。參數型別名為 `{Action}Request`、回傳型別名為 `{Action}Response`，
它就能被呼叫；名稱比對區分大小寫。

```csharp
public AddResponse Add(AddRequest request) => new() { Sum = request.A + request.B };
```

- `params` 會反序列化成 request，回傳的 response 成為 `result`。
- request 與 response 類別放在 [QuickStart.Contracts](../QuickStart.Contracts)，用戶端也引用它，兩端用同一套型別。
- 要回傳特定錯誤就丟 `JsonRpcErrorException`，像 `Divide` 那樣。其他例外一律回 `-32603 Internal error`，
  不會帶出例外訊息。

用 [QuickStart.Client](../QuickStart.Client/README.zh-TW.md) 呼叫它。
