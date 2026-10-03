namespace Polhem.JsonRpc.Payload;

/// <summary>
/// The exception thrown when the JSON of a payload envelope is not one: it is not an object, a member has the wrong
/// kind of value, or an encoded value is not Base64. A server also throws it for a call in a format below the one the
/// method requires.
/// </summary>
/// <remarks>
/// A server set up with <c>UsePayload</c> (Polhem.JsonRpc.Payload.Server) answers it as invalid parameters, unless the
/// host's own exception mapper answers it first. The message describes what was expected and never echoes the input.
/// </remarks>
public sealed class InvalidPayloadException : Exception
{
    /// <summary>Initializes a new instance with no message.</summary>
    public InvalidPayloadException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The message.</param>
    public InvalidPayloadException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a message and the exception that caused it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public InvalidPayloadException(string message, Exception innerException) : base(message, innerException) { }
}
