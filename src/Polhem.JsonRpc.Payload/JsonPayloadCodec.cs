using System.Text.Json;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// The built-in codec <c>json</c>: serializes a body with System.Text.Json.
/// </summary>
/// <remarks>
/// Under Native AOT, construct it with options whose <see cref="JsonSerializerOptions.TypeInfoResolver"/> is a
/// source-generated <c>JsonSerializerContext</c> that knows every body type.
/// </remarks>
public sealed class JsonPayloadCodec : IPayloadCodec
{
    /// <summary>The name of the codec.</summary>
    public const string CodecName = "json";

    private readonly JsonSerializerOptions _options;

    /// <summary>Initializes a new instance with the web defaults of System.Text.Json.</summary>
    public JsonPayloadCodec() : this(new JsonSerializerOptions(JsonSerializerDefaults.Web)) { }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="options">The options bodies are serialized with.</param>
    public JsonPayloadCodec(JsonSerializerOptions options)
    {
        _options = PayloadJsonOptions.WithResolver(options);
    }

    /// <inheritdoc/>
    public string Name => CodecName;

    /// <inheritdoc/>
    public byte[] Serialize(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.SerializeToUtf8Bytes(value, _options.GetTypeInfo(type));
    }

    /// <inheritdoc/>
    public object? Deserialize(byte[] bytes, Type type)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(type);
        return JsonSerializer.Deserialize(bytes, _options.GetTypeInfo(type));
    }
}
