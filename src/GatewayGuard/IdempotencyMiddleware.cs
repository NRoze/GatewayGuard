using Microsoft.AspNetCore.Http;

namespace GatewayGuard;

public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IIdempotencyStore _store;
    private readonly IdempotencyOptions _options;

    public IdempotencyMiddleware(RequestDelegate next, IIdempotencyStore store, IdempotencyOptions options)
    {
        _next = next;
        _store = store;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Determine key (header or fingerprint)
        var key = context.Request.Headers[_options.IdempotencyHeaderName].ToString();
        var fingerprint = await RequestFingerprint.GenerateAsync(context);

        if (_options.EnableFingerprinting && string.IsNullOrWhiteSpace(key))
        {
            key = fingerprint;
        }

        // 2. Check cache in Redis
        var cached = await _store.GetAsync(key);
        if (cached != null && cached.RequestHash == fingerprint)
        {
            await ReplayCachedResponse(context, cached);
            return;
        }

        // 3. Capture response for caching
        var originalBody = context.Response.Body;
        using var memStream = new MemoryStream();
        context.Response.Body = memStream;

        try
        {
            await _next(context); // Execute backend

            memStream.Seek(0, SeekOrigin.Begin);
            await _store.SetAsync(key, fingerprint, context.Response);

            memStream.Seek(0, SeekOrigin.Begin);
            await memStream.CopyToAsync(originalBody); // write to original response
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

        await context.Response.Body.WriteAsync(record.Body);
    }
}