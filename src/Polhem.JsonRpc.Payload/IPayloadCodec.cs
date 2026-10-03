namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Serializes the value of an encoded or encrypted payload to bytes and back. The codec of a payload is named by the
/// <c>codec</c> member of its envelope.
/// </summary>
/// <remarks>
/// An implementation used on a client built with Native AOT must not fall back to reflection-based serialization.
/// <para>
/// A reader takes a body that does not start with the gzip header (<c>1F 8B</c>) as uncompressed. A codec whose output
/// can start with those bytes still works, because a writer compresses such a body, but it cannot benefit from leaving
/// small bodies uncompressed (ADR-002, decision 2, amended).
/// </para>
/// </remarks>
public interface IPayloadCodec
{
    /// <summary>
    /// Gets the name the codec is registered and negotiated under: lower-case letters, digits and hyphens, at most
    /// 32 characters.
    /// </summary>
    string Name { get; }

    /// <summary>Serializes a value.</summary>
    /// <param name="value">The value.</param>
    /// <param name="type">The type to serialize the value as.</param>
    /// <returns>The serialized bytes.</returns>
    byte[] Serialize(object value, Type type);

    /// <summary>Deserializes a value.</summary>
    /// <param name="bytes">The serialized bytes.</param>
    /// <param name="type">The type to deserialize into.</param>
    /// <returns>The value.</returns>
    object? Deserialize(byte[] bytes, Type type);
}
