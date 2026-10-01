namespace Polhem.JsonRpc.Server;

/// <summary>
/// Finds the method a request names.
/// </summary>
public interface IJsonRpcMethodResolver
{
    /// <summary>
    /// Resolves a method name.
    /// </summary>
    /// <param name="context">The call; <see cref="JsonRpcRequestContext.Request"/> holds the method name.</param>
    /// <returns>The method, or <c>null</c> to answer with <see cref="JsonRpcErrorCodes.MethodNotFound"/>.</returns>
    JsonRpcMethod? Resolve(JsonRpcRequestContext context);
}
