using System.Security.Cryptography;
using System.Text.Json;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

public partial class PayloadServerTests
{
    [Theory(DisplayName = "Payload server: a null result is answered in the format of the request, with no type, and opens to null")]
    [InlineData(PayloadFormat.Plain)]
    [InlineData(PayloadFormat.Encoded)]
    [InlineData(PayloadFormat.Encrypted)]
    public async Task Call_NullResult_AnsweredInRequestFormat(PayloadFormat format)
    {
        var (rpc, client) = CreateWithoutMapper(new TestPolicy());

        var result = await rpc.InvokeAsync<JsonElement>("Spec.Nothing",
            client.WrapRequest("Spec.Nothing", new NothingRequest("a"), format, key: s_key, sequence: 1));

        var envelope = PayloadEnvelope.Read(result);
        Assert.Equal(format, envelope.Format);
        Assert.True(string.IsNullOrEmpty(envelope.TypeName));
        Assert.Null(client.UnwrapResult("Spec.Nothing", format, result, s_key));
        Assert.Null(client.UnwrapResult<NothingResponse>("Spec.Nothing", format, result, s_key));
    }

    [Theory(DisplayName = "Payload client: a result in another format than the request is refused, a plain null included")]
    [InlineData(PayloadFormat.Encrypted, PayloadFormat.Plain, false)]
    [InlineData(PayloadFormat.Encrypted, PayloadFormat.Plain, true)]
    [InlineData(PayloadFormat.Encrypted, PayloadFormat.Encoded, false)]
    [InlineData(PayloadFormat.Encoded, PayloadFormat.Plain, false)]
    [InlineData(PayloadFormat.Plain, PayloadFormat.Encoded, false)]
    public void UnwrapResult_OtherFormat_IsRefused(PayloadFormat requestFormat, PayloadFormat resultFormat, bool nullResult)
    {
        var (_, client) = CreateWithoutMapper(new TestPolicy());
        var result = client.SealResponse(Subtract, nullResult ? null : new SubtractResponse(2), resultFormat, key: s_key).ToElement();

        Assert.Throws<InvalidPayloadException>(() => client.UnwrapResult(Subtract, requestFormat, result, s_key));
        Assert.Throws<InvalidPayloadException>(() => client.UnwrapResult<SubtractResponse>(Subtract, requestFormat, result, s_key));
        Assert.Throws<InvalidPayloadException>(() => client.OpenResult(PayloadEnvelope.Read(result), s_key, Subtract, requestFormat, out _));
    }

    [Fact(DisplayName = "Payload client: a result that names no type but carries a body is refused")]
    public void UnwrapResult_NoTypeWithBody_IsRefused()
    {
        var (_, client) = CreateWithoutMapper(new TestPolicy());
        var sealedResult = client.SealResponse(Subtract, new SubtractResponse(2), PayloadFormat.Encoded);
        var stripped = new PayloadEnvelope { Format = sealedResult.Format, Body = sealedResult.Body, TypeName = "" }.ToElement();

        Assert.Throws<InvalidOperationException>(() => client.UnwrapResult(Subtract, PayloadFormat.Encoded, stripped));
    }

