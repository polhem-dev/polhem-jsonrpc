using System.Text.Json;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// The payload envelope carried as the <c>params</c> of a request and the <c>result</c> of a response: a JSON object with
/// the members <c>format</c>, <c>value</c>, <c>type</c> and <c>codec</c>.
/// </summary>
/// <remarks>
/// A plain envelope carries its value as ordinary JSON in <see cref="Value"/>. An encoded or encrypted envelope carries
/// the body bytes in <see cref="Body"/>, written as a Base64 string, and names the body's type in <see cref="TypeName"/>.
/// <see cref="PayloadProcessor"/> builds and opens envelopes; this type only reads and writes their JSON.
/// </remarks>
public sealed class PayloadEnvelope
{
    private const string FormatMember = "format";
    private const string ValueMember = "value";
    private const string TypeMember = "type";
    private const string CodecMember = "codec";

    /// <summary>Gets the format of the value.</summary>
    public PayloadFormat Format { get; init; }

    /// <summary>
    /// Gets the value of a plain envelope, or <see langword="null"/> when it is JSON <c>null</c> or absent.
    /// Always <see langword="null"/> for an encoded or encrypted envelope.
    /// </summary>
    public JsonElement? Value { get; init; }

    /// <summary>
    /// Gets the body bytes of an encoded or encrypted envelope. Always <see langword="null"/> for a plain envelope.
    /// </summary>
    public byte[]? Body { get; init; }

    /// <summary>Gets the name of the body's type; empty for a plain envelope.</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the name of the body's codec; empty when the envelope names none, which means the reader's default codec.
    /// </summary>
    public string Codec { get; init; } = string.Empty;

