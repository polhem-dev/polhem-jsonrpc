using System.IO.Compression;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Compresses payload bodies with gzip, and refuses to decompress beyond a size limit.
/// </summary>
/// <remarks>
/// Two limits guard against a small body that expands: the decompressed size may exceed neither
/// <see cref="MaxDecompressedBytes"/> nor <see cref="MaxCompressionRatio"/> times the compressed size. A body that
/// decompresses to 1 MiB or less is never refused for its ratio, since a short body may compress far better than JSON
/// usually does. Both limits apply per body: a batch makes the server decompress each of its bodies up to them, and an
/// encoded body is decompressed without a key.
/// </remarks>
public sealed class GzipPayloadCompressor : IPayloadCompressor
{
    /// <summary>The default limit on the decompressed size: 50 MiB.</summary>
    public const long DefaultMaxDecompressedBytes = 50L * 1024 * 1024;

    /// <summary>The default limit on the decompressed size relative to the compressed size: 100 times.</summary>
    public const int DefaultMaxCompressionRatio = 100;

    private const long RatioExemptBytes = 1024 * 1024;

    /// <summary>Initializes a new instance with the default limits.</summary>
    public GzipPayloadCompressor() : this(DefaultMaxDecompressedBytes) { }

    /// <summary>Initializes a new instance with the default compression ratio limit.</summary>
    /// <param name="maxDecompressedBytes">The largest decompressed size accepted, in bytes.</param>
    public GzipPayloadCompressor(long maxDecompressedBytes) : this(maxDecompressedBytes, DefaultMaxCompressionRatio) { }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="maxDecompressedBytes">The largest decompressed size accepted, in bytes.</param>
    /// <param name="maxCompressionRatio">The largest decompressed size accepted, as a multiple of the compressed size.</param>
    public GzipPayloadCompressor(long maxDecompressedBytes, int maxCompressionRatio)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCompressionRatio);
        MaxDecompressedBytes = maxDecompressedBytes;
        MaxCompressionRatio = maxCompressionRatio;
    }

    /// <summary>Gets the largest decompressed size accepted, in bytes.</summary>
    public long MaxDecompressedBytes { get; }

    /// <summary>Gets the largest decompressed size accepted, as a multiple of the compressed size.</summary>
    public int MaxCompressionRatio { get; }

    /// <inheritdoc/>
    public string Name => "gzip";

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
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        long limit = Math.Min(MaxDecompressedBytes, Math.Max(RatioExemptBytes, (long)bytes.Length * MaxCompressionRatio));
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
