using System.Net;
using Kyntus.Characterization.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Conge.CharacterizationTests;

public class AuthorizationGoldenTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AuthorizationGoldenTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(b => b.UseEnvironment("Testing")).CreateClient();
    }

    [Fact]
    public async Task Conges_list_without_token_returns_401()
    {
        var employeId = Guid.Parse("11111111-1111-4111-8111-111111111103");
        var response = await _client.GetAsync($"/api/conges/employe/{employeId}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Conges_list_with_test_jwt_returns_200_and_array()
    {
        KyntusTestJwt.AuthorizeClient(_client);
        var employeId = Guid.Parse("11111111-1111-4111-8111-111111111103");
        var response = await _client.GetAsync($"/api/conges/employe/{employeId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await GoldenJson.ReadAsync(response);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task Conges_solde_without_snapshot_for_self_returns_200_and_inits()
    {
        var employeId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaa01");
        KyntusTestJwt.AuthorizeClient(
            _client,
            KyntusTestJwt.CreateBearerToken(
                email: "pilote@kyntus.ma",
                role: "Employee",
                subjectId: employeId));

        var response = await _client.GetAsync($"/api/conges/employe/{employeId}/solde");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await GoldenJson.ReadAsync(response);
        GoldenJson.AssertPropertyExists(json.RootElement, "employeId");
        GoldenJson.AssertPropertyExists(json.RootElement, "soldeInitial");
        GoldenJson.AssertPropertyExists(json.RootElement, "soldeRestant");
        Assert.True(json.RootElement.GetProperty("soldeInitial").GetDouble() > 0);
        Assert.True(json.RootElement.GetProperty("soldeRestant").GetDouble() > 0);
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
