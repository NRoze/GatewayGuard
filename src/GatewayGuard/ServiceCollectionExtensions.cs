using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace GatewayGuard;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayGuard(this IServiceCollection services, Action<IdempotencyOptions> configure)
    {
        var options = new IdempotencyOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

        return services;
    }

    public static IApplicationBuilder UseGatewayGuard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<IdempotencyMiddleware>();
    }
}