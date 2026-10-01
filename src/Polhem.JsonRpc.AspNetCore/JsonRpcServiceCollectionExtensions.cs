using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.AspNetCore;

/// <summary>
/// Registers the JSON-RPC server with dependency injection.
/// </summary>
public static class JsonRpcServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JsonRpcDispatcher"/> and <see cref="JsonRpcHttpHandler"/>.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">Configures the server, or <c>null</c> for the defaults.</param>
    /// <param name="configureHttp">Configures the HTTP endpoint, or <c>null</c> for the defaults.</param>
    /// <returns>The services.</returns>
    /// <remarks>
    /// When <see cref="JsonRpcServerOptions.ObjectFactory"/> is not set, the <see cref="IJsonRpcObjectFactory"/>
    /// registered with the services is used, so an application registers its factory like any other service.
    /// </remarks>
    [RequiresUnreferencedCode(JsonRpcEndpointRouteBuilderExtensions.ReflectionMessage)]
    [RequiresDynamicCode(JsonRpcEndpointRouteBuilderExtensions.ReflectionMessage)]
    public static IServiceCollection AddJsonRpcServer(
        this IServiceCollection services,
        Action<JsonRpcServerOptions>? configure = null,
        Action<JsonRpcHttpOptions>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new JsonRpcServerOptions();
        configure?.Invoke(options);
        var httpOptions = new JsonRpcHttpOptions();
        configureHttp?.Invoke(httpOptions);

        services.AddSingleton(httpOptions);
        services.AddSingleton(provider =>
        {
            options.ObjectFactory ??= provider.GetRequiredService<IJsonRpcObjectFactory>();
            return new JsonRpcDispatcher(options);
        });
        services.AddSingleton<JsonRpcHttpHandler>();
        return services;
    }
}
