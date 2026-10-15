using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace MiniGestor.Resilience;

public sealed class DependencyUnavailableException(string dependency, bool circuitOpen, Exception? inner = null)
    : Exception($"El servicio {dependency} no está disponible temporalmente.", inner)
{
    public string Code => circuitOpen ? "circuit_open" : "dependency_unavailable";
}

public static class DependencyResilience
{
    // Una pipeline por cliente y proceso; no se crea un circuito por request.
    public static IHttpClientBuilder AddDependencyResilience(
        this IHttpClientBuilder client, IConfiguration configuration, string dependency)
    {
        var section = configuration.GetSection("CircuitBreaker");
        client.ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
        client.AddHttpMessageHandler(() => new DependencyErrorHandler(dependency));
        client.AddResilienceHandler("dependency", (pipeline, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("CircuitBreaker");
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = section.GetValue("FailureRatio", 0.5),
                MinimumThroughput = section.GetValue("MinimumThroughput", 3),
                SamplingDuration = TimeSpan.FromSeconds(section.GetValue("SamplingSeconds", 10)),
                BreakDuration = TimeSpan.FromSeconds(section.GetValue("BreakSeconds", 15)),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(response => (int)response.StatusCode >= 500),
                OnOpened = _ =>
                {
                    logger.LogWarning("Circuit {Dependency}: Open", dependency);
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    logger.LogInformation("Circuit {Dependency}: HalfOpen", dependency);
                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("Circuit {Dependency}: Closed", dependency);
                    return default;
                }
            });
            // Dentro del breaker para que el timeout cuente como fallo. Sin retries.
            pipeline.AddTimeout(TimeSpan.FromSeconds(section.GetValue("TimeoutSeconds", 2)));
        });
        client.AddHttpMessageHandler(sp => new DependencyAttemptHandler(dependency,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("CircuitBreaker")));
        return client;
    }
}

internal sealed class DependencyErrorHandler(string dependency) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        try
        {
            var response = await base.SendAsync(request, token);
            if ((int)response.StatusCode < 500) return response;
            response.Dispose();
            throw new DependencyUnavailableException(dependency, false);
        }
        catch (BrokenCircuitException ex)
        {
            throw new DependencyUnavailableException(dependency, true, ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new DependencyUnavailableException(dependency, false, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new DependencyUnavailableException(dependency, false, ex);
        }
    }
}

internal sealed class DependencyAttemptHandler(string dependency, ILogger logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        // Este mensaje solo aparece si el breaker permite intentar la llamada.
        logger.LogInformation("Dependency HTTP attempt {Dependency}", dependency);
        return base.SendAsync(request, token);
    }
}
