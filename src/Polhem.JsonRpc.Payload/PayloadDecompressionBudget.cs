namespace Polhem.JsonRpc.Payload;

/// <summary>
/// What is left of the bytes a server will decompress for one message, shared by the calls of a batch.
/// </summary>
/// <remarks>
/// An encoded body is decompressed without a key, so a small request could otherwise make the server decompress the
/// per-body limit once for every call of a batch. <see cref="PayloadProcessor.OpenRequest(PayloadEnvelope, Type, byte[], PayloadDecompressionBudget, out PayloadFrame)"/>
/// draws on it. It is not thread-safe: the calls of a batch are opened one after another.
/// </remarks>
public sealed class PayloadDecompressionBudget
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="bytes">The bytes the message may decompress in total.</param>
    public PayloadDecompressionBudget(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        Remaining = bytes;
    }

    /// <summary>Gets the bytes the message may still decompress.</summary>
    public long Remaining { get; private set; }

    internal void Spend(long bytes) => Remaining = Math.Max(0, Remaining - bytes);
}
