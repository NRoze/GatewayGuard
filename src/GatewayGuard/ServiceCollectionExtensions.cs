using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using StackExchange.Redis;

namespace GatewayGuard;

/// <summary>
/// Extension methods to register and use GatewayGuard idempotency middleware and services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds GatewayGuard services to the DI container and configures options.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">A callback to configure <see cref="IdempotencyOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddGatewayGuard(this IServiceCollection services, Action<IdempotencyOptions> configure)
    {
        var options = new IdempotencyOptions();
        configure(options);

        services.AddResiliencePipeline(options.CircuitBreakerPolicyName, builder =>
        {
            builder.AddTimeout(options.CircuitBreakerExpiration)
                .AddCircuitBreaker(options.CircuitBreakerStrategy);
        });
        services.AddSingleton(options);
        services.AddSingleton<SingleFlight>();
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var config = ConfigurationOptions.Parse(options.RedisConnection);

            config.ConnectTimeout = options.RedisConnectionTimeoutMs;

            return ConnectionMultiplexer.Connect(config);
        });
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

        return services;
    }

    /// <summary>
    /// Registers the idempotency middleware into the application's request pipeline.
    /// </summary>
    /// <param name="app">The application builder to configure.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> instance for chaining.</returns>
    public static IApplicationBuilder UseGatewayGuard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<IdempotencyMiddleware>();
    }
}