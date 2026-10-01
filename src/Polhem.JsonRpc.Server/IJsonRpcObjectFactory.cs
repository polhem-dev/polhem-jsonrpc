namespace Polhem.JsonRpc.Server;

/// <summary>
/// Creates the object a call runs on, from the ProgId of its method name.
/// </summary>
/// <remarks>
/// A method name has the form <c>ProgId.Action</c>: the ProgId names the object, and the action names the method
/// called on it. The application implements this interface to decide which object a ProgId stands for, for example
/// from a switch, a configuration file or a registry of types.
/// </remarks>
public interface IJsonRpcObjectFactory
{
    /// <summary>
    /// Creates the object for a ProgId.
    /// </summary>
    /// <param name="progId">The ProgId, the part of the method name before the dot.</param>
    /// <param name="context">The call.</param>
    /// <returns>The object, or <c>null</c> when the ProgId is unknown, which is answered with
    /// <see cref="JsonRpcErrorCodes.MethodNotFound"/>.</returns>
    object? CreateObject(string progId, JsonRpcRequestContext context);

    /// <summary>
    /// Releases an object after the call, for example by disposing it.
    /// </summary>
    /// <param name="instance">The object <see cref="CreateObject"/> returned.</param>
    /// <param name="context">The call.</param>
    /// <returns>A task that completes when the object is released.</returns>
    ValueTask ReleaseObjectAsync(object instance, JsonRpcRequestContext context) => ValueTask.CompletedTask;
}
