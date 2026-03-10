using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace GatewayGuard;

public static class RequestFingerprint
{
    public static async Task<string> GenerateAsync(HttpContext context)
    {
        using var memStream = new MemoryStream();
        await context.Request.Body.CopyToAsync(memStream);
        var bodyBytes = memStream.ToArray();

        memStream.Seek(0, SeekOrigin.Begin);

        var raw = $"{context.Request.Method}:{context.Request.Path}{context.Request.QueryString}:{Convert.ToBase64String(bodyBytes)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}