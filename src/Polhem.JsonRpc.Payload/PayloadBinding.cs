using System.Text;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// The call an encrypted payload belongs to: the JSON-RPC method and the direction. The HMAC of an encrypted payload
/// covers them, so a captured payload cannot be sent to another method, or a response sent back as a request
/// (ADR-003).
/// </summary>
internal sealed class PayloadBinding
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="direction">Which way the payload travels.</param>
    /// <param name="method">The JSON-RPC method of the call, exactly as the request names it; a response names the method
    /// of the request it answers.</param>
    /// <exception cref="ArgumentException">The direction is not defined, or the method is empty.</exception>
    public PayloadBinding(PayloadDirection direction, string method)
    {
        if (!Enum.IsDefined(direction))
            throw new ArgumentException("The direction of a payload binding is not defined.", nameof(direction));
        ArgumentException.ThrowIfNullOrEmpty(method);
        Direction = direction;
        Method = method;
    }

    /// <summary>Gets which way the payload travels.</summary>
    public PayloadDirection Direction { get; }

    /// <summary>Gets the JSON-RPC method of the call.</summary>
    public string Method { get; }

    /// <summary>Gets the binding of the parameters of a call to a method.</summary>
    /// <param name="method">The JSON-RPC method.</param>
    /// <returns>The binding.</returns>
    public static PayloadBinding Request(string method) => new(PayloadDirection.Request, method);

    /// <summary>Gets the binding of the result of a call to a method.</summary>
    /// <param name="method">The JSON-RPC method of the request the result answers.</param>
    /// <returns>The binding.</returns>
    public static PayloadBinding Response(string method) => new(PayloadDirection.Response, method);

    /// <summary>
    /// Gets the bytes the HMAC covers after the ciphertext: the direction as one byte, then the method in UTF-8.
    /// </summary>
    /// <returns>The associated data.</returns>
    public byte[] ToAssociatedData()
    {
        var data = new byte[1 + Encoding.UTF8.GetByteCount(Method)];
        data[0] = (byte)Direction;
        Encoding.UTF8.GetBytes(Method, data.AsSpan(1));
        return data;
    }
}
