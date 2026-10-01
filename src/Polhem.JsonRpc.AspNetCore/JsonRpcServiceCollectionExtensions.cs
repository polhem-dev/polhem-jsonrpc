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
    /// Registers <see cref="JsonRpcDispatcher"/> and <see cref="JsonRpcHttpHandler"/>, and every target as a
    /// transient service, so that a target can take its own dependencies in its constructor.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">Configures the server: targets, filters and the other settings.</param>
    /// <param name="configureHttp">Configures the HTTP endpoint, or <c>null</c> for the defaults.</param>
    /// <returns>The services.</returns>
    [RequiresUnreferencedCode(JsonRpcEndpointRouteBuilderExtensions.ReflectionMessage)]
    [RequiresDynamicCode(JsonRpcEndpointRouteBuilderExtensions.ReflectionMessage)]
    public static IServiceCollection AddJsonRpcServer(
        this IServiceCollection services,
        Action<JsonRpcServerOptions> configure,
        Action<JsonRpcHttpOptions>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new JsonRpcServerOptions();
        configure(options);
        var httpOptions = new JsonRpcHttpOptions();
        configureHttp?.Invoke(httpOptions);

        foreach (var targetType in options.Targets.Values)
        {
            services.TryAddTransient(targetType);
        }

        services.AddSingleton(options);
        services.AddSingleton(httpOptions);
        services.AddSingleton(new JsonRpcDispatcher(options));
        services.AddSingleton<JsonRpcHttpHandler>();
        return services;
    }
}
