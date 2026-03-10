using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

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
    Task<IdempotencyRecord?> GetAsync(string key);

    /// <summary>
    /// Stores the response for a completed request together with the request hash under the provided key.
    /// </summary>
    /// <param name="key">The idempotency key to store the response under.</param>
    /// <param name="requestHash">A fingerprint or hash representing the request payload.</param>
    /// <param name="response">The <see cref="HttpResponse"/> to capture and persist.</param>
    /// <returns>A task that completes when the record has been stored.</returns>
    Task SetAsync(string key, string requestHash, HttpResponse response);
}