using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _store = store;
        _options = options;
        _singleFlight = singleFlight;
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
        //_logger.LogInformation("Processing request {Method} {Path} ({TraceId})", context.Request.Method, context.Request.Path, Activity.Current?.TraceId);
        if (!IsIdempotentMethod(context))
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();

        (string key, string fingerprint) input = 
            await TryExtractKeyAndFingerprintAsync(context).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(input.key))
        {
            await context.SetResponseErrorMissingIdemKey();

            return;
        }

        await _singleFlight.ExecuteAsync(input.key, async (ct) =>
        {
            await ExecuteRequestAsync(context, input).ConfigureAwait(false);

            return context.Response;
        });
    }

    private async Task ExecuteRequestAsync(HttpContext context, (string key, string fingerprint) input)
    {
        _logger.LogInformation("Acquiring lock for key {Key} ({TraceId})", input.key, Activity.Current?.TraceId);
        var lockValue = await _store.TryAcquireLockAsync(
                input.key,
                _options.IdempotencyKeyExpiration)
            .ConfigureAwait(false);

        if (lockValue is null)
        {
            _logger.LogInformation("Lock already held for key {Key}, waiting for completion ({TraceId})", input.key, Activity.Current?.TraceId);
            await _store.WaitForCompletionAsync(input.key, TimeSpan.FromSeconds(10));//TBD configurable
            if (!await TryHandleCachedKey(context, input.key, input.fingerprint))
            { 
                await context.SetResponseErrorConflictIdemKey();
            }
            var bodyString = context.Request.Body.CanSeek ? new StreamReader(context.Request.Body).ReadToEnd() : "<non-seekable-body>";
            _logger.LogInformation("Finished waiting for key {Key}:{Body} ({TraceId})", input.key, bodyString, Activity.Current?.TraceId);
            return;
        }

        try
        {
            _logger.LogInformation("Lock acquired for key {Key}, executing request ({TraceId})", input.key, Activity.Current?.TraceId);
            if (!await TryHandleCachedKey(context, input.key, input.fingerprint))
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

    private async Task<bool> TryHandleCachedKey(HttpContext context, string key, string fingerprint)
    {
        var cached = await _store.GetResponse(key).ConfigureAwait(false);

        //_logger.LogInformation("Checking for cached response for key {Key} ({TraceId})", key, Activity.Current?.TraceId);
        if (cached != null)
        {
            //_logger.LogInformation("Cached response found for key {Key} (TraceId: {TraceId})", key, Activity.Current?.TraceId);
            if (cached.RequestHash == fingerprint)
            {
                await ReplayCachedResponse(context, cached);
                return true;
            }
            else
            {
                await context.SetResponseErrorConflictIdemKey();
                return true;
            }
        }

        return false;
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