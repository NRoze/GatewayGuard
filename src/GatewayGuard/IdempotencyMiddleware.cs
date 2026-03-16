using Microsoft.AspNetCore.Http;

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
        SingleFlight singleFlight)
    {
        _next = next;
        _store = store;
        _options = options;
        _singleFlight = singleFlight;
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

        context.Request.EnableBuffering();

        (string key, string fingerprint) input = 
            await TryExtractKeyAndFingerprintAsync(context).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(input.key))
        {
            await context.SetResponseErrorMissingIdemKey();
        }

        await _singleFlight.ExecuteAsync(input.key, async (ct) =>
        {
            if (!await TryHandleCachedKey(context, input.key, input.fingerprint))
            {
                await ExecuteAndCaptureResponse(context, input.key, input.fingerprint);
            }

            return context.Response;
        });
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
        var cached = await _store.GetAsync(key).ConfigureAwait(false);

        if (cached != null)
        {
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
                await _store.SetAsync(key, fingerprint, context.Response).ConfigureAwait(false);
            }

            memStream.SeekBegin();
            await memStream.CopyToAsync(originalBody).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

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
    private bool IsIdempotentMethod(HttpContext context) =>
        _options.EnabledForMethods.Contains(new HttpMethod(context.Request.Method));

}