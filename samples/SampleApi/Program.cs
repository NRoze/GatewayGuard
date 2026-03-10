using GatewayGuard;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGatewayGuard(options =>
{
    options.IdempotencyHeaderName = "X-Idempotency-Key";
    options.EnableFingerprinting = true;
});

var app = builder.Build();
app.UseGatewayGuard();

app.MapPost("/orders", async (HttpContext ctx) =>
{
    await Task.Delay(500); // simulate processing
    await ctx.Response.WriteAsync("Order processed!");
});

app.Run();