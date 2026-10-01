# Polhem.JsonRpc

[English](README.md) | **繁體中文**

[![Build CI](https://github.com/polhem-dev/polhem-jsonrpc/actions/workflows/build-ci.yml/badge.svg)](https://github.com/polhem-dev/polhem-jsonrpc/actions/workflows/build-ci.yml)

以 System.Text.Json 實作的 .NET JSON-RPC 2.0 套件：與傳輸無關的伺服器、ASP.NET Core 端點與用戶端，
拆成多個 NuGet 套件發佈，應用程式只引用需要的部分。

> **狀態：開發中。** 尚未發佈任何套件，API 仍可能變動。

## 套件

| 套件 | 內容 | 依賴 |
|------|------|------|
| `Polhem.JsonRpc` | 伺服器與用戶端共用的訊息型別、錯誤碼與傳輸抽象 | 只有 .NET |
| `Polhem.JsonRpc.Server` | dispatcher：方法解析、參數繫結、filter，以及 in-process 傳輸 | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.AspNetCore` | ASP.NET Core 端點（`MapJsonRpc`）與 HTTP 請求處理 | `Polhem.JsonRpc.Server`、ASP.NET Core |
| `Polhem.JsonRpc.Client` | `JsonRpcConnector`：HTTP 傳輸、batch、notification 與請求攔截器 | `Polhem.JsonRpc` |

伺服器端應用程式引用 `Polhem.JsonRpc.AspNetCore`（不是 ASP.NET Core 的 host 則引用 `Polhem.JsonRpc.Server`），
用戶端應用程式只引用 `Polhem.JsonRpc.Client`。

這些套件不依賴 [Polhem 框架](https://github.com/polhem-dev/polhem)。Polhem 用它們實作自己的 API，
並透過 filter 與攔截器加上自己的 payload 加密、壓縮與授權。

## 快速上手

伺服器（ASP.NET Core）：

```csharp
builder.Services.AddJsonRpcServer(options => options.AddTarget<Calculator>("math"));
app.MapJsonRpc("/api");

public sealed class Calculator
{
    [JsonRpcMethod]
    public int Add(AddArgs args) => args.A + args.B;   // 回應 "math.add"
}
```

用戶端：

```csharp
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http));
var sum = await rpc.InvokeAsync<int>("math.add", new AddArgs(1, 2));
```

## 範例

| 範例 | 示範內容 |
|------|----------|
| [QuickStart.Server](samples/QuickStart.Server/README.zh-TW.md) | 以 ASP.NET Core minimal API 架設伺服器 |
| [QuickStart.Client](samples/QuickStart.Client/README.zh-TW.md) | 從主控台程式發出一般呼叫、處理錯誤、notification 與 batch |
| [ApiKey](samples/ApiKey/README.zh-TW.md) | 伺服器以 filter 檢查 API key，用戶端以 handler 送出 |

套件本身不做 payload 的加密與壓縮。請用 HTTPS 與 HTTP 壓縮，或在伺服器的 filter 與用戶端的攔截器裡改寫參數與結果。

## 設計

套件切分與主要設計取捨的理由，記錄在 [ADR-001](maintainers/adr/adr-001-package-split-and-design.md)（英文）。

## 參與貢獻

見 [CONTRIBUTING.md](CONTRIBUTING.md)（英文）。

## 授權

[MIT](LICENSE)
