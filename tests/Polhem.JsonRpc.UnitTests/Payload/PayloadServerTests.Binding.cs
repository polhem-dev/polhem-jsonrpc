using System.Text.Json;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

public partial class PayloadServerTests
{
    [Fact(DisplayName = "Payload server: a captured encrypted call sent to another method that takes the same shape, with its type renamed, is refused and does not run")]
    public async Task Call_CapturedParametersSentToAnotherMethod_IsRefused()
    {
        var (rpc, client) = CreateWithoutMapper(new TestPolicy { ReplayScope = "s", UniqueSequence = true });
        var text = $"redirected-{Guid.NewGuid():N}";
        // Upper and Update both take { "text": ... }, so without the binding the redirected body would bind and run.
        var captured = PayloadEnvelope.Read(client.WrapRequest("Spec.Upper", new UpperRequest(text), PayloadFormat.Encrypted, key: s_key, sequence: 1));
        var redirected = new PayloadEnvelope
        {
            Format = captured.Format,
            Body = captured.Body,
            TypeName = Registry().GetTypeName(typeof(UpdateRequest)),
        }.ToElement();

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>("Spec.Update", redirected));

        Assert.Equal(JsonRpcErrorCodes.InternalError, ex.Code);
        Assert.DoesNotContain(text, SpecTarget.Updates);
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
