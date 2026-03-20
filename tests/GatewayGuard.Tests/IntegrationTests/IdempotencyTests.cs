
using FluentAssertions;
using GatewayGuard.Tests.Providers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using SampleApi;
using StackExchange.Redis;
using System.Net;
using System.Net.Http.Json;
using Xunit.Abstractions;

namespace GatewayGuard.Tests.IntegrationTests;


public class IdempotencyTests : IClassFixture<TestApiFactory>
{
    private readonly ITestOutputHelper _output;
    private readonly HttpClient _client;
    private int randomId;
    private string randomKey;

    public IdempotencyTests(TestApiFactory factory, ITestOutputHelper output)
    {
        _client = factory.CreateClient();
        TestState.Reset();
        randomId = Random.Shared.Next(1, 1000);
        randomKey = $"key-{randomId}";
        _output = output;
    }

    [Fact]
    public async Task First_Request_Should_Succeed()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req.Headers.Add("X-Idempotency-Key", randomKey);
        req.Content = JsonContent.Create(new { amount = 123 });

        var response = await _client.SendAsync(req);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Duplicate_Request_Should_Return_Same_Response()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req1.Headers.Add("X-Idempotency-Key", randomKey);

        var res1 = await _client.SendAsync(req1);
        var body1 = await res1.Content.ReadAsStringAsync();

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req2.Headers.Add("X-Idempotency-Key", randomKey);

        var res2 = await _client.SendAsync(req2);
        var body2 = await res2.Content.ReadAsStringAsync();

