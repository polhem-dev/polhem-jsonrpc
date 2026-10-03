using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// Opens the payload envelope of a request, checks its frame, and seals the result in the same format and codec.
/// </summary>
/// <remarks>
/// It runs after the method is resolved and allowed, and after any filter placed before it, so a body is decrypted only
/// for a call that is allowed to happen. The opened payload is left in the request items as a
/// <see cref="PayloadRequest"/>; <see cref="PayloadParameterBinder"/> binds the parameter from it.
/// </remarks>
public sealed class PayloadFilter : IJsonRpcFilter
{
    private readonly PayloadProcessor _processor;
    private readonly IPayloadServerPolicy _policy;
    private readonly IPayloadReplayStore _replayStore;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="options">The payload settings shared with the clients.</param>
    /// <param name="policy">The application's answers about each call.</param>
    /// <param name="replayStore">Where sequence numbers are remembered; an in-memory store when <see langword="null"/>.</param>
    public PayloadFilter(PayloadOptions options, IPayloadServerPolicy policy, IPayloadReplayStore? replayStore = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _processor = new PayloadProcessor(options);
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _replayStore = replayStore ?? new MemoryPayloadReplayStore(options.FrameTimestampTolerance * 2);
    }

    /// <inheritdoc/>
    public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var cancellationToken = context.CancellationToken;

        var envelope = PayloadEnvelope.Read(context.Request.Params);
        var key = envelope.Format == PayloadFormat.Encrypted
            ? await _policy.GetKeyAsync(context).ConfigureAwait(false)
            : null;

        // The frame rides inside the body, so the replay checks can only run once the body is decrypted.
        var value = _processor.OpenRequest(envelope, _policy.GetPayloadType(context), key, out var frame);
        if (frame != null)
        {
            ValidateTimestamp(frame);
            await ValidateSequenceAsync(context, frame).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        new PayloadRequest(envelope.Format, envelope.Codec, value, frame).Attach(context);

        await next(context).ConfigureAwait(false);

        // A method that returns nothing answers with a plain null: there is no body to encode or protect.
        var format = context.ReturnValue == null ? PayloadFormat.Plain : envelope.Format;
        context.Result = _processor.Seal(context.ReturnValue, format, envelope.Codec, key).ToElement();
    }

    private void ValidateTimestamp(PayloadFrame frame)
    {
        long driftMs = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - frame.TimestampMs);
        if (driftMs > _processor.Options.FrameTimestampTolerance.TotalMilliseconds)
        {
            throw new ReplayRejectedException(
                $"The request timestamp is {driftMs / 1000} seconds away from server time, outside the accepted window. Check the client clock.");
        }
    }

    private async ValueTask ValidateSequenceAsync(JsonRpcRequestContext context, PayloadFrame frame)
    {
        if (_policy.GetReplayScope(context) is not { } scope || !_policy.RequiresUniqueSequence(context)) { return; }

        if (!await _replayStore.TryAcceptAsync(scope, frame.Sequence, context.CancellationToken).ConfigureAwait(false))
        {
            throw new ReplayRejectedException(
                "This request repeats a sequence number the session has already used, or falls outside the accepted range.");
        }
    }
}
