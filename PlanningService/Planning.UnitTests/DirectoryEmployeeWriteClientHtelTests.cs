using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

public class DirectoryEmployeeWriteClientHtelTests
{
    [Fact]
    public async Task Selected_HTEL_identity_is_sent_in_the_initial_Directory_creation()
    {
        var handler = new Handler(HttpStatusCode.Created);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client = new DirectoryEmployeeWriteClient(new Factory(handler), new HttpContextAccessor(), db,
            new ConfigurationBuilder().Build(), NullLogger<DirectoryEmployeeWriteClient>.Instance);
        var result = await client.TryCreateEmployeeAsync("Fatima Zahra", "AARAB", "rh-test@example.test", "Employe", null,
            new DateTime(2026, 9, 15), idTechnicien: 4551);
        Assert.True(result.Success);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal(4551, payload.RootElement.GetProperty("idTechnicien").GetInt32());
        Assert.Equal("Fatima Zahra", payload.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("AARAB", payload.RootElement.GetProperty("lastName").GetString());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Duplicate_HTEL_identity_is_returned_without_retrying_creation()
    {
        var handler = new Handler(HttpStatusCode.Conflict);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client = new DirectoryEmployeeWriteClient(new Factory(handler), new HttpContextAccessor(), db,
            new ConfigurationBuilder().Build(), NullLogger<DirectoryEmployeeWriteClient>.Instance);
        var result = await client.TryCreateEmployeeAsync("Fatima Zahra", "AARAB", "rh-test@example.test", "Employe", null,
            new DateTime(2026, 9, 15), idTechnicien: 4551);
        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains("déjà lié", result.ErrorMessage);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.Created
                    ? JsonSerializer.Serialize(new { id = Guid.NewGuid() })
                    : JsonSerializer.Serialize(new { error = "Technicien HTEL déjà lié à une fiche désactivée." }),
                    System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
