using System.Net.Http.Json;
using Kyntus.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Planning.Infrastructure.Services;

public interface IDirectoryHierarchyClient
{
    Task<Guid> ResolveSupervisorIdAsync(Guid employeeGuid, CancellationToken ct = default);
}

public sealed class DirectoryHierarchyClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ICacheService cache,
    ILogger<DirectoryHierarchyClient> logger) : IDirectoryHierarchyClient
{
    public Task<Guid> ResolveSupervisorIdAsync(Guid employeeGuid, CancellationToken ct = default)
    {
        if (employeeGuid == Guid.Empty) return Task.FromResult(Guid.Empty);

        return cache.GetOrCreateAsync(
            KyntusCacheKeys.PlanningDirectoryParent(employeeGuid),
            token => LookupAsync(employeeGuid, token),
            KyntusCacheTtl.Entity,
            ct);
    }

    private async Task<Guid> LookupAsync(Guid employeeGuid, CancellationToken ct)
    {
        try
        {
            var baseUrl = configuration["Directory:BaseUrl"]?.Trim()
                ?? "http://employee-directory-backend:8080/";
            if (!baseUrl.EndsWith('/')) baseUrl += "/";

            var client = httpClientFactory.CreateClient("DirectorySync");
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(5);

            var employee = await client.GetFromJsonAsync<DirectoryEmployeeJson>(
                $"api/directory/employees/{employeeGuid:D}", ct);

            if (employee?.ParentId is not null
                && Guid.TryParse(employee.ParentId, out var parentId)
                && parentId != Guid.Empty)
            {
                return parentId;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Directory hierarchy lookup failed for {Guid}", employeeGuid);
        }

        return Guid.Empty;
    }

    private sealed class DirectoryEmployeeJson
    {
        public string? ParentId { get; set; }
    }
}