    [Fact(DisplayName = "Payload server: the parameters of a call that name no type are refused, even with an empty body")]
    public async Task Call_ParametersWithNoType_AreRefused()
    {
        var (rpc, client) = CreateWithoutMapper(new TestPolicy());
        // A sealed null result has the shape a caller would need to send parameters without a type.
        var empty = client.SealResponse(Subtract, null, PayloadFormat.Encoded).ToElement();

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, empty));

        Assert.Equal(JsonRpcErrorCodes.InternalError, ex.Code);
    }

    public static TheoryData<PayloadFormat, bool, bool> NullResultSettings()
    {
        var data = new TheoryData<PayloadFormat, bool, bool>();
        foreach (var format in new[] { PayloadFormat.Plain, PayloadFormat.Encoded, PayloadFormat.Encrypted })
        {
            foreach (var frames in new[] { false, true })
            {
                data.Add(format, frames, false);
                data.Add(format, frames, true);
            }
        }
        return data;
    }

    [Theory(DisplayName = "Payload client: a sealed null result opens to null in every format, with frames on or off and either compressor, even when it names a codec the reader lacks")]
    [MemberData(nameof(NullResultSettings))]
    public void UnwrapResult_SealedNull_OpensToNullEverywhere(PayloadFormat format, bool frames, bool gzip)
    {
        var processor = ResultProcessor(frames, gzip);
        var sealedNull = processor.SealResponse(Subtract, null, format, codec: format == PayloadFormat.Plain ? null : "absent", key: s_key);
        var element = sealedNull.ToElement();

        Assert.Equal(format, sealedNull.Format);
        Assert.Null(processor.UnwrapResult(Subtract, format, element, s_key));
        Assert.Null(processor.UnwrapResult<SubtractResponse>(Subtract, format, element, s_key));
        Assert.Null(processor.OpenResult(sealedNull, s_key, Subtract, format, out _));
    }

    [Fact(DisplayName = "Payload server: the body of a sealed null result is zero bytes, uncompressed, before the frame and the encryption")]
    public void SealResponse_Null_WritesEmptyBody()
    {
        var body = ResultProcessor(frames: false, gzip: true).SealResponse(Subtract, null, PayloadFormat.Encoded).Body!;

        Assert.Empty(body);
    }

    [Fact(DisplayName = "Payload client: a null result whose body is gzip of nothing also opens to null, as a writer may compress it")]
    public void UnwrapResult_GzipOfNothing_OpensToNull()
    {
        var processor = ResultProcessor(frames: false, gzip: true);
        var gzipOfNothing = Convert.FromBase64String("H4sIAAAAAAAAAwMAAAAAAAAAAAA=");
        var result = new PayloadEnvelope { Format = PayloadFormat.Encoded, Body = gzipOfNothing, TypeName = "" }.ToElement();

        Assert.Null(processor.UnwrapResult(Subtract, PayloadFormat.Encoded, result));
    }

    [Theory(DisplayName = "Payload client: an encrypted result with no type is opened only after its HMAC checks, so one written without the key is refused")]
    [InlineData("empty body")]
    [InlineData("random body")]
    [InlineData("another key")]
    [InlineData("another method")]
    public void UnwrapResult_ForgedEncryptedNull_FailsAuthentication(string forgery)
    {
        var processor = ResultProcessor(frames: true, gzip: true);
        byte[] body = forgery switch
        {
            "empty body" => [],
            "random body" => RandomNumberGenerator.GetBytes(100),
            "another key" => processor.SealResponse(Subtract, null, PayloadFormat.Encrypted, key: RandomNumberGenerator.GetBytes(64)).Body!,
            _ => processor.SealResponse("Spec.Other", null, PayloadFormat.Encrypted, key: s_key).Body!,
        };
        var forged = new PayloadEnvelope { Format = PayloadFormat.Encrypted, Body = body, TypeName = "" }.ToElement();

        Assert.Throws<CryptographicException>(() => processor.UnwrapResult(Subtract, PayloadFormat.Encrypted, forged, s_key));
        Assert.Throws<CryptographicException>(() => processor.UnwrapResult<SubtractResponse>(Subtract, PayloadFormat.Encrypted, forged, s_key));
    }

    [Fact(DisplayName = "Payload client: a real encrypted result whose type is blanked, or a sealed null given a type, is refused")]
    public void UnwrapResult_EncryptedTypeSwapped_IsRefused()
    {
        var processor = ResultProcessor(frames: true, gzip: true);
        var real = processor.SealResponse(Subtract, new SubtractResponse(2), PayloadFormat.Encrypted, key: s_key);
        var empty = processor.SealResponse(Subtract, null, PayloadFormat.Encrypted, key: s_key);
        var blanked = new PayloadEnvelope { Format = real.Format, Body = real.Body, TypeName = "" }.ToElement();
        var typed = new PayloadEnvelope { Format = empty.Format, Body = empty.Body, TypeName = Registry().GetTypeName(typeof(SubtractResponse)) }.ToElement();

        Assert.Throws<InvalidOperationException>(() => processor.UnwrapResult(Subtract, PayloadFormat.Encrypted, blanked, s_key));
        Assert.Throws<InvalidOperationException>(() => processor.UnwrapResult<SubtractResponse>(Subtract, PayloadFormat.Encrypted, blanked, s_key));
        Assert.Throws<InvalidOperationException>(() => processor.UnwrapResult(Subtract, PayloadFormat.Encrypted, typed, s_key));
        Assert.Throws<InvalidOperationException>(() => processor.UnwrapResult<SubtractResponse>(Subtract, PayloadFormat.Encrypted, typed, s_key));
    }

    private static PayloadProcessor ResultProcessor(bool frames, bool gzip) => new(new PayloadOptions
    {
        RequireFrame = frames,
        TypeResolver = Registry(),
        Compressor = gzip ? new GzipPayloadCompressor() : NoPayloadCompressor.Instance,
    });
}
