using System.Net;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Http;

namespace AuthService.CharacterizationTests;
public class SessionValidationTests
{
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
    [Theory]
    [InlineData(204, true)]
    [InlineData(200, false)]
    [InlineData(301, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(500, false)]
    public async Task Business_services_require_an_explicit_valid_session(int status, bool expected)
    {
        using var client = new HttpClient(new Handler((HttpStatusCode)status)) { BaseAddress = new Uri("http://auth.test/api/Auth/session/validate") };
        Assert.Equal(expected, await new KyntusSessionValidator(client).IsValidAsync("test-token", default));
    }
    [Fact]
    public void Query_tokens_are_accepted_only_for_hubs()
    {
        var context = new DefaultHttpContext(); context.Request.QueryString = new QueryString("?access_token=test-token");
        context.Request.Path = "/api/users";
        Assert.Null(KyntusSessionValidator.ReadToken(context));
        context.Request.Path = "/hubs/notifications";
        Assert.Equal("test-token", KyntusSessionValidator.ReadToken(context));
    }
}
