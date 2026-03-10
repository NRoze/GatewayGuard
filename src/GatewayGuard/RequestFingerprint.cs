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
        var body = await context.Request.Body.ReadAsync(Array.Empty<byte>(), 0, 0);
        //var body = new StreamReader(context.Request.Body).ReadToEnd();
        var raw = $"{context.Request.Method}:{context.Request.Path}:{body}";
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
    }
}