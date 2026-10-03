using System.Diagnostics.CodeAnalysis;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Names the type of an encoded value for the <c>type</c> member of the envelope, and decides which names a reader
/// accepts.
/// </summary>
/// <remarks>
/// Resolving a type from a name received over the network is how deserialization attacks reach code the application
/// never meant to run, so the package resolves names only through this interface. An implementation that allows a
/// name by a prefix must screen the whole assembly-qualified name, generic arguments included, and not only the part
/// before the first comma: for a generic type that comma sits inside the brackets of the arguments.
/// </remarks>
public interface IPayloadTypeResolver
{
    /// <summary>Gets the name written for a type.</summary>
    /// <param name="type">The type of the encoded value.</param>
    /// <returns>The name.</returns>
    string GetTypeName(Type type);

    /// <summary>
    /// Resolves a name read from an envelope to a type, when the name is allowed. A client uses it to decode a result.
    /// </summary>
    /// <param name="typeName">The name read from the envelope.</param>
    /// <param name="type">The type, when the name is allowed and resolves.</param>
    /// <returns><see langword="true"/> when the name is allowed and resolves.</returns>
    bool TryResolveType(string typeName, [NotNullWhen(true)] out Type? type);

    /// <summary>
    /// Says whether a name read from an envelope is allowed and names <paramref name="type"/>. A server uses it to check
    /// a request against the type it decided to decode into, and a client a result against the type it expects; the
    /// name never chooses that type.
    /// </summary>
    /// <param name="typeName">The name read from the envelope.</param>
    /// <param name="type">The type the reader decodes into: a server's method parameter, or the type a client expects.</param>
    /// <returns><see langword="true"/> when the name is allowed and names the type.</returns>
    bool IsNameOf(string typeName, Type type);
}
