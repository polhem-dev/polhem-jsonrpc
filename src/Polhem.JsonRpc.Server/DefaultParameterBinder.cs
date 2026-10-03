using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// Deserializes a <c>params</c> object into the method's single parameter. An absent <c>params</c> binds
/// <c>null</c> (the default value for a value type); an array is rejected, because a method has one parameter and
/// positional parameters would have to be matched by position.
/// </summary>
internal sealed class DefaultParameterBinder(JsonSerializerOptions options) : IJsonRpcParameterBinder
{
    private const string InvalidParamsMessage = "Invalid params";

    public object? Bind(JsonRpcRequestContext context)
    {
        var method = context.Method ?? throw new InvalidOperationException("The method is not resolved.");
        if (context.Request.Params is not { } parameters)
        {
            return DefaultValueOf(method.ParameterType);
        }

        if (parameters.ValueKind != JsonValueKind.Object)
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

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Binders run under JsonRpcDispatcher, whose constructor requires unreferenced code: the parameter type is read from the application's method by reflection, and keeping it is the application's part.")]
    private static object? DefaultValueOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
}
