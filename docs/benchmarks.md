# GatewayGuard Performance Benchmarks

This document contains performance benchmarks for the GatewayGuard middleware to help you understand the overhead and latency characteristics under different configuration scenarios.

The benchmarks were executed with [BenchmarkDotNet](https://benchmarkdotnet.org/) simulating high-throughput conditions.

## Benchmark Environment

- **OS:** Windows 10 (22H2)
- **CPU:** Intel Core i7-7500U, 1 CPU, 4 logical and 2 physical cores
- **Runtime:** .NET 10.0

## Results Summary

The table below details the performance characteristics for simple requests (`Echo` endpoint) comparing the baseline (GatewayGuard disabled) with various enabled configurations.

| Scenario | Guard Enabled | Fingerprint Enabled | Mean Duration | Median Duration | Notes |
|----------|---------------|---------------------|--------------:|----------------:|-------|
| **Baseline (No Guard)** | `false` | `false` | `42.14 μs` | `40.91 μs` | The baseline processing time without the idempotency middleware. |
| **Baseline with Contention**| `false` | `false` | `185.96 μs` | `182.97 μs` | Simulates concurrent requests hitting the endpoint simultaneously. |
| **No Guard (Header Only)** | `false` | `true` | `40.63 μs` | `39.81 μs` | Minimal difference; fingerprinting is ignored since the guard is disabled. |
| **Guard Enabled (Header)** | `true` | `false` | `2,572.68 μs` | `2,347.30 μs` | Idempotency guard checking Redis cache. Reflects typical Redis I/O overhead. |
| **Guard + Contention (Header)**| `true` | `false` | `3,444.61 μs` | `3,248.16 μs` | Idempotency guard under contention using standard idempotency keys. |
| **Guard Enabled (Fingerprint)**| `true` | `true` | `3,932.28 μs` | `3,753.49 μs` | Uses full body hashing for key generation when header is missing. |
| **Guard + Contention (Fp)** | `true` | `true` | `3,313.03 μs` | `3,045.53 μs` | Fingerprinting under heavy contention. |

### Key Takeaways

1. **Fingerprinting Cost**: Payload fingerprinting (hashing the request body) introduces an additional ~1 millisecond of processing time over header-based keys for small payloads (`3,932.28 μs` vs `2,572.68 μs`). It is strongly recommended that clients provide the `Idempotency-Key` header when possible.
2. **Redis I/O is the Bottleneck**: The vast majority of the time added by the middleware is dictated by network latency to the `IIdempotencyStore` (Redis). 
3. **SingleFlight Works**: As seen under contention, the times effectively scale up linearly but without executing duplicate underlying requests.

> **Note**: These numbers are highly dependent on your Redis setup proximity. In a cloud environment where the app and Redis are in the same VPC with low latency, you can expect significantly lower overhead.
