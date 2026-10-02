namespace Polhem.JsonRpc.Server;

/// <summary>
/// Turns the <c>params</c> member of a request into the argument of the resolved method.
/// </summary>
public interface IJsonRpcParameterBinder
{
    /// <summary>
    /// Binds the parameters of a call.
    /// </summary>
    /// <param name="context">The call; <see cref="JsonRpcRequestContext.Method"/> is resolved.</param>
    /// <returns>The argument for the method's single parameter.</returns>
    /// <exception cref="JsonRpcErrorException">The parameters do not fit the method.</exception>
    object? Bind(JsonRpcRequestContext context);
}
