namespace GatewayGuard.Models;

public record struct IdempotencyKeyRecord(string key, string fingerprint)
{
    public static IdempotencyKeyRecord Create(string key, string fingerprint)
        => new(key, fingerprint);
}