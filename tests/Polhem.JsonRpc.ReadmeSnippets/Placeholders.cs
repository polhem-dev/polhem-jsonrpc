namespace Polhem.JsonRpc.ReadmeSnippets;

// Types and values the snippets refer to without declaring them.
public sealed class MyPayloadPolicy : IPayloadServerPolicy
{
    public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context) => ValueTask.FromResult<byte[]?>(null);
}

public sealed class MyObjectFactory : IJsonRpcObjectFactory
{
    public object? CreateObject(string progId, JsonRpcRequestContext context) => null;
}

public sealed class MyAccessFilter : IJsonRpcFilter
{
    public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next) => next(context);
}
