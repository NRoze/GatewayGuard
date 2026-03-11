using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;

namespace GatewayGuard;

internal static class RequestFingerprint
{
    /// <summary>
    /// Generates a deterministic fingerprint for the given HTTP request.
    /// The fingerprint includes the request method, path, query string and the request body.
    /// </summary>
    /// <param name="context">The current <see cref="HttpContext"/> whose request will be fingerprinted.</param>
    /// <returns>
    /// A hex-encoded SHA256 hash representing the request method, path, query and body.
    /// </returns>
    public static async Task<string> GenerateAsync(HttpContext context)
    {
        var request = context.Request;
        byte[] bodyBytes = await request.Body.CopyAsync();
        var raw = $"{request.Method}:{request.Path}{request.QueryString}:{Convert.ToBase64String(bodyBytes)}"; 

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}