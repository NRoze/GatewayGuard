using StackExchange.Redis;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace GatewayGuard;

public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IDatabase _db;
    private readonly IdempotencyOptions _options;

    public RedisIdempotencyStore(string connection, IdempotencyOptions options)
    {
        _options = options;
        var config = ConfigurationOptions.Parse(options.RedisConnection);
        config.AbortOnConnectFail = false;
        var redis = ConnectionMultiplexer.Connect(config);
        _db = redis.GetDatabase();
    }

    public async Task<IdempotencyRecord?> GetAsync(string key)
    {
        var data = await _db.StringGetAsync(key);

        return data.HasValue ? JsonSerializer.Deserialize<IdempotencyRecord>(((byte[])data!).AsSpan()) : null;
    }

    public async Task SetAsync(string key, string requestHash, HttpResponse response)
    {
        // Capture response body
        response.Body.Seek(0, SeekOrigin.Begin); // ensure position at start
        using var ms = new MemoryStream();
        await response.Body.CopyToAsync(ms);
        var bodyBytes = ms.ToArray();

        // Capture headers
        var headers = response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

        var record = new IdempotencyRecord
        {
            RequestHash = requestHash,
            StatusCode = response.StatusCode,
            Headers = headers,
            Body = bodyBytes
        };

        var json = JsonSerializer.Serialize(record);
        await _db.StringSetAsync(key, json, _options.IdempotencyKeyExpiration);
    }
}