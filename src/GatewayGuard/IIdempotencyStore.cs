using System.Threading.Tasks;
using GatewayGuard.Models;
using Microsoft.AspNetCore.Http;
using StackExchange.Redis;

namespace GatewayGuard;

/// <summary>
/// Defines storage operations required to persist and retrieve idempotency records.
/// Implementations provide the backing store for idempotency keys and cached responses.
/// </summary>
public interface IIdempotencyStore
{
    Task SaveResponse(string key, string requestHash, HttpResponse response, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a cached <see cref="IdempotencyRecord"/> for the given idempotency key.
    /// </summary>
    /// <param name="key">The idempotency key to retrieve.</param>
    /// <returns>The cached record if found; otherwise <c>null</c>.</returns>
    Task<IdempotencyRecord?> GetResponse(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Waits for a completion notification for the given idempotency key or until the specified timeout elapses.
    /// </summary>
    /// <param name="key">The idempotency key to wait for.</param>
    /// <param name="timeout">Maximum time to wait for the completion notification.</param>
    /// <returns>A task that completes when the notification is received or the timeout expires.</returns>
    Task WaitForCompletionAsync(string key, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to acquire an exclusive lock for the given idempotency key.
    /// Used to coordinate single-flight request execution and prevent duplicate processing.
    /// </summary>
    /// <param name="key">The idempotency key to lock.</param>
    /// <param name="ttl">The time-to-live for the lock. The lock will be automatically released after this duration.</param>
    /// <returns>A lock value (token) if the lock was acquired; otherwise <c>null</c> if the lock was already held.</returns>
    Task<string?> TryAcquireLockAsync(string key, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>
    /// Releases a lock previously acquired via <see cref="TryAcquireLockAsync"/>.
    /// </summary>
    /// <param name="key">The idempotency key whose lock should be released.</param>
    /// <param name="lockValue">The lock token returned from <see cref="TryAcquireLockAsync"/>.</param>
    /// <returns>A task that completes with the result of the lock release operation.</returns>
    Task<RedisResult> ReleaseLockAsync(string key, string lockValue, CancellationToken cancellationToken);
}