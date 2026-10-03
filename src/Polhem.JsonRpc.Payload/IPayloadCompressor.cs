namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Compresses the serialized body of an encoded or encrypted payload, and decompresses it.
/// </summary>
/// <remarks>
/// Both ends of a connection must use the same compressor; the envelope does not name it.
/// </remarks>
public interface IPayloadCompressor
{
    /// <summary>Gets the name of the compression method.</summary>
    string Name { get; }

    /// <summary>Compresses data.</summary>
    /// <param name="bytes">The data.</param>
    /// <returns>The compressed data.</returns>
    byte[] Compress(byte[] bytes);

    /// <summary>Decompresses data.</summary>
    /// <param name="bytes">The compressed data.</param>
    /// <returns>The data.</returns>
    byte[] Decompress(byte[] bytes);
}
