using System.Reflection;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// The default method policy: a method is callable when it carries <see cref="JsonRpcMethodAttribute"/>.
/// </summary>
public sealed class JsonRpcMethodAttributePolicy : IJsonRpcMethodPolicy
{
    /// <inheritdoc/>
    public bool IsCallable(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.IsDefined(typeof(JsonRpcMethodAttribute), inherit: true);
    }
}
