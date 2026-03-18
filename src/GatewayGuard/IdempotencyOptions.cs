using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using StackExchange.Redis;

namespace GatewayGuard;

/// <summary>
/// Options controlling idempotency middleware behavior and storage configuration.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// The header name to read the idempotency key from. Defaults to "Idempotency-Key".
    /// </summary>
    public string IdempotencyHeaderName { get; set; } = "Idempotency-Key";

    /// <summary>
    /// How long idempotency keys and their cached responses should be retained.
    /// </summary>
    public TimeSpan IdempotencyKeyExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to wait for a lock acquisition before timing out during single-flight coordination.
    /// </summary>
    public TimeSpan IdempotencyLockExpiration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long retry requests cached duration should be retained.
    /// </summary>
    public TimeSpan SingleFlightExpiration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long the circuit breaker should remain open before attempting to resume normal operation.
    /// </summary>
    public TimeSpan ResiliencePipelineExpiration { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Timeout in milliseconds for Redis connection attempts.
    /// </summary>
    public int RedisConnectionTimeoutMs { get; set; } = 500;

    /// <summary>
    /// Redis connection string used by the <see cref="RedisIdempotencyStore"/>.
    /// </summary>
    public string RedisConnection { get; set; } = "localhost:6379";

    /// <summary>
    /// When true, a fingerprint will be generated from the request body and used as the key when no header is provided.
    /// </summary>
    public bool EnableFingerprinting { get; set; } = true;

    /// <summary>
    /// When true, GatewayGuard records runtime metrics through the configured meter.
    /// Disable to avoid metric recording overhead in latency-sensitive scenarios.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Maximum number of concurrent in-flight requests that will be coordinated by the single-flight manager.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 1000;

    /// <summary>
    /// Maximum response body size (in bytes) that will be captured and cached for idempotency replay.
    /// Responses larger than this threshold will not be saved to the idempotency store to avoid excessive memory/Redis usage.
    /// Defaults to 256 KiB.
    /// </summary>
    public long MaxCachedBodySizeBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// The set of HTTP methods for which idempotency behavior is applied by the middleware.
    /// </summary>
    /// <remarks>
    /// By default this contains only <see cref="HttpMethod.Post"/>. Modify this set to enable
    /// idempotency for additional methods (for example, <c>HttpMethod.Put</c> or <c>HttpMethod.Patch</c>).
    /// The middleware checks this collection to decide whether to attempt to read an idempotency key,
    /// to generate a request fingerprint when enabled, or to coordinate single-flight request handling.
    /// </remarks>
    public ISet<HttpMethod> EnabledForMethods { get; set; } =
        new HashSet<HttpMethod>
        {
            HttpMethod.Post
        };
    /// <summary>
    /// Configuration for the circuit breaker policy used to handle resilience and fault tolerance.
    /// Defines failure thresholds and handling predicates for Redis-related exceptions.
    /// </summary>
    public CircuitBreakerStrategyOptions CircuitBreakerStrategy { get; set; } =
        new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(1),
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(5),
            ShouldHandle = new PredicateBuilder()
                        .Handle<RedisConnectionException>()
                        .Handle<RedisTimeoutException>()
                        .Handle<TimeoutRejectedException>()
        };

    /// <summary>
    /// Determines behavior when the idempotency store (Redis) is unavailable.
    /// True = Fail-Closed (returns 503 Service Unavailable).
    /// False = Fail-Open (bypasses idempotency and executes the request anyway).
    /// Defaults to False for maximum availability.
    /// </summary>
    public bool FailClosedOnStoreError { get; set; } = false;

    /// <summary>
    /// The name of the resilience pipeline policy registered for circuit breaker management.
    /// Used to retrieve the circuit breaker from the resilience pipeline provider.
    /// </summary>
    public string ResiliencePolicyName { get; set; } = "GatewayGuardCircuitBreaker";
}
