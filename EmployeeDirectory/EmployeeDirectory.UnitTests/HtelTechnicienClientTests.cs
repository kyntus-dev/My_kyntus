using System.Net;
using EmployeeDirectory.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmployeeDirectory.UnitTests;

public class HtelTechnicienClientTests
{
    [Fact]
    public async Task Reads_all_statuses_and_the_four_HTEL_columns()
    {
        var client = Create("""[{"id_technicien":4551,"technicien":" AARAB Fatima Zahra ","actif":1,"code":"SUPPORT"},{"id_technicien":2,"technicien":"AUTRE Personne","actif":0,"code":""}]""");
        var rows = await client.GetTechniciensAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(4551, rows[0].IdTechnicien);
        Assert.Equal("AARAB Fatima Zahra", rows[0].Technicien);
        Assert.Equal("SUPPORT", rows[0].Code);
        Assert.Equal(0, rows[1].Actif);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("{\"data\":[]}")]
    [InlineData("[{\"id_technicien\":4551,\"technicien\":\"AARAB Fatima Zahra\"}]")]
    [InlineData("[{\"technicien\":\"AARAB Fatima Zahra\",\"actif\":1}]")]
    [InlineData("[{\"id_technicien\":0,\"technicien\":\"AARAB Fatima Zahra\",\"actif\":1}]")]
    public async Task Rejects_incomplete_or_invalid_responses_instead_of_silently_dropping_rows(string json)
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Create(json).GetTechniciensAsync());
    }

    [Fact]
    public async Task Http_failure_is_not_reported_as_an_empty_catalogue()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Create("[]", HttpStatusCode.ServiceUnavailable).GetTechniciensAsync());
    }

    private static HtelTechnicienClient Create(string json, HttpStatusCode status = HttpStatusCode.OK) => new(
        new Factory(json, status), new ConfigurationBuilder().Build(), NullLogger<HtelTechnicienClient>.Instance);
    private sealed class Factory(string json, HttpStatusCode status) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(json, status)) { BaseAddress = new Uri("https://htel.example.test/") };
    }
    private sealed class Handler(string json, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
