using System.Text.Json;

namespace Polhem.JsonRpc.Payload;

public sealed partial class PayloadProcessor
{
    /// <summary>
    /// Reads the result of a call to a method and opens it into <typeparamref name="T"/>, using the <c>type</c> member
    /// only to check that the writer meant the same type. An encrypted result must be bound to the method and to the
    /// response direction.
    /// </summary>
    /// <typeparam name="T">The result type the caller expects.</typeparam>
    /// <param name="method">The JSON-RPC method the call was sent to.</param>
    /// <param name="requestFormat">The format the request was sent in; the result must be in the same format.</param>
    /// <param name="payload">The <c>result</c> element.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <returns>The value; a plain envelope's value is deserialized with <see cref="PayloadOptions.SerializerOptions"/>.</returns>
    /// <exception cref="ArgumentException">The method is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is not allowed or names another type, the key is missing, the body could not be decoded (a body
    /// under an empty type name must be empty), or <see cref="NoPayloadEncryptor"/> is set without
    /// <see cref="PayloadOptions.AllowNoEncryption"/>.
    /// </exception>
    /// <exception cref="InvalidPayloadException">
    /// The element is not an envelope, the result is in another format than the request, or an encoded or encrypted
    /// envelope has no body.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body is malformed or fails authentication, which includes a body bound to another method or
    /// direction, or the key is not the size the encryptor needs.
    /// </exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">
    /// The envelope names a codec that is not registered, or the encryptor does not authenticate associated data.
    /// </exception>
    /// <exception cref="JsonException">A plain value cannot be read as <typeparamref name="T"/>.</exception>
    public T? UnwrapResult<T>(string method, PayloadFormat requestFormat, JsonElement? payload, byte[]? key = null)
    {
        var envelope = PayloadEnvelope.Read(payload);
        EnsureAnswersRequest(envelope, requestFormat);
        return UnwrapCore<T>(envelope, key, PayloadBinding.Response(method));
    }

    /// <summary>
    /// Reads the result of a call to a method and opens it into the type its <c>type</c> member names, which must be
    /// registered with <see cref="PayloadOptions.TypeResolver"/>. An encrypted result must be bound to the method and to
    /// the response direction.
    /// </summary>
    /// <param name="method">The JSON-RPC method the call was sent to.</param>
    /// <param name="requestFormat">The format the request was sent in; the result must be in the same format.</param>
    /// <param name="payload">The <c>result</c> element.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The method is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is not allowed, the key is missing, the body could not be decoded (a body under an empty type name
    /// must be empty), or <see cref="NoPayloadEncryptor"/> is set without <see cref="PayloadOptions.AllowNoEncryption"/>.
    /// </exception>
    /// <exception cref="InvalidPayloadException">
    /// The element is not an envelope, the result is in another format than the request, or an encoded or encrypted
    /// envelope has no body.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body is malformed or fails authentication, which includes a body bound to another method or
    /// direction, or the key is not the size the encryptor needs.
    /// </exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">
    /// The envelope names a codec that is not registered, or the encryptor does not authenticate associated data.
    /// </exception>
    public object? UnwrapResult(string method, PayloadFormat requestFormat, JsonElement? payload, byte[]? key = null)
    {
        var envelope = PayloadEnvelope.Read(payload);
        EnsureAnswersRequest(envelope, requestFormat);
        return OpenResultCore(envelope, key, PayloadBinding.Response(method), out _);
    }

    /// <summary>
    /// Reads an envelope and opens it into <typeparamref name="T"/>, using the <c>type</c> member only to check that the
    /// writer meant the same type.
    /// </summary>
    /// <typeparam name="T">The result type the caller expects.</typeparam>
    /// <param name="payload">The <c>result</c> element.</param>
    /// <param name="key">Not used: an encrypted envelope is refused.</param>
    /// <returns>The value; a plain envelope's value is deserialized with <see cref="PayloadOptions.SerializerOptions"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The envelope is encrypted: an encrypted payload is bound to its method, so use
    /// <see cref="UnwrapResult{T}"/> (ADR-003). Or the type name names another type, or the body could not be decoded
    /// (a body under an empty type name must be empty).
    /// </exception>
    /// <exception cref="InvalidPayloadException">The element is not an envelope, or an encoded envelope has no body.</exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">The envelope names a codec that is not registered.</exception>
    /// <remarks>
    /// The caller chose the type, so nothing needs to be registered with <see cref="PayloadOptions.TypeResolver"/>.
    /// </remarks>
    public T? Unwrap<T>(JsonElement? payload, byte[]? key = null) => UnwrapCore<T>(PayloadEnvelope.Read(payload), key, binding: null);

