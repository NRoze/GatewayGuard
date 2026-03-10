using StackExchange.Redis;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace GatewayGuard;

/// <summary>
/// Redis-backed implementation of <see cref="IIdempotencyStore"/>.
/// Stores serialized <see cref="IdempotencyRecord"/> instances keyed by idempotency keys.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore, IDisposable, IAsyncDisposable
{
    private readonly ConnectionMultiplexer _multiplexer;
    private readonly IDatabase _db;
    private readonly IdempotencyOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="RedisIdempotencyStore"/>.
    /// </summary>
    /// <param name="options">Configuration options used to connect to Redis and control expiration.</param>
    public RedisIdempotencyStore(IdempotencyOptions options)
    {
        _options = options;
        var config = ConfigurationOptions.Parse(options.RedisConnection);
        config.AbortOnConnectFail = false;
        _multiplexer = ConnectionMultiplexer.Connect(config);
        _db = _multiplexer.GetDatabase();
    }

    /// <summary>
    /// Retrieves the cached idempotency record for the specified key.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <returns>The <see cref="IdempotencyRecord"/> if found; otherwise <c>null</c>.</returns>
    public async Task<IdempotencyRecord?> GetAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        var data = await _db.StringGetAsync(key).ConfigureAwait(false);
        return data.HasValue ? JsonSerializer.Deserialize<IdempotencyRecord>(((byte[])data!).AsSpan()) : null;
    }

    /// <summary>
    /// Stores the provided response and request hash under the given idempotency key.
    /// </summary>
    /// <param name="key">The idempotency key to store.</param>
    /// <param name="requestHash">A hash/fingerprint of the request used for collision detection.</param>
    /// <param name="response">The HTTP response to capture.</param>
    public async Task SetAsync(string key, string requestHash, HttpResponse response)
    {
        if (string.IsNullOrEmpty(key)) return;

        var bodyBytes = await response.Body.CopyAsync().ConfigureAwait(false);
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

    /// <summary>
    /// Synchronously disposes Redis resources.
    /// </summary>
    public void Dispose()
    {
        _multiplexer?.Dispose();
    }

    /// <summary>
    /// Asynchronously closes the Redis connection and releases resources.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes when resources are released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_multiplexer != null)
        {
            await _multiplexer.CloseAsync().ConfigureAwait(false);
        }
    }
}