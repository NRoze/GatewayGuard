using GatewayGuard;
using SampleApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGatewayGuard(options =>
{
    options.IdempotencyHeaderName = "X-Idempotency-Key";
    options.EnableFingerprinting = true;
    options.IdempotencyKeyExpiration = TimeSpan.FromMinutes(1);
    options.MaxConcurrentRequests = 500;
    options.RedisConnection = "localhost:6379";
    options.CircuitBreakerThreshold = 0.2;
});

var app = builder.Build();
app.UseGatewayGuard();

app.MapPost("/orders", async (HttpContext ctx) =>
{
    Interlocked.Increment(ref TestState.ExecutionCount);

    await Task.Delay(200); 
    await ctx.Response.WriteAsync("Order processed!");
});

app.Run();