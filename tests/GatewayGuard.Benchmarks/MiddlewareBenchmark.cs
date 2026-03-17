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

//| Method | GuardEnabled | FingerprintEnabled | Mean | Error | StdDev |
//| ---------------------------- | ------------- | ------------------- | -------------:| -------------:| -------------:|
//| Echo                        | false | false | 86.35 us | 274.68 us | 15.056 us |
//| Echo_WithContention_SameKey | false | false | 329.11 us | 1,230.23 us | 67.433 us |
//| Echo                        | false | true | 81.66 us | 20.72 us | 1.136 us |
//| Echo_WithContention_SameKey | false | true | 295.47 us | 1,038.82 us | 56.941 us |
//| Echo                        | true | false | 3,580.99 us | 10,325.28 us | 565.964 us |
//| Echo_WithContention_SameKey | true | false | 10,676.23 us | 80,786.40 us | 4,428.175 us |
//| Echo                        | true | true | 3,225.76 us | 12,582.96 us | 689.714 us |
//| Echo_WithContention_SameKey | true | true | 4,724.31 us | 16,262.16 us | 891.384 us |