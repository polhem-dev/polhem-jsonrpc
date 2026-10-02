# QuickStart.Server

**English** | [繁體中文](README.zh-TW.md)

A JSON-RPC server on ASP.NET Core minimal APIs. It answers `Calculator.Add`, `Calculator.Divide` and
`Calculator.Log` at `http://localhost:5080/api`.

```bash
dotnet run --project samples/QuickStart.Server
```

## The lines that matter

A method name has the form `ProgId.Action`. The ProgId names the object, and the application decides which object
that is, in its own factory (`AppObjectFactory.cs`):

```csharp
public object? CreateObject(string progId, JsonRpcRequestContext context) => progId switch
{
    "Calculator" => new Calculator(),
    _ => null,
};
```

`Program.cs` registers the factory and maps the endpoint. No method is registered:

```csharp
builder.Services.AddSingleton<IJsonRpcObjectFactory, AppObjectFactory>();
builder.Services.AddJsonRpcServer();
app.MapJsonRpc("/api");
```

The action is a method of the object (`Calculator.cs`). It is callable because its parameter is named
`{Action}Request` and its result `{Action}Response`; names are matched case-sensitively.

```csharp
public AddResponse Add(AddRequest request) => new() { Sum = request.A + request.B };
```

- `params` is deserialized into the request, and the response becomes `result`.
- The request and response classes live in [QuickStart.Contracts](../QuickStart.Contracts), which the client
  references too, so both sides use the same types.
- Throw `JsonRpcErrorException` to answer with a specific error, as `Divide` does. Any other exception is answered with
  `-32603 Internal error`, without its message.

Call it with [QuickStart.Client](../QuickStart.Client/README.md).
