namespace GatewayGuard
{
    public class IdempotencyRecord
    {
        public string RequestHash { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new();
        public byte[] Body { get; set; } = Array.Empty<byte>();
    }
}