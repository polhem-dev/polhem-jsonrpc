using System.Text.Json;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Seals values into payload envelopes and opens them again: serialize, compress, frame and encrypt on the way out, and
/// the reverse on the way in.
/// </summary>
/// <remarks>
/// A client wraps its parameters with <see cref="WrapRequest"/> before calling <c>JsonRpcConnector.InvokeAsync</c> and unwraps
/// the result with <see cref="UnwrapResult{T}"/>, or with <see cref="UnwrapResult"/> when it does not know the result type. Everything that differs per call (the format, the codec, the key and the
/// sequence number) is passed in; the processor keeps no per-call state and can be shared.
/// <para>
/// An encrypted payload is bound to its call: <see cref="WrapRequest"/>, <see cref="SealResponse"/>,
/// <see cref="UnwrapResult{T}"/> and the overloads of <c>OpenRequest</c> and <c>OpenResult</c> that take the method
/// write and check the method and the direction under the HMAC (ADR-003). The methods that do not take one refuse an
/// encrypted payload.
/// </para>
/// </remarks>
public sealed class PayloadProcessor
{
    private readonly PayloadOptions _options;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="options">The settings shared with the other end.</param>
    public PayloadProcessor(PayloadOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Gets the settings.</summary>
    public PayloadOptions Options => _options;

    /// <summary>Seals a value into an envelope and writes it as a JSON element.</summary>
    /// <param name="value">The value.</param>
    /// <param name="format">The format.</param>
    /// <param name="codec">The codec to name; empty or <see langword="null"/> for <see cref="PayloadOptions.DefaultCodec"/>.</param>
    /// <param name="key">The key; required when <paramref name="format"/> is <see cref="PayloadFormat.Encrypted"/>.</param>
    /// <param name="sequence">The sequence number written to the frame, when frames are required.</param>
    /// <returns>The <c>params</c> element.</returns>
    /// <exception cref="InvalidOperationException">The format is <see cref="PayloadFormat.Encrypted"/>: an encrypted
    /// payload is bound to its method, so use <see cref="WrapRequest"/> (ADR-003).</exception>
    public JsonElement Wrap(object? value, PayloadFormat format, string? codec = null, byte[]? key = null, long sequence = 0)
        => SealCore(value, format, binding: null, codec, key, sequence).ToElement();

    /// <summary>
    /// Seals the parameters of a call to a method into an envelope, and writes it as a JSON element. An encrypted
    /// envelope is bound to the method and to the request direction (ADR-003).
    /// </summary>
    /// <param name="method">The JSON-RPC method the parameters are sent to, exactly as the request names it. It must not
    /// be empty, whatever the format.</param>
    /// <param name="value">The value.</param>
    /// <param name="format">The format.</param>
    /// <param name="codec">The codec to name; empty or <see langword="null"/> for <see cref="PayloadOptions.DefaultCodec"/>.</param>
    /// <param name="key">The key; required when <paramref name="format"/> is <see cref="PayloadFormat.Encrypted"/>.</param>
    /// <param name="sequence">The sequence number written to the frame, when frames are required.</param>
    /// <returns>The <c>params</c> element.</returns>
    /// <exception cref="InvalidOperationException">
    /// An encoded value is <see langword="null"/>, an encrypted one has no key, or the value could not be encoded.
    /// </exception>
    public JsonElement WrapRequest(string method, object? value, PayloadFormat format, string? codec = null, byte[]? key = null, long sequence = 0)
        => SealCore(value, format, PayloadBinding.Request(method), codec, key, sequence).ToElement();

    /// <summary>
    /// Seals the result of a call into an envelope. An encrypted envelope is bound to the method of the request it
    /// answers and to the response direction (ADR-003).
    /// </summary>
    /// <param name="method">The JSON-RPC method of the request the result answers.</param>
    /// <param name="value">The value. It may be <see langword="null"/>: a null result is sealed in the format too, with no type and an empty body.</param>
    /// <param name="format">The format.</param>
    /// <param name="codec">The codec to name; empty or <see langword="null"/> for <see cref="PayloadOptions.DefaultCodec"/>.</param>
    /// <param name="key">The key; required when <paramref name="format"/> is <see cref="PayloadFormat.Encrypted"/>.</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="InvalidOperationException">
    /// An encoded value is <see langword="null"/>, an encrypted one has no key, or the value could not be encoded.
    /// </exception>
    public PayloadEnvelope SealResponse(string method, object? value, PayloadFormat format, string? codec = null, byte[]? key = null)
        => SealCore(value, format, PayloadBinding.Response(method), codec, key, sequence: 0);

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
    /// <exception cref="InvalidOperationException">
    /// The type name is missing or names another type, the key is missing, or the body could not be decoded.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body fails authentication, which includes a body bound to another method or direction.
    /// </exception>
    /// <exception cref="InvalidPayloadException">The result is in another format than the request.</exception>
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
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body fails authentication, which includes a body bound to another method or direction.
    /// </exception>
    /// <exception cref="InvalidPayloadException">The result is in another format than the request.</exception>
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
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <returns>The value; a plain envelope's value is deserialized with <see cref="PayloadOptions.SerializerOptions"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The type name is missing or names another type, or the key is missing.
    /// </exception>
    /// <remarks>
    /// The caller chose the type, so nothing needs to be registered with <see cref="PayloadOptions.TypeResolver"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The envelope is encrypted: an encrypted payload is bound to its method, so use the method that takes it (ADR-003).</exception>
    public T? Unwrap<T>(JsonElement? payload, byte[]? key = null) => UnwrapCore<T>(PayloadEnvelope.Read(payload), key, binding: null);


    private T? UnwrapCore<T>(PayloadEnvelope envelope, byte[]? key, PayloadBinding? binding)
    {
        var value = OpenAs(envelope, typeof(T), key, budget: null, binding, out _, isResult: true);
        if (value is JsonElement element)
            value = element.Deserialize(_options.SerializerOptions.GetTypeInfo(typeof(T)));
        return value is null ? default : (T)value;
    }

    /// <summary>
    /// Reads an envelope and opens it into the type its <c>type</c> member names, which must be registered with
    /// <see cref="PayloadOptions.TypeResolver"/>.
    /// </summary>
    /// <param name="payload">The <c>result</c> element.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">The envelope is encrypted: an encrypted payload is bound to its method, so use the method that takes it (ADR-003).</exception>
    public object? Unwrap(JsonElement? payload, byte[]? key = null)
        => OpenResultCore(PayloadEnvelope.Read(payload), key, binding: null, out _);


    /// <summary>Seals a value into an envelope.</summary>
    /// <param name="value">The value. It may be <see langword="null"/> only for a plain envelope.</param>
    /// <param name="format">The format.</param>
    /// <param name="codec">The codec to name; empty or <see langword="null"/> for <see cref="PayloadOptions.DefaultCodec"/>.</param>
    /// <param name="key">The key; required when <paramref name="format"/> is <see cref="PayloadFormat.Encrypted"/>.</param>
    /// <param name="sequence">The sequence number written to the frame, when frames are required.</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="InvalidOperationException">
    /// An encoded value is <see langword="null"/>, an encrypted one has no key, or the value could not be encoded.
    /// </exception>
    /// <remarks>
    /// The body is serialized as the value's runtime type, and that type is named through
    /// <see cref="PayloadOptions.TypeResolver"/>. The codec name is written as given, so that a reader resolves the codec
    /// the writer used; an empty name is left out of the envelope.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The envelope is encrypted: an encrypted payload is bound to its method, so use the method that takes it (ADR-003).</exception>
    public PayloadEnvelope Seal(object? value, PayloadFormat format, string? codec = null, byte[]? key = null, long sequence = 0)
        => SealCore(value, format, binding: null, codec, key, sequence);


    private PayloadEnvelope SealCore(object? value, PayloadFormat format, PayloadBinding? binding, string? codec, byte[]? key, long sequence)
    {
        codec ??= string.Empty;
        if (format == PayloadFormat.Plain)
        {
            JsonElement? plain = value == null
                ? null
                : JsonSerializer.SerializeToElement(value, _options.SerializerOptions.GetTypeInfo(value.GetType()));
            return new PayloadEnvelope { Value = plain };
        }

        // A result answers in the format of its request, so a null result is sealed too: with no type and an empty body,
        // which reads the same whatever the codec (ADR-003, decision 6). Parameters are never null.
        bool emptyResult = value == null && binding?.Direction == PayloadDirection.Response;
        if (value == null && !emptyResult)
            throw new InvalidOperationException("An encoded payload needs a value.");
        if (format == PayloadFormat.Encrypted && (key == null || key.Length == 0))
            throw new InvalidOperationException("An encrypted payload needs a key.");
        var associatedData = format == PayloadFormat.Encrypted ? AssociatedData(binding) : null;

        string typeName;
        byte[] bytes;
        if (emptyResult)
        {
            typeName = string.Empty;
            bytes = Compress([]);
        }
        else
        {
            var type = value!.GetType();
            typeName = _options.TypeResolver.GetTypeName(type);
            bytes = Encode(value, type, _options.ResolveCodec(codec));
        }

        if (_options.RequireFrame)
        {
            // Put in front after encoding and before encryption, so the HMAC covers the frame.
            bytes = new PayloadFrame(_options.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds(), sequence).Prepend(bytes);
        }

        if (format == PayloadFormat.Encrypted)
            bytes = Encryptor().Encrypt(bytes, key!, associatedData);

        return new PayloadEnvelope { Format = format, Body = bytes, TypeName = typeName, Codec = codec };
    }

    /// <summary>
    /// Opens a result envelope into the type its <c>type</c> member names, after checking the name through
    /// <see cref="PayloadOptions.TypeResolver"/>. For a client reading a result from a server it trusts.
    /// </summary>
    /// <remarks>
    /// WARNING: a server never opens a request this way, because the type would then be chosen by the caller. Use
    /// <see cref="OpenRequest(PayloadEnvelope, Type, byte[], out PayloadFrame)"/>.
    /// </remarks>
    /// <param name="envelope">The envelope.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">The type name is missing or not allowed, the key is missing, the body could not be decoded, or <see cref="NoPayloadEncryptor"/> is set without <see cref="PayloadOptions.AllowNoEncryption"/>.</exception>
    /// <exception cref="InvalidPayloadException">An encoded or encrypted envelope has no body.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">An encrypted body is malformed or fails authentication, or the key is not the size the encryptor needs.</exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">The envelope names a codec that is not registered.</exception>
    /// <exception cref="InvalidOperationException">The envelope is encrypted: an encrypted payload is bound to its method, so use the method that takes it (ADR-003).</exception>
    public object? OpenResult(PayloadEnvelope envelope, byte[]? key, out PayloadFrame? frame)
        => OpenResultCore(envelope, key, binding: null, out frame);

    /// <summary>
    /// Opens the result of a call to a method, like <see cref="OpenResult(PayloadEnvelope, byte[], out PayloadFrame)"/>.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <param name="method">The JSON-RPC method the call was sent to; an encrypted result must be bound to it and to the
    /// response direction.</param>
    /// <param name="requestFormat">The format the request was sent in; the result must be in the same format.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>.
    /// </returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body fails authentication, which includes a body bound to another method or direction.
    /// </exception>
    /// <exception cref="InvalidPayloadException">The result is in another format than the request.</exception>
    public object? OpenResult(PayloadEnvelope envelope, byte[]? key, string method, PayloadFormat requestFormat, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        EnsureAnswersRequest(envelope, requestFormat);
        return OpenResultCore(envelope, key, PayloadBinding.Response(method), out frame);
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

    /// <summary>
    /// Opens a request envelope into a type the reader chose, using the <c>type</c> member only to check that the writer
    /// meant the same type. For a server reading the parameters of a method.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="type">The type to decode into, decided by the reader.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>
    /// The value. A plain envelope returns its value as a <see cref="JsonElement"/>, or <see langword="null"/>, for the
    /// reader to bind.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The type name is missing, not allowed or names another type, the key is missing, the body could not be decoded,
    /// or <see cref="NoPayloadEncryptor"/> is set without <see cref="PayloadOptions.AllowNoEncryption"/>.
    /// </exception>
    /// <exception cref="InvalidPayloadException">An encoded or encrypted envelope has no body.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">An encrypted body is malformed or fails authentication, or the key is not the size the encryptor needs.</exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">The envelope names a codec that is not registered.</exception>
    /// <exception cref="InvalidOperationException">The envelope is encrypted: an encrypted payload is bound to its method, so use the method that takes it (ADR-003).</exception>
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, out PayloadFrame? frame)
        => OpenAs(envelope, type, key, budget: null, binding: null, out frame);

    /// <summary>
    /// Opens the parameters of a call to a method, like
    /// <see cref="OpenRequest(PayloadEnvelope, Type, byte[], out PayloadFrame)"/>.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="type">The type to decode into, decided by the reader.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <param name="method">The JSON-RPC method of the request; an encrypted body must be bound to it and to the
    /// request direction.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>The value, as <see cref="OpenRequest(PayloadEnvelope, Type, byte[], out PayloadFrame)"/> returns it.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body fails authentication, which includes a body bound to another method or direction.
    /// </exception>
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, string method, out PayloadFrame? frame)
        => OpenAs(envelope, type, key, budget: null, PayloadBinding.Request(method), out frame);

