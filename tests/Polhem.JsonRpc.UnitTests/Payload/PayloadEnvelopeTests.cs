using System.Text.Json;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

public class PayloadEnvelopeTests
{
    [Theory(DisplayName = "Envelope: the format reads from params without reading the rest")]
    [InlineData("""{"format":2,"value":"!"}""", PayloadFormat.Encrypted)]
    [InlineData("""{"value":1}""", PayloadFormat.Plain)]
    [InlineData("[1,2]", PayloadFormat.Plain)]
    public void ReadFormat_EnvelopeOrOther_ReturnsFormat(string json, PayloadFormat expected)
    {
        Assert.Equal(expected, PayloadEnvelope.ReadFormat(Parse(json)));
    }

    [Fact(DisplayName = "Envelope: missing params read as plain")]
    public void ReadFormat_NoParams_ReturnsPlain()
    {
        Assert.Equal(PayloadFormat.Plain, PayloadEnvelope.ReadFormat(null));
    }

    [Theory(DisplayName = "Envelope: a format that is not 0, 1 or 2 is rejected")]
    [InlineData("""{"format":"1"}""")]
    [InlineData("""{"format":3}""")]
    [InlineData("""{"format":1.5}""")]
    public void Read_InvalidFormat_Throws(string json)
    {
        Assert.Throws<InvalidPayloadException>(() => PayloadEnvelope.Read(Parse(json)));
        Assert.Throws<InvalidPayloadException>(() => PayloadEnvelope.ReadFormat(Parse(json)));
    }

    [Theory(DisplayName = "Envelope: malformed members are rejected")]
    [InlineData("[]")]
    [InlineData("""{"format":1,"value":12}""")]
    [InlineData("""{"format":1,"value":"not base64!"}""")]
    [InlineData("""{"format":1}""")]
    [InlineData("""{"format":0,"type":5}""")]
    [InlineData("""{"format":0,"codec":true}""")]
    public void Read_Malformed_Throws(string json)
    {
        Assert.Throws<InvalidPayloadException>(() => PayloadEnvelope.Read(Parse(json)));
    }

    // The escaped row is the same name once decoded, which is how every JSON reader compares member names.
    [Theory(DisplayName = "Envelope: a member of the envelope that appears twice is refused, by Read and by ReadFormat alike")]
    [InlineData("""{"format":2,"format":0,"value":{"a":1}}""")]
    [InlineData("""{"format":0,"format":2,"value":"AAAA","type":"t"}""")]
    [InlineData("""{"format":0,"form\u0061t":2,"value":{"a":1}}""")]
    [InlineData("""{"format":0,"value":{"a":1},"value":{"a":2}}""")]
    [InlineData("""{"format":1,"value":"AAAA","type":"a","type":"b"}""")]
    [InlineData("""{"format":1,"value":"AAAA","type":"a","codec":"json","codec":"other"}""")]
    public void Read_RepeatedMember_Throws(string json)
    {
        Assert.Throws<InvalidPayloadException>(() => PayloadEnvelope.Read(Parse(json)));
        Assert.Throws<InvalidPayloadException>(() => PayloadEnvelope.ReadFormat(Parse(json)));
    }

    [Fact(DisplayName = "Envelope: members the envelope does not define are ignored")]
    public void Read_UnknownMember_IsIgnored()
    {
        var envelope = PayloadEnvelope.Read(Parse("""{"format":0,"value":{"a":1},"extra":[1]}"""));

        Assert.Equal(1, envelope.Value!.Value.GetProperty("a").GetInt32());
    }

    [Fact(DisplayName = "Envelope: an encoded envelope writes and reads its body as Base64")]
    public void ToElement_Encoded_RoundTrips()
    {
        var envelope = new PayloadEnvelope { Format = PayloadFormat.Encoded, Body = [0, 255, 7], TypeName = "T", Codec = "json" };

        var read = PayloadEnvelope.Read(envelope.ToElement());

        Assert.Equal("""{"format":1,"value":"AP8H","type":"T","codec":"json"}""", envelope.ToElement().GetRawText());
        Assert.Equal(envelope.Body, read.Body);
        Assert.Equal("T", read.TypeName);
        Assert.Equal("json", read.Codec);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
