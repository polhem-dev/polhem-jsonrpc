namespace Polhem.JsonRpc.Server;

/// <summary>
/// Creates the object a resolved method is called on.
/// </summary>
public interface IJsonRpcTargetFactory
{
    /// <summary>
    /// Creates the target of a call.
    /// </summary>
    /// <param name="method">The resolved method.</param>
    /// <param name="context">The call.</param>
    /// <returns>An instance of <see cref="JsonRpcMethod.TargetType"/>.</returns>
    object CreateTarget(JsonRpcMethod method, JsonRpcRequestContext context);

    /// <summary>
    /// Releases a target after the call, for example by disposing one the factory created itself.
    /// </summary>
    /// <param name="target">The target <see cref="CreateTarget"/> returned.</param>
    /// <param name="context">The call.</param>
    /// <returns>A task that completes when the target is released.</returns>
    ValueTask ReleaseTargetAsync(object target, JsonRpcRequestContext context) => ValueTask.CompletedTask;
}
