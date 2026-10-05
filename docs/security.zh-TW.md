# 安全性

[English](security.md) | **繁體中文**

Polhem.JsonRpc 套件如何防範惡意的呼叫端與不可信任的網路、哪些事留給應用程式負責，以及已知並接受的限制。
有設計紀錄說明的防護，該節或該列會附上連結。這裡不重複列預設值：每個選項的說明文件會寫明自己的預設值。

在方法本身的程式碼執行之前，所有處理都建立在呼叫端可控制的位元組上，而且往往發生在驗證呼叫端身分之前。

## 資源上限

| 防護 | 位置 |
|------|------|
| 解析之前先限制 HTTP body 大小，不論有沒有 `Content-Length`，chunked body 也一樣。 | `JsonRpcHttpOptions.MaxRequestBodySize` |
| 限制一個 batch 可以包含的請求數。 | `JsonRpcServerOptions.MaxBatchSize` |
| 限制每個 body 的 gzip 解壓後大小。 | `GzipPayloadCompressor` |
| 同一則訊息的所有 body 共用一份解壓額度，batch 無法把 gzip 上限放大好幾倍。解壓失敗的 body 會用光剩餘額度。未壓縮送來的 body 不受 gzip 上限限制，但會依長度從額度扣除。 | `PayloadOptions.MaxDecompressedBytesPerMessage` |
| 記憶體內的重放紀錄會遺忘閒置的 scope，序號也不能無上限地往前跳。 | `MemoryPayloadReplayStore` |

## 哪些方法可以被呼叫

