using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// The opened payload of a call, left in the request items by <see cref="PayloadFilter"/> for the parameter binder and
/// any filter that runs after it.
/// </summary>
public sealed class PayloadRequest
{
    private const string ItemKey = "Polhem.JsonRpc.Payload.Server.PayloadRequest";

    internal PayloadRequest(PayloadFormat format, string codec, byte[]? key, object? value, PayloadFrame? frame)
    {
        Format = format;
        Codec = codec;
        Key = key;
        Value = value;
        Frame = frame;
    }

    /// <summary>Gets the format the request arrived in; the result is written in the same format.</summary>
    public PayloadFormat Format { get; }

    /// <summary>Gets the codec the request named; the result names the same one.</summary>
    public string Codec { get; }

    /// <summary>Gets the key of an encrypted call, or <see langword="null"/>.</summary>
    public byte[]? Key { get; }

    /// <summary>
    /// Gets the value: the decoded body of an encoded or encrypted request, or the JSON value of a plain one, which the
    /// binder deserializes.
    /// </summary>
    public object? Value { get; }

    /// <summary>Gets the frame of the request, or <see langword="null"/> when frames are not required.</summary>
    public PayloadFrame? Frame { get; }

    /// <summary>Finds the opened payload of a call.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>The payload, or <see langword="null"/> when <see cref="PayloadFilter"/> has not run.</returns>
    public static PayloadRequest? Find(JsonRpcRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(ItemKey, out var value) ? value as PayloadRequest : null;
    }

    internal void Attach(JsonRpcRequestContext context) => context.Items[ItemKey] = this;
}
