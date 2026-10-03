using System.IO.Compression;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Compresses payload bodies with gzip, and refuses to decompress beyond a size limit.
/// </summary>
/// <remarks>
/// The limit guards against a small body that expands without bound. It applies to each body. A server also bounds what
/// one message decompresses in total, the call alone or all the calls of a batch, with
/// <see cref="PayloadOptions.MaxDecompressedBytesPerMessage"/>; a body stops at whichever of the two it reaches first.
/// An encoded body is decompressed without a key, so a deployment that cares requires encrypted calls
/// (<c>IPayloadServerPolicy.GetMinimumFormat</c>).
/// <para>
/// A body that does not start with the gzip header (<c>1F 8B</c>) is read as it is, uncompressed. A body written by the
/// built-in JSON codec, or by MessagePack, never starts with those bytes; a custom codec may, so a writer that leaves a
/// body uncompressed must still compress one that starts with them. Writers leave small bodies uncompressed only once
/// every reader accepts that (ADR-002, decision 2, amended).
/// </para>
/// </remarks>
public sealed class GzipPayloadCompressor : IPayloadCompressor
{
    /// <summary>The default limit on the decompressed size: 50 MiB.</summary>
    public const long DefaultMaxDecompressedBytes = 50L * 1024 * 1024;

    /// <summary>Initializes a new instance with the default size limit.</summary>
    public GzipPayloadCompressor() : this(DefaultMaxDecompressedBytes) { }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="maxDecompressedBytes">The largest decompressed size accepted, in bytes.</param>
    public GzipPayloadCompressor(long maxDecompressedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecompressedBytes);
        MaxDecompressedBytes = maxDecompressedBytes;
    }

    /// <summary>Gets the largest decompressed size accepted, in bytes.</summary>
    public long MaxDecompressedBytes { get; }

    /// <inheritdoc/>
    public string Name => "gzip";

    // The two bytes every gzip member starts with (RFC 1952).
    private static ReadOnlySpan<byte> GzipHeader => [0x1F, 0x8B];

    /// <inheritdoc/>
    public byte[] Compress(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes);
        }
        return output.ToArray();
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidDataException">The data is not gzip, or decompresses beyond the size limit.</exception>
    public byte[] Decompress(byte[] bytes) => Decompress(bytes, MaxDecompressedBytes);

    /// <inheritdoc/>
    /// <exception cref="InvalidDataException">
    /// The data is not gzip, or decompresses beyond the smaller of <paramref name="maxDecompressedBytes"/> and
    /// <see cref="MaxDecompressedBytes"/>.
    /// </exception>
    public byte[] Decompress(byte[] bytes, long maxDecompressedBytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        long limit = Math.Min(maxDecompressedBytes, MaxDecompressedBytes);
        if (!bytes.AsSpan().StartsWith(GzipHeader)) { return bytes; }

        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        long total = 0;
        int count;
        while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += count;
            if (total > limit)
            {
                throw new InvalidDataException($"The decompressed payload exceeds the limit of {limit} bytes.");
            }
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
