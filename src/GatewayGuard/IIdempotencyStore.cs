using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using StackExchange.Redis;

namespace GatewayGuard;

/// <summary>
/// Defines storage operations required to persist and retrieve idempotency records.
/// Implementations provide the backing store for idempotency keys and cached responses.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Retrieves an <see cref="IdempotencyRecord"/> for the given idempotency key if present.
    /// </summary>
    /// <param name="key">The idempotency key to look up.</param>
    /// <returns>
    /// The matching <see cref="IdempotencyRecord"/> if found; otherwise <c>null</c>.
    /// </returns>
    //Task<IdempotencyRecord?> GetAsync(string key);

    /// <summary>
    /// Stores the response for a completed request together with the request hash under the provided key.
    /// </summary>
    /// <param name="key">The idempotency key to store the response under.</param>
    /// <param name="requestHash">A fingerprint or hash representing the request payload.</param>
    /// <param name="response">The <see cref="HttpResponse"/> to capture and persist.</param>
    /// <returns>A task that completes when the record has been stored.</returns>
    //Task SetAsync(string key, string requestHash, HttpResponse response);
    Task SaveResponse(string key, string requestHash, HttpResponse response);

    /// <summary>
    /// Retrieves a cached <see cref="IdempotencyRecord"/> for the given idempotency key.
    /// </summary>
    /// <param name="key">The idempotency key to retrieve.</param>
    /// <returns>The cached record if found; otherwise <c>null</c>.</returns>
    Task<IdempotencyRecord?> GetResponse(string key);

    /// <summary>
    /// Waits for a completion notification for the given idempotency key or until the specified timeout elapses.
    /// </summary>
    /// <param name="key">The idempotency key to wait for.</param>
    /// <param name="timeout">Maximum time to wait for the completion notification.</param>
    /// <returns>A task that completes when the notification is received or the timeout expires.</returns>
    Task WaitForCompletionAsync(string key, TimeSpan timeout);

    /// <summary>
    /// Attempts to acquire an exclusive lock for the given idempotency key.
    /// Used to coordinate single-flight request execution and prevent duplicate processing.
    /// </summary>
    /// <param name="key">The idempotency key to lock.</param>
    /// <param name="ttl">The time-to-live for the lock. The lock will be automatically released after this duration.</param>
    /// <returns>A lock value (token) if the lock was acquired; otherwise <c>null</c> if the lock was already held.</returns>
    Task<string?> TryAcquireLockAsync(string key, TimeSpan ttl);

    /// <summary>
    /// Releases a lock previously acquired via <see cref="TryAcquireLockAsync"/>.
    /// </summary>
    /// <param name="key">The idempotency key whose lock should be released.</param>
    /// <param name="lockValue">The lock token returned from <see cref="TryAcquireLockAsync"/>.</param>
    /// <returns>A task that completes with the result of the lock release operation.</returns>
    Task<RedisResult> ReleaseLockAsync(string key, string lockValue);
}