using GatewayGuard;
using SampleApi;

var builder = WebApplication.CreateBuilder(args);
var guardEnabled = builder.Configuration.GetValue<bool?>("GatewayGuard:Enabled") ?? true;
var fingerprintEnabled = builder.Configuration.GetValue<bool?>("FingerprintEnabled:Enabled") ?? true;

builder.Services.AddLogging();

if (guardEnabled)
{
    builder.Services.AddGatewayGuard(options =>
    {
        options.IdempotencyHeaderName = "X-Idempotency-Key";
        options.EnableFingerprinting = fingerprintEnabled;
        options.IdempotencyKeyExpiration = TimeSpan.FromMinutes(1);
        options.MaxConcurrentRequests = 500;
        options.RedisConnection = "localhost:6379";
    });
}

var app = builder.Build();

if (guardEnabled)
{
    app.UseGatewayGuard();
}

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

    var payload = new string('X', 200_000);
    await ctx.Response.WriteAsync(payload);
});

app.MapPost("/flaky", async (HttpContext ctx) =>
{
    if (TestState.ExecutionCount == 1)
    {
        ctx.Response.StatusCode = 500;
    }

    await ctx.Response.WriteAsync("Success");
});

app.Run();