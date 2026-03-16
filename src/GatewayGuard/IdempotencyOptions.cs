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
    /// How long retry requests cached duration should be retained.
    /// </summary>
    public TimeSpan SingleFlightExpiration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Redis connection string used by the <see cref="RedisIdempotencyStore"/>.
    /// </summary>
    public string RedisConnection { get; set; } = "localhost:6379";

    /// <summary>
    /// When true, a fingerprint will be generated from the request body and used as the key when no header is provided.
    /// </summary>
    public bool EnableFingerprinting { get; set; } = true;

    /// <summary>
    /// Maximum number of concurrent in-flight requests that will be coordinated by the single-flight manager.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 1000;

    /// <summary>
    /// Threshold used by any circuit breaker logic (fraction of failures that triggers the breaker).
    /// </summary>
    public double CircuitBreakerThreshold { get; set; } = 0.1;

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
}
