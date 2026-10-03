namespace Polhem.JsonRpc.Payload;

/// <summary>
/// The exception thrown when a payload fails replay protection: its frame is missing or unreadable, its timestamp is
/// outside the accepted window, or its sequence number was already used.
/// </summary>
public sealed class ReplayRejectedException : Exception
{
    /// <summary>Initializes a new instance with no message.</summary>
    public ReplayRejectedException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The message.</param>
    public ReplayRejectedException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a message and the exception that caused it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public ReplayRejectedException(string message, Exception innerException) : base(message, innerException) { }
}
