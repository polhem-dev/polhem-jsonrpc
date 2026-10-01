using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <para>
    /// <see cref="JsonRpcServerOptions"/> already registered with the services as an instance are used, and
    /// <paramref name="configure"/> is applied to them. A framework that sets up its own object factory, method policy
    /// and filters registers its options first; the application's <paramref name="configure"/> then adds to them,
    /// and filters it adds run inside the framework's.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(JsonRpcEndpointRouteBuilderExtensions.ReflectionMessage)]
    [RequiresDynamicCode(JsonRpcEndpointRouteBuilderExtensions.ReflectionMessage)]
    public static IServiceCollection AddJsonRpcServer(
        this IServiceCollection services,
        Action<JsonRpcServerOptions>? configure = null,
        Action<JsonRpcHttpOptions>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(JsonRpcServerOptions))
            ?.ImplementationInstance as JsonRpcServerOptions;
        if (options is null)
        {
            options = new JsonRpcServerOptions();
            services.AddSingleton(options);
        }
        configure?.Invoke(options);
        var httpOptions = new JsonRpcHttpOptions();
        configureHttp?.Invoke(httpOptions);

        services.AddSingleton(httpOptions);
        services.TryAddSingleton(provider =>
        {
            options.ObjectFactory ??= provider.GetRequiredService<IJsonRpcObjectFactory>();
            return new JsonRpcDispatcher(options);
        });
        services.TryAddSingleton<JsonRpcHttpHandler>();
        return services;
    }
}
