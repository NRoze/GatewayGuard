# GatewayGuard — Architecture Overview

Status: stable | Target: .NET 10 | C# 14

This document describes the architecture and runtime behavior of GatewayGuard, a small library that provides idempotency guarantees for HTTP requests in ASP.NET Core applications.

## Goals
- Provide exactly-once semantics (best-effort) for idempotent HTTP operations (configured methods).
- Coordinate concurrent requests that share an idempotency key (single-flight).
- Cache responses for replay for duplicate requests.
- Be pluggable (DI-friendly) and exporter-agnostic for metrics/telemetry.

## High-level components
- `IdempotencyMiddleware`
  - Core middleware that orchestrates request processing.
  - Responsibilities:
    - Extract idempotency key (header or generated fingerprint).
    - Short-circuit replay when a cached response exists.
    - Coordinate concurrent requests via `SingleFlight`.
    - Acquire per-key lock in the `IIdempotencyStore`.
    - Execute pipeline, capture response, and persist via `IIdempotencyStore`.
    - Expose minimal points for metrics and logging.
- `IIdempotencyStore`
  - Abstract storage contract for idempotency records, locks and completion notifications.
  - Implementations:
    - `RedisIdempotencyStore` — production-ready Redis-backed store; uses SET NX for locks, stores serialized `IdempotencyRecord`, uses pub/sub to notify waiters.
- `SingleFlight`
  - In-process coordination of concurrent requests that reference the same idempotency key.
  - Ensures only the first execution runs to completion; others wait for its result.
- `IdempotencyOptions`
  - Central configuration object registered via DI.
  - Important knobs: header name, expirations, fingerprinting, max cached body size, single-flight TTL, resilience policy name, and enable/disable metrics.
- `IdempotencyRecord`
  - Serialized representation of saved response: status code, headers, body and request fingerprint.
- Extensions
  - Small helpers for stream handling and consistent HTTP error responses (kept minimal and performant).
- Metrics (library-level)
  - Library instruments a `Meter` named `GatewayGuard` and exposes counters/histograms.
  - Exporters are the app's responsibility (library is exporter-agnostic).

## Request flow (conceptual)
1. Incoming HTTP request hits `IdempotencyMiddleware`.
2. Middleware checks whether method is enabled (e.g., POST).
3. Middleware extracts idempotency key:
   - Uses header `IdempotencyHeaderName` if present.
   - Otherwise (and if enabled) computes request fingerprint (method+path+body).
4. If key missing and fingerprinting disabled: respond 400.
5. Check store for cached response:
   - If cached and fingerprint matches: replay cached response (status, headers, body).
   - If cached but fingerprint mismatch: respond 409 Conflict.
6. Coordinate using `SingleFlight.ExecuteAsync(key, ...)`:
   - First caller acquires lock via `IIdempotencyStore.TryAcquireLockAsync`.
     - If lock acquired: execute request pipeline, capture response and call `SaveResponse`.
     - If lock not acquired: wait for completion notification (pub/sub / key existence), then read cached response.
7. On errors connecting to the store:
   - If `FailClosedOnStoreError` is true: return 503.
   - Else: bypass idempotency and call the next middleware.

## Concurrency & correctness notes
- Single-flight ensures a single execution for concurrent duplicates within a single process. Distributed concurrency is handled by store-level locks (Redis SET NX).
- Notification for waiters uses Redis pub/sub. Implementation uses subscribe-then-check to avoid the publish-before-subscribe race.
- To limit memory and Redis usage, responses larger than `MaxCachedBodySizeBytes` are not cached by default (configurable).

## Resilience & Observability
- Circuit breaker and timeouts are handled via the application's resilience pipeline (configurable in `IdempotencyOptions`).
- Metrics: library defines a `Meter` named `GatewayGuard`. Applications configure exporters (Prometheus/OTLP) and choose how to expose telemetry.
- Logging: middleware and store surfaces key events (lock acquired/released, cache hit/miss, store errors). Log level should be chosen in the host app to balance observability vs runtime cost.

## Performance considerations & optimizations
- Avoid caching extremely large responses to prevent memory and network pressure — use `MaxCachedBodySizeBytes`.
- Buffer sizes for fingerprint generation use pooled arrays (ArrayPool) to reduce allocations.
- `IdempotencyRecord` serialization uses UTF-8 bytes to avoid extra string allocations when storing to Redis.
- Keep metric labels low-cardinality; do not use idempotency key or request body as metric labels.

## Extension points
- `IIdempotencyStore` allows swapping Redis for any other persistence (in-memory, database, object storage).
- `IdempotencyOptions` can be extended with additional policies (e.g., selective caching rules).
- Metrics are produced via `System.Diagnostics.Metrics` (OpenTelemetry compatible); apps decide exporters.

## Security & privacy
- Do not store sensitive request bodies in the idempotency cache. If required, implement sanitization or avoid fingerprinting/caching for sensitive endpoints.
- Ensure Redis (or any backing store) is secured and access-controlled.

## Recommended deployment notes
- Use Redis with sufficient memory and TTL settings matching `IdempotencyKeyExpiration`.
- Scale `MaxConcurrentRequests` according to service capacity.
- Monitor metrics (cache hit ratio, lock contention, average request duration) and tune accordingly.
