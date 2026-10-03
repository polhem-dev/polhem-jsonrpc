using System.IO.Compression;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Compresses payload bodies with gzip, and refuses to decompress beyond a size limit.
/// </summary>
/// <remarks>
/// The limit guards against a small body that expands without bound. It applies to each body: a batch makes the server
/// decompress each of its bodies up to it, and an encoded body is decompressed without a key, so a deployment that cares
/// requires encrypted calls (<c>IPayloadServerPolicy.GetMinimumFormat</c>).
/// <para>
/// A body that does not start with the gzip header is read as it is, uncompressed. JSON and MessagePack bodies never
/// start with those bytes, so a writer may leave a small body uncompressed once every reader accepts that
/// (ADR-002, decision 2, amended).
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
    public byte[] Decompress(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
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
            if (total > MaxDecompressedBytes)
            {
                throw new InvalidDataException(
                    $"The decompressed payload exceeds the limit of {MaxDecompressedBytes} bytes.");
            }
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
