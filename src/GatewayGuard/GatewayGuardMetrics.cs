using System.Diagnostics.Metrics;

namespace GatewayGuard;

internal static class GatewayGuardMetrics
{
    internal static readonly Meter Meter = new("GatewayGuard", "1.0.0");

    internal static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>("gatewayguard_requests_total");
    internal static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("gatewayguard_cache_hits");
    internal static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("gatewayguard_cache_misses");
    internal static readonly Counter<long> LockAttempts = Meter.CreateCounter<long>("gatewayguard_lock_attempts");
    internal static readonly Counter<long> LockAcquired = Meter.CreateCounter<long>("gatewayguard_lock_acquired");
    internal static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>("gatewayguard_request_duration_ms");
}
