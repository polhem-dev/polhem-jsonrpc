using System.Text.Json;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Seals values into payload envelopes and opens them again: serialize, compress, frame and encrypt on the way out, and
/// the reverse on the way in.
/// </summary>
/// <remarks>
/// A client wraps its parameters with <see cref="Wrap"/> before calling <c>JsonRpcConnector.InvokeAsync</c> and unwraps
/// the result with <see cref="Unwrap{T}"/>, or with <see cref="Unwrap"/> when it does not know the result type. Everything that differs per call (the format, the codec, the key and the
/// sequence number) is passed in; the processor keeps no per-call state and can be shared.
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
    public JsonElement Wrap(object? value, PayloadFormat format, string? codec = null, byte[]? key = null, long sequence = 0)
        => Seal(value, format, codec, key, sequence).ToElement();

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
    public T? Unwrap<T>(JsonElement? payload, byte[]? key = null)
    {
        var value = OpenAs(PayloadEnvelope.Read(payload), typeof(T), key, out _);
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
    public object? Unwrap(JsonElement? payload, byte[]? key = null)
        => OpenResult(PayloadEnvelope.Read(payload), key, out _);

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
    public PayloadEnvelope Seal(object? value, PayloadFormat format, string? codec = null, byte[]? key = null, long sequence = 0)
    {
        codec ??= string.Empty;
        if (format == PayloadFormat.Plain)
        {
            JsonElement? plain = value == null
                ? null
                : JsonSerializer.SerializeToElement(value, _options.SerializerOptions.GetTypeInfo(value.GetType()));
            return new PayloadEnvelope { Value = plain };
        }

        if (value == null)
            throw new InvalidOperationException("An encoded payload needs a value.");
        if (format == PayloadFormat.Encrypted && (key == null || key.Length == 0))
            throw new InvalidOperationException("An encrypted payload needs a key.");

        var type = value.GetType();
        var typeName = _options.TypeResolver.GetTypeName(type);
        var bytes = Encode(value, type, _options.ResolveCodec(codec));

        if (_options.RequireFrame)
        {
            // Put in front after encoding and before encryption, so the HMAC covers the frame.
            bytes = new PayloadFrame(_options.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds(), sequence).Prepend(bytes);
        }

        if (format == PayloadFormat.Encrypted)
            bytes = Encryptor().Encrypt(bytes, key!);

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
    /// <exception cref="InvalidOperationException">The type name is missing or not allowed, or the key is missing.</exception>
    public object? OpenResult(PayloadEnvelope envelope, byte[]? key, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        frame = null;
        if (envelope.Format == PayloadFormat.Plain)
            return envelope.Value;

        if (string.IsNullOrEmpty(envelope.TypeName))
            throw new InvalidOperationException("The payload names no type to decode into.");
        if (!_options.TypeResolver.TryResolveType(envelope.TypeName, out var type))
            throw new InvalidOperationException("The payload type is not in the allowed types.");
        return Decode(envelope, key, type, budget: null, out frame);
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
    /// The type name is missing, not allowed or names another type, or the key is missing.
    /// </exception>
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, out PayloadFrame? frame)
        => OpenAs(envelope, type, key, budget: null, out frame);

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
    public object? OpenRequest(PayloadEnvelope envelope, Type type, byte[]? key, PayloadDecompressionBudget budget, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return OpenAs(envelope, type, key, budget, out frame);
    }

    private object? OpenAs(PayloadEnvelope envelope, Type type, byte[]? key, out PayloadFrame? frame)
        => OpenAs(envelope, type, key, budget: null, out frame);

    private object? OpenAs(PayloadEnvelope envelope, Type type, byte[]? key, PayloadDecompressionBudget? budget, out PayloadFrame? frame)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(type);
        frame = null;
        if (envelope.Format == PayloadFormat.Plain)
            return envelope.Value;

        if (string.IsNullOrEmpty(envelope.TypeName))
            throw new InvalidOperationException("The payload names no type to decode into.");
        if (!_options.TypeResolver.IsNameOf(envelope.TypeName, type))
            throw new InvalidOperationException("The payload type does not match the type the reader expects.");
        return Decode(envelope, key, type, budget, out frame);
    }

    private object? Decode(PayloadEnvelope envelope, byte[]? key, Type type, PayloadDecompressionBudget? budget, out PayloadFrame? frame)
    {
        var bytes = envelope.Body ?? throw new InvalidPayloadException("An encoded payload envelope has no body.");

        if (envelope.Format == PayloadFormat.Encrypted)
        {
            if (key == null || key.Length == 0)
                throw new InvalidOperationException("Missing encryption key for encrypted payload.");
            bytes = Encryptor().Decrypt(bytes, key);
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
