using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// The default <see cref="IPayloadTypeResolver"/>: accepts only the types registered with it.
/// </summary>
/// <remarks>
/// A type is named <c>FullName, AssemblyName</c> unless it is registered under a name of its own. Registering is the
/// whole allow-list: a name that was not registered never resolves, and the registry never loads a type by name.
/// </remarks>
public sealed class PayloadTypeRegistry : IPayloadTypeResolver
{
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _names = new();

    /// <summary>Registers a type under its default name.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>This registry.</returns>
    public PayloadTypeRegistry Register<T>() => Register(typeof(T));

    /// <summary>Registers a type under its default name.</summary>
    /// <param name="type">The type.</param>
    /// <returns>This registry.</returns>
    public PayloadTypeRegistry Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Register(type, DefaultName(type));
    }

    /// <summary>Registers a type under a name.</summary>
    /// <param name="type">The type.</param>
    /// <param name="typeName">The name written to and accepted from the <c>type</c> member.</param>
    /// <returns>This registry.</returns>
    /// <exception cref="InvalidOperationException">The name or the type is already registered differently.</exception>
    public PayloadTypeRegistry Register(Type type, string typeName)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrEmpty(typeName);

        var registeredType = _types.GetOrAdd(typeName, type);
        if (registeredType != type)
            throw new InvalidOperationException($"The payload type name '{typeName}' is already registered for another type.");
        var registeredName = _names.GetOrAdd(type, typeName);
        if (!string.Equals(registeredName, typeName, StringComparison.Ordinal))
            throw new InvalidOperationException($"The type '{type.FullName}' is already registered under another payload type name.");
        return this;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The type is not registered.</exception>
    public string GetTypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _names.TryGetValue(type, out var name)
            ? name
            : throw new InvalidOperationException($"The type '{type.FullName}' is not registered as a payload type.");
    }

    /// <inheritdoc/>
    public bool TryResolveType(string typeName, [NotNullWhen(true)] out Type? type)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        return _types.TryGetValue(typeName, out type);
    }

    /// <inheritdoc/>
    public bool IsNameOf(string typeName, Type type)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(type);
        return _types.TryGetValue(typeName, out var registered) && registered == type;
    }

    private static string DefaultName(Type type) => type.FullName + ", " + type.Assembly.GetName().Name;
}
