using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Polhem.JsonRpc.AspNetCore;

/// <summary>
/// Maps the JSON-RPC endpoint.
/// </summary>
public static class JsonRpcEndpointRouteBuilderExtensions
{
    internal const string ReflectionMessage =
        "The JSON-RPC server resolves methods and binds parameters by reflection, which trimming and Native AOT do not support.";

    /// <summary>
    /// Answers JSON-RPC calls posted to <paramref name="pattern"/>. Register the server first with
    /// <see cref="JsonRpcServiceCollectionExtensions.AddJsonRpcServer"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern, such as <c>/api</c>.</param>
    /// <returns>A builder to customize the endpoint, for example to require authorization.</returns>
    public static IEndpointConventionBuilder MapJsonRpc(this IEndpointRouteBuilder endpoints, string pattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);
        var handler = endpoints.ServiceProvider.GetRequiredService<JsonRpcHttpHandler>();
        return endpoints.MapPost(pattern, (RequestDelegate)handler.HandleAsync);
    }
}