    /// <summary>
    /// Reads an envelope and opens it into the type its <c>type</c> member names, which must be registered with
    /// <see cref="PayloadOptions.TypeResolver"/>.
    /// </summary>
    /// <param name="payload">The <c>result</c> element.</param>
    /// <param name="key">Not used: an encrypted envelope is refused.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The envelope is encrypted: an encrypted payload is bound to its method, so use <see cref="UnwrapResult"/>
    /// (ADR-003). Or the type name is not allowed, or the body could not be decoded.
    /// </exception>
    /// <exception cref="InvalidPayloadException">The element is not an envelope, or an encoded envelope has no body.</exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">The envelope names a codec that is not registered.</exception>
    public object? Unwrap(JsonElement? payload, byte[]? key = null)
        => OpenResultCore(PayloadEnvelope.Read(payload), key, binding: null, out _);

    /// <summary>
    /// Opens a result envelope into the type its <c>type</c> member names, after checking the name through
    /// <see cref="PayloadOptions.TypeResolver"/>. For a client reading a result from a server it trusts.
    /// </summary>
    /// <remarks>
    /// WARNING: a server never opens a request this way, because the type would then be chosen by the caller. Use
    /// <see cref="OpenRequest(PayloadEnvelope, Type, byte[], string, out PayloadFrame)"/>.
    /// </remarks>
    /// <param name="envelope">The envelope.</param>
    /// <param name="key">Not used: an encrypted envelope is refused.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The envelope is encrypted: an encrypted payload is bound to its method, so use
    /// <see cref="OpenResult(PayloadEnvelope, byte[], string, PayloadFormat, out PayloadFrame)"/> (ADR-003). Or the type
    /// name is not allowed, or the body could not be decoded (a body under an empty type name must be empty).
    /// </exception>
    /// <exception cref="InvalidPayloadException">An encoded envelope has no body.</exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">The envelope names a codec that is not registered.</exception>
    public object? OpenResult(PayloadEnvelope envelope, byte[]? key, out PayloadFrame? frame)
        => OpenResultCore(envelope, key, binding: null, out frame);

    /// <summary>
    /// Opens the result of a call to a method into the type its <c>type</c> member names, after checking the name
    /// through <see cref="PayloadOptions.TypeResolver"/>. For a client reading a result from a server it trusts.
    /// </summary>
    /// <remarks>
    /// WARNING: a server never opens a request this way, because the type would then be chosen by the caller. Use
    /// <see cref="OpenRequest(PayloadEnvelope, Type, byte[], string, out PayloadFrame)"/>.
    /// </remarks>
    /// <param name="envelope">The envelope.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <param name="method">The JSON-RPC method the call was sent to; an encrypted result must be bound to it and to the
    /// response direction.</param>
    /// <param name="requestFormat">The format the request was sent in; the result must be in the same format.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The method is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is not allowed, the key is missing, the body could not be decoded (a body under an empty type name
    /// must be empty), or <see cref="NoPayloadEncryptor"/> is set without <see cref="PayloadOptions.AllowNoEncryption"/>.
    /// </exception>
    /// <exception cref="InvalidPayloadException">
    /// The result is in another format than the request, or an encoded or encrypted envelope has no body.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body is malformed or fails authentication, which includes a body bound to another method or
    /// direction, or the key is not the size the encryptor needs.
    /// </exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">
    /// The envelope names a codec that is not registered, or the encryptor does not authenticate associated data.
    /// </exception>
    public object? OpenResult(PayloadEnvelope envelope, byte[]? key, string method, PayloadFormat requestFormat, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        EnsureAnswersRequest(envelope, requestFormat);
        return OpenResultCore(envelope, key, PayloadBinding.Response(method), out frame);
    }

