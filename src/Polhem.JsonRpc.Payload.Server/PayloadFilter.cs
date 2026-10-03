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
    private readonly TimeProvider _clock;
    private readonly TimeSpan _tolerance;
    private readonly long _maxDecompressedBytesPerMessage;

    // The key of the message's decompression budget in JsonRpcRequestContext.MessageItems.
    private const string BudgetItem = "Polhem.JsonRpc.Payload.DecompressionBudget";

    /// <summary>Initializes a new instance.</summary>
    /// <remarks>
    /// <see cref="PayloadOptions.FrameTimestampTolerance"/>, <see cref="PayloadOptions.TimeProvider"/> and
    /// <see cref="PayloadOptions.MaxDecompressedBytesPerMessage"/> are read here, once. The tolerance and the clock go
    /// together with the lifetime of the in-memory replay store they decide: a tolerance raised later would let a frame
    /// outlive the scope that remembers its sequence number.
    /// </remarks>
    /// <param name="options">The payload settings shared with the clients.</param>
    /// <param name="policy">The application's answers about each call.</param>
    /// <param name="replayStore">Where sequence numbers are remembered; an in-memory store when <see langword="null"/>.</param>
    public PayloadFilter(PayloadOptions options, IPayloadServerPolicy policy, IPayloadReplayStore? replayStore = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _processor = new PayloadProcessor(options);
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _clock = options.TimeProvider;
        _tolerance = options.FrameTimestampTolerance;
        _maxDecompressedBytesPerMessage = options.MaxDecompressedBytesPerMessage;
        _replayStore = replayStore ?? new MemoryPayloadReplayStore(_tolerance * 2, _clock);
    }

    /// <inheritdoc/>
    public async ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var cancellationToken = context.CancellationToken;

        var envelope = PayloadEnvelope.Read(context.Request.Params);
        if (envelope.Format < _policy.GetMinimumFormat(context))
        {
            throw new InvalidPayloadException("The method requires a more protected payload format.");
        }
        // Only an encrypted frame is covered by the HMAC, so only an encrypted call can prove its sequence number is new.
        // A method that requires unique sequence numbers refuses the others rather than letting them repeat unchecked.
        if (envelope.Format != PayloadFormat.Encrypted && _policy.RequiresUniqueSequence(context))
        {
            throw new InvalidPayloadException("The method requires an encrypted payload, whose sequence number can be checked.");
        }
        var key = envelope.Format == PayloadFormat.Encrypted
            ? await _policy.GetKeyAsync(context).ConfigureAwait(false)
            : null;

        // The frame rides inside the body, so the replay checks can only run once the body is decrypted.
        if (!context.MessageItems.TryGetValue(BudgetItem, out var item) || item is not PayloadDecompressionBudget budget)
        {
            budget = new PayloadDecompressionBudget(_maxDecompressedBytesPerMessage);
            context.MessageItems[BudgetItem] = budget;
        }
        var value = _processor.OpenRequest(envelope, _policy.GetPayloadType(context), key, budget, out var frame);
        if (frame != null)
        {
            ValidateTimestamp(frame);
            // Only an encrypted frame is covered by the HMAC. Anybody can write the frame of an encoded body, so recording
            // its sequence number would let a forged call move the scope's window and lock out the genuine ones.
            if (envelope.Format == PayloadFormat.Encrypted)
            {
                await ValidateSequenceAsync(context, frame).ConfigureAwait(false);
            }
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
        // The difference is taken in double: the timestamp comes from the caller, and `now - TimestampMs` overflows a long
        // for a timestamp near either end of its range.
        double driftMs = Math.Abs((double)_clock.GetUtcNow().ToUnixTimeMilliseconds() - frame.TimestampMs);
        if (driftMs > _tolerance.TotalMilliseconds)
        {
            throw new ReplayRejectedException(
                $"The request timestamp is {Math.Floor(driftMs / 1000)} seconds away from server time, outside the accepted window. Check the client clock.");
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
