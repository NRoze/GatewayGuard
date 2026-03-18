# GatewayGuard

GatewayGuard is a lightweight ASP.NET Core library that provides best-effort idempotency guarantees for HTTP requests. 
It coordinates concurrent requests that share an idempotency key, caches responses for replay, and integrates with OpenTelemetry-compatible metrics.

This repository contains the library (`src/GatewayGuard`), integration tests (`tests/GatewayGuard.Tests`), and benchmark scenarios (`tests/GatewayGuard.Benchmarks`).

## Key features

- Middleware-based idempotency for configurable HTTP methods (POST by default).
- Client-provided idempotency header or request fingerprinting when header is missing.
- In-process deduplication (`SingleFlight`) and distributed coordination via `IIdempotencyStore` implementations (Redis provided).
- Configurable limits to avoid caching very large responses.
- Library-level metrics via `System.Diagnostics.Metrics` (OpenTelemetry compatible).

## Prerequisites

- .NET 10 SDK
- Redis (optional for full integration; the sample app and tests may expect a Redis instance)

## Quick build & test

From the repository root:

- Build: `dotnet build`
- Run tests: `dotnet test`
- Run benchmarks (requires BenchmarkDotNet tooling): open the `tests/GatewayGuard.Benchmarks` project and run via Visual Studio or `dotnet run` in that project.

## Example: wire into an ASP.NET Core app

See `docs/usage.md` for a complete example (Prometheus exporter sample). In short:

1. In `Program.cs`, register GatewayGuard:

```csharp
builder.Services.AddGatewayGuard(options =>
{
    options.IdempotencyHeaderName = "X-Idempotency-Key";
    options.EnableFingerprinting = true;
    options.RedisConnection = "localhost:6379"; // production: secure and configure appropriately
});

app.UseGatewayGuard();
```

2. Configure metrics in the host app (optional): call `AddMeter("GatewayGuard")` when configuring OpenTelemetry so library instruments are collected.

## Documentation

- Architecture overview: `docs/architecture.md`
- Usage and examples: `docs/usage.md`
- Performance benchmarks: `docs/benchmarks.md`

## Contribution

Contributions are welcome. Please follow these guidelines:

- Run and update unit/integration tests for behavioral changes.
- Keep public APIs documented with XML comments. Consider enabling XML doc warnings in CI (`CS1591`).
- Keep metric label cardinality low; do not use idempotency keys or request bodies as tags.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.