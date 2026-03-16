using GatewayGuard;
using SampleApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging();
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

app.Use(async (ctx, next) =>
{
    Interlocked.Increment(ref TestState.ExecutionCount);
    await next();
});
app.MapPost("/orders", async (HttpContext ctx) =>
{
    await Task.Delay(Random.Shared.Next(10, 50));
    await ctx.Response.WriteAsync("Order processed!");
});

app.MapPut("/orders", async (HttpContext ctx) =>
{
    await Task.Delay(Random.Shared.Next(10, 50));
    await ctx.Response.WriteAsync("Order created!");
});

app.MapPost("/echo", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    return await reader.ReadToEndAsync();
});

app.MapPost("/large-response", async (HttpContext ctx) =>
{
    ctx.Response.StatusCode = 201;
    ctx.Response.Headers["X-Test-Header"] = "gateway-guard";

    var payload = new string('X', 200_000); // 200 KB payload
    await ctx.Response.WriteAsync(payload);
});

app.MapPost("/flaky", async (HttpContext ctx) =>
{
    if (TestState.ExecutionCount == 1)
    {
        ctx.Response.StatusCode = 500;
        //throw new Exception("Simulated failure");
    }

    await ctx.Response.WriteAsync("Success");
});
app.Run();