# 變更紀錄

[English](CHANGELOG.md) | **繁體中文**

Polhem.JsonRpc、Polhem.JsonRpc.Server、Polhem.JsonRpc.AspNetCore 與 Polhem.JsonRpc.Client 的重要變更。格式依循
[Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/)，版號依循[語意化版本](https://semver.org/lang/zh-TW/)。

## [Unreleased]

### 新增

- `Polhem.JsonRpc`：request、response、error 與 id 型別，規格定義的錯誤碼，`JsonRpcSerializer`（不靠反射讀寫訊息）
  與 `IJsonRpcTransport`。
- `Polhem.JsonRpc.Server`：`JsonRpcDispatcher`，支援 batch 與 notification。方法名 `ProgId.Action` 會在應用程式的
  `IJsonRpcObjectFactory` 依 ProgId 建立的物件上呼叫該 action；參數型別名為 `{Action}Request`、回傳型別名為
  `{Action}Response` 的 action 才可被呼叫。另有 filter、例外對應與 `InProcessTransport`。
- `Polhem.JsonRpc.AspNetCore`：`AddJsonRpcServer`、`MapJsonRpc` 與 `JsonRpcHttpHandler`。
- `Polhem.JsonRpc.Client`：`JsonRpcConnector`，支援一般呼叫、notification 與 batch，`HttpTransport` 與攔截器。
  支援 trimming 與 Native AOT。
