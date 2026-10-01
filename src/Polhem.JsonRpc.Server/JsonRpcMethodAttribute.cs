namespace Polhem.JsonRpc.Server;

/// <summary>
/// Marks a method as callable over JSON-RPC.
/// </summary>
/// <remarks>
/// The default <see cref="IJsonRpcMethodPolicy"/> admits only methods that carry this attribute, so a public method
/// added for internal use is not reachable from the network by accident.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class JsonRpcMethodAttribute : Attribute
{
}
