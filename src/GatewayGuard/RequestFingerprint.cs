using GatewayGuard.Extentions;
using Microsoft.AspNetCore.Http;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace GatewayGuard;

internal static class RequestFingerprint
{
    private const int bufferSize = 8192; 
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
        int bytesRead;
        var request = context.Request;
        var stream = request.Body;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var requestLine = $"{request.Method}:{request.Path}{request.QueryString}:";

        hash.AppendData(Encoding.UTF8.GetBytes(requestLine));

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            stream.SeekBegin();
            while ((bytesRead = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
            }

            stream.SeekBegin();

            byte[] hashBytes = hash.GetHashAndReset();
            return Convert.ToHexString(hashBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}