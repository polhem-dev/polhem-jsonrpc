using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// The default <see cref="IPayloadTypeResolver"/>: resolves a name only to a type registered with it, and accepts the
/// name of any type the reader chose itself.
/// </summary>
/// <remarks>
/// A type is named <c>FullName, AssemblyName</c> unless it is registered under a name of its own.
/// <para>
/// Registering is the allow-list for <see cref="TryResolveType"/>, where the name chooses the type: a name that was not
/// registered never resolves, and the registry never loads a type by name. <see cref="IsNameOf"/> needs no
/// registration, because there the reader has already chosen the type, a server from the parameter of the method it
/// resolved; the name is only compared with that type's name. So a server and a client that reads results with
/// <see cref="PayloadProcessor.UnwrapResult{T}"/> register nothing.
/// </para>
/// </remarks>
public sealed class PayloadTypeRegistry : IPayloadTypeResolver
{
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _names = new();

    // `Assembly.GetName()` builds a new `AssemblyName` on every call, and an unregistered type is named on every payload.
    private readonly ConcurrentDictionary<Type, string> _defaultNames = new();

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
    /// <remarks>A type that is not registered gets its default name.</remarks>
    public string GetTypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _names.TryGetValue(type, out var name) ? name : DefaultNameOf(type);
    }

    /// <inheritdoc/>
    public bool TryResolveType(string typeName, [NotNullWhen(true)] out Type? type)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        return _types.TryGetValue(typeName, out type);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The name must be the one <see cref="GetTypeName"/> writes for <paramref name="type"/>: its registered name, or its
    /// default name when it is not registered.
    /// </remarks>
    public bool IsNameOf(string typeName, Type type)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(type);
        return string.Equals(typeName, GetTypeName(type), StringComparison.Ordinal);
    }

    private string DefaultNameOf(Type type) => _defaultNames.GetOrAdd(type, DefaultName);

    private static string DefaultName(Type type) => type.FullName + ", " + type.Assembly.GetName().Name;
}
