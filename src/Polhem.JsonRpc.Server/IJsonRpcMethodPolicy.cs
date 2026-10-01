using System.Reflection;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Decides which methods may be called over JSON-RPC.
/// </summary>
/// <remarks>
/// The default is <see cref="JsonRpcNamingConventionPolicy"/>. A host replaces it to admit methods by its own rule,
/// for example by an attribute that also carries access requirements.
/// </remarks>
public interface IJsonRpcMethodPolicy
{
    /// <summary>
    /// Determines whether a method may be called.
    /// </summary>
    /// <param name="method">A method that satisfies <see cref="JsonRpcMethod.IsResolvableAction"/>.</param>
    /// <returns><c>true</c> to admit the method; <c>false</c> to answer as if it did not exist.</returns>
    bool IsCallable(MethodInfo method);
}
