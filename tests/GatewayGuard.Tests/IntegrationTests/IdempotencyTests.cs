
using FluentAssertions;
using SampleApi;
using System.Net;
using System.Net.Http.Json;

namespace GatewayGuard.Tests.IntegrationTests;


public class IdempotencyTests : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client;
    private int randomId;
    private string randomKey;

    public IdempotencyTests(TestApiFactory factory)
    {
        _client = factory.CreateClient();
        TestState.ExecutionCount = 0;
        randomId = Random.Shared.Next(1, 1000);
        randomKey = $"key-{randomId}";
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
    public async Task Same_Key_Different_Method_Should_Not_Collide_Be_Blocked()
    {
        var post = new HttpRequestMessage(HttpMethod.Post, "/orders");
        post.Headers.Add("X-Idempotency-Key", randomKey);

        var put = new HttpRequestMessage(HttpMethod.Put, "/orders");
        put.Headers.Add("X-Idempotency-Key", randomKey);

        var r1 = await _client.SendAsync(post);
        var r2 = await _client.SendAsync(put);

        TestState.ExecutionCount.Should().Be(1);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);   
        r2.StatusCode.Should().Be(HttpStatusCode.Conflict);
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
        var r2 = await _client.PostAsync("/orders", JsonContent.Create(new { data = payload }));

        TestState.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Retry_Storm_Should_Only_Execute_Once()
    {
        const int bursts = 10;
        const int requestsPerBurst = 20;

        var tasks = new List<Task<HttpResponseMessage>>();

        for (int b = 0; b < bursts; b++)
        {
            for (int i = 0; i < requestsPerBurst; i++)
            {
                tasks.Add(SendRequest());
            }

            // simulate network jitter between retries
            await Task.Delay(Random.Shared.Next(5, 25));
        }

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
}
