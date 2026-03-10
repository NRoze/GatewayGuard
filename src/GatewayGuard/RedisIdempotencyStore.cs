using StackExchange.Redis;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace GatewayGuard;

public class RedisIdempotencyStore : IIdempotencyStore, IDisposable, IAsyncDisposable
{
    private readonly ConnectionMultiplexer _multiplexer;
    private readonly IDatabase _db;
    private readonly IdempotencyOptions _options;

    public RedisIdempotencyStore(IdempotencyOptions options)
    {
        _options = options;
        var config = ConfigurationOptions.Parse(options.RedisConnection);
        config.AbortOnConnectFail = false;
        _multiplexer = ConnectionMultiplexer.Connect(config);
        _db = _multiplexer.GetDatabase();
    }

    public async Task<IdempotencyRecord?> GetAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        var data = await _db.StringGetAsync(key).ConfigureAwait(false);
        return data.HasValue ? JsonSerializer.Deserialize<IdempotencyRecord>(((byte[])data!).AsSpan()) : null;
    }

    public async Task SetAsync(string key, string requestHash, HttpResponse response)
    {
        if (string.IsNullOrEmpty(key)) return;

        // Capture response body
        response.Body.Seek(0, SeekOrigin.Begin);
        using var ms = new MemoryStream();
        await response.Body.CopyToAsync(ms).ConfigureAwait(false);
        var bodyBytes = ms.ToArray();

        // Preserve multi-value headers safely
        var headers = response.Headers.ToDictionary(
            h => h.Key,
            h => string.Join(",", h.Value.ToArray()));

        var record = new IdempotencyRecord
        {
            RequestHash = requestHash,
            StatusCode = response.StatusCode,
            Headers = headers,
            Body = bodyBytes
        };

        var json = JsonSerializer.Serialize(record);
        await _db.StringSetAsync(key, json, _options.IdempotencyKeyExpiration).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _multiplexer?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer != null)
        {
            await _multiplexer.CloseAsync().ConfigureAwait(false);
        }
    }
}