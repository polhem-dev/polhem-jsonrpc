# PayloadQuickStart.Client

[English](README.md) | **繁體中文**

以加密參數呼叫 [PayloadQuickStart.Server](../PayloadQuickStart.Server/README.zh-TW.md) 的主控台程式，並重送一次呼叫，
展示它被拒絕。先啟動伺服器，再以相同的 `PayloadDemoKey` 執行：

```bash
dotnet run --project samples/PayloadQuickStart.Client
```

## 關鍵的幾行

```csharp
var key = HMACSHA512.HashData(Convert.FromBase64String(base64Key), Encoding.UTF8.GetBytes(clientId));
var payload = new PayloadProcessor(new PayloadOptions { RequireFrame = true });

var parameters = payload.WrapRequest("Calculator.Add", new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: key, sequence: 1);
var result = await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
var added = payload.UnwrapResult<AddResponse>("Calculator.Add", result, key)!;
```

- 金鑰由示範金鑰與用戶端放在 `X-Client-Id` 的 client id 推導而來，推導方式與伺服器完全相同，因此呼叫無法換一個 client id 重送。
- `WrapRequest` 把請求序列化、壓縮、加上 frame 並加密；`UnwrapResult` 對結果反向處理。connector 收送的是一般的 `JsonElement`，
  所以核心用戶端套件不需要任何改變。
- 兩者都要傳入方法名稱。方法名稱與方向都在 HMAC 的涵蓋範圍內，所以加密的請求不能改送給別的方法，結果也不能當成請求送回去。
- 格式、金鑰與序號逐次呼叫決定。每次呼叫取下一個序號；再送一次相同的參數，會收到錯誤（本範例為 `-32005`）。
- `UnwrapResult<AddResponse>` 把結果解碼成呼叫端預期的型別，伺服器寫的 `type` 只拿來比對，因此不需要登錄合約型別。
  不帶型別參數的 `UnwrapResult` 則依該名稱解析型別，只接受登錄在 `PayloadOptions.TypeResolver` 的型別。
- `Polhem.JsonRpc.Payload` 可在 Native AOT 下執行；此時請給選項一個 source generator 產生的 `JsonSerializerContext`。
