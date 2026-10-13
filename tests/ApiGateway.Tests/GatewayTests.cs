using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using ApiGateway.Auth;
using ApiGateway.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ApiGateway.Tests;

public class GatewayTests
{
    private const string Secret = "gateway-tests-secret-at-least-32-bytes";
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = Secret, ["Jwt:Issuer"] = "issuer", ["Jwt:Audience"] = "audience",
            ["Services:Users"] = "http://users:8080", ["Services:Projects"] = "http://projects:8080",
            ["Services:Tasks"] = "http://tasks:8080"
        }).Build();

    private static string Token(string secret = Secret, string issuer = "issuer", string audience = "audience", bool expired = false)
    {
        var token = new JwtSecurityToken(issuer, audience, expires: DateTime.UtcNow.AddMinutes(expired ? -1 : 5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256));
        return "Bearer " + new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public void ValidatesSignatureIssuerAudienceAndExpiration()
    {
        var validator = new JwtTokenValidator(Config());
        Assert.True(validator.IsValid(Token()));
        Assert.False(validator.IsValid(Token(secret: "different-secret-at-least-32-bytes-long")));
        Assert.False(validator.IsValid(Token(issuer: "other")));
        Assert.False(validator.IsValid(Token(audience: "other")));
        Assert.False(validator.IsValid(Token(expired: true)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer invalid")]
    [InlineData("Basic abc")]
    public void RejectsMissingOrMalformedCredentials(string? value) =>
        Assert.False(new JwtTokenValidator(Config()).IsValid(value));

    [Theory]
    [InlineData("/auth/login", "http://users:8080", true)]
    [InlineData("/AUTH/register/", "http://users:8080", true)]
    [InlineData("/auth/login/extra", "http://users:8080", false)]
    [InlineData("/users/123", "http://users:8080", false)]
    [InlineData("/projects", "http://projects:8080", false)]
    [InlineData("/tasks/123/status", "http://tasks:8080", false)]
    [InlineData("/tasks-other", null, false)]
    public void MatchesCompleteRouteSegments(string path, string? target, bool isPublic)
    {
        var routes = new RouteTable(Config());
        Assert.Equal(target, routes.ResolveTarget(path));
        Assert.Equal(isPublic, routes.IsPublic(path));
    }

    [Theory]
    [InlineData("/users", 401)]
    [InlineData("/auth/login/extra", 401)]
    [InlineData("/unknown", 404)]
    public async Task RejectsRequestsBeforeContactingBackend(string path, int status)
    {
        var context = Context(path);
        await Router(_ => throw new Exception("Backend should not be called")).Route(context);
        Assert.Equal(status, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/auth/login", false)]
    [InlineData("/tasks", true)]
    public async Task ForwardsMethodQueryBodyAndEndToEndHeaders(string path, bool authenticated)
    {
        var context = Context(path);
        context.Request.Method = "POST";
        context.Request.QueryString = new QueryString("?name=a%20b&name=c");
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"test\"}"));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Request.ContentType = "application/json";
        context.Request.Headers.Connection = "X-Internal";
        context.Request.Headers["X-Internal"] = "private";
        if (authenticated) context.Request.Headers.Authorization = Token();
        var router = Router(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(path, request.RequestUri!.AbsolutePath);
            Assert.Equal(authenticated ? "tasks" : "users", request.RequestUri.Host);
            Assert.Equal("?name=a%20b&name=c", request.RequestUri.Query);
            Assert.Equal("{\"name\":\"test\"}", await request.Content!.ReadAsStringAsync());
            Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
            Assert.Equal(authenticated, request.Headers.Authorization is not null);
            Assert.False(request.Headers.Contains("Connection"));
            Assert.False(request.Headers.Contains("X-Internal"));
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("created") };
            response.Headers.Add("X-Served-By", "replica-1");
            response.Headers.Connection.Add("X-Backend-Internal");
            response.Headers.Add("X-Backend-Internal", "private");
            response.Headers.TryAddWithoutValidation("Set-Cookie", new[] { "a=1", "b=2" });
            return response;
        });
        await router.Route(context);
        Assert.Equal(201, context.Response.StatusCode);
        Assert.Equal("replica-1", context.Response.Headers["X-Served-By"].ToString());
        Assert.Equal(2, context.Response.Headers.SetCookie.Count);
        Assert.False(context.Response.Headers.ContainsKey("X-Backend-Internal"));
        context.Response.Body.Position = 0;
        Assert.Equal("created", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData(false, 502)]
    [InlineData(true, 504)]
    public async Task ConvertsBackendFailures(bool timeout, int status)
    {
        var context = Context("/auth/login");
        await Router(_ => throw (timeout ? new TaskCanceledException() : new HttpRequestException())).Route(context);
        Assert.Equal(status, context.Response.StatusCode);
    }

    private static DefaultHttpContext Context(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static GatewayRouter Router(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new JwtTokenValidator(Config()), new RouteTable(Config()), new ClientFactory(send));

    private sealed class ClientFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(send));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
