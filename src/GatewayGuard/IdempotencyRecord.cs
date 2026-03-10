namespace GatewayGuard
{
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
    }
}