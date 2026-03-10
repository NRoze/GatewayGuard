using System;
using System.Collections.Generic;
using System.Text;

namespace GatewayGuard;

public sealed class IdempotencyOptions
{
    public string IdempotencyHeaderName { get; set; } = "Idempotency-Key";
    public TimeSpan IdempotencyKeyExpiration { get; set; } = TimeSpan.FromMinutes(5);
    public string RedisConnection { get; set; } = "localhost:6379";
    public bool EnableFingerprinting { get; set; } = true;
    public int MaxConcurrentRequests { get; set; } = 1000;
    public double CircuitBreakerThreshold { get; set; } = 0.1;
}
