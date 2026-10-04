# 變更紀錄

[English](CHANGELOG.md) | **繁體中文**

Polhem.JsonRpc 各套件的重要變更。格式依循
[Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/)，版號依循[語意化版本](https://semver.org/lang/zh-TW/)，例外寫在 1.1.0。

## [Unreleased]

### 新增

- `JsonRpcServerOptions.AllowCodeCompiledAgainst10` 及其背後的檢查：**行為變更**，已載入的組件中若有以
  `Polhem.JsonRpc.Server` 1.0 編譯的，`JsonRpcDispatcher` 的建構子會擲出 `InvalidOperationException` 並列出其名稱，
  因為這類程式碼以舊數值讀取 `JsonRpcTransportKind`。只有在那些程式碼從不讀取 transport kind 時才設定此選項。
- 新增一個 `JsonRpcDispatcher` 建構子，物件工廠與選項分開傳入。
- `PayloadProcessor.WrapRequest`、`SealResponse`、`UnwrapResult` 與 `UnwrapResult<T>`，以及 `OpenRequest`、`OpenResult`
  帶 JSON-RPC method 的多載：把加密 payload 綁定到它的呼叫（見「變更」）。`IPayloadEncryptor` 新增驗證附加資料的
  `Encrypt` 與 `Decrypt` 多載。
- `PayloadOptions.TimeProvider`：寫入 frame 時間戳與檢查時間戳所用的時鐘，預設為系統時鐘；`PayloadFilter` 自行建立的記憶體
  replay store 也使用它。
- `JsonRpcSerializer.ReadRequests(utf8Json, maxBatchSize)`：批次超過上限時，依解析後陣列的長度拒絕，不會從任何項目讀出請求。
- `IPayloadServerPolicy.GetMinimumFormat`：方法接受的最低格式。低於它的呼叫會在詢問金鑰或讀取內容之前就回 `-32602`。
  預設接受所有格式，與先前相同。
- `JsonRpcRequestContext.MessageItems`：同一則訊息的所有呼叫共用的值（單一呼叫，或 batch 的全部呼叫），供需要為整則訊息計量的 filter 使用。
- `PayloadOptions.MaxDecompressedBytesPerMessage`（預設 50 MiB）：伺服器為一則訊息（單一呼叫或整個 batch）解壓的總量上限。
  由 `PayloadDecompressionBudget`、`IPayloadCompressor.Decompress(byte[], long)`（含預設實作，`GzipPayloadCompressor` 有實作）與 `PayloadProcessor.OpenRequest`
  的一個多載承載。`PayloadFilter` 只在建立時讀取一次這個設定。

### 變更

- **破壞性變更（wire 格式）：** 加密 payload 的 HMAC 另外涵蓋呼叫的方向與 JSON-RPC method（ADR-003），攔到的加密呼叫
  不能改送給別的方法，結果也不能當成請求送回。這兩件事先前都做得到，不論有沒有 frame。位元組排列不變，但 1.0、
  對應版本之前的 Polhem 框架或 polhem-connector-js 寫出的 payload，HMAC 都對不上而被拒；不提供相容模式，因為同時接受
  新舊兩種的讀取端會被降級。三者同步發佈。與下方 `JsonRpcTransportKind` 重新編號相同，這是在 minor 版中破壞相容性，
  屬於語意化版本的例外。
- **破壞性變更（行為）：** 不帶 method 的 `Wrap`、`Seal`、`Unwrap`、`Unwrap<T>`，以及 `OpenRequest`、`OpenResult`
  的同類多載，遇到加密 payload 會擲出 `InvalidOperationException`；plain 與 encoded payload 照舊。依 1.0 寫的
  `IPayloadEncryptor` 在實作帶附加資料的多載之前，會擲出 `NotSupportedException`。`AesCbcHmacPayloadEncryptor`
  兩個參數的 `Encrypt` 與 `Decrypt` 維持 1.0 的未綁定形式，供非 payload 的資料使用。
- `JsonRpcDispatcher` 改為在某個物件型別第一次被使用時，一次解析該型別的所有方法，並在那時逐一詢問 method policy
  （每個方法只問一次），而不再逐個方法名稱處理；答案在 dispatcher 存續期間保留。
- **wire 可見：** policy 擲出例外的方法不可呼叫，每次都回 `-32601 Method not found`；該型別的其他方法不受影響。
  先前每次呼叫都以 `-32603` 失敗，或由主機的 `ExceptionMapper` 轉換該例外。
- **wire 可見：** `PayloadParameterBinder` 收到不是物件、或 System.Text.Json 無法建立的 plain 值時，回 `-32602 Invalid params`
  而非 `-32603 Internal error`，與 dispatcher 自己的 binder 一致。
- **wire 可見：** `UsePayload` 對 `InvalidPayloadException`（格式錯誤的外殼、低於方法最低格式的呼叫，或在檢查唯一序號時被拒絕的未加密呼叫）回 `-32602 Invalid params`，而非 `-32603 Internal error`。
  在 `UsePayload` 之前設定的 `ExceptionMapper` 仍然優先；其他 payload 失敗維持 `-32603`，回應不會透露是哪一項檢查失敗。
- **行為變更：** `JsonRpcConnector` 只在建立時讀取一次 `JsonRpcClientOptions`（含 interceptor）。先前只有 `SerializerOptions` 在建立時讀取，
  其餘每次呼叫都重讀，因此在呼叫進行中新增 interceptor 可能擲出例外。
- **行為變更：** `JsonRpcConnector` 拒絕帶著另一個請求 id 的回應，也拒絕不產生 id 的 `IdGenerator`；批次拒絕 id 為 null 或與批次內重複的呼叫。
- PayloadQuickStart 範例改由示範金鑰與 `X-Client-Id` 推導每個用戶端的金鑰，換一個 client id 重送的呼叫不再落入全新的防重放範圍。
  範例也改為只接受加密的呼叫。
- **破壞性變更（binary）：** `JsonRpcTransportKind.Http` 改為 0、`InProcess` 改為 1，`Custom` 維持 2。`InProcess` 原本是預設值，
  自訂傳輸若沒有設定 kind，就會被當成 in-process，而主機可能給 in-process 比遠端呼叫更多信任。請以此版本重新編譯：
  以 1.0 編譯的程式碼仍用舊數值比較，會把每個 HTTP 呼叫當成 in-process。
  此版本在 minor 版中破壞 binary 相容性，是語意化版本的例外：Polhem 框架會同時發佈以此版編譯的版本並停用前一版。
  升級這些套件、卻仍執行以 1.0 編譯之程式碼的應用程式，會把未經驗證的 HTTP 呼叫當成 in-process 放行。
  **所有 `Polhem.JsonRpc` 套件必須一起升級：** 1.0 的套件彼此接受任何更新的版本，只升級其中一個（例如
  `Polhem.JsonRpc.Payload.Server`）就可能在 `Polhem.JsonRpc.AspNetCore` 1.0 底下帶進 `Polhem.JsonRpc.Server` 1.1，
  使每個 HTTP 呼叫都被標成 in-process。`JsonRpcDispatcher` 現在遇到這種情況會拒絕啟動（見下）。
- **wire 可見：** 沒有 `params` 的請求一律回 `-32602 Invalid params`，不論是否使用 payload 套件。先前會綁定 `null`，方法通常隨之失敗，呼叫端得到 `-32603`。
- **行為變更：** record 的 `Equals(T)` 不再被解析為 action，即使 method policy 允許所有方法。
- **wire 格式（讀取端）：** `GzipPayloadCompressor` 遇到不以 gzip 標頭開頭的內容時，視為未壓縮照原樣讀取。這是「小資料不壓縮」的第一步：
  所有讀取端都接受之後，寫出端才會送出未壓縮的內容。encoded 內容不需金鑰就會被解壓；除了下方的解壓上限，
  在意的部署請以 `IPayloadServerPolicy.GetMinimumFormat` 要求加密呼叫。
- **wire 可見：** 在會檢查序號的部署（`PayloadOptions.RequireFrame` 開啟，且 `IPayloadServerPolicy.GetReplayScope`
  回傳了 replay scope），`RequiresUniqueSequence` 為 true 的方法拒絕 plain 與 encoded 呼叫並回 `-32602`。
  只有加密的 frame 受 HMAC 保護；encoded 呼叫的 frame 誰都能寫，既無法證明序號是新的，也不能讓它推進 scope 的 window。
  在 1.0.0，encoded 呼叫會被檢查，偽造的呼叫可使之後合法的加密呼叫被拒；plain 呼叫沒有 frame，從未被檢查。
  沒有 frame 或沒有 scope 時，一如以往，不檢查也不拒絕。plain 與 encoded 呼叫不會向 policy 要金鑰，policy 回答
  `RequiresUniqueSequence` 與 `GetReplayScope` 時必須只依據請求的 context。
- **行為變更：** 一則訊息共用一份解壓額度（`MaxDecompressedBytesPerMessage`）：單一呼叫，或 batch 的全部呼叫；
  每個呼叫使用前面的呼叫剩下的額度，notification 也算在內。encoded 內容不需金鑰就會解壓；以最高壓縮等級的 gzip，
  一個含 62 個內容的 4 MiB 請求原本可讓伺服器配置約 11 GiB、耗費約 10 秒 CPU。這份額度疊加在每個內容自己的上限
  `GzipPayloadCompressor.MaxDecompressedBytes` 之上，兩者互不放寬。解壓失敗的內容會用掉剩餘的全部額度；
  未壓縮送出的內容依其長度計入額度。

### 修正

- `AddJsonRpcServer` 不再把它解析出的物件工廠寫回共用的 `JsonRpcServerOptions`。先前由同一組服務建立的第二個
  service provider 會使用第一個 provider 的工廠，第一個 provider 被釋放後就會失敗。
- `HttpTransport` 為每個請求建立自己的 `Content-Type` 值。先前整個行程的請求共用同一個，handler 若就地修改它（例如加一個參數），
  之後每個請求、每個 `HttpClient` 都會跟著改，直到 header 長到無法送出。
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
- `JsonRpcConnector` 不論以 `JsonElement` 作為參數送出（`PayloadProcessor.Wrap` 的回傳值）或讀取為結果，都不再需要在
  source-generated 序列化 context 中列出 `JsonElement`，因此 payload 流程可在 Native AOT 下使用。非泛型的 `InvokeAsync` 完全不讀取結果。
- 經過 trimming 的應用程式（例如 iOS head）不再從 `PayloadParameterBinder` 或 `DefaultParameterBinder` 收到 IL2072 警告。
  `Polhem.JsonRpc.Server`、`Polhem.JsonRpc.AspNetCore` 與 `Polhem.JsonRpc.Payload.Server` 現在會執行 trim analyzer，
  但仍不宣稱支援 trimming。
- 以 UTF-8 BOM 開頭的請求或回應會被正常讀取，不再回 `-32700 Parse error`。
- frame 時間戳與伺服器時鐘的差距超出 64 位元整數範圍時，會和其他超出容許範圍的時間戳一樣被拒絕，不再因算術溢位而失敗。
- `PayloadFilter` 在建立時一次讀取 `FrameTimestampTolerance` 與檢查請求時間戳的 `TimeProvider`，與它們決定的 replay store 保存時間一致。
  先前在 `UsePayload` 之後調高容許範圍，攔截到的呼叫可在其 scope 被遺忘後重送成功。

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
