using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace GatewayGuard;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayGuard(this IServiceCollection services, Action<IdempotencyOptions> configure)
    {
        var options = new IdempotencyOptions();
        configure(options);

        services.AddSingleton<IIdempotencyStore>(new RedisIdempotencyStore(options.RedisConnection));
        services.AddSingleton(options);

        return services;
    }

    public static IApplicationBuilder UseGatewayGuard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<IdempotencyMiddleware>();
    }
}