using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;

namespace GatewayGuard;

internal static class RequestFingerprint
{
    public static async Task<string> GenerateAsync(HttpContext context)
    {
        var request = context.Request;
        byte[] bodyBytes = await request.Body.CopyAsync();
        var raw = $"{request.Method}:{request.Path}{request.QueryString}:{Convert.ToBase64String(bodyBytes)}"; 

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    }
}