using System.Reflection;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Decides which methods may be called over JSON-RPC.
/// </summary>
public interface IJsonRpcMethodPolicy
{
    /// <summary>
    /// Determines whether a method may be called.
    /// </summary>
    /// <param name="method">A public, non-generic instance method with one parameter.</param>
    /// <returns><c>true</c> to admit the method; <c>false</c> to answer as if it did not exist.</returns>
    bool IsCallable(MethodInfo method);
}
