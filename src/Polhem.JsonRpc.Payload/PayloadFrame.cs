using System.Buffers.Binary;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// The replay-protection frame put in front of an encoded body: a version byte, then the timestamp in Unix
/// milliseconds and a sequence number, both as big-endian 64-bit integers.
/// </summary>
/// <remarks>
/// The frame is added after the body is encoded and before it is encrypted, so the HMAC of an encrypted payload covers
/// it, and it never appears in the JSON of the envelope, where it could be rewritten. Whether a frame is expected is
/// <see cref="PayloadOptions.RequireFrame"/>, a deployment setting; a payload cannot declare that it carries none.
/// </remarks>
public sealed class PayloadFrame
{
    /// <summary>The frame version this package writes and reads.</summary>
    public const byte CurrentVersion = 1;

    /// <summary>The size in bytes of a version 1 frame.</summary>
    public const int Version1Size = 17;

    /// <summary>Initializes a new frame of the current version.</summary>
    /// <param name="timestampMs">The time the payload was written, in Unix milliseconds.</param>
    /// <param name="sequence">The sequence number of the payload within its session.</param>
    public PayloadFrame(long timestampMs, long sequence)
    {
        TimestampMs = timestampMs;
        Sequence = sequence;
    }

    /// <summary>Gets the frame version.</summary>
    public byte Version { get; } = CurrentVersion;

    /// <summary>Gets the time the payload was written, in Unix milliseconds.</summary>
    public long TimestampMs { get; }

    /// <summary>Gets the sequence number of the payload within its session.</summary>
    public long Sequence { get; }

    /// <summary>Returns the frame followed by <paramref name="body"/>.</summary>
    /// <param name="body">The encoded body.</param>
    /// <returns>A new array holding the frame and the body.</returns>
    public byte[] Prepend(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var framed = new byte[Version1Size + body.Length];
        framed[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(framed.AsSpan(1, 8), TimestampMs);
        BinaryPrimitives.WriteInt64BigEndian(framed.AsSpan(9, 8), Sequence);
        body.CopyTo(framed, Version1Size);
        return framed;
    }

    /// <summary>Reads the frame at the start of <paramref name="framed"/>.</summary>
    /// <param name="framed">The frame followed by the encoded body.</param>
    /// <param name="body">The encoded body after the frame.</param>
    /// <returns>The frame.</returns>
    /// <exception cref="ReplayRejectedException">
    /// The data is shorter than a frame, or the frame has a version this package does not read.
    /// </exception>
    public static PayloadFrame Extract(byte[] framed, out byte[] body)
    {
        ArgumentNullException.ThrowIfNull(framed);

        if (framed.Length < Version1Size)
        {
            throw new ReplayRejectedException(
                "The payload is too short to contain a wire frame. The client is likely older than the server's replay-protection requirement.");
        }

        byte version = framed[0];
        if (version != CurrentVersion)
        {
            throw new ReplayRejectedException(
                $"Unsupported wire frame version {version}. Update the client to match the server.");
        }

        long timestampMs = BinaryPrimitives.ReadInt64BigEndian(framed.AsSpan(1, 8));
        long sequence = BinaryPrimitives.ReadInt64BigEndian(framed.AsSpan(9, 8));
        body = framed[Version1Size..];
        return new PayloadFrame(timestampMs, sequence);
    }
}
