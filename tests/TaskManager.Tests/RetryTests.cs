using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniGestor.Resilience;
using Xunit;

namespace TaskManager.Tests;

public class RetryTests
{
    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent("true") };
    private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("x") };

    [Fact]
    public async Task RecoversWhenServerErrorsAreTransient()
    {
        using var h = new Harness((n, _) => Task.FromResult(n <= 2 ? Status(HttpStatusCode.ServiceUnavailable) : Ok()));

        using var response = await h.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, h.Handler.Calls);
    }

    [Fact]
    public async Task RecoversWhenConnectionFailsTransiently()
    {
        using var h = new Harness((n, _) => n == 1
            ? throw new HttpRequestException("Connection refused")
            : Task.FromResult(Ok()));

        using var response = await h.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, h.Handler.Calls);
    }

    [Fact]
    public async Task SlowAttemptTimesOutAndIsRetried()
    {
        using var h = new Harness(async (n, token) =>
        {
            if (n == 1) await Task.Delay(Timeout.Infinite, token);
            return Ok();
        });

        using var response = await h.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, h.Handler.Calls);
    }

    [Fact]
    public async Task GivesUpAfterMaxAttemptsAndReportsDependencyUnavailable()
    {
        using var h = new Harness((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)));

        var error = await Assert.ThrowsAsync<DependencyUnavailableException>(() => h.Client.GetAsync("/"));

        Assert.Equal("dependency_unavailable", error.Code);
        Assert.Equal(3, h.Handler.Calls); // 1 intento inicial + 2 reintentos
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task ClientErrorsAreNotRetried(int status)
    {
        using var h = new Harness((_, _) => Task.FromResult(Status((HttpStatusCode)status)));

        using var response = await h.Client.GetAsync("/");

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(1, h.Handler.Calls);
    }


    [Fact]
    public async Task RetriesAreDisabledWhenMaxRetryAttemptsIsZero()
    {
        using var h = new Harness((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)), maxRetries: 0);

        await Assert.ThrowsAsync<DependencyUnavailableException>(() => h.Client.GetAsync("/"));

        Assert.Equal(1, h.Handler.Calls);
    }

    [Fact]
    public async Task OpenCircuitRejectsWithoutAnyAttemptOrRetry()
    {
        using var h = new Harness((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)),
            minThroughput: 2);

        for (var i = 0; i < 2; i++)
            Assert.Equal("dependency_unavailable",
                (await Assert.ThrowsAsync<DependencyUnavailableException>(() => h.Client.GetAsync("/"))).Code);
        Assert.Equal(6, h.Handler.Calls);

        var blocked = await Assert.ThrowsAsync<DependencyUnavailableException>(() => h.Client.GetAsync("/"));

        Assert.Equal("circuit_open", blocked.Code);
        Assert.Equal(6, h.Handler.Calls); // ni un intento nuevo, tampoco reintentos
    }

    [Fact]
    public async Task CallerCancellationIsNotRetried()
    {
        using var h = new Harness(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Ok();
        });
        using var cancellation = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.Client.GetAsync("/", cancellation.Token));

        Assert.Equal(1, h.Handler.Calls);
    }

    /// <summary>Cliente "users" con la pipeline real (breaker + retry) sobre un handler guionado.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;
        public Scripted Handler { get; }
        public HttpClient Client { get; }

        public Harness(Func<int, CancellationToken, Task<HttpResponseMessage>> respond,
            int maxRetries = 2, int minThroughput = 3)
        {
            Handler = new Scripted(respond);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retry:MaxRetryAttempts"] = maxRetries.ToString(),
                ["Retry:DelayMilliseconds"] = "1",
                ["Retry:TotalTimeoutSeconds"] = "10",
                ["CircuitBreaker:TimeoutSeconds"] = "1",
                ["CircuitBreaker:BreakSeconds"] = "30",
                ["CircuitBreaker:MinimumThroughput"] = minThroughput.ToString()
            }).Build();
            var services = new ServiceCollection().AddLogging();
            services.AddHttpClient("users", c => c.BaseAddress = new Uri("http://users"))
                .ConfigurePrimaryHttpMessageHandler(() => Handler)
                .AddDependencyResilience(config, "UserManager");
            _provider = services.BuildServiceProvider();
            Client = _provider.GetRequiredService<IHttpClientFactory>().CreateClient("users");
        }

        public void Dispose()
        {
            Client.Dispose();
            _provider.Dispose();
        }
    }

    private sealed class Scripted(Func<int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => await respond(Interlocked.Increment(ref Calls), token);
    }
}