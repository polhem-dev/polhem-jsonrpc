# ApiKey

[English](README.md) | **繁體中文**

在同一個程序裡示範兩端的擴充點：伺服器拒絕沒有正確 `X-Api-Key` header 的呼叫，用戶端負責加上這個 header。

```bash
dotnet run --project samples/ApiKey
```

## 關鍵的幾行

伺服器端，filter 會包在每個呼叫外面執行。它從 `context.Transport.Headers` 讀 header，丟出
`JsonRpcErrorException` 就能拒絕呼叫（`ApiKeyFilter.cs`）：

```csharp
options.Filters.Add(new ApiKeyFilter(DemoKey));
```

用戶端，header 屬於 HTTP 層的事，所以由 `HttpClient` 上的 `DelegatingHandler` 加上（`ApiKeyHandler.cs`），
connector 不需要知道：

```csharp
using var http = new HttpClient(new ApiKeyHandler(DemoKey) { InnerHandler = new HttpClientHandler() }) { BaseAddress = endpoint };
```

filter 也可以在呼叫前改寫 `context.Request.Params`、在呼叫後改寫 `context.Result`；用戶端的攔截器
（`IJsonRpcClientInterceptor`）在另一端做同樣的事。要加密或壓縮 payload 的 host，就在這裡實作。
