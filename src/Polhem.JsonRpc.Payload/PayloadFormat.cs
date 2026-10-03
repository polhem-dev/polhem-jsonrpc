namespace Polhem.JsonRpc.Payload;

/// <summary>
/// How the value of a payload envelope is carried. The numeric values are written to the wire.
/// </summary>
public enum PayloadFormat
{
    /// <summary>The value is ordinary JSON.</summary>
    Plain = 0,

    /// <summary>The value is serialized by a codec, compressed and written as a Base64 string.</summary>
    Encoded = 1,

    /// <summary>The value is encoded as for <see cref="Encoded"/>, then encrypted.</summary>
    Encrypted = 2,
}
