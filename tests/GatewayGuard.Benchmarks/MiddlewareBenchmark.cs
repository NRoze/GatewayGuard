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
//| Echo                         | false | false | 86.35 us | 274.68 us | 15.056 us |
//| Echo_WithContention_SameKey  | false | false | 329.11 us | 1,230.23 us | 67.433 us |
//| Echo                         | false | true | 81.66 us | 20.72 us | 1.136 us |
//| Echo_WithContention_SameKey  | false | true | 295.47 us | 1,038.82 us | 56.941 us |
//| Echo                         | true | false | 3,580.99 us | 10,325.28 us | 565.964 us |
//| Echo_WithContention_SameKey  | true | false | 10,676.23 us | 80,786.40 us | 4,428.175 us |
//| Echo                         | true | true | 3,225.76 us | 12,582.96 us | 689.714 us |
//| Echo_WithContention_SameKey  | true | true | 4,724.31 us | 16,262.16 us | 891.384 us |

//| Method | GuardEnabled | FingerprintEnabled | Mean | Error | StdDev |
//| ---------------------------- | ------------- | ------------------- | ------------:| -------------:| -------------:|
//| Echo                         | false | false | 71.98 us | 158.78 us | 8.703 us |
//| Echo_WithContention_SameKey  | false | false | 398.15 us | 983.26 us | 53.896 us |
//| Echo                         | false | true | 69.08 us | 345.31 us | 18.928 us |
//| Echo_WithContention_SameKey  | false | true | 374.02 us | 770.03 us | 42.208 us |
//| Echo                         | true | false | 2,471.25 us | 3,994.46 us | 218.950 us |
//| Echo_WithContention_SameKey  | true | false | 3,365.49 us | 4,041.14 us | 221.509 us |
//| Echo                         | true | true | 4,228.51 us | 29,752.17 us | 1,630.817 us |
//| Echo_WithContention_SameKey  | true | true | 2,923.79 us | 7,997.53 us | 438.372 us |

//| Method | GuardEnabled | FingerprintEnabled | Mean | Error | StdDev | Median |
//| ---------------------------- | ------------- | ------------------- | ------------:| -------------:| -------------:| ------------:|
//| Echo                         | false | false | 89.05 us | 108.55 us | 5.950 us | 91.10 us |
//| Echo_WithContention_SameKey  | false | false | 292.49 us | 368.81 us | 20.216 us | 297.72 us |
//| Echo                         | false | true | 153.89 us | 195.05 us | 10.691 us | 157.70 us |
//| Echo_WithContention_SameKey  | false | true | 290.74 us | 1,476.22 us | 80.917 us | 325.65 us |
//| Echo                         | true | false | 4,238.78 us | 23,046.55 us | 1,263.259 us | 3,681.33 us |
//| Echo_WithContention_SameKey  | true | false | 4,412.65 us | 29,691.95 us | 1,627.516 us | 3,521.88 us |
//| Echo                         | true | true | 3,406.54 us | 18,266.54 us | 1,001.250 us | 2,933.12 us |
//| Echo_WithContention_SameKey  | true | true | 3,927.62 us | 10,210.23 us | 559.657 us | 4,039.94 us |