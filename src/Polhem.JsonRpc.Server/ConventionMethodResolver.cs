using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Resolves <c>target.action</c>: the target registered under <c>target</c>, and its method <c>action</c>.
/// </summary>
/// <remarks>
/// The method must satisfy <see cref="IsResolvableMethod"/>, be the only such method with that name (an overload
/// would make the name ambiguous), and be admitted by the method policy. Target and method names are compared
/// case-insensitively, so <c>math.add</c> finds a C# method named <c>Add</c>; two methods whose names differ only
/// in case make the name ambiguous.
/// </remarks>
public sealed class ConventionMethodResolver : IJsonRpcMethodResolver
{
    private const int MaxNamePartLength = 64;

    private readonly IReadOnlyDictionary<string, Type> _targets;
    private readonly IJsonRpcMethodPolicy _policy;
    private readonly ConcurrentDictionary<(Type Type, string Action), MethodInfo?> _methods = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConventionMethodResolver"/> class.
    /// </summary>
    /// <param name="targets">The target types, by name.</param>
    /// <param name="policy">Which methods may be called.</param>
    public ConventionMethodResolver(IReadOnlyDictionary<string, Type> targets, IJsonRpcMethodPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(policy);
        _targets = new Dictionary<string, Type>(targets, StringComparer.OrdinalIgnoreCase);
        _policy = policy;
    }

    /// <summary>
    /// Determines whether a method has the shape a JSON-RPC method needs: public, an instance method, not generic,
    /// not a property or event accessor, not declared by <see cref="object"/>, and exactly one parameter.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns><c>true</c> when the method can be resolved.</returns>
    public static bool IsResolvableMethod(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.IsPublic
            && !method.IsStatic
            && !method.IsSpecialName
            && !method.IsGenericMethodDefinition
            && method.GetParameters().Length == 1
            && method.GetBaseDefinition().DeclaringType != typeof(object);
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Target types are registered through AddTarget, whose parameter keeps their public methods.")]
    public JsonRpcMethod? Resolve(JsonRpcRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var name = context.Request.Method;

        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0) { return null; }
        var targetName = name[..dot];
        var action = name[(dot + 1)..];
        if (!IsValidName(targetName, allowHyphen: true) || !IsValidName(action, allowHyphen: false))
        {
            return null;
        }

        // The specification reserves method names that begin with "rpc." for rpc-internal methods.
        if (string.Equals(targetName, "rpc", StringComparison.OrdinalIgnoreCase)) { return null; }

        if (!_targets.TryGetValue(targetName, out var targetType)) { return null; }

        var method = _methods.GetOrAdd((targetType, action), key => FindMethod(key.Type, key.Action));
        return method is null ? null : new JsonRpcMethod(name, targetName, targetType, method);
    }

    internal static bool IsValidName(string? part, bool allowHyphen)
    {
        if (string.IsNullOrEmpty(part) || part.Length > MaxNamePartLength) { return false; }
        foreach (var c in part)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || (allowHyphen && c == '-'))) { return false; }
        }
        return true;
    }

    private MethodInfo? FindMethod([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type, string action)
    {
        MethodInfo? found = null;
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, action, StringComparison.OrdinalIgnoreCase) || !IsResolvableMethod(method))
            {
                continue;
            }
            if (found is not null) { return null; }
            found = method;
        }
        return found is not null && _policy.IsCallable(found) ? found : null;
    }
}
