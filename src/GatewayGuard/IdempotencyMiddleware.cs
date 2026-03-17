using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Retry;
using Polly.Timeout;
using StackExchange.Redis;
using System.Diagnostics;

namespace GatewayGuard;

/// <summary>
/// ASP.NET Core middleware that enforces idempotency for incoming HTTP requests.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IIdempotencyStore _store;
    private readonly SingleFlight _singleFlight;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyMiddleware> _logger;
    private readonly ResiliencePipelineProvider<string> _pipelineProvider;
    /// <summary>
    /// Constructs a new instance of <see cref="IdempotencyMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="store">The store used to persist and retrieve idempotency records.</param>
    /// <param name="options">Options that control middleware behavior.</param>
    public IdempotencyMiddleware(
        RequestDelegate next,
        IIdempotencyStore store,
        IdempotencyOptions options,
        SingleFlight singleFlight,
        ILogger<IdempotencyMiddleware> logger,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _next = next;
        _store = store;
        _options = options;
        _singleFlight = singleFlight;
        _logger = logger;
        _pipelineProvider = pipelineProvider;
    }
    /// <summary>
    /// Processes an incoming HTTP request and enforces idempotency rules:
    /// - Extracts or generates the idempotency key/fingerprint
    /// - Replays cached responses when available
    /// - Captures and stores responses for future replay
    /// </summary>
    /// <param name="context">The current <see cref="HttpContext"/>.</param>
    /// <returns>A task that completes when request processing is finished.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsIdempotentMethod(context))
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering(bufferThreshold: 1024 * 64);

        (string key, string fingerprint) input =
            await TryExtractKeyAndFingerprintAsync(context).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(input.key))
        {
            await context.SetResponseErrorMissingIdemKey();

            return;
        }

        IdempotencyRecord? cachedRecord = null;

        try
        {
            cachedRecord = await _singleFlight.ExecuteAsync(input.key, async (ct) =>
            {
                var pipeline = _pipelineProvider.GetPipeline(_options.CircuitBreakerPolicyName);

                return await pipeline.ExecuteAsync(async innerCt =>
                    await ExecuteRequestAsync(context, input).ConfigureAwait(false), context.RequestAborted);
            });
        }
        catch (Exception ex) when (
            ex is BrokenCircuitException ||
            ex is RedisConnectionException ||
            ex is RedisTimeoutException ||
            ex is TimeoutRejectedException)
        {
            if (_options.FailClosedOnStoreError)
            {
                _logger.LogError(ex, "Redis is down and FailClosedOnStoreError is true. Rejecting request.");
                await context.SetResponseErrorUnavailableIdemStore();
                return;
            }

            _logger.LogWarning(ex, "Redis Idempotency layer degraded. Bypassing exact-once guarantees for request {Key}", input.key);
            await _next(context);
            return;
        }

        if (cachedRecord is not null)
        {
            await ReplayCachedResponse(context, cachedRecord);
        }
    }

    private async Task<IdempotencyRecord?> ExecuteRequestAsync(HttpContext context, (string key, string fingerprint) input)
    {
        IdempotencyRecord? record = default;

        _logger.LogInformation("Acquiring lock for key {Key} ({TraceId})", input.key, Activity.Current?.TraceId);
        var lockValue = await _store.TryAcquireLockAsync(
                input.key,
                _options.IdempotencyKeyExpiration)
            .ConfigureAwait(false);

        if (lockValue is null)
        {
            _logger.LogInformation(
                "Lock already held for key {Key}, waiting for completion ({TraceId})",
                input.key,
                Activity.Current?.TraceId);
            await _store.WaitForCompletionAsync(input.key, _options.IdempotencyLockExpiration);
            record = await TryHandleCachedKey(context, input.key, input.fingerprint);
            if (record is null)
            {
                await context.SetResponseErrorUnknown();
            }

            return record;
        }

        try
        {
            _logger.LogInformation("Lock acquired for key {Key}, executing request ({TraceId})", input.key, Activity.Current?.TraceId);
            record = await TryHandleCachedKey(context, input.key, input.fingerprint);

            if (record is null)
            {
                await ExecuteAndCaptureResponse(context, input.key, input.fingerprint);
            }
        }
        finally
        {
            if (lockValue is not null)
            {
                _logger.LogInformation("Releasing lock for key {Key} ({TraceId})", input.key, Activity.Current?.TraceId);
                await _store.ReleaseLockAsync(input.key, lockValue).ConfigureAwait(false);
            }
        }

        return record;
    }

    private async Task<(string, string)> TryExtractKeyAndFingerprintAsync(HttpContext context)
    {
        var key = context.Request.Headers[_options.IdempotencyHeaderName].ToString();
        var fingerprint = await RequestFingerprint.GenerateAsync(context).ConfigureAwait(false);

        key = string.IsNullOrWhiteSpace(key) && _options.EnableFingerprinting
            ? fingerprint
            : key;

        return (key, fingerprint);
    }

    private async Task<IdempotencyRecord?> TryHandleCachedKey(HttpContext context, string key, string fingerprint)
    {
        var cached = await _store.GetResponse(key).ConfigureAwait(false);

        if (cached is not null && cached.RequestHash != fingerprint)
        {
            await context.SetResponseErrorConflictIdemKey();
            return null;
        }

        return cached;
    }

    private async Task ExecuteAndCaptureResponse(HttpContext context, string key, string fingerprint)
    {
        var originalBody = context.Response.Body;
        using var memStream = new MemoryStream();
        context.Response.Body = memStream;

        try
        {
            await _next(context).ConfigureAwait(false);

            if (context.Response.StatusCode < 500)
            {
                memStream.SeekBegin();
                await _store.SaveResponse(key, fingerprint, context.Response).ConfigureAwait(false);
            }

            memStream.SeekBegin();
            await memStream.CopyToAsync(originalBody).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private bool IsIdempotentMethod(HttpContext context) =>
        _options.EnabledForMethods.Contains(new HttpMethod(context.Request.Method));
    private static async Task ReplayCachedResponse(HttpContext context, IdempotencyRecord record)
    {
        context.Response.StatusCode = record.StatusCode;
        context.Response.Headers.Clear();
        foreach (var h in record.Headers)
        {
            context.Response.Headers[h.Key] = h.Value;
        }

        await context.Response.Body.WriteAsync(record.Body).ConfigureAwait(false);
    }
}