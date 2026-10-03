# PayloadQuickStart.Server

[English](README.md) | **繁體中文**

參數與結果以加密方式傳輸、並有防重放保護的 JSON-RPC 伺服器：在 [QuickStart.Server](../QuickStart.Server/README.zh-TW.md)
之上加上選用套件 `Polhem.JsonRpc.Payload.Server`。在 `http://localhost:5081/api` 回應 `Calculator.Add`。

兩端需要同一把 64 bytes 的金鑰。產生一把，並在每個終端機匯出：

```bash
export PayloadDemoKey=$(openssl rand -base64 64 | tr -d '\n')
dotnet run --project samples/PayloadQuickStart.Server
```

## 關鍵的幾行

`Program.cs` 以兩端共用的選項把 payload filter 加進伺服器：

```csharp
var payload = new PayloadOptions
{
    RequireFrame = true,
    TypeResolver = new PayloadTypeRegistry().Register<AddRequest>().Register<AddResponse>(),
};
builder.Services.AddJsonRpcServer(options => options.UsePayload(payload, policy));
```

filter 向應用程式詢問只有它知道的事（`DemoKeyPolicy.cs`）：這次呼叫的金鑰、序號必須唯一的範圍，以及方法是否拒絕重複的序號。

```csharp
public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context) => ValueTask.FromResult<byte[]?>(key);
public string? GetReplayScope(JsonRpcRequestContext context) => /* X-Client-Id header */;
public bool RequiresUniqueSequence(JsonRpcRequestContext context) => true;
```

- 方法本身（`Calculator.cs`）和不用外殼時一樣：filter 在呼叫前開啟請求、呼叫後以請求的格式與 codec 封裝結果。
- 請求解碼成什麼型別由伺服器決定；用戶端寫的 `type` 只拿來比對。
- 所有用戶端共用一把金鑰是為了讓範例簡短。實際的應用程式會在登入時為每個 session 協商一把金鑰，並以 session 作為防重放範圍。
  金鑰如何協商不在 payload 套件的範圍內。
- `MemoryPayloadReplayStore` 把序號記在行程記憶體裡。多個伺服器執行個體需要共用的 `IPayloadReplayStore`。

以 [PayloadQuickStart.Client](../PayloadQuickStart.Client/README.zh-TW.md) 呼叫它。格式說明見
[ADR-002](../../maintainers/adr/adr-002-payload-packages.md)（英文）。