    /// <summary>
    /// Opens a request envelope like <see cref="OpenRequest(PayloadEnvelope, Type, byte[], out PayloadFrame)"/>, drawing
    /// the decompressed size on a budget the calls of one message share.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="type">The type the server decodes into.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <param name="budget">What the message may still decompress; the decompressed size is taken from it.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>The value, as <see cref="OpenRequest(PayloadEnvelope, Type, byte[], out PayloadFrame)"/> returns it.</returns>
    /// <exception cref="InvalidOperationException">
    /// The type name is missing, not allowed or names another type, the key is missing, or the body decompresses beyond
    /// the budget.
    /// </exception>
    /// <exception cref="InvalidPayloadException">An encoded or encrypted envelope has no body.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">An encrypted body is malformed or fails authentication, or the key is not the size the encryptor needs.</exception>
    /// <exception cref="ReplayRejectedException">Frames are required and the body's frame is missing or of another version.</exception>
    /// <exception cref="NotSupportedException">The envelope names a codec that is not registered.</exception>
    /// <remarks>
    /// A body that fails to decompress, for any reason, uses up what is left of the budget, because the failure may
    /// already have cost that much. A later compressed body of the message is then refused by a compressor that
    /// implements <see cref="IPayloadCompressor.Decompress(byte[], long)"/>, as <see cref="GzipPayloadCompressor"/> does;
    /// a body sent uncompressed is already in memory and is still opened.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The envelope is encrypted: an encrypted payload is bound to its method, so use the method that takes it (ADR-003).</exception>
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, PayloadDecompressionBudget budget, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return OpenAs(envelope, type, key, budget, binding: null, out frame);
    }

    /// <summary>
    /// Opens the parameters of a call to a method, drawing the decompressed size on a budget, like
    /// <see cref="OpenRequest(PayloadEnvelope, Type, byte[], PayloadDecompressionBudget, out PayloadFrame)"/>.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="type">The type the server decodes into.</param>
    /// <param name="key">The key; required when the envelope is encrypted.</param>
    /// <param name="budget">What the message may still decompress; the decompressed size is taken from it.</param>
    /// <param name="method">The JSON-RPC method of the request; an encrypted body must be bound to it and to the
    /// request direction.</param>
    /// <param name="frame">The frame read from the body, or <see langword="null"/> when frames are not required.</param>
    /// <returns>The value, as <see cref="OpenRequest(PayloadEnvelope, Type, byte[], out PayloadFrame)"/> returns it.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// An encrypted body fails authentication, which includes a body bound to another method or direction.
    /// </exception>
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, PayloadDecompressionBudget budget, string method, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return OpenAs(envelope, type, key, budget, PayloadBinding.Request(method), out frame);
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
        var codec = _options.ResolveCodec(envelope.Codec);
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
            return codec.Deserialize(plain, type);
        }
        catch (Exception ex)
        {
            // Boundary: the codec and the compressor are pluggable, so a failure in either is reported as one decoding
            // error, with the original as the inner exception. A catch-all is intentional here.
            throw new InvalidOperationException("An error occurred during the data decoding process.", ex);
        }
    }

    private byte[] Encode(object value, Type type, IPayloadCodec codec)
    {
        try
        {
            return _options.Compressor.Compress(codec.Serialize(value, type));
        }
        catch (Exception ex)
        {
            // Boundary: as in Decode, a failure of a pluggable codec or compressor is reported as one encoding error.
            throw new InvalidOperationException("An error occurred during the data encoding process.", ex);
        }
    }

    private byte[] Compress(byte[] bytes)
    {
        try
        {
            return _options.Compressor.Compress(bytes);
        }
        catch (Exception ex)
        {
            // Boundary: as in Decode, a failure of a pluggable codec or compressor is reported as one encoding error.
            throw new InvalidOperationException("An error occurred during the data encoding process.", ex);
        }
    }

    // A server answers in the format of the request, a null result included (ADR-003, decision 6). Without this check a
    // client that sent an encrypted call would take a plain or encoded result that anybody on the way could have written.
    private static void EnsureAnswersRequest(PayloadEnvelope envelope, PayloadFormat requestFormat)
    {
        if (envelope.Format != requestFormat)
            throw new InvalidPayloadException("The result is not in the format of the request.");
    }

    // The binding is required wherever there is an HMAC to put it under: an encrypted payload that is not bound to its
    // call can be sent to another method, or sent back as a request (ADR-003).
    private static byte[] AssociatedData(PayloadBinding? binding) => binding is not null
        ? binding.ToAssociatedData()
        : throw new InvalidOperationException(
            "An encrypted payload is bound to its method and direction; use a method that takes the method name.");

    private IPayloadEncryptor Encryptor()
    {
        var encryptor = _options.Encryptor;
        if (encryptor is NoPayloadEncryptor && !_options.AllowNoEncryption)
        {
            throw new InvalidOperationException(
                "NoPayloadEncryptor is only permitted when PayloadOptions.AllowNoEncryption is set, for development.");
        }
        return encryptor;
    }
}
