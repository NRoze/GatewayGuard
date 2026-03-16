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
        //config.AbortOnConnectFail = false;
        config.ConnectTimeout = 100;//TBD: make this configurable
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

        var record = await IdempotencyRecord.CreateAsync(requestHash, response);
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

    /// <summary>
    /// Publishes a completion notification for the specified idempotency key using Redis pub/sub.
    /// This can be used to notify waiters that a response for the key is available.
    /// </summary>
    /// <param name="key">The idempotency key for which completion is being published.</param>
    /// <returns>A task that completes when the publish operation has finished.</returns>
    public async Task PublishCompletedAsync(string key)
    {
        var sub = _multiplexer.GetSubscriber();
        var channel = RedisChannel.Literal($"idem:{key}:done");

        await sub.PublishAsync(channel, "1").ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a completion notification for the given idempotency key or until the specified timeout elapses.
    /// </summary>
    /// <param name="key">The idempotency key to wait for.</param>
    /// <param name="timeout">Maximum time to wait for the completion notification.</param>
    /// <returns>
    /// A task that completes when the notification is received or the timeout expires.
    /// </returns>
    public async Task WaitForCompletionAsync(string key, TimeSpan timeout)
    {
        var sub = _multiplexer.GetSubscriber();
        var tcs = new TaskCompletionSource();

        var channel = RedisChannel.Literal($"idem:{key}:done");

        await sub.SubscribeAsync(channel, (_, _) =>
        {
            tcs.TrySetResult();
        });

        await Task.WhenAny(tcs.Task, Task.Delay(timeout));

        await sub.UnsubscribeAsync(channel);
    }
    public async Task<bool> TryAcquireLockAsync(string key, TimeSpan ttl)
    {
        return await _db.StringSetAsync(
            $"{key}:lock",
            "1",
            ttl,
            When.NotExists
        ).ConfigureAwait(false);
    }

    public async Task ReleaseLockAsync(string key)
    {
        await _db.KeyDeleteAsync($"{key}:lock").ConfigureAwait(false);
    }
}