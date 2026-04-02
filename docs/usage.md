# GatewayGuard — Usage Guide

This document describes how to add, configure and run GatewayGuard in an ASP.NET Core application. 
GatewayGuard is exporter-agnostic; the host application configures OpenTelemetry exporters (Prometheus, OTLP, etc.) as needed.

## Recommended packages (host application)
- `OpenTelemetry`
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Instrumentation.AspNetCore`
- `OpenTelemetry.Exporter.Prometheus.AspNetCore` (optional — only if you want a Prometheus scrape endpoint)

## Quick start (Program.cs)
The host application must register GatewayGuard services and may optionally register OpenTelemetry exporters. 
The following example demonstrates wiring GatewayGuard and exposing a Prometheus scrape endpoint.

```csharp
using GatewayGuard;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Services.AddLogging();

// Configure OpenTelemetry metrics (host chooses exporter)
builder.Services.AddOpenTelemetry()
    .WithMetrics(metricBuilder =>
    {
        metricBuilder
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("SampleApi"))
            .AddAspNetCoreInstrumentation()
            .AddMeter("GatewayGuard") // must match Meter name used by the library
            .AddPrometheusExporter();   // optional: expose /metrics for Prometheus scraping
    });

// Configure GatewayGuard
builder.Services.AddGatewayGuard(options =>
{
    options.IdempotencyHeaderName = "X-Idempotency-Key";
    options.EnableFingerprinting = true; // generate fingerprint when header missing
    
    // Security: Scope keys to the authenticated user to prevent cross-tenant data leakage
    options.KeyScopeResolver = ctx => ctx.User.Identity?.Name ?? "";
    
    options.IdempotencyKeyExpiration = TimeSpan.FromMinutes(5);
    options.IdempotencyLockExpiration = TimeSpan.FromSeconds(5);
    options.MaxConcurrentRequests = 1000;
    options.MaxCachedBodySizeBytes = 256 * 1024; // 256 KiB
    options.RedisConnection = "localhost:6379";
});

var app = builder.Build();

// Enable the GatewayGuard middleware
app.UseGatewayGuard();

// If using the Prometheus exporter, map the scraping endpoint
app.MapPrometheusScrapingEndpoint();

app.MapPost("/orders", async ctx =>
{
    // your handler
    await ctx.Response.WriteAsync("Order processed!");
});

app.Run();
```

Notes
- The library publishes metrics to a `Meter` named `GatewayGuard`. The host must call `AddMeter("GatewayGuard")` when configuring OpenTelemetry so those instruments are collected.
- The Prometheus exporter exposes `/metrics` only if you call `app.MapPrometheusScrapingEndpoint()` and the Prometheus exporter package is installed.

## Idempotency configuration
Configure application behaviour through `IdempotencyOptions` (provided to `AddGatewayGuard`):

- `IdempotencyHeaderName` (string): header to read the idempotency key from (default `Idempotency-Key`).
- `EnableFingerprinting` (bool): when true, the middleware computes a fingerprint from method+path+body if the header is missing.
- `KeyScopeResolver` (Func<HttpContext, string>): A delegate to prefix and scope idempotency keys to a specific user or tenant, preventing cross-tenant data leakage.
- `FingerprintedHeaders` (ISet<string>): Which HTTP request headers should be included in fingerprint generation (default includes `Authorization`).
- `IgnoredResponseHeaders` (ISet<string>): Which HTTP response headers should not be cached and replayed (default blocks `Date`, `Set-Cookie`, `Transfer-Encoding`, `Connection`).
- `EnableMetrics` (bool): enable or disable metric recording inside the library.
- `IdempotencyKeyExpiration` / `IdempotencyLockExpiration` / `SingleFlightExpiration`: TTLs used for stored responses, locks and in-process coordination.
- `MaxCachedBodySizeBytes` (long): skip caching responses larger than this threshold to avoid excessive memory & storage usage.
- `FailClosedOnStoreError` (bool): when true, requests fail with 503 if the backing store is unavailable; otherwise middleware falls back to pass-through.

## How it works (summary)

1. Middleware checks whether the request method is enabled (POST by default).
2. It extracts an idempotency key from the configured header or generates a fingerprint when enabled.
3. If a cached response exists that matches the fingerprint, it is replayed (status, headers, body).
4. Concurrent requests for the same key are coordinated via `SingleFlight` (in-process) and a distributed lock via `IIdempotencyStore` (e.g., Redis). 
Only the first request executes the handler; others wait and then read the cached result.

## Metrics and observability
- The library registers a `Meter` named `GatewayGuard` and exposes counters/histograms for request duration, cache hits/misses and lock activity. 
- Do not use idempotency keys as metric labels (high cardinality).
- Let the host application decide how to export metrics. Prometheus scraping and OTLP collectors are common choices.

## Testing
- Use the included integration tests in `tests/GatewayGuard.Tests` for functional verification.
- The `tests/GatewayGuard.Benchmarks` project includes `BenchmarkDotNet` scenarios for latency and contention.

## Best practices
- Avoid caching very large responses; tune `MaxCachedBodySizeBytes` according to your workload.
- Secure the backing store (Redis) and use TLS/auth where appropriate.
- Enable application-level logging and metrics (host controls sinks/exporters).
- Keep metric label cardinality low.
