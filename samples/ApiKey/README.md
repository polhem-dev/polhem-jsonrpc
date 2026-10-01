# ApiKey

**English** | [繁體中文](README.zh-TW.md)

The extension points on both sides, in one process: the server rejects calls without the right `X-Api-Key` header,
and the client adds the header.

```bash
dotnet run --project samples/ApiKey
```

## The lines that matter

On the server, a filter runs around every call. It reads the header from `context.Transport.Headers` and rejects the
call by throwing `JsonRpcErrorException` (`ApiKeyFilter.cs`):

```csharp
options.Filters.Add(new ApiKeyFilter(DemoKey));
```

On the client, a header is an HTTP concern, so it is added by a `DelegatingHandler` on the `HttpClient`
(`ApiKeyHandler.cs`); the connector does not need to know:

```csharp
using var http = new HttpClient(new ApiKeyHandler(DemoKey) { InnerHandler = new HttpClientHandler() }) { BaseAddress = endpoint };
```

A filter can also rewrite `context.Request.Params` before the call and `context.Result` after it, and a client
interceptor (`IJsonRpcClientInterceptor`) can do the same on the other side. That is where a host that encrypts or
compresses its payloads does it.
