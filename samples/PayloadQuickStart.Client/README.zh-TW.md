# PayloadQuickStart.Client

[English](README.md) | **繁體中文**

以加密參數呼叫 [PayloadQuickStart.Server](../PayloadQuickStart.Server/README.zh-TW.md) 的主控台程式，接著送出一個伺服器
已經看過的序號，展示它被拒絕。先啟動伺服器，再以相同的 `PayloadDemoKey` 執行：

```bash
dotnet run --project samples/PayloadQuickStart.Client
```

## 關鍵的幾行

```csharp
var key = HMACSHA512.HashData(Convert.FromBase64String(base64Key), Encoding.UTF8.GetBytes(clientId));
var payload = new PayloadProcessor(new PayloadOptions { RequireFrame = true });
var rpc = new PayloadConnector(connector, payload, new PayloadConnectorOptions { KeyProvider = () => key });

var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
```

- 金鑰由示範金鑰與用戶端放在 `X-Client-Id` 的 client id 推導而來，推導方式與伺服器完全相同，因此呼叫無法換一個 client id 重送。
- `PayloadConnector`（套件 `Polhem.JsonRpc.Payload.Client`）的呼叫方式與 `JsonRpcConnector` 相同。每次呼叫時它把參數序列化、
  壓縮、加上 frame 並加密，對結果則反向處理。
- 方法名稱與方向都在 HMAC 的涵蓋範圍內，所以加密的請求不能改送給別的方法，結果也不能當成請求送回去。
- 除非選項或該次呼叫指定別的格式，呼叫一律加密。每次呼叫取 connector 的下一個序號；在同一個 client id 下，另一個從 1
  重新編號的 connector 會收到錯誤（本範例為 `-32005`）。要讓序號跨 connector 延續，設定
  `PayloadConnectorOptions.SequenceGenerator`。
- 結果解碼成呼叫端要求的型別，伺服器寫的 `type` 只拿來比對，因此不需要登錄合約型別。`InvokeAsync<object>` 則依該名稱解析型別，
  只接受登錄在 `PayloadOptions.TypeResolver` 的型別。
- 兩個 payload 套件都可在 Native AOT 下執行；此時請給選項一個 source generator 產生的 `JsonSerializerContext`。
