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
}
