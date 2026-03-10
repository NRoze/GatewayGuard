using System;
using System.Collections.Generic;
using System.Text;

namespace GatewayGuard;

public class IdempotencyOptions
{
    public string IdempotencyHeaderName { get; set; } = "Idempotency-Key";
    public string RedisConnection { get; set; } = "localhost:6379";
    public bool EnableFingerprinting { get; set; } = true;
    public int MaxConcurrentRequests { get; set; } = 1000;
    public double CircuitBreakerThreshold { get; set; } = 0.1;
}
