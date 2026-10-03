using System.Reflection;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// A resolved method: the object the call runs on and the CLR method to call.
/// </summary>
public sealed class JsonRpcMethod
{
    internal JsonRpcMethod(string progId, string action, object instance, MethodInfo methodInfo)
    {
        ProgId = progId;
        Action = action;
        Instance = instance;
        MethodInfo = methodInfo;
        ParameterType = methodInfo.GetParameters()[0].ParameterType;
    }

    /// <summary>
    /// Gets the ProgId, the part of the method name before the dot.
    /// </summary>
    public string ProgId { get; }

    /// <summary>
    /// Gets the action, the part of the method name after the dot.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Gets the object <see cref="IJsonRpcObjectFactory"/> created for the call.
    /// </summary>
    public object Instance { get; }

    /// <summary>
    /// Gets the method to call.
    /// </summary>
    public MethodInfo MethodInfo { get; }

    /// <summary>
    /// Gets the type of the method's single parameter, which <c>params</c> is bound to.
    /// </summary>
    public Type ParameterType { get; }

    /// <summary>
    /// Determines whether a method has the shape an action needs: public, an instance method, not generic, not a
    /// property or event accessor, not declared by <see cref="object"/>, not an <c>Equals</c> that compares the type
    /// with its own kind (the <see cref="IEquatable{T}"/> method a record declares), and exactly one parameter.
    /// </summary>
    /// <remarks>
    /// Public methods a type inherits from its base types are resolvable, as they are for the Polhem framework's
    /// <c>Type.GetMethod</c>.
    /// </remarks>
    /// <param name="method">The method.</param>
    /// <returns><c>true</c> when the method can be resolved as an action.</returns>
    public static bool IsResolvableAction(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.IsPublic
            && !method.IsStatic
            && !method.IsSpecialName
            && !method.IsGenericMethod
            && method.GetParameters() is [var parameter]
            && method.GetBaseDefinition().DeclaringType != typeof(object)
            && !(method.Name == nameof(Equals) && method.DeclaringType is { } declaring
                && parameter.ParameterType.IsAssignableFrom(declaring));
    }
}