    /// <summary>
    /// Opens a request envelope into a type the reader chose, using the <c>type</c> member only to check that the writer
    /// meant the same type.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="type">The type to decode into, decided by the reader.</param>
    /// <param name="key">Not used: an encrypted envelope is refused.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>, for the
    /// reader to bind.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The envelope is encrypted: an encrypted payload is bound to its method, so use
    /// <see cref="OpenRequest(PayloadEnvelope, Type, byte[], string, out PayloadFrame)"/> (ADR-003). Or the type name is
    /// missing, not allowed or names another type, or the body could not be decoded.
    /// </exception>
    /// <exception cref="InvalidPayloadException">An encoded envelope has no body.</exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">The envelope names a codec that is not registered.</exception>
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, out PayloadFrame? frame)
        => OpenAs(envelope, type, key, budget: null, binding: null, out frame);

    /// <summary>
    /// Opens the parameters of a call to a method into a type the reader chose, using the <c>type</c> member only to
    /// check that the writer meant the same type. For a server reading the parameters of a method.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="type">The type to decode into, decided by the reader.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <param name="method">The JSON-RPC method of the request; an encrypted body must be bound to it and to the
    /// request direction.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>, for the
    /// reader to bind.
    /// </returns>
    /// <exception cref="ArgumentException">The method is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is missing, not allowed or names another type, the key is missing, the body could not be decoded,
    /// or <see cref="NoPayloadEncryptor"/> is set without <see cref="PayloadOptions.AllowNoEncryption"/>.
    /// </exception>
    /// <exception cref="InvalidPayloadException">An encoded or encrypted envelope has no body.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body is malformed or fails authentication, which includes a body bound to another method or
    /// direction, or the key is not the size the encryptor needs.
    /// </exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">
    /// The envelope names a codec that is not registered, or the encryptor does not authenticate associated data.
    /// </exception>
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, string method, out PayloadFrame? frame)
        => OpenAs(envelope, type, key, budget: null, PayloadBinding.Request(method), out frame);

    /// <summary>
    /// Opens a request envelope like <see cref="OpenRequest(PayloadEnvelope, Type, byte[], out PayloadFrame)"/>, drawing
    /// the decompressed size on a budget the calls of one message share.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="type">The type the server decodes into.</param>
    /// <param name="key">Not used: an encrypted envelope is refused.</param>
    /// <param name="budget">What the message may still decompress; the decompressed size is taken from it.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>The value, as <see cref="OpenRequest(PayloadEnvelope, Type, byte[], out PayloadFrame)"/> returns it.</returns>
    /// <exception cref="InvalidOperationException">
    /// The envelope is encrypted: an encrypted payload is bound to its method, so use
    /// <see cref="OpenRequest(PayloadEnvelope, Type, byte[], PayloadDecompressionBudget, string, out PayloadFrame)"/>
    /// (ADR-003). Or the type name is missing, not allowed or names another type, or the body could not be decoded,
    /// which includes a body that decompresses beyond the budget.
    /// </exception>
    /// <exception cref="InvalidPayloadException">An encoded envelope has no body.</exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">The envelope names a codec that is not registered.</exception>
    /// <remarks>
    /// A body that fails to decompress, for any reason, uses up what is left of the budget, because the failure may
    /// already have cost that much. A later compressed body of the message is then refused by a compressor that
    /// implements <see cref="IPayloadCompressor.Decompress(byte[], long)"/>, as <see cref="GzipPayloadCompressor"/> does;
    /// a body sent uncompressed is already in memory and is still opened.
    /// </remarks>
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, PayloadDecompressionBudget budget, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return OpenAs(envelope, type, key, budget, binding: null, out frame);
    }

    /// <summary>
    /// Opens the parameters of a call to a method, drawing the decompressed size on a budget the calls of one message
    /// share, like <see cref="OpenRequest(PayloadEnvelope, Type, byte[], string, out PayloadFrame)"/>.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="type">The type the server decodes into.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <param name="budget">What the message may still decompress; the decompressed size is taken from it.</param>
    /// <param name="method">The JSON-RPC method of the request; an encrypted body must be bound to it and to the
    /// request direction.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>The value, as <see cref="OpenRequest(PayloadEnvelope, Type, byte[], string, out PayloadFrame)"/> returns it.</returns>
    /// <exception cref="ArgumentException">The method is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is missing, not allowed or names another type, the key is missing, the body could not be decoded,
    /// which includes a body that decompresses beyond the budget, or <see cref="NoPayloadEncryptor"/> is set without
    /// <see cref="PayloadOptions.AllowNoEncryption"/>.
    /// </exception>
    /// <exception cref="InvalidPayloadException">An encoded or encrypted envelope has no body.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body is malformed or fails authentication, which includes a body bound to another method or
    /// direction, or the key is not the size the encryptor needs.
    /// </exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">
    /// The envelope names a codec that is not registered, or the encryptor does not authenticate associated data.
    /// </exception>
    /// <remarks>
    /// The budget is drawn on as in
    /// <see cref="OpenRequest(PayloadEnvelope, Type, byte[], PayloadDecompressionBudget, out PayloadFrame)"/>.
    /// </remarks>
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, PayloadDecompressionBudget budget, string method, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return OpenAs(envelope, type, key, budget, PayloadBinding.Request(method), out frame);
    }

    private T? UnwrapCore<T>(PayloadEnvelope envelope, byte[]? key, PayloadBinding? binding)
    {
        var value = OpenAs(envelope, typeof(T), key, budget: null, binding, out _, isResult: true);
        if (value is JsonElement element)
            value = element.Deserialize(_options.SerializerOptions.GetTypeInfo(typeof(T)));
        return value is null ? default : (T)value;
    }

    private object? OpenResultCore(PayloadEnvelope envelope, byte[]? key, PayloadBinding? binding, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        frame = null;
        if (envelope.Format == PayloadFormat.Plain)
            return envelope.Value;

        if (string.IsNullOrEmpty(envelope.TypeName))
            return Decode(envelope, key, type: null, budget: null, binding, out frame);
        if (!_options.TypeResolver.TryResolveType(envelope.TypeName, out var type))
            throw new InvalidOperationException("The payload type is not in the allowed types.");
        return Decode(envelope, key, type, budget: null, binding, out frame);
    }

    private object? OpenAs(PayloadEnvelope envelope, Type type, byte[]? key, PayloadDecompressionBudget? budget, PayloadBinding? binding, out PayloadFrame? frame, bool isResult = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(type);
        frame = null;
        if (envelope.Format == PayloadFormat.Plain)
            return envelope.Value;

        // Only a result may carry no value; the parameters of a call always name their type.
        if (string.IsNullOrEmpty(envelope.TypeName) && isResult)
            return Decode(envelope, key, type: null, budget, binding, out frame);
        if (string.IsNullOrEmpty(envelope.TypeName))
            throw new InvalidOperationException("The payload names no type to decode into.");
        if (!_options.TypeResolver.IsNameOf(envelope.TypeName, type))
            throw new InvalidOperationException("The payload type does not match the type the reader expects.");
        return Decode(envelope, key, type, budget, binding, out frame);
    }

    // A null type reads a result that carries no value: its body must be empty once decompressed.
    private object? Decode(PayloadEnvelope envelope, byte[]? key, Type? type, PayloadDecompressionBudget? budget, PayloadBinding? binding, out PayloadFrame? frame)
    {
        var bytes = envelope.Body ?? throw new InvalidPayloadException("An encoded payload envelope has no body.");

        if (envelope.Format == PayloadFormat.Encrypted)
        {
            if (key == null || key.Length == 0)
                throw new InvalidOperationException("Missing encryption key for encrypted payload.");
            bytes = Encryptor().Decrypt(bytes, key, AssociatedData(binding));
        }

        // Whether a frame is expected is a deployment decision, never read from the payload: a payload able to declare
        // that it carries no frame would be a downgrade attack.
        frame = _options.RequireFrame ? PayloadFrame.Extract(bytes, out bytes) : null;

        // The codec is read off the envelope, never passed in: the writer named it, and the reader honours what arrived.
        // A result with no type has no body to decode, so its codec is not resolved.
        var codec = type is null ? null : _options.ResolveCodec(envelope.Codec);
        try
        {
            byte[] plain;
            try
            {
                plain = budget is null
                    ? _options.Compressor.Decompress(bytes)
                    : _options.Compressor.Decompress(bytes, budget.Remaining);
            }
            catch (Exception) when (budget is not null)
            {
                // A body that fails to decompress has already cost what it decompressed before failing, up to the whole
                // remainder. Charging the remainder keeps the next call of the batch from starting over at it.
                budget.Spend(budget.Remaining);
                throw;
            }
            budget?.Spend(plain.Length);
            if (type is null)
            {
                return plain.Length == 0
                    ? null
                    : throw new InvalidDataException("A result that names no type carries a body.");
            }
            return codec!.Deserialize(plain, type);
        }
        catch (Exception ex)
        {
            // Boundary: the codec and the compressor are pluggable, so a failure in either is reported as one decoding
            // error, with the original as the inner exception. A catch-all is intentional here.
            throw new InvalidOperationException("An error occurred during the data decoding process.", ex);
        }
    }

    // A server answers in the format of the request, a null result included (ADR-003, decision 6). Without this check a
    // client that sent an encrypted call would take a plain or encoded result that anybody on the way could have written.
    private static void EnsureAnswersRequest(PayloadEnvelope envelope, PayloadFormat requestFormat)
    {
        if (envelope.Format != requestFormat)
            throw new InvalidPayloadException("The result is not in the format of the request.");
    }
}
