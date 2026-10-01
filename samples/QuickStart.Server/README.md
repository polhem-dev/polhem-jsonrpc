# QuickStart.Server

**English** | [繁體中文](README.zh-TW.md)

A JSON-RPC server on ASP.NET Core minimal APIs. It answers `math.add`, `math.divide` and `math.log` at
`http://localhost:5080/api`.

```bash
dotnet run --project samples/QuickStart.Server
```

## The lines that matter

`Program.cs` registers a target and maps the endpoint:

```csharp
builder.Services.AddJsonRpcServer(options => options.AddTarget<Calculator>("math"));
app.MapJsonRpc("/api");
```

`Calculator.cs` is the target. A method is callable when it is a public instance method with one parameter and
carries `[JsonRpcMethod]`. `math.add` calls `Add`: names are matched case-insensitively.

```csharp
[JsonRpcMethod]
public int Add(AddArgs args) => args.A + args.B;
```

- `params` is deserialized into the method's parameter (`AddArgs`), and the return value becomes `result`.
- A target can take services in its constructor: `Calculator` takes an `ILogger`.
- Throw `JsonRpcErrorException` to answer with a specific error, as `Divide` does. Any other exception is answered with
  `-32603 Internal error`, without its message.

Call it with [QuickStart.Client](../QuickStart.Client/README.md).
