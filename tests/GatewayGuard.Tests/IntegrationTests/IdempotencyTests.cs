
using FluentAssertions;
using SampleApi;
using System.Net;
using System.Net.Http.Json;

namespace GatewayGuard.Tests.IntegrationTests;


public class IdempotencyTests : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client;

    public IdempotencyTests(TestApiFactory factory)
    {
        _client = factory.CreateClient();
        TestState.ExecutionCount = 0;
    }

    [Fact]
    public async Task First_Request_Should_Succeed()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req.Headers.Add("X-Idempotency-Key", "abc123");

        var response = await _client.SendAsync(req);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Duplicate_Request_Should_Return_Same_Response()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req1.Headers.Add("X-Idempotency-Key", "dup-key");

        var res1 = await _client.SendAsync(req1);
        var body1 = await res1.Content.ReadAsStringAsync();

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req2.Headers.Add("X-Idempotency-Key", "dup-key");

        var res2 = await _client.SendAsync(req2);
        var body2 = await res2.Content.ReadAsStringAsync();

        body2.Should().Be(body1);
    }

    [Fact]
    public async Task Same_Key_With_Different_Payload_Should_Return_409()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req1.Headers.Add("X-Idempotency-Key", "conflict");
        req1.Content = JsonContent.Create(new { amount = 100 });

        await _client.SendAsync(req1);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/orders");
        req2.Headers.Add("X-Idempotency-Key", "conflict");
        req2.Content = JsonContent.Create(new { amount = 200 });

        var res = await _client.SendAsync(req2);

        res.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_Requests_With_Same_Key_Should_Execute_Only_Once()
    {
        const int requestCount = 1000;
        int rand = Random.Shared.Next(1, 1000);
        string key = $"concurrency-test-{rand}";

        var tasks = Enumerable.Range(0, requestCount)
            .Select(_ =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "/orders");
                req.Headers.Add("X-Idempotency-Key", key);
                req.Content = JsonContent.Create(new { amount = rand });

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
}
