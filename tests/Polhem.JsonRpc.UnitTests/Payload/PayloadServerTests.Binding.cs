using System.Text.Json;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

public partial class PayloadServerTests
{
    [Fact(DisplayName = "Payload server: a captured encrypted call sent to another method, with its type renamed to match, is refused")]
    public async Task Call_CapturedParametersSentToAnotherMethod_IsRefused()
    {
        var (rpc, client) = CreateWithoutMapper(new TestPolicy { ReplayScope = "s", UniqueSequence = true });
        var captured = PayloadEnvelope.Read(client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1));
        var redirected = new PayloadEnvelope
        {
            Format = captured.Format,
            Body = captured.Body,
            TypeName = Registry().GetTypeName(typeof(UpperRequest)),
        }.ToElement();

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>("Spec.Upper", redirected));

        Assert.Equal(JsonRpcErrorCodes.InternalError, ex.Code);
    }

    [Fact(DisplayName = "Payload server: an encrypted result sent back as the parameters of the same method is refused")]
    public async Task Call_ResultSentBackAsParameters_IsRefused()
    {
        var (rpc, client) = CreateWithoutMapper(new TestPolicy { ReplayScope = "s", UniqueSequence = true });
        var result = await rpc.InvokeAsync<JsonElement>(Subtract,
            client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1));
        var answer = PayloadEnvelope.Read(result);
        var reflected = new PayloadEnvelope
        {
            Format = answer.Format,
            Body = answer.Body,
            TypeName = Registry().GetTypeName(typeof(SubtractRequest)),
        }.ToElement();

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, reflected));

        Assert.Equal(JsonRpcErrorCodes.InternalError, ex.Code);
    }
}