伺服器不需要註冊方法，而是依名稱找出方法，所以「拒絕找到哪些方法」就是第一道防線
（[ADR-001](../maintainers/adr/adr-001-package-split-and-design.md) 決策 2、3，英文；規則見
[README](../README.zh-TW.md#方法怎麼被找到)）。

| 防護 | 位置 |
|------|------|
| 只有 public、非泛型、恰好一個參數的 instance 方法才能成為 action。static 方法、屬性存取子與 `object` 宣告的方法都不是。 | `JsonRpcMethod.IsResolvableAction` |
| 名稱對應到多個候選方法時視為有歧義，以「找不到方法」回應；伺服器不會從中挑一個。 | `JsonRpcDispatcher` |
| 預設只有型別符合 `{Action}Request` → `{Action}Response` 的方法可以呼叫，不符合慣例的 public 輔助方法會被當成不存在。policy 擲出例外時，該方法視為不可呼叫。 | `JsonRpcServerOptions.MethodPolicy` |
| 查找之前，先限制 ProgId 與 action 的字元與長度。 | `JsonRpcDispatcher` |
| method policy 在所有 filter 之前執行，所以只有允許執行的呼叫才會解密 payload。 | ADR-001 決策 2、ADR-002 決策 4 |
| 內建的 HTTP 與行程內傳輸層會自行設定傳輸類型；header 與 `params` 無法冒稱其他類型。 | `JsonRpcTransportInfo`、ADR-001 決策 4 |

## 加密的 payload

選用的 payload 套件把 `params` 與 `result` 放進可以端對端加密的信封
（[ADR-002](../maintainers/adr/adr-002-payload-packages.md) 決策 2、6；
[ADR-003](../maintainers/adr/adr-003-bind-method-into-payload-hmac.md)，皆為英文）。

| 防護 | 位置 |
|------|------|
| AES-256-CBC 搭配 HMAC-SHA256，先加密再算 MAC，金鑰 64 bytes。 | `AesCbcHmacPayloadEncryptor` |
| HMAC 以固定時間比對，並在解密任何資料之前驗證，所以 padding 錯誤與竄改無法區分。 | `AesCbcHmacPayloadEncryptor` |
| 每則訊息都由 `RandomNumberGenerator` 產生新的隨機 IV。 | `AesCbcHmacPayloadEncryptor` |
| 切片之前先檢查長度欄位，截斷的訊息會被拒絕，而不是越界讀取。 | `AesCbcHmacPayloadEncryptor`、`PayloadFrame` |
| HMAC 涵蓋 method 名稱與方向（請求或回應）。截獲的呼叫無法改送到別的方法，回應也無法當成請求送回。 | ADR-003 決策 1、2 |
| 結果以請求的格式回應，`null` 結果也一樣。以 `UnwrapResult` 或 `UnwrapResult<T>`（或傳入請求格式的 `OpenResult`）讀取結果的用戶端，會拒絕其他格式的結果，所以加密的呼叫無法被回以 plain 結果。舊的 `Unwrap` 方法不做這項檢查。 | ADR-003 決策 6 |
| 方法可以要求最低格式，較低的格式在索取金鑰之前就被拒絕。 | `IPayloadServerPolicy.GetMinimumFormat` |
| 不加密的 `NoPayloadEncryptor` 會被拒絕，除非為了開發而允許。 | `PayloadOptions.AllowNoEncryption` |

## 重放的呼叫

重放保護只在兩個條件同時成立時生效：要求 frame，而且 policy 給了這個呼叫一個重放 scope。
缺少任一項時不做任何檢查，重複的呼叫會再執行一次（ADR-002 決策 4）。

| 防護 | 位置 |
|------|------|
| 要求 frame 時，每個 encoded 與 encrypted payload 都帶時間戳與序號。伺服器會拒絕時間戳超出容許範圍的請求；結果的時間戳不檢查。 | `PayloadOptions.RequireFrame`、`PayloadOptions.FrameTimestampTolerance` |
| 要求唯一序號的方法，會拒絕呼叫端 scope 內已出現過的序號。內建的重放紀錄也會拒絕落後最高序號太多、或超前太多的序號。 | `IPayloadServerPolicy.RequiresUniqueSequence`、`IPayloadServerPolicy.GetReplayScope`、`MemoryPayloadReplayStore` |
| 這類方法只接受加密呼叫。plain 呼叫沒有 frame，encoded 呼叫的 frame 沒有驗證，兩者都無法證明序號是新的。 | ADR-002 決策 4（1.1.0 修訂） |
| 是否要求 frame 是伺服器設定，請求無法把它關掉。 | ADR-002 決策 7 |
| 沒有傳入重放紀錄時，`UsePayload` 自己建立的那一份保留 scope 的時間是時間戳容許範圍的兩倍，所以舊到已被遺忘的 frame，早就會因時間戳而被拒絕。自行建立的重放紀錄必須給它這樣的存活時間。 | `JsonRpcServerOptions.UsePayload`、`MemoryPayloadReplayStore` |

## 型別、codec 與參數

| 防護 | 位置 |
|------|------|
| 讀取端自己決定型別時（伺服器依方法的參數，用戶端透過 `UnwrapResult<T>`），`type` 成員只用來比對，不會載入任何型別。 | ADR-002 決策 5 |
| 必須解析名稱時，內建的 registry 只解析向它註冊過的名稱，以完全相同比對，從不依名稱載入型別。改用自己提供的 resolver 時，它必須比對完整的 assembly-qualified 名稱，泛型參數也包含在內。 | `PayloadTypeRegistry`、`PayloadOptions.TypeResolver` |
| 無法以同名註冊另一個 codec 來取代內建的 `json` codec；不論是註冊的或收到的 codec 名稱，都限制了字元與長度。 | `PayloadOptions.RegisterCodec` |
| `params` 必須是 JSON 物件。位置參數與缺少值都以 `-32602 Invalid params` 回應。 | ADR-001 決策 2 |

## 錯誤與資訊外洩

| 防護 | 位置 |
|------|------|
| 非預期的例外以 `-32603 Internal error` 回應，不帶訊息、型別或 stack trace。只有 `JsonRpcErrorException`，或應用程式設定的 `ExceptionMapper`，能決定呼叫端看到什麼。 | `JsonRpcDispatcher`、`JsonRpcServerOptions.ExceptionMapper` |
| 除了信封格式錯誤或格式過低之外，其他 payload 失敗（HMAC 不符、重放的 frame、不認得的型別名稱、缺少金鑰）都回同一個 `-32603`，呼叫端分不出是哪一項檢查失敗。 | `JsonRpcServerOptions.UsePayload` |
| 只有設定 `IncludeExceptionDetails` 時，例外訊息才會放進 `data`。它可能透露是哪一項檢查失敗，所以只用於開發環境。 | `JsonRpcServerOptions.IncludeExceptionDetails` |
| 用戶端中斷的呼叫會被取消，不會回報成內部錯誤。 | `JsonRpcDispatcher`、`JsonRpcHttpHandler` |

## 部署

| 防護 | 位置 |
|------|------|
| 當下已載入的組件中，有對 `Polhem.JsonRpc.Server` 1.0 編譯的，dispatcher 就拒絕啟動；1.0 的傳輸類型數值不同。之後才載入的組件看不到。 | `JsonRpcServerOptions.AllowCodeCompiledAgainst10`、ADR-001 決策 4 |
| 套件只相依 .NET，端點另外相依 ASP.NET Core。本 repo 的建置遇到會傳給使用端的套件參考就失敗。 | ADR-001 決策 1 |

## 應用程式要負責的部分

- **HTTPS。** payload 套件保護的是 `params` 與 `result`；請求的其餘部分，包括 method 名稱，沒有 HTTPS 就看得到。
- **身分驗證與授權**：放在 object factory，或排在 payload filter 之前的 filter。method policy 只決定哪些方法存在，看不到呼叫端是誰。
- **金鑰**：如何協商、儲存與輪替。每個呼叫的金鑰由 `IPayloadServerPolicy.GetKeyAsync` 回傳。
- **重放 scope** 必須取自在 payload filter 之前已驗證的 session，並涵蓋這把金鑰的所有持有者。
  多個 session 共用一把金鑰時，在某個 session 截獲的呼叫，任何能出示另一個 session 的人都能拿到那裡重放；
  請讓每個 session 各有自己的金鑰，並以 session 作為 scope（`IPayloadServerPolicy.GetReplayScope`）。
- **多個伺服器行程。** 記憶體內的重放紀錄屬於單一行程：另一台伺服器，或重新啟動後的同一台，會接受它在時間戳容許範圍內沒見過的呼叫。
  需要時請提供共用的 `IPayloadReplayStore`。
- **自訂傳輸層**要自己設定 `JsonRpcTransportInfo.Kind`，預設值是 HTTP。
- **header 值。** 同名的多個 header 會以逗號合併（`JsonRpcTransportInfo.Headers`）；預期單一值的程式（例如 token）應拒絕含逗號的值。
- **用戶端的回應大小。** HTTP 傳輸層會讀取整個回應；請以 `HttpClient.MaxResponseContentBufferSize` 限制。
- **正式環境不要開啟 `IncludeExceptionDetails` 與 `AllowNoEncryption`。**

## 已知限制

- **錯誤回應沒有驗證。** JSON-RPC 的 error 不帶 payload，所以網路上的攻擊者可以把結果換成錯誤，
  但無法偽造加密呼叫的結果（ADR-003 決策 5、6）。
- **`format`、`type`、`codec` 成員與請求的 `id` 不在 HMAC 範圍內**（ADR-003 決策 5）。
  更改 `format` 或 `codec` 會讓 body 解碼或驗證失敗。用戶端開啟結果時若沒有指定型別，會依 `type` 從
  `PayloadTypeRegistry` 允許的型別中解析；要排除這種情況，請以 `UnwrapResult<T>` 指定型別。
- **結果在回程中可能被換成、或重放成同一個方法的另一個結果**，因為 `id` 沒有綁定。綁定它會讓每個用戶端都得追蹤 `id`，所以沒有這麼做。
- **重放檢查在解密與解壓之後執行。** 每次重放會讓伺服器付出金鑰查詢、解密與解壓原本 body 的成本，但方法不會執行。
- **Encoded payload 沒有驗證。** encoded 只是壓縮，不是保護；只有 encrypted payload 有驗證。
