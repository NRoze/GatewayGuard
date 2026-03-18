using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace GatewayGuard.Benchmarks
{
    [ShortRunJob()]
    public class MiddlewareBenchmark
    {
        private HttpClient _client = null!;
        private readonly TestApiFactory _factory;
        private readonly string _idemKey = Guid.NewGuid().ToString();

        [Params("true", "false")]
        public string GuardEnabled { get; set; } = null!;

        [Params("true", "false")]
        public string FingerprintEnabled { get; set; } = null!;

        public MiddlewareBenchmark()
        {
            _factory = new TestApiFactory();
        }

        [GlobalSetup]
        public void Setup()
        {
            Environment.SetEnvironmentVariable("GatewayGuard__Enabled", GuardEnabled);

            _client = _factory.CreateClient();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable("GatewayGuard__Enabled", null);
        }

        [Benchmark]
        public async Task Echo()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/echo");
            req.Headers.Add("X-Idempotency-Key", _idemKey);
            await _client.SendAsync(req);
        }

        [Benchmark]
        public async Task Echo_WithContention_SameKey()
        {

            var tasks = Enumerable.Range(0, 10)
                .Select(_ =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, "/echo");
                    req.Headers.Add("X-Idempotency-Key", _idemKey);

                    return _client.SendAsync(req);
                });

            await Task.WhenAll(tasks);
        }
        static public void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<MiddlewareBenchmark>();
        }
    }
}