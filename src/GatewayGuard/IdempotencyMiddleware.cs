using GatewayGuard.Extensions;
using Microsoft.AspNetCore.Http;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Timeout;
using StackExchange.Redis;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using GatewayGuard.Logging;

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
    private readonly ResiliencePipelineProvider<string> _pipelineProvider;
    private readonly ILogger<IdempotencyMiddleware> _logger;
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
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _store = store;
        _options = options;
        _singleFlight = singleFlight;
        _pipelineProvider = pipelineProvider;
        _logger = logger;
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
            await CallNext(context);
            return;
        }

        context.Request.EnableBuffering(bufferThreshold: 1024 * 64);

        (string key, string fingerprint) input =
            await TryExtractKeyAndFingerprintAsync(context).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(input.key))
        {
            _logger.MissingIdempotencyKeyWarning();
            await context.SetResponseErrorMissingIdemKey();

            return;
        }

        IdempotencyRecord? cachedRecord = null;

        try
        {
            cachedRecord = await _singleFlight.ExecuteAsync(input.key, async (ct) =>
            {
                var pipeline = _pipelineProvider.GetPipeline(_options.ResiliencePolicyName);

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
            _logger.IdempotencyStoreUnavailableWarning(ex);
            
            if (_options.FailClosedOnStoreError)
            {
                await context.SetResponseErrorUnavailableIdemStore();
                return;
            }

            await CallNext(context);
            return;
        }

        if (cachedRecord is not null)
        {
            _logger.ReplayingCachedResponseDebug(input.key);
            await ReplayCachedResponse(context, cachedRecord);
        }
    }

    /// <summary>
    /// Executes the idempotency logic for the current request, including lock acquisition, cache lookup, and response capture.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="input">A tuple containing the idempotency key and request fingerprint.</param>
    /// <returns>A cached record if found or the response was captured; otherwise <c>null</c>.</returns>
    private async Task<IdempotencyRecord?> ExecuteRequestAsync(HttpContext context, (string key, string fingerprint) input)
    {
        IdempotencyRecord? record = default;

        var lockValue = await _store.TryAcquireLockAsync(
                input.key,
                _options.IdempotencyKeyExpiration)
            .ConfigureAwait(false);

        if (lockValue is null)
        {
            _logger.WaitingForLockCompletionDebug(input.key);
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
            record = await TryHandleCachedKey(context, input.key, input.fingerprint);

            if (record is null)
            {
                _logger.ExecutingRequestDebug(input.key);
                await ExecuteAndCaptureResponse(context, input.key, input.fingerprint);
            }
        }
        finally
        {
            if (lockValue is not null)
            {
                await _store.ReleaseLockAsync(input.key, lockValue).ConfigureAwait(false);
            }
        }

        return record;
    }

    /// <summary>
    /// Extracts or generates the idempotency key and request fingerprint from the current HTTP request.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A tuple containing the idempotency key and request fingerprint.</returns>
    private async Task<(string, string)> TryExtractKeyAndFingerprintAsync(HttpContext context)
    {
        var key = context.Request.Headers[_options.IdempotencyHeaderName].ToString();
        var fingerprint = await RequestFingerprint.GenerateAsync(context).ConfigureAwait(false);

        key = string.IsNullOrWhiteSpace(key) && _options.EnableFingerprinting
            ? fingerprint
            : key;

        return (key, fingerprint);
    }

    /// <summary>
    /// Retrieves a cached response for the given idempotency key and validates the request fingerprint.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="key">The idempotency key.</param>
    /// <param name="fingerprint">The request fingerprint to validate against cached data.</param>
    /// <returns>The cached record if found and fingerprint matches; otherwise <c>null</c>.</returns>
    private async Task<IdempotencyRecord?> TryHandleCachedKey(HttpContext context, string key, string fingerprint)
    {
        var cached = await _store.GetResponse(key).ConfigureAwait(false);

        if (cached is not null && cached.RequestHash != fingerprint)
        {
            _logger.ConflictingIdempotencyKeyWarning(key);
            await context.SetResponseErrorConflictIdemKey();
            return null;
        }

        return cached;
    }

    /// <summary>
    /// Executes the next middleware in the pipeline, captures the response, and stores it for future idempotency replay.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="key">The idempotency key for storage.</param>
    /// <param name="fingerprint">The request fingerprint for validation.</param>
    /// <returns>A task that completes when the response has been executed and stored.</returns>
    private async Task ExecuteAndCaptureResponse(HttpContext context, string key, string fingerprint)
    {
        var originalBody = context.Response.Body;
        using var memStream = new MemoryStream();
        context.Response.Body = memStream;

        try
        {
            await CallNext(context);

            if (context.Response.StatusCode < 500)
            {
                memStream.SeekBegin();

                if ((ulong)memStream.Length <= (ulong)_options.MaxCachedBodySizeBytes)
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

    /// <summary>
    /// Determines whether the current request method is configured for idempotency handling.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns><c>true</c> if the request method is enabled for idempotency; otherwise <c>false</c>.</returns>
    private bool IsIdempotentMethod(HttpContext context) =>
        _options.EnabledForMethods.Contains(new HttpMethod(context.Request.Method));

    /// <summary>
    /// Replays a cached HTTP response to the client by restoring status code, headers, and body.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="record">The cached idempotency record containing the response to replay.</param>
    /// <returns>A task that completes when the response has been written.</returns>
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

    private async Task CallNext(HttpContext context)
    {
        if (!_options.EnableMetrics)
        { 
            await _next(context).ConfigureAwait(false);
            return;
        }

        var sw = Stopwatch.StartNew();

        await _next(context).ConfigureAwait(false);
        sw.Stop();

        GatewayGuardMetrics.RequestDurationMs.Record(
            sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("method", context.Request.Method));

        GatewayGuardMetrics.RequestsTotal.Add(
            1,
            new KeyValuePair<string, object?>("result", context.Response.StatusCode.ToString()));

    }
}