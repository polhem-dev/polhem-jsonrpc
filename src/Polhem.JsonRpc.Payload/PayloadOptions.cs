using System.Text.Json;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// The settings both ends of a connection share for payload envelopes.
/// </summary>
/// <remarks>
/// The client and the server must agree on <see cref="Compressor"/>, <see cref="Encryptor"/> and
/// <see cref="RequireFrame"/>: the envelope names none of them. The codec, by contrast, is chosen per call and named in
/// the envelope; the reader resolves it from <see cref="ResolveCodec"/>.
/// </remarks>
public sealed class PayloadOptions
{
    private const int MaxCodecNameLength = 32;

    private readonly Lock _codecsLock = new();
    private volatile Dictionary<string, IPayloadCodec> _codecs = new(StringComparer.Ordinal);
    private JsonSerializerOptions _serializerOptions = PayloadJsonOptions.WithResolver(new(JsonSerializerDefaults.Web));
    private JsonPayloadCodec _jsonCodec = new();
    private string _defaultCodec = JsonPayloadCodec.CodecName;
    private IPayloadCompressor _compressor = new GzipPayloadCompressor();
    private IPayloadEncryptor _encryptor = new AesCbcHmacPayloadEncryptor();
    private IPayloadTypeResolver _typeResolver = new PayloadTypeRegistry();
    private TimeSpan _frameTimestampTolerance = TimeSpan.FromMinutes(5);
    private TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>
    /// Gets or sets the options the value of a plain envelope is serialized and deserialized with. The default is the
    /// web defaults of System.Text.Json.
    /// </summary>
    /// <remarks>
    /// Under Native AOT, set options whose <see cref="JsonSerializerOptions.TypeInfoResolver"/> is a source-generated
    /// <c>JsonSerializerContext</c>.
    /// </remarks>
    public JsonSerializerOptions SerializerOptions
    {
        get => _serializerOptions;
        set => _serializerOptions = PayloadJsonOptions.WithResolver(value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>
    /// Gets or sets the codec registered as <c>json</c>. It is always available; replace it to serialize JSON bodies with
    /// other options.
    /// </summary>
    public JsonPayloadCodec JsonCodec
    {
        get => _jsonCodec;
        set => _jsonCodec = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the codec used when an envelope names none. The default is <c>json</c>.
    /// </summary>
    /// <remarks>
    /// Both ends must agree on it: a writer that names no codec relies on the reader's default. Set it to a codec that
    /// is registered.
    /// </remarks>
    public string DefaultCodec
    {
        get => _defaultCodec;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _defaultCodec = value;
        }
    }

    /// <summary>Gets or sets the compressor. The default is <see cref="GzipPayloadCompressor"/>.</summary>
    public IPayloadCompressor Compressor
    {
        get => _compressor;
        set => _compressor = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the encryptor. The default is <see cref="AesCbcHmacPayloadEncryptor"/>.</summary>
    public IPayloadEncryptor Encryptor
    {
        get => _encryptor;
        set => _encryptor = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets whether <see cref="NoPayloadEncryptor"/> may be used. Off by default; meant for development.
    /// </summary>
    public bool AllowNoEncryption { get; set; }

    /// <summary>
    /// Gets or sets whether every encoded or encrypted body carries a <see cref="PayloadFrame"/>. Off by default.
    /// </summary>
    /// <remarks>
    /// This is a deployment setting and is never read from a payload: a payload able to declare that it carries no frame
    /// would let an attacker switch replay protection off.
    /// </remarks>
    public bool RequireFrame { get; set; }

    /// <summary>
    /// Gets or sets how far the timestamp of a frame may be from the reader's clock. The default is five minutes.
    /// </summary>
    public TimeSpan FrameTimestampTolerance
    {
        get => _frameTimestampTolerance;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _frameTimestampTolerance = value;
        }
    }

    /// <summary>
    /// Gets or sets the clock a writer stamps frames with and a reader checks their timestamps against. The default is
    /// <see cref="TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider
    {
        get => _timeProvider;
        set => _timeProvider = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets how body types are named and which names are accepted. The default is an empty
    /// <see cref="PayloadTypeRegistry"/>: it accepts the name of a type the reader chose (a server's method parameter,
    /// the type given to <see cref="PayloadProcessor.Unwrap{T}"/>), and resolves no name to a type until types are
    /// registered with it.
    /// </summary>
    public IPayloadTypeResolver TypeResolver
    {
        get => _typeResolver;
        set => _typeResolver = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the names of the codecs a reader accepts, <c>json</c> included.</summary>
    public IReadOnlyCollection<string> CodecNames => [JsonPayloadCodec.CodecName, .. _codecs.Keys];

    /// <summary>Registers a codec under its <see cref="IPayloadCodec.Name"/>.</summary>
    /// <param name="codec">The codec.</param>
    /// <exception cref="ArgumentException">The name is not lower-case letters, digits and hyphens of at most 32 characters.</exception>
    /// <exception cref="InvalidOperationException">A codec with the name is already registered, or the name is <c>json</c>.</exception>
    public void RegisterCodec(IPayloadCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        string name = codec.Name;
        if (string.IsNullOrEmpty(name) || !IsWellFormedCodecName(name))
        {
            throw new ArgumentException(
                "A payload codec name must be lower-case letters, digits and hyphens, up to 32 characters.", nameof(codec));
        }
        if (string.Equals(name, JsonPayloadCodec.CodecName, StringComparison.Ordinal))
            throw new InvalidOperationException("'json' is the built-in payload codec; set JsonCodec to change it.");

        lock (_codecsLock)
        {
            if (_codecs.ContainsKey(name))
                throw new InvalidOperationException($"A payload codec named '{name}' is already registered.");
            _codecs = new Dictionary<string, IPayloadCodec>(_codecs, StringComparer.Ordinal) { [name] = codec };
        }
    }

    /// <summary>Resolves the codec an envelope names.</summary>
    /// <param name="name">The name from the envelope; empty or <see langword="null"/> means <see cref="DefaultCodec"/>.</param>
    /// <returns>The codec.</returns>
    /// <exception cref="NotSupportedException">The name is malformed or no codec is registered under it.</exception>
    public IPayloadCodec ResolveCodec(string? name)
    {
        if (string.IsNullOrEmpty(name))
            name = DefaultCodec;
        if (!IsWellFormedCodecName(name))
            throw new NotSupportedException("The requested payload codec name is not valid.");
        if (string.Equals(name, JsonPayloadCodec.CodecName, StringComparison.Ordinal))
            return JsonCodec;
        if (_codecs.TryGetValue(name, out var codec))
            return codec;
        throw new NotSupportedException($"Unknown payload codec '{name}'.");
    }

    private static bool IsWellFormedCodecName(string name)
    {
        if (name.Length > MaxCodecNameLength)
            return false;
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
                return false;
        }
        return true;
    }
}
