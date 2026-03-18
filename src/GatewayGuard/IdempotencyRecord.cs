using GatewayGuard.Extensions;
using Microsoft.AspNetCore.Http;

namespace GatewayGuard;

/// <summary>
/// Represents a cached record of an idempotent HTTP request and its response.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>
    /// Gets or sets the hash representing the request payload for idempotency validation.
    /// </summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HTTP status code of the cached response.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the HTTP headers of the cached response.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Gets or sets the body of the cached response as a byte array.
    /// </summary>
    public byte[] Body { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Creates an <see cref="IdempotencyRecord"/> by capturing the provided <see cref="HttpResponse"/>.
    /// </summary>
    /// <param name="requestHash">A hash/fingerprint of the request used for collision detection.</param>
    /// <param name="response">The <see cref="HttpResponse"/> whose status, headers and body will be captured.</param>
    /// <returns>A task that completes with the constructed <see cref="IdempotencyRecord"/>.</returns>
    static public async Task<IdempotencyRecord> CreateAsync(string requestHash, HttpResponse response)
    {
        var headers = response.Headers.ToDictionary(
            h => h.Key,
            h => h.Value.ToString());

        return new IdempotencyRecord
        {
            RequestHash = requestHash,
            StatusCode = response.StatusCode,
            Headers = headers,
            Body = await response.Body.CopyAsync()
        };
    }
}