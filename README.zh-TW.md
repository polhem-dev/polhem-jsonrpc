# Polhem.JsonRpc

[English](README.md) | **繁體中文**

[![Build CI](https://github.com/polhem-dev/polhem-jsonrpc/actions/workflows/build-ci.yml/badge.svg)](https://github.com/polhem-dev/polhem-jsonrpc/actions/workflows/build-ci.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=alert_status)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=bugs)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=vulnerabilities)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=code_smells)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=polhem-dev_polhem-jsonrpc&metric=coverage)](https://sonarcloud.io/project/overview?id=polhem-dev_polhem-jsonrpc)

以 System.Text.Json 實作的 .NET JSON-RPC 2.0 套件：與傳輸無關的伺服器、ASP.NET Core 端點與用戶端，
拆成多個 NuGet 套件發佈，應用程式只引用需要的部分。

套件已發佈在 [nuget.org](https://www.nuget.org/packages?q=Polhem.JsonRpc)；每一版的變更見 [CHANGELOG](CHANGELOG.zh-TW.md)。

## 套件

| 套件 | 內容 | 依賴 |
|------|------|------|
| `Polhem.JsonRpc` | 伺服器與用戶端共用的訊息型別、錯誤碼與傳輸抽象 | 只有 .NET |
| `Polhem.JsonRpc.Server` | dispatcher：方法解析、參數繫結、filter，以及 in-process 傳輸 | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.AspNetCore` | ASP.NET Core 端點（`MapJsonRpc`）與 HTTP 請求處理 | `Polhem.JsonRpc.Server`、ASP.NET Core |
| `Polhem.JsonRpc.Client` | `JsonRpcConnector`：HTTP 傳輸、batch、notification 與請求攔截器 | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.Payload` | 選用：包住 `params` 與 `result` 的 payload 外殼，含 codec、gzip、AES-CBC-HMAC 加密與防重放 frame | `Polhem.JsonRpc` |
| `Polhem.JsonRpc.Payload.Client` | 選用：`PayloadConnector`，每次呼叫自動封裝參數、開啟結果 | `Polhem.JsonRpc.Payload`、`Polhem.JsonRpc.Client` |
| `Polhem.JsonRpc.Payload.Server` | 選用：開啟外殼並檢查 frame 的伺服器 filter | `Polhem.JsonRpc.Payload`、`Polhem.JsonRpc.Server` |

伺服器端應用程式引用 `Polhem.JsonRpc.AspNetCore`（不是 ASP.NET Core 的 host 則引用 `Polhem.JsonRpc.Server`），
用戶端應用程式只引用 `Polhem.JsonRpc.Client`。要加密 payload 的應用程式，用戶端加上 `Polhem.JsonRpc.Payload.Client`，
伺服器加上 `Polhem.JsonRpc.Payload.Server`。

這些套件不依賴 [Polhem 框架](https://github.com/polhem-dev/polhem)。Polhem 用它們實作自己的 API，
以 payload 套件處理加密與壓縮，並以自己的 filter 處理授權。

## 快速上手

伺服器（ASP.NET Core）：

```csharp
builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
builder.Services.AddJsonRpcServer();
app.MapJsonRpc("/api");

// "Calculator.Add"：ProgId "Calculator" 指定物件，action "Add" 指定方法。
public sealed class AppObjectFactory : IJsonRpcObjectFactory
{
    public object? CreateObject(string progId, JsonRpcRequestContext context) => progId switch
    {
        "Calculator" => new Calculator(),
        _ => null,
    };
}

public sealed class Calculator
{
    // 參數是 AddRequest、回傳是 AddResponse，所以可被呼叫。
    public AddResponse Add(AddRequest request) => new() { Sum = request.A + request.B };
}
```

用戶端：

```csharp
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5080/api") };
var rpc = new JsonRpcConnector(new HttpTransport(http));
var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", new AddRequest { A = 1, B = 2 });
```

## 方法怎麼被找到

方法名的格式是 `ProgId.Action`，例如 `Calculator.Add`。不需要註冊任何方法，伺服器依下列規則找到它。

| 步驟 | 規則 |
|------|------|
| 名稱 | `ProgId.Action`。ProgId 可用英數字、底線、連字號；action 可用英數字、底線；各最多 64 字。action 比對區分大小寫；ProgId 原樣交給 factory。 |
| 物件 | 由應用程式的 `IJsonRpcObjectFactory.CreateObject(progId, context)` 建立呼叫要執行的物件，回傳 `null` 代表不認得這個 ProgId。呼叫結束後物件會交給 `ReleaseObjectAsync`。 |
| Action | 該物件上公開、非泛型、恰好一個參數的實例方法。同名而符合條件的方法有一個以上時視為不明確，當作找不到。 |
| 約定 | 參數型別名為 `{Action}Request`、回傳型別（或 `Task<T>`／`ValueTask<T>` 的結果型別）名為 `{Action}Response` 的方法才能被呼叫：`AddResponse Add(AddRequest request)`。其他公開方法一律當作不存在。要換規則就設定 `JsonRpcServerOptions.MethodPolicy`。 |
| 參數 | `params` 必須是 JSON 物件，反序列化成 request（預設 camelCase 名稱）。沒有 `params` 或為陣列時回 `-32602 Invalid params`。 |
| 結果 | 回傳的 response 物件成為 `result`。 |
| Id | 字串、整數或 `null`，原樣回傳。帶小數或指數的數字（`1.0`、`1e2`），或超出 64 位元整數的數字，會回 `-32600 Invalid Request` 且 id 為 `null`，因為規格不建議使用這些值；請使用整數或字串。 |
| 錯誤 | 名稱、物件或 action 找不到時回 `-32601 Method not found`。丟 `JsonRpcErrorException` 可回傳自訂的錯誤碼與訊息；其他例外一律回 `-32603 Internal error`，不帶出例外訊息。 |

## 設計取向與不適用的場景

固定的形狀（`ProgId.Action` 名稱、傳入一個 request 類別、傳出一個 response 類別）是刻意的：讓 API 可以演進，
而不會弄壞已經部署出去的前端。request 或 response 類別新增成員，不會影響在那之前建置的前端：前端沒送的成員，
在伺服器端繫結為預設值；前端不認得的結果成員則略過。新舊前端繼續呼叫同一個方法，不必維護 `v1`、`v2` 兩套端點。
這在 System.Text.Json 的預設設定下成立；設定 `JsonUnmappedMemberHandling.Disallow` 或
`RespectRequiredConstructorParameters` 就會關掉這個行為。

同一個選擇也排除了「方法名稱和參數由別人訂好」的協定。這些套件不實作 Language Server Protocol、
Model Context Protocol 或以太坊節點的 API，它們使用 `textDocument/didOpen`、`eth_call` 這類名稱與位置參數；
這類需求適合 [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc) 或
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)。它們也不提供伺服器反過來呼叫前端的全雙工連線：
一次呼叫就是一個 HTTP 請求與它的回應。

## 範例

| 範例 | 示範內容 |
|------|----------|
| [QuickStart.Server](samples/QuickStart.Server/README.zh-TW.md) | 以 ASP.NET Core minimal API 架設伺服器 |
| [QuickStart.Client](samples/QuickStart.Client/README.zh-TW.md) | 從主控台程式發出一般呼叫、處理錯誤、notification 與 batch |
| [QuickStart.Contracts](samples/QuickStart.Contracts) | 兩端共用的 request 與 response 類別 |
| [PayloadQuickStart.Server](samples/PayloadQuickStart.Server/README.zh-TW.md) | 伺服器端：加密的參數與結果，以及防重放 |
| [PayloadQuickStart.Client](samples/PayloadQuickStart.Client/README.zh-TW.md) | 主控台程式以 `PayloadConnector` 做同一件事，含重複的序號被拒絕 |

## 擴充點

**filter** 會包在伺服器端每個呼叫外面執行。丟出 `JsonRpcErrorException` 可以拒絕呼叫；在 `next` 之前可改寫
`context.Request.Params`，之後可改寫 `context.Result`：

```csharp
public sealed class ApiKeyFilter(string expectedKey) : IJsonRpcFilter
{
    public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
    {
        context.Transport.Headers.TryGetValue("X-Api-Key", out var key);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key ?? ""), Encoding.UTF8.GetBytes(expectedKey)))
        {
            throw new JsonRpcErrorException(-32001, "Unauthorized");
        }
        return next(context);
    }
}

builder.Services.AddJsonRpcServer(options => options.Filters.Add(new ApiKeyFilter(apiKey)));
```

用戶端的 HTTP header 屬於 `HttpClient` 的事，用 `DelegatingHandler` 加上。要在每次呼叫改寫參數與結果，
就在 `JsonRpcClientOptions.Interceptors` 加一個 `IJsonRpcClientInterceptor`。

```csharp
public sealed class ApiKeyHandler(string key) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Api-Key", key);
        return base.SendAsync(request, cancellationToken);
    }
}

using var http = new HttpClient(new ApiKeyHandler(apiKey) { InnerHandler = new HttpClientHandler() }) { BaseAddress = endpoint };
```

## 加密 payload

HTTPS 保護的是連線。若參數與結果還需要端對端保護，或呼叫不得被重放，就加上選用的 payload 套件：伺服器用
`Polhem.JsonRpc.Payload.Server`，用戶端用 `Polhem.JsonRpc.Payload.Client`。它們把 `params` 與 `result` 放進外殼，外殼可以是一般 JSON、
編碼（codec 加 gzip）或加密（AES-256-CBC 加 HMAC-SHA256），並可加上拒絕重複序號的 frame。

```csharp
// 伺服器：金鑰與防重放規則由應用程式提供。
builder.Services.AddJsonRpcServer(options => options.UsePayload(payloadOptions, new MyPayloadPolicy()));

// 用戶端：呼叫方式與 JsonRpcConnector 相同；每次呼叫自動封裝參數、開啟結果。
var rpc = new PayloadConnector(connector, new PayloadProcessor(payloadOptions), new PayloadConnectorOptions { KeyProvider = () => sessionKey });
var added = await rpc.InvokeAsync<AddResponse>("Calculator.Add", request);
```

兩端使用相同的 `PayloadOptions` 設定；金鑰如何協商由應用程式決定。要自行封裝的用戶端，則在 `JsonRpcConnector` 前後
呼叫 `PayloadProcessor.WrapRequest` 與 `UnwrapResult`。[PayloadQuickStart](samples/PayloadQuickStart.Server/README.zh-TW.md)
範例完整跑過一遍，[ADR-002](maintainers/adr/adr-002-payload-packages.md)（英文）說明格式，其他用戶端可依此實作。

## 安全性

套件如何防範惡意的呼叫端與網路、哪些事留給應用程式負責，以及已知限制，見
[docs/security.zh-TW.md](docs/security.zh-TW.md)。

## 設計

套件切分與主要設計取捨的理由，記錄在 [ADR-001](maintainers/adr/adr-001-package-split-and-design.md)（英文）；payload 套件的理由在
[ADR-002](maintainers/adr/adr-002-payload-packages.md)（英文）。

## 參與貢獻

見 [CONTRIBUTING.md](CONTRIBUTING.md)（英文）。

## 授權

[MIT](LICENSE)
