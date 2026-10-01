namespace Polhem.JsonRpc.Server;

/// <summary>
/// Runs around every call, after the method is resolved and before its parameters are bound.
/// </summary>
/// <remarks>
/// A filter can reject a call by throwing <see cref="JsonRpcErrorException"/>, rewrite
/// <see cref="JsonRpcRequest.Params"/> before calling <c>next</c> (for example to decrypt it), and rewrite
/// <see cref="JsonRpcRequestContext.Result"/> after <c>next</c> returns (for example to encrypt it).
/// </remarks>
public interface IJsonRpcFilter
{
    /// <summary>
    /// Runs the filter.
    /// </summary>
    /// <param name="context">The call.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>A task that completes when the filter and the rest of the pipeline have run.</returns>
    ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next);
}
