# 變更紀錄

[English](CHANGELOG.md) | **繁體中文**

Polhem.JsonRpc 各套件的重要變更。格式依循
[Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/)，版號依循[語意化版本](https://semver.org/lang/zh-TW/)。

## [Unreleased]

### 新增

- `PayloadOptions.TimeProvider`：寫入 frame 時間戳與檢查時間戳所用的時鐘，預設為系統時鐘；`PayloadFilter` 自行建立的記憶體
  replay store 也使用它。
- `JsonRpcSerializer.ReadRequests(utf8Json, maxBatchSize)`：批次超過上限時，在讀取任何項目之前就拒絕。
- `IPayloadServerPolicy.GetMinimumFormat`：方法接受的最低格式。低於它的呼叫會在詢問金鑰或讀取內容之前就回 `-32602`。
  預設接受所有格式，與先前相同。
- `GzipPayloadCompressor.MaxCompressionRatio`，以及設定它的建構子。

### 變更

- `JsonRpcDispatcher` 改為在某個物件型別第一次被使用時，一次解析該型別的所有方法，並在那時逐一詢問 method policy，
  而不再逐個方法名稱處理。
- `PayloadParameterBinder` 收到不是物件、或 System.Text.Json 無法建立的 plain 值時，回 `-32602 Invalid params`
  而非 `-32603 Internal error`，與 dispatcher 自己的 binder 一致。
- `UsePayload` 對 `InvalidPayloadException`（格式錯誤的外殼）回 `-32602 Invalid params`，而非 `-32603 Internal error`。
  在 `UsePayload` 之前設定的 `ExceptionMapper` 仍然優先；其他 payload 失敗維持 `-32603`，回應不會透露是哪一項檢查失敗。
- `GzipPayloadCompressor` 另外拒絕解壓後超過壓縮前 100 倍的內容（`DefaultMaxCompressionRatio`），解壓後在 1 MiB 以內者除外。
  encoded 內容不需金鑰就會被解壓，只靠大小上限時，一個請求可以讓伺服器解壓遠多於它送出的資料量。
- `JsonRpcConnector` 只在建立時讀取一次 `JsonRpcClientOptions`（含 interceptor）。先前只有 `SerializerOptions` 在建立時讀取，
  其餘每次呼叫都重讀，因此在呼叫進行中新增 interceptor 可能擲出例外。
- `JsonRpcConnector` 拒絕帶著另一個請求 id 的回應，也拒絕不產生 id 的 `IdGenerator`；批次拒絕 id 為 null 或與批次內重複的呼叫。

### 修正

- 呼叫端自行編造的方法名稱不再留在 dispatcher 的方法快取中，解析不到任何方法的名稱無法再讓記憶體成長。
  這個查找在任何 filter 之前執行，也就是在驗證之前。
- 超過 `MaxBatchSize` 的批次會在逐一建立請求物件之前就被拒絕。
- `IJsonRpcObjectFactory.ReleaseObjectAsync` 或 `ExceptionMapper` 擲出的例外不再逃出 dispatcher：該呼叫回
  `-32603`，批次中其餘呼叫照常回應；原本就已失敗的呼叫保留它自己的錯誤。
- 呼叫端取消的批次會在下一個呼叫之前停止，不再把剩下的呼叫全部跑完。
- `MemoryPayloadReplayStore` 在呼叫抵達一個閒置 scope、而清理工作正好在移除它時，可能接受重放的序號。
- 伺服器以單一錯誤回應整個批次時（例如批次過大），批次中的每個呼叫現在都以該錯誤失敗，而非「The server did not answer
  this call of the batch」。
- `IdGenerator` 產生重複 id 時，批次中會有一個 task 永遠不會完成。
- 非泛型的 `InvokeAsync` 與 `InvokeAsync<JsonElement>` 不再需要在 source-generated 序列化 context 中列出 `JsonElement`，
  因此可在 Native AOT 下使用；非泛型版本完全不讀取結果。

## [1.0.0] - 2026-10-03

### 移除

- 為舊線上格式提供的選項：`JsonRpcServerOptions.InternalErrorCode`、`JsonRpcRequestContext.ResponseMembers`、
  `JsonRpcResponse.AdditionalMembers`，以及 `JsonRpcWriteOptions` 與其 `OmitNullId`（連同接收它的 `options` 參數與
  `JsonRpcHttpOptions.WriteOptions`）。回應一律依規格寫出，讀取時忽略未知的回應成員
  （[ADR-001](maintainers/adr/adr-001-package-split-and-design.md)，英文，決策 6）。

### 新增

- `Polhem.JsonRpc.Payload`（選用）：包住 `params` 與 `result` 的 payload 外殼（`format`、`value`、`type`、`codec`）、
  JSON codec、限制解壓縮後大小的 gzip、AES-256-CBC 加 HMAC-SHA256、17 bytes 的重放 frame，以及逐次呼叫包裝參數、還原
  結果的 `PayloadProcessor`。自行決定型別的讀取端（伺服器依方法的參數，用戶端透過 `Unwrap<T>`）只拿外殼的 `type` 與該型別比對，
  因此只有依名稱解析結果的用戶端需要登錄合約型別。支援 trimming 與 Native AOT。線上格式就是 Polhem 框架與 polhem-connector-js 已在使用的格式
  （[ADR-002](maintainers/adr/adr-002-payload-packages.md)，英文）。
- `Polhem.JsonRpc.Payload.Server`（選用）：`PayloadFilter` 開啟請求的外殼、檢查 frame 的時間戳與序號，並以相同格式與
  codec 回應；`IPayloadServerPolicy` 提供金鑰、重放範圍與解碼型別；`MemoryPayloadReplayStore`；以及
  `JsonRpcServerOptions.UsePayload`。
- PayloadQuickStart 範例：完整示範一次加密呼叫，以及重送的呼叫被拒絕。

## [0.1.0] - 2026-10-02

### 新增

- `Polhem.JsonRpc`：request、response、error 與 id 型別，規格定義的錯誤碼，`JsonRpcSerializer`（不靠反射讀寫訊息）
  與 `IJsonRpcTransport`。
- `Polhem.JsonRpc.Server`：`JsonRpcDispatcher`，支援 batch 與 notification。方法名 `ProgId.Action` 會在應用程式的
  `IJsonRpcObjectFactory` 依 ProgId 建立的物件上呼叫該 action；參數型別名為 `{Action}Request`、回傳型別名為
  `{Action}Response` 的 action 才可被呼叫。另有 filter、例外對應與 `InProcessTransport`。方法的回傳值在 filter
  執行完才序列化，filter 因此可從 `ReturnValue` 以自己的格式寫出結果。
- `Polhem.JsonRpc.AspNetCore`：`AddJsonRpcServer`、`MapJsonRpc` 與 `JsonRpcHttpHandler`。`AddJsonRpcServer` 沿用先前
  已註冊的伺服器與 HTTP 選項，框架可以先設定，應用程式再加上自己的。
- 讓宿主維持舊線路格式的選項：`InternalErrorCode`、`ResponseMembers`、`OmitNullId` 與 `StatusCodeSelector`。
- `Polhem.JsonRpc.Client`：`JsonRpcConnector`，支援一般呼叫、notification 與 batch，`HttpTransport` 與攔截器。
  支援 trimming 與 Native AOT。

[Unreleased]: https://github.com/polhem-dev/polhem-jsonrpc/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/polhem-dev/polhem-jsonrpc/compare/v0.1.0...v1.0.0
[0.1.0]: https://github.com/polhem-dev/polhem-jsonrpc/releases/tag/v0.1.0
