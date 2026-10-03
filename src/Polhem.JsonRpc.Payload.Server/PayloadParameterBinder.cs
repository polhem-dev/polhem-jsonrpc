using System.Text.Json;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// Binds the parameter of a method from the payload <see cref="PayloadFilter"/> opened: a decoded body as it is, and a
/// plain value by deserializing it with <see cref="PayloadOptions.SerializerOptions"/>.
/// </summary>
/// <remarks>
/// An application whose <see cref="IPayloadServerPolicy.GetPayloadType"/> decodes into a type other than the parameter
/// type writes its own binder and reads <see cref="PayloadRequest.Find"/>.
/// </remarks>
public sealed class PayloadParameterBinder : IJsonRpcParameterBinder
{
    private const string InvalidParamsMessage = "Invalid params";

    private readonly PayloadOptions _options;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="options">The payload settings; their serializer options read plain values.</param>
    public PayloadParameterBinder(PayloadOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public object? Bind(JsonRpcRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var method = context.Method ?? throw new InvalidOperationException("The method is not resolved.");
        var payload = PayloadRequest.Find(context)
            ?? throw new InvalidOperationException("PayloadFilter did not run for this call.");

        switch (payload.Value)
        {
            case null:
            case JsonElement { ValueKind: JsonValueKind.Null }:
                // No value to bind: the same as an absent `params` without the payload packages.
                throw new JsonRpcErrorException(JsonRpcErrorCodes.InvalidParams, InvalidParamsMessage);
            case JsonElement element when payload.Format == PayloadFormat.Plain:
                // The same rules as the dispatcher's own binder: the value must be an object, because positional
                // parameters would have to be matched by position, and a value that does not fit is invalid params.
                // `PayloadServerTests.Call_PlainParamsOfWrongShape_ReturnsInvalidParamsLikeDefaultBinder` holds them
                // together.
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonRpcErrorException(JsonRpcErrorCodes.InvalidParams, InvalidParamsMessage);
                }
                try
                {
                    return element.Deserialize(_options.SerializerOptions.GetTypeInfo(method.ParameterType));
                }
                catch (JsonException)
                {
                    throw new JsonRpcErrorException(JsonRpcErrorCodes.InvalidParams, InvalidParamsMessage);
                }
                catch (NotSupportedException)
                {
                    throw new JsonRpcErrorException(JsonRpcErrorCodes.InvalidParams, InvalidParamsMessage);
                }
            case var value when method.ParameterType.IsInstanceOfType(value):
                return value;
            default:
                throw new InvalidOperationException("The decoded payload is not of the parameter type of the method.");
        }
    }
}