        body2.Should().Be(body1);
    }

    [Fact]
    public async Task Same_Key_With_Different_Payload_Should_Return_409()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req1.Headers.Add("X-Idempotency-Key", randomKey);
        req1.Content = JsonContent.Create(new { amount = 100 });

        await _client.SendAsync(req1);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req2.Headers.Add("X-Idempotency-Key", randomKey);
        req2.Content = JsonContent.Create(new { amount = 200 });

        var res = await _client.SendAsync(req2);

        res.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_Requests_With_Same_Key_Should_Execute_Only_Once()
    {
        const int requestCount = 1000;

        var tasks = Enumerable.Range(0, requestCount)
            .Select(_ =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "/orders");
                req.Headers.Add("X-Idempotency-Key", randomKey);
                req.Content = JsonContent.Create(new { amount = 123 });

                return _client.SendAsync(req);
            });

        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        TestState.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Empty_Key_With_Same_Body_Should_Execute_Once()
    {
        var content = JsonContent.Create(new { amount = 123 });

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = content };
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = content };

        var tasks = new[]
        {
            _client.SendAsync(req1),
            _client.SendAsync(req2)
        };

        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        TestState.ExecutionCount.Should().Be(1);
    }


    [Fact]
    public async Task Empty_Key_With_Different_Body_Should_Return_Ok()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new { amount = 100 })
        };

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new { amount = 200 })
        };

        // Send the first request
        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Send the second request
        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cached_Response_Should_Preserve_Status_Headers_And_Body()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req1.Headers.Add("X-Idempotency-Key", randomKey);

        var res1 = await _client.SendAsync(req1);
        var body1 = await res1.Content.ReadAsStringAsync();

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req2.Headers.Add("X-Idempotency-Key", randomKey);

        var res2 = await _client.SendAsync(req2);
        var body2 = await res2.Content.ReadAsStringAsync();

        res2.StatusCode.Should().Be(res1.StatusCode);
        body2.Should().Be(body1);
    }

    [Fact]
    public async Task Same_Key_Different_Method_Should_Not_Collide()
    {
        var post = new HttpRequestMessage(HttpMethod.Post, "/orders");
        post.Headers.Add("X-Idempotency-Key", randomKey);

        var put = new HttpRequestMessage(HttpMethod.Put, "/orders");
        put.Headers.Add("X-Idempotency-Key", randomKey);

        var r1 = await _client.SendAsync(post);
        var r2 = await _client.SendAsync(put);

        TestState.ExecutionCount.Should().Be(2);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Query_String_Should_Affect_Fingerprint()
    {
        var r1 = await _client.PostAsync("/orders?id=1", null);
        var r2 = await _client.PostAsync("/orders?id=2", null);

        TestState.ExecutionCount.Should().Be(2);
    }

    [Fact]
    public async Task Request_Body_Should_Not_Be_Consumed_By_Middleware()
    {
        var content = JsonContent.Create(new { amount = 55 });

        var res = await _client.PostAsync("/echo", content);

        var body = await res.Content.ReadAsStringAsync();

        body.Should().Contain("55");
    }

    [Fact]
    public async Task Header_Name_Should_Be_Case_Insensitive()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req1.Headers.Add("x-idempotency-key", randomKey);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req2.Headers.Add("X-IDEMPOTENCY-KEY", randomKey);

        await _client.SendAsync(req1);
        await _client.SendAsync(req2);

        TestState.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Large_Body_Should_Be_Handled()
    {
        // key?
        var payload = new string('A', 100000);

        var content = JsonContent.Create(new { data = payload });

        var r1 = await _client.PostAsync("/orders", content);
        var r2 = await _client.PostAsync("/orders", content);

        TestState.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Retry_Storm_Should_Only_Execute_Once()
    {
        var tasks = Enumerable.Range(0, 1000).Select(_ => SendRequest()).ToList();
        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        TestState.ExecutionCount.Should().Be(1);
    }

    private Task<HttpResponseMessage> SendRequest()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req.Headers.Add("X-Idempotency-Key", randomKey);

        return _client.SendAsync(req);
    }

    [Fact]
    public async Task Cached_Response_Should_Be_Identical_To_Original()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/large-response");
        req1.Headers.Add("X-Idempotency-Key", randomKey);

        var res1 = await _client.SendAsync(req1);
        var body1 = await res1.Content.ReadAsStringAsync();

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/large-response");
        req2.Headers.Add("X-Idempotency-Key", randomKey);

        var res2 = await _client.SendAsync(req2);
        var body2 = await res2.Content.ReadAsStringAsync();

        // status code
        res2.StatusCode.Should().Be(res1.StatusCode);

        // headers
        res2.Headers.GetValues("X-Test-Header")
            .Should().Contain("gateway-guard");

        // body
        body2.Should().Be(body1);
        body2.Length.Should().Be(body1.Length);

        // backend executed once
        TestState.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Failed_Request_Should_Not_Be_Cached()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/flaky");
        req1.Headers.Add("X-Idempotency-Key", randomKey);

        var res1 = await _client.SendAsync(req1);

        res1.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/flaky");
        req2.Headers.Add("X-Idempotency-Key", randomKey);

        var res2 = await _client.SendAsync(req2);
        var body = await res2.Content.ReadAsStringAsync();

        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("Success");

        TestState.ExecutionCount.Should().Be(2);
    }

    [Fact]
    public async Task CircuitBreaker_FailOpen_Should_Bypass_Idempotency_And_Return_200()
    {
        // Arrange
        using var brokenFactory = new TestApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
                if (descriptor != null) services.Remove(descriptor);

                services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    // An invalid host to trigger immediate connection failures
                    var config = ConfigurationOptions.Parse("invalid-host:9999,abortConnect=false,connectTimeout=100");
                    return ConnectionMultiplexer.Connect(config);
                });
            });
        });

        var client = brokenFactory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req.Headers.Add("X-Idempotency-Key", "fail-open-test-key");
        req.Content = JsonContent.Create(new { amount = 100 });

        // Act
        // This hits the invalid redis, triggers Polly CircuitBreaker, and gracefully degrades
        var res = await client.SendAsync(req);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CircuitBreaker_FailClosed_Should_Return_503_ServiceUnavailable()
    {
        // Arrange
        using var brokenFactory = new TestApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var optionsDescriptor = services.SingleOrDefault(d => d.ImplementationInstance is IdempotencyOptions);
                if (optionsDescriptor != null && optionsDescriptor.ImplementationInstance is IdempotencyOptions options)
                {
                    options.FailClosedOnStoreError = true;
                }

                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
                if (descriptor != null) services.Remove(descriptor);

                services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    var config = ConfigurationOptions.Parse("invalid-host:9999,abortConnect=false,connectTimeout=100");
                    return ConnectionMultiplexer.Connect(config);
                });
            });
        });

        var client = brokenFactory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req.Headers.Add("X-Idempotency-Key", "fail-closed-test-key");
        req.Content = JsonContent.Create(new { amount = 100 });

        // Act
        var res = await client.SendAsync(req);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Be("Idempotency store is temporarily unavailable.");
    }

    [Fact]
    public async Task Performance_Cached_Response_Should_Be_Faster_Than_Initial_Request()
    {
        // Arrange
        var key = $"latency-test-{Guid.NewGuid()}";

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req1.Headers.Add("X-Idempotency-Key", key);
        req1.Content = JsonContent.Create(new { amount = 100 });

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req2.Headers.Add("X-Idempotency-Key", key);
        req2.Content = JsonContent.Create(new { amount = 100 });

        // Act - Initial Request (Cold Start)
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        var res1 = await _client.SendAsync(req1);
        sw1.Stop();

        // Act - Second Request (Cached Hit)
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var res2 = await _client.SendAsync(req2);
        sw2.Stop();

        // Assert
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);

        // The mock processing delay in /orders is 10-50ms.
        // The cached response should completely bypass this.
        // In a real-world scenario, sw2.ElapsedMilliseconds should be < 5ms.
        sw2.ElapsedMilliseconds.Should().BeLessThan(sw1.ElapsedMilliseconds);
        TestState.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_Requests_With_One_Cancelled_Should_Not_Fail_Others()
    {
        var key = $"cancel-test-{Guid.NewGuid()}";
        using var cancelSource = new CancellationTokenSource();

        var requestCreator = () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/orders");
            req.Headers.Add("X-Idempotency-Key", key);
            req.Content = JsonContent.Create(new { amount = 100 });
            return req;
        };

        var task1 = _client.SendAsync(requestCreator());
        var task2 = _client.SendAsync(requestCreator(), cancelSource.Token);
        var task3 = _client.SendAsync(requestCreator());

        cancelSource.CancelAfter(TimeSpan.FromMilliseconds(5));

        var ex = await Record.ExceptionAsync(() => task2);
        ex.Should().BeAssignableTo<OperationCanceledException>();

        var res1 = await task1;
        var res3 = await task3;

        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        res3.StatusCode.Should().Be(HttpStatusCode.OK);

        TestState.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task DistributedLock_ConcurrentRequestsOnDifferentServers_HandledCorrectly()
    {
        // Arrange: Spin up two separate server instances (simulating a horizontal scale out)
        await using var server1 = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                    logging.ClearProviders();
                    logging.AddProvider(new PrefixLoggerProvider("SERVER 1", _output));
                });
            });
        using var client1 = server1.CreateClient();
        await using var server2 = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                    logging.ClearProviders();
                    logging.AddProvider(new PrefixLoggerProvider("SERVER 2", _output));
                });
            });
        using var client2 = server2.CreateClient();

        var idempotencyKey = Guid.NewGuid().ToString();
        var requestBody1 = new StringContent("{\"data\": 123}");
        requestBody1.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var requestBody2 = new StringContent("{\"data\": 123}");
        requestBody2.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var task1 = client1.PostAsync("/orders", requestBody1);
        var task2 = client2.PostAsync("/orders", requestBody2);
        var responses = await Task.WhenAll(task1, task2);

        var comparison = await CompareResponsesContent(responses[0], responses[1]);
        responses[0].StatusCode.Should().Be(HttpStatusCode.OK);
        responses[1].StatusCode.Should().Be(HttpStatusCode.OK);
        comparison.Should().BeTrue();
    }

    [Fact]
    public async Task DistributedLock_ConcurrentRequestsOnDifferentServers_Conflict()
    {
        // Arrange: Spin up two separate server instances (simulating a horizontal scale out)
        await using var server1 = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                    logging.ClearProviders();
                    logging.AddProvider(new PrefixLoggerProvider("SERVER 1", _output));
                });
            });
        using var client1 = server1.CreateClient();
        await using var server2 = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                    logging.ClearProviders();
                    logging.AddProvider(new PrefixLoggerProvider("SERVER 2", _output));
                });
            });
        using var client2 = server2.CreateClient();

        var idempotencyKey = Guid.NewGuid().ToString();
        var requestBody1 = new StringContent("{\"data\": 123}");
        requestBody1.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var requestBody2 = new StringContent("{\"data\": 123456}");
        requestBody2.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var task1 = client1.PostAsync("/orders", requestBody1);
        var task2 = client2.PostAsync("/orders", requestBody2);
        var responses = await Task.WhenAll(task1, task2);

        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    private async Task<bool> CompareResponsesContent(HttpResponseMessage httpResponseMessage1, HttpResponseMessage httpResponseMessage2)
    {
        var body1 = await httpResponseMessage1.Content.ReadAsStringAsync();
        var body2 = await httpResponseMessage2.Content.ReadAsStringAsync();

        return string.Equals(body1, body2);
    }
}
