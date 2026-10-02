using System.Reflection;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// The default method policy: a method <c>Action</c> is callable when its parameter type is named
/// <c>{Action}Request</c> and its return type, or the result type of the task it returns, is named
/// <c>{Action}Response</c>.
/// </summary>
/// <remarks>
/// For example <c>PingResponse Ping(PingRequest request)</c> or <c>Task&lt;PingResponse&gt; Ping(PingRequest request)</c>.
/// A public method that does not follow the convention, such as one added for internal use, is not reachable from
/// the network. Type names are compared exactly; the namespace does not matter.
/// </remarks>
public sealed class JsonRpcNamingConventionPolicy : IJsonRpcMethodPolicy
{
    /// <inheritdoc/>
    public bool IsCallable(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var parameters = method.GetParameters();
        if (parameters.Length != 1) { return false; }

        return string.Equals(parameters[0].ParameterType.Name, method.Name + "Request", StringComparison.Ordinal)
            && string.Equals(ResponseType(method.ReturnType)?.Name, method.Name + "Response", StringComparison.Ordinal);
    }

    private static Type? ResponseType(Type returnType)
    {
        if (!returnType.IsGenericType) { return returnType; }
        var definition = returnType.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>)
            ? returnType.GetGenericArguments()[0]
            : returnType;
    }
}
