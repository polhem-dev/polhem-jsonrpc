namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Leaves payload bodies uncompressed.
/// </summary>
public sealed class NoPayloadCompressor : IPayloadCompressor
{
    /// <summary>Gets the shared instance.</summary>
    public static NoPayloadCompressor Instance { get; } = new();

    private NoPayloadCompressor() { }

    /// <inheritdoc/>
    public string Name => "none";

    /// <inheritdoc/>
    public byte[] Compress(byte[] bytes) => bytes ?? throw new ArgumentNullException(nameof(bytes));

    /// <inheritdoc/>
    public byte[] Decompress(byte[] bytes) => bytes ?? throw new ArgumentNullException(nameof(bytes));
}
