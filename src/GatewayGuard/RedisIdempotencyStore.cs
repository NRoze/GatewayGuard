using Microsoft.AspNetCore.Http;
using StackExchange.Redis;
using System.Text.Json;

namespace GatewayGuard;

/// <summary>
/// Redis-backed implementation of <see cref="IIdempotencyStore"/>.
/// Stores serialized <see cref="IdempotencyRecord"/> instances keyed by idempotency keys.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private const string UnlockScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1]
        then
            return redis.call('DEL', KEYS[1])
        else
            return 0
        end
        """;
    static private RedisKey LockKey(string key) => string.Concat("GatewayGuard:", key, ":lock");
    static private RedisKey ResponseKey(string key) => string.Concat("GatewayGuard:", key, ":response");
    static private RedisChannel CompletedChannelKey(string key) => RedisChannel.Literal($"GatewayGuard:{key}:done");

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _db;
    private readonly IdempotencyOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="RedisIdempotencyStore"/>.
    /// </summary>
    /// <param name="options">Configuration options used to connect to Redis and control expiration.</param>
    public RedisIdempotencyStore(
        IdempotencyOptions options, 
        IConnectionMultiplexer connectionMultiplexer)
    {
        _options = options;
        _multiplexer = connectionMultiplexer;
        _db = connectionMultiplexer.GetDatabase();
    }
    /// <summary>
    /// Retrieves a cached response for the given idempotency key from Redis.
    /// </summary>
    /// <param name="key">The idempotency key to retrieve the cached response for.</param>
    /// <returns>The cached record if found; otherwise <c>null</c>.</returns>
    public async Task<IdempotencyRecord?> GetResponse(string key)
    {
        var value = await _db.StringGetAsync(ResponseKey(key));

        if (value.IsNullOrEmpty)
        {
            if (_options.EnableMetrics)
            {
                GatewayGuardMetrics.CacheMisses.Add(1);
            }

            return null;
        }

        if (_options.EnableMetrics)
        {
            GatewayGuardMetrics.CacheHits.Add(1); 
        }

        return JsonSerializer.Deserialize<IdempotencyRecord>(((byte[])value!).AsSpan());
    }

    /// <summary>
    /// Stores the provided response and request hash under the given idempotency key.
    /// </summary>
    /// <param name="key">The idempotency key to store.</param>
    /// <param name="requestHash">A hash/fingerprint of the request used for collision detection.</param>
    /// <param name="response">The HTTP response to capture.</param>
    public async Task SaveResponse(string key, string requestHash, HttpResponse response)
    {
        if (string.IsNullOrEmpty(key)) return;

        var record = await IdempotencyRecord.CreateAsync(requestHash, response);

        await _db.StringSetAsync(
            ResponseKey(key),
            JsonSerializer.SerializeToUtf8Bytes(record),
            _options.IdempotencyKeyExpiration).ConfigureAwait(false);

        await PublishCompletedAsync(key).ConfigureAwait(false);
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
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = CompletedChannelKey(key);
        var handler = new Action<RedisChannel, RedisValue>((_, _) =>
        {
            tcs.TrySetResult();
        });

        await sub.SubscribeAsync(channel, handler).ConfigureAwait(false);
        try
        {
            if (await _db.KeyExistsAsync(ResponseKey(key)).ConfigureAwait(false))
            {
                return;
            }

            await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
        }
        finally
        {
            await sub.UnsubscribeAsync(channel, handler).ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Attempts to acquire an exclusive lock for the given idempotency key using Redis SET NX (set if not exists).
    /// </summary>
    /// <param name="key">The idempotency key to lock.</param>
    /// <param name="ttl">The time-to-live for the lock.</param>
    /// <returns>A unique lock value if the lock was acquired; otherwise <c>null</c>.</returns>
    public async Task<string?> TryAcquireLockAsync(string key, TimeSpan ttl)
    {
        var lockValue = Guid.NewGuid().ToString();
        var acquired = await _db.StringSetAsync(
            LockKey(key),
            lockValue,
            ttl,
            When.NotExists
        ).ConfigureAwait(false);

        if (_options.EnableMetrics)
        {
            GatewayGuardMetrics.LockAttempts.Add(1);
            if (acquired) GatewayGuardMetrics.LockAcquired.Add(1);
        }

        return acquired ? lockValue : null;
    }

    /// <summary>
    /// Releases a lock acquired via <see cref="TryAcquireLockAsync"/> using a Lua script for atomic operation.
    /// The lock is only released if the provided lock value matches the stored value.
    /// </summary>
    /// <param name="key">The idempotency key whose lock should be released.</param>
    /// <param name="lockValue">The lock token returned from <see cref="TryAcquireLockAsync"/>.</param>
    /// <returns>A task that completes with the result of the Lua script execution.</returns>
    public Task<RedisResult> ReleaseLockAsync(string key, string lockValue)
    {
        return _db.ScriptEvaluateAsync(
            UnlockScript,
            [LockKey(key)],
            [lockValue]);
    }
    private async Task PublishCompletedAsync(string key)
    {
        var sub = _multiplexer.GetSubscriber();
        var channel = CompletedChannelKey(key);

        await sub.PublishAsync(channel, "done").ConfigureAwait(false);
    }
}