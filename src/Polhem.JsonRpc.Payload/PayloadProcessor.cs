using System.Text.Json;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Seals values into payload envelopes and opens them again: serialize, compress, frame and encrypt on the way out, and
/// the reverse on the way in.
/// </summary>
/// <remarks>
/// A client wraps its parameters with <see cref="WrapRequest"/> before calling <c>JsonRpcConnector.InvokeAsync</c> and
/// unwraps the result with <see cref="UnwrapResult{T}"/>, or with <see cref="UnwrapResult"/> when it does not know the
/// result type. <c>PayloadConnector</c>, in <c>Polhem.JsonRpc.Payload.Client</c>, does both for every call. Everything that differs per call (the format, the codec, the key and the sequence number) is passed in;
/// the processor keeps no per-call state and can be shared.
/// <para>
/// An encrypted payload is bound to its call: <see cref="WrapRequest"/>, <see cref="SealResponse"/>,
/// <see cref="UnwrapResult{T}"/> and the overloads of <c>OpenRequest</c> and <c>OpenResult</c> that take the method
/// write and check the method and the direction under the HMAC (ADR-003). The methods that do not take one refuse an
/// encrypted payload.
/// </para>
/// </remarks>
public sealed partial class PayloadProcessor
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
    /// <param name="key">Not used: an encrypted payload is refused.</param>
    /// <param name="sequence">The sequence number written to the frame, when frames are required.</param>
    /// <returns>The <c>params</c> element.</returns>
    /// <exception cref="InvalidOperationException">
    /// The format is <see cref="PayloadFormat.Encrypted"/>: an encrypted payload is bound to its method, so use
    /// <see cref="WrapRequest"/> (ADR-003). Or an encoded value is <see langword="null"/>, or the value could not be
    /// encoded.
    /// </exception>
    /// <exception cref="NotSupportedException">The codec is not registered.</exception>
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
    /// <exception cref="ArgumentException">The method is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// An encoded value is <see langword="null"/>, an encrypted one has no key, the value could not be encoded, or
    /// <see cref="NoPayloadEncryptor"/> is set without <see cref="PayloadOptions.AllowNoEncryption"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The codec is not registered, or the encryptor does not authenticate associated data.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The key is not the size the encryptor needs.</exception>
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
    /// <exception cref="ArgumentException">The method is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// An encrypted value has no key, the value could not be encoded, or <see cref="NoPayloadEncryptor"/> is set
    /// without <see cref="PayloadOptions.AllowNoEncryption"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The codec is not registered, or the encryptor does not authenticate associated data.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The key is not the size the encryptor needs.</exception>
    public PayloadEnvelope SealResponse(string method, object? value, PayloadFormat format, string? codec = null, byte[]? key = null)
        => SealCore(value, format, PayloadBinding.Response(method), codec, key, sequence: 0);

    /// <summary>Seals a value into an envelope.</summary>
    /// <param name="value">The value. It may be <see langword="null"/> only for a plain envelope.</param>
    /// <param name="format">The format.</param>
    /// <param name="codec">The codec to name; empty or <see langword="null"/> for <see cref="PayloadOptions.DefaultCodec"/>.</param>
    /// <param name="key">Not used: an encrypted payload is refused.</param>
    /// <param name="sequence">The sequence number written to the frame, when frames are required.</param>
    /// <returns>The envelope.</returns>
    /// <exception cref="InvalidOperationException">
    /// The format is <see cref="PayloadFormat.Encrypted"/>: an encrypted payload is bound to its method, so use
    /// <see cref="WrapRequest"/> or <see cref="SealResponse"/> (ADR-003). Or an encoded value is
    /// <see langword="null"/>, or the value could not be encoded.
    /// </exception>
    /// <exception cref="NotSupportedException">The codec is not registered.</exception>
    /// <remarks>
    /// The body is serialized as the value's runtime type, and that type is named through
    /// <see cref="PayloadOptions.TypeResolver"/>. The codec name is written as given, so that a reader resolves the codec
    /// the writer used; an empty name is left out of the envelope.
    /// </remarks>
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
            // Left uncompressed, so the body is exactly zero bytes before the frame and the encryption.
            typeName = string.Empty;
            bytes = [];
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
