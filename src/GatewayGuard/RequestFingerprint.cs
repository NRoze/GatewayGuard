using GatewayGuard.Extensions;
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
    public static async Task<string> GenerateAsync(HttpContext context, ISet<string>? fingerprintedHeaders)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        TryAppendHeaders(hash, context.Request, fingerprintedHeaders);
        AppendRequest(hash, context.Request);
        await AppendBody(hash, context.Request.Body);

        return hash.ToHexString();
    }

    private static void TryAppendHeaders(IncrementalHash hash, HttpRequest request, ISet<string>? fingerprintedHeaders)
    {
        if (fingerprintedHeaders is not null)
        {
            foreach (var headerName in fingerprintedHeaders)
            {
                if (request.Headers.TryGetValue(headerName, out var values))
                {
                    var headerVal = $"{headerName}:{values}:";
                    hash.AppendDataUTF8(headerVal);
                }
            }
        }
    }

    private static void AppendRequest(IncrementalHash hash, HttpRequest request)
    {
        var requestLine = $"{request.Method}:{request.Path}{request.QueryString}:";

        hash.AppendDataUTF8(requestLine);
    }

    private static async Task AppendBody(IncrementalHash hash, Stream stream)
    {
        int bytesRead;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            stream.SeekBegin();
            while ((bytesRead = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
            }

            stream.SeekBegin();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}