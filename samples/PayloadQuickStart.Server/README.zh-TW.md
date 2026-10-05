# PayloadQuickStart.Server

[English](README.md) | **繁體中文**

參數與結果以加密方式傳輸、並有防重放保護的 JSON-RPC 伺服器：在 [QuickStart.Server](../QuickStart.Server/README.zh-TW.md)
之上加上選用套件 `Polhem.JsonRpc.Payload.Server`。在 `http://localhost:5081/api` 回應 `Calculator.Add`。

兩端需要同一把 64 bytes 的示範金鑰，每個用戶端自己的金鑰由它推導而來。產生一把，並在每個終端機匯出：

```bash
export PayloadDemoKey=$(openssl rand -base64 64 | tr -d '\n')
dotnet run --project samples/PayloadQuickStart.Server
```

## 關鍵的幾行

`Program.cs` 以兩端共用的選項把 payload filter 加進伺服器：

```csharp
var payload = new PayloadOptions { RequireFrame = true };
builder.Services.AddJsonRpcServer(options =>
{
    options.ExceptionMapper = (exception, _) => exception is ReplayRejectedException
        ? new JsonRpcError(-32005, "Replay rejected")
        : null;
    options.UsePayload(payload, policy);
});
```

exception mapper 以 `-32005`（本範例自選的錯誤碼）回應重放的呼叫；少了它，重放會回 `-32603`，和其他 payload 失敗一樣，
只有格式錯誤的外殼與低於最低格式的呼叫由 `UsePayload` 回 `-32602`。mapper 設定在 `UsePayload` 之前，因此仍會優先回應。

filter 向應用程式詢問只有它知道的事（`DemoKeyPolicy.cs`）：這次呼叫的金鑰、序號必須唯一的範圍、方法是否拒絕重複的序號，
以及方法接受的最低格式。

```csharp
public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context) => /* 由示範金鑰與 X-Client-Id 推導 */;
public string? GetReplayScope(JsonRpcRequestContext context) => /* X-Client-Id header */;
public bool RequiresUniqueSequence(JsonRpcRequestContext context) => true;
public PayloadFormat GetMinimumFormat(JsonRpcRequestContext context) => PayloadFormat.Encrypted;
```

- 只有加密的呼叫帶著呼叫端無法偽造的 frame：plain 呼叫沒有 frame，encoded 呼叫的 frame 則誰都能寫。要求 `Encrypted`
  才能讓防重放檢查無法被略過。低於此格式的呼叫會在讀取內容之前就回 `-32602 Invalid params`。

- 方法本身（`Calculator.cs`）和不用外殼時一樣：filter 在呼叫前開啟請求、呼叫後以請求的格式與 codec 封裝結果。
- 請求解碼成什麼型別由伺服器決定（方法的參數型別），用戶端寫的 `type` 只拿來比對。因此不需要登錄任何合約型別：
  伺服器公開的方法就是白名單。
- 防重放範圍必須涵蓋這把金鑰的所有持有者，否則攔截到的呼叫換到使用同一把金鑰的另一個範圍重送，就會再次被接受。
  `X-Client-Id` header 本身沒有經過驗證，所以金鑰由它推導（`HMACSHA512(demoKey, clientId)`）：每個 client id 各有一把金鑰，
  換一個 client id 重送的呼叫，HMAC 驗證會失敗。這只在範例要求加密呼叫時成立；plain 與 encoded 呼叫沒有 HMAC，
  呼叫端可以改掉或拿掉這個 header。
- 從同一把示範金鑰推導是為了讓範例簡短，但用戶端也持有這把示範金鑰。拿到用戶端的人都能推導出任何 client id 的金鑰，
  冒充那個用戶端呼叫：各自的金鑰擋得住換 client id 重送，擋不住冒充。實際的應用程式絕不把根金鑰交給用戶端。
  根金鑰只留在伺服器，登入後再為每個 session 產生或推導一把金鑰，只把這把金鑰交給用戶端，並以 session 作為防重放範圍。
  金鑰如何協商不在 payload 套件的範圍內。
- `MemoryPayloadReplayStore` 把序號記在行程記憶體裡。多個伺服器執行個體需要共用的 `IPayloadReplayStore`。

以 [PayloadQuickStart.Client](../PayloadQuickStart.Client/README.zh-TW.md) 呼叫它。格式說明見
[ADR-002](../../maintainers/adr/adr-002-payload-packages.md)（英文）。
