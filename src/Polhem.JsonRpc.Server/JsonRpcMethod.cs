using System.Reflection;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// A resolved JSON-RPC method: the type that serves it and the CLR method to call.
/// </summary>
public sealed class JsonRpcMethod
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcMethod"/> class.
    /// </summary>
    /// <param name="name">The method name as the request gave it.</param>
    /// <param name="targetName">The name of the target, the part before the dot in <c>target.action</c>.</param>
    /// <param name="targetType">The type that serves the method.</param>
    /// <param name="methodInfo">The method to call: an instance method of <paramref name="targetType"/> with one parameter.</param>
    public JsonRpcMethod(string name, string targetName, Type targetType, MethodInfo methodInfo)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(targetName);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(methodInfo);
        var parameters = methodInfo.GetParameters();
        if (parameters.Length != 1)
        {
            throw new ArgumentException("A JSON-RPC method takes exactly one parameter.", nameof(methodInfo));
        }

        Name = name;
        TargetName = targetName;
        TargetType = targetType;
        MethodInfo = methodInfo;
        ParameterType = parameters[0].ParameterType;
    }

    /// <summary>
    /// Gets the method name as the request gave it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the name of the target.
    /// </summary>
    public string TargetName { get; }

    /// <summary>
    /// Gets the type that serves the method.
    /// </summary>
    public Type TargetType { get; }

    /// <summary>
    /// Gets the method to call.
    /// </summary>
    public MethodInfo MethodInfo { get; }

    /// <summary>
    /// Gets the type of the method's single parameter, which <c>params</c> is bound to.
    /// </summary>
    public Type ParameterType { get; }
}
