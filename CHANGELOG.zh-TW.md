# 變更紀錄

[English](CHANGELOG.md) | **繁體中文**

Polhem.JsonRpc 各套件的重要變更。格式依循
[Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/)，版號依循[語意化版本](https://semver.org/lang/zh-TW/)。

## [Unreleased]

## [1.0.0] - 2026-10-03

### 移除

- 為舊線上格式提供的選項：`JsonRpcServerOptions.InternalErrorCode`、`JsonRpcRequestContext.ResponseMembers`、
  `JsonRpcResponse.AdditionalMembers`，以及 `JsonRpcWriteOptions` 與其 `OmitNullId`（連同接收它的 `options` 參數與
  `JsonRpcHttpOptions.WriteOptions`）。回應一律依規格寫出，讀取時忽略未知的回應成員
  （[ADR-001](maintainers/adr/adr-001-package-split-and-design.md)，英文，決策 6）。

### 新增

- `Polhem.JsonRpc.Payload`（選用）：包住 `params` 與 `result` 的 payload 外殼（`format`、`value`、`type`、`codec`）、
  JSON codec、限制解壓縮後大小的 gzip、AES-256-CBC 加 HMAC-SHA256、17 bytes 的重放 frame，以及逐次呼叫包裝參數、還原
  結果的 `PayloadProcessor`。支援 trimming 與 Native AOT。線上格式就是 Polhem 框架與 polhem-connector-js 已在使用的格式
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
