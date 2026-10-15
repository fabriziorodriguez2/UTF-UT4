using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniGestor.Resilience;
using TaskManager.Middleware;
using Xunit;

namespace TaskManager.Tests;

public class CircuitBreakerTests
{
    private static ServiceProvider Provider(Stub users, Stub? projects = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CircuitBreaker:BreakSeconds"] = "1",
            ["CircuitBreaker:TimeoutSeconds"] = "1",
            ["CircuitBreaker:BreakSeconds"] = "1",
            ["CircuitBreaker:TimeoutSeconds"] = "1",
            ["Retry:MaxRetryAttempts"] = "0"
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddHttpClient("users", c => c.BaseAddress = new Uri("http://users"))
            .ConfigurePrimaryHttpMessageHandler(() => users)
            .AddDependencyResilience(config, "UserManager");
        services.AddHttpClient("projects", c => c.BaseAddress = new Uri("http://projects"))
            .ConfigurePrimaryHttpMessageHandler(() => projects ?? new Stub())
            .AddDependencyResilience(config, "ProjectManager");
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task OpensAcrossClientInstancesAndDoesNotBlockOtherDependency()
    {
        var users = new Stub { Status = HttpStatusCode.ServiceUnavailable };
        var projects = new Stub();
        using var provider = Provider(users, projects);
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        for (var i = 0; i < 3; i++)
        {
            using var client = factory.CreateClient("users");
            var error = await Assert.ThrowsAsync<DependencyUnavailableException>(() => client.GetAsync("/"));
            Assert.Equal("dependency_unavailable", error.Code);
        }
        using var next = factory.CreateClient("users");
        var blocked = await Assert.ThrowsAsync<DependencyUnavailableException>(() => next.GetAsync("/"));
        Assert.Equal("circuit_open", blocked.Code);
        Assert.Equal(3, users.Calls);
        using var other = factory.CreateClient("projects");
        using var response = await other.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, projects.Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(429)]
    public async Task ClientErrorsDoNotOpenCircuit(int status)
    {
        var stub = new Stub { Status = (HttpStatusCode)status };
        using var provider = Provider(stub);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("users");
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync("/");
            Assert.Equal(status, (int)response.StatusCode);
        }
        Assert.Equal(5, stub.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionErrorsAndTimeoutsOpenCircuit(bool timeout)
    {
        var stub = new Stub { Hang = timeout, NetworkFailure = !timeout };
        using var provider = Provider(stub);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("users");
        for (var i = 0; i < 3; i++)
            Assert.Equal("dependency_unavailable",
                (await Assert.ThrowsAsync<DependencyUnavailableException>(() => client.GetAsync("/"))).Code);
        Assert.Equal("circuit_open",
            (await Assert.ThrowsAsync<DependencyUnavailableException>(() => client.GetAsync("/"))).Code);
        Assert.Equal(3, stub.Calls);
    }

    [Fact]
    public async Task HalfOpenFailureReopensAndLaterSuccessCloses()
    {
        var stub = new Stub { NetworkFailure = true };
        using var provider = Provider(stub);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("users");
        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<DependencyUnavailableException>(() => client.GetAsync("/"));
        await Task.Delay(1200);
        Assert.Equal("dependency_unavailable",
            (await Assert.ThrowsAsync<DependencyUnavailableException>(() => client.GetAsync("/"))).Code);
        Assert.Equal("circuit_open",
            (await Assert.ThrowsAsync<DependencyUnavailableException>(() => client.GetAsync("/"))).Code);
        Assert.Equal(4, stub.Calls);
        stub.NetworkFailure = false;
        await Task.Delay(1200);
        using var probe = await client.GetAsync("/");
        using var normal = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        Assert.Equal(6, stub.Calls);
    }

    [Fact]
    public async Task CallerCancellationDoesNotOpenCircuit()
    {
        var stub = new Stub { Hang = true };
        using var provider = Provider(stub);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("users");
        for (var i = 0; i < 4; i++)
        {
            using var cancellation = new CancellationTokenSource(50);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("/", cancellation.Token));
        }
        stub.Hang = false;
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, stub.Calls);
    }

    [Theory]
    [InlineData(false, "dependency_unavailable")]
    [InlineData(true, "circuit_open")]
    public async Task MiddlewareReturns503WithSpecificCode(bool open, string code)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new DependencyUnavailableException("UserManager", open),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.Invoke(context);
        Assert.Equal(503, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Contains(code, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private sealed class Stub : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool NetworkFailure { get; set; }
        public bool Hang { get; set; }
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (Hang) await Task.Delay(Timeout.Infinite, token);
            if (NetworkFailure) throw new HttpRequestException("Connection refused");
            return new HttpResponseMessage(Status) { Content = new StringContent("true") };
        }
    }
}
