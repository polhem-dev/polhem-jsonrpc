# PayloadQuickStart.Client

[English](README.md) | **繁體中文**

以加密參數呼叫 [PayloadQuickStart.Server](../PayloadQuickStart.Server/README.zh-TW.md) 的主控台程式，並重送一次呼叫，
展示它被拒絕。先啟動伺服器，再以相同的 `PayloadDemoKey` 執行：

```bash
dotnet run --project samples/PayloadQuickStart.Client
```

## 關鍵的幾行

```csharp
var payload = new PayloadProcessor(new PayloadOptions
{
    RequireFrame = true,
    TypeResolver = new PayloadTypeRegistry().Register<AddRequest>().Register<AddResponse>(),
});

var parameters = payload.Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: key, sequence: 1);
var result = await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
var added = (AddResponse)payload.Unwrap(result, key)!;
```

- `Wrap` 把請求序列化、壓縮、加上 frame 並加密；`Unwrap` 對結果反向處理。connector 收送的是一般的 `JsonElement`，
  所以核心用戶端套件不需要任何改變。
- 格式、金鑰與序號逐次呼叫決定。每次呼叫取下一個序號；再送一次相同的參數，會收到錯誤（本範例為 `-32005`）。
- 結果只會解碼成登錄在 `TypeResolver` 的型別。
- `Polhem.JsonRpc.Payload` 可在 Native AOT 下執行；此時請給選項一個 source generator 產生的 `JsonSerializerContext`。