    /// <summary>
    /// Reads the <c>format</c> of the envelope in <paramref name="payload"/> without reading the rest.
    /// </summary>
    /// <param name="payload">The <c>params</c> or <c>result</c> element, or <see langword="null"/> when there is none.</param>
    /// <returns>
    /// The format, or <see cref="PayloadFormat.Plain"/> when there is no envelope object or it has no <c>format</c>.
    /// </returns>
    /// <exception cref="InvalidPayloadException">
    /// The <c>format</c> member is not one of the formats, or a member of the envelope appears more than once.
    /// </exception>
    /// <remarks>
    /// An access filter that runs before the payload is opened uses it to apply rules by format, for example to reject a
    /// plain call to a method that must be encrypted. It refuses the envelopes <see cref="Read"/> refuses for a repeated
    /// member, so the format it returns is the one the payload is opened in.
    /// </remarks>
    public static PayloadFormat ReadFormat(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } element)
            return PayloadFormat.Plain;

        var format = PayloadFormat.Plain;
        var seen = EnvelopeMembers.None;
        foreach (var member in element.EnumerateObject())
        {
            Note(ref seen, member.Name);
            if (member.NameEquals(FormatMember))
                format = ParseFormat(member.Value);
        }
        return format;
    }

    /// <summary>Reads an envelope.</summary>
    /// <param name="payload">The <c>params</c> or <c>result</c> element, or <see langword="null"/> when there is none.</param>
    /// <returns>
    /// The envelope. A missing element reads as an empty plain envelope, and members the envelope does not define are
    /// ignored.
    /// </returns>
    /// <exception cref="InvalidPayloadException">
    /// The element is not an envelope, which includes an object in which a member of the envelope appears more than once.
    /// </exception>
    /// <remarks>
    /// A repeated member is refused rather than resolved, because readers need not resolve it alike: one may keep the first
    /// value and another the last, and a filter that decides by the format would then see another format than the one
    /// the payload is opened in.
    /// </remarks>
    public static PayloadEnvelope Read(JsonElement? payload)
    {
        if (payload is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new PayloadEnvelope();
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidPayloadException("A payload envelope must be a JSON object.");

        var format = PayloadFormat.Plain;
        JsonElement? value = null;
        string typeName = string.Empty;
        string codec = string.Empty;
        var seen = EnvelopeMembers.None;
        foreach (var member in element.EnumerateObject())
        {
            Note(ref seen, member.Name);
            switch (member.Name)
            {
                case FormatMember:
                    format = ParseFormat(member.Value);
                    break;
                case ValueMember:
                    value = member.Value.ValueKind == JsonValueKind.Null ? null : member.Value.Clone();
                    break;
                case TypeMember:
                    typeName = ReadString(member.Value, TypeMember);
                    break;
                case CodecMember:
                    codec = ReadString(member.Value, CodecMember);
                    break;
            }
        }

        if (format == PayloadFormat.Plain)
            return new PayloadEnvelope { Value = value, TypeName = typeName, Codec = codec };

        return new PayloadEnvelope { Format = format, Body = ReadBody(value), TypeName = typeName, Codec = codec };
    }

    /// <summary>Writes the envelope as a JSON element.</summary>
    /// <returns>The element.</returns>
    public JsonElement ToElement()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteTo(writer);
        }
        // ParseValue gives an element that owns its data, so it needs no Clone and no document to dispose.
        var reader = new Utf8JsonReader(buffer.WrittenSpan);
        return JsonElement.ParseValue(ref reader);
    }

    /// <summary>Writes the envelope.</summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="InvalidOperationException">An encoded or encrypted envelope has no body.</exception>
    /// <remarks>
    /// The members are written in the order <c>format</c>, <c>value</c>, <c>type</c>, <c>codec</c>, and <c>codec</c> is left
    /// out when it is empty, so that an envelope on the default codec is written as it was before codecs could be
    /// negotiated.
    /// </remarks>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteNumber(FormatMember, (int)Format);
        writer.WritePropertyName(ValueMember);
        if (Format == PayloadFormat.Plain)
        {
            if (Value is { } value) { value.WriteTo(writer); } else { writer.WriteNullValue(); }
        }
        else
        {
            writer.WriteBase64StringValue(Body ?? throw new InvalidOperationException("An encoded payload envelope needs a body."));
        }
        writer.WriteString(TypeMember, TypeName);
        if (!string.IsNullOrEmpty(Codec))
            writer.WriteString(CodecMember, Codec);
        writer.WriteEndObject();
    }

    // Records a member of the envelope and refuses it the second time; members the envelope does not define are ignored.
    private static void Note(ref EnvelopeMembers seen, string name)
    {
        var member = name switch
        {
            FormatMember => EnvelopeMembers.Format,
            ValueMember => EnvelopeMembers.Value,
            TypeMember => EnvelopeMembers.Type,
            CodecMember => EnvelopeMembers.Codec,
            _ => EnvelopeMembers.None,
        };
        if ((seen & member) != 0)
            throw new InvalidPayloadException($"The {name} member appears more than once in a payload envelope.");
        seen |= member;
    }

    private static PayloadFormat ParseFormat(JsonElement format)
    {
        if (format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var value)
            || value is < (int)PayloadFormat.Plain or > (int)PayloadFormat.Encrypted)
        {
            throw new InvalidPayloadException("The format member of a payload envelope must be 0, 1 or 2.");
        }
        return (PayloadFormat)value;
    }

    private static string ReadString(JsonElement element, string member) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Null => string.Empty,
        _ => throw new InvalidPayloadException($"The {member} member of a payload envelope must be a string."),
    };

    private static byte[] ReadBody(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.String } element)
            throw new InvalidPayloadException("The value of an encoded payload envelope must be a Base64 string.");
        if (!element.TryGetBytesFromBase64(out var body))
            throw new InvalidPayloadException("The value of an encoded payload envelope is not valid Base64.");
        return body;
    }

    [Flags]
    private enum EnvelopeMembers
    {
        None = 0,
        Format = 1,
        Value = 2,
        Type = 4,
        Codec = 8,
    }
}
