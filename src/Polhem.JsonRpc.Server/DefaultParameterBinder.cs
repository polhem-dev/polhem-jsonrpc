using System.Text.Json;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Deserializes a <c>params</c> object into the method's single parameter. An absent <c>params</c> and an array are
/// both invalid params: the method has one parameter to bind, and positional parameters would have to be matched by
/// position.
/// </summary>
// NOTE: `PayloadParameterBinder` in Polhem.JsonRpc.Payload.Server applies the same rules to a plain payload value.
// `PayloadServerTests.Call_PlainParamsOfWrongShape_ReturnsInvalidParamsLikeDefaultBinder` sends the same inputs to both.
internal sealed class DefaultParameterBinder(JsonSerializerOptions options) : IJsonRpcParameterBinder
{
    private const string InvalidParamsMessage = "Invalid params";

    public object? Bind(JsonRpcRequestContext context)
    {
        var method = context.Method ?? throw new InvalidOperationException("The method is not resolved.");
        if (context.Request.Params is not { ValueKind: JsonValueKind.Object } parameters)
        {
            throw new JsonRpcErrorException(JsonRpcErrorCodes.InvalidParams, InvalidParamsMessage);
        }

        try
        {
            return parameters.Deserialize(options.GetTypeInfo(method.ParameterType));
        }
        catch (JsonException)
        {
            throw new JsonRpcErrorException(JsonRpcErrorCodes.InvalidParams, InvalidParamsMessage);
        }
        catch (NotSupportedException)
        {
            throw new JsonRpcErrorException(JsonRpcErrorCodes.InvalidParams, InvalidParamsMessage);
        }
    }
}
