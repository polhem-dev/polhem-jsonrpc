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

    /// <summary>Decompresses data, refusing to produce more than a given size.</summary>
    /// <param name="bytes">The compressed data.</param>
    /// <param name="maxDecompressedBytes">The most bytes the result may hold, on top of any limit of the compressor's own.</param>
    /// <returns>The data.</returns>
    /// <remarks>
    /// A server passes what is left of a message's decompression budget. The default ignores it and calls
    /// <see cref="Decompress(byte[])"/>, so a compressor that does not implement this is bounded only by its own limit.
    /// </remarks>
    byte[] Decompress(byte[] bytes, long maxDecompressedBytes) => Decompress(bytes);
}
