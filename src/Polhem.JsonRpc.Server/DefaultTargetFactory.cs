namespace Polhem.JsonRpc.Server;

/// <summary>
/// Takes a target from the call's services when it is registered there, and creates it with its parameterless
/// constructor otherwise. Only a target it created itself is disposed after the call.
/// </summary>
internal sealed class DefaultTargetFactory : IJsonRpcTargetFactory
{
    private const string CreatedHereKey = "Polhem.JsonRpc.Server.DefaultTargetFactory.CreatedHere";

    public object CreateTarget(JsonRpcMethod method, JsonRpcRequestContext context)
    {
        var fromServices = context.Services?.GetService(method.TargetType);
        if (fromServices is not null) { return fromServices; }

        var created = Activator.CreateInstance(method.TargetType)
            ?? throw new InvalidOperationException($"Could not create the target '{method.TargetName}'.");
        context.Items[CreatedHereKey] = created;
        return created;
    }

    public async ValueTask ReleaseTargetAsync(object target, JsonRpcRequestContext context)
    {
        if (!context.Items.TryGetValue(CreatedHereKey, out var created) || !ReferenceEquals(created, target)) { return; }
        context.Items.Remove(CreatedHereKey);
        if (target is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (target is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
