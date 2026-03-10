using System;
using System.Collections.Generic;
using System.Text;

namespace GatewayGuard;

public sealed record IdempotencyOptions
{
    public string IdempotencyHeaderName { get; init; } = "Idempotency-Key";
    public TimeSpan IdempotencyKeyExpiration { get; init; } = TimeSpan.FromMinutes(5);
    public string RedisConnection { get; init; } = "localhost:6379";
    public bool EnableFingerprinting { get; init; } = true;
    public int MaxConcurrentRequests { get; init; } = 1000;
    public double CircuitBreakerThreshold { get; init; } = 0.1;
}
