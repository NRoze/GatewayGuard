using Microsoft.AspNetCore.Http;

namespace GatewayGuard;

public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IIdempotencyStore _store;
    private readonly IdempotencyOptions _options;

    public IdempotencyMiddleware(
        RequestDelegate next, 
        IIdempotencyStore store, 
        IdempotencyOptions options)
    {
        _next = next;
        _store = store;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var key = context.Request.Headers[_options.IdempotencyHeaderName].ToString();
        var fingerprint = await RequestFingerprint.GenerateAsync(context).ConfigureAwait(false);

        if (_options.EnableFingerprinting && string.IsNullOrWhiteSpace(key))
        {
            key = fingerprint;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            SetError(
                context, 
                StatusCodes.Status400BadRequest, 
                $"Missing required idempotency key header.");
            return;
        }

        if (await TryHandleCachedKey(context, key, fingerprint))
        {
            return;
        }

        await ExecuteAndCaptureResponse(context, key, fingerprint);
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
                SetError(
                    context, 
                    StatusCodes.Status409Conflict, 
                    "Idempotency key already used with a different payload.");
                return true;
            }
        }

        return false;
    }

    private static void SetError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.WriteAsync(message).ConfigureAwait(false);
    }

    private async Task ExecuteAndCaptureResponse(HttpContext context, string key, string fingerprint)
    {
        var originalBody = context.Response.Body;
        using var memStream = new MemoryStream();
        context.Response.Body = memStream;

        try
        {
            await _next(context); // Execute backend

            memStream.SeekBegin();
            await _store.SetAsync(key, fingerprint, context.Response).ConfigureAwait(false);

            memStream.SeekBegin();
            await memStream.CopyToAsync(originalBody).ConfigureAwait(false); // write to original response
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
}