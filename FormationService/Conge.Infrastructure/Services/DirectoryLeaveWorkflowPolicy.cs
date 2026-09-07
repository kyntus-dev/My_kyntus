using System.Net.Http.Json;
using Conge.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Conge.Infrastructure.Services;

public sealed class DirectoryLeaveWorkflowPolicy(
    IHttpClientFactory httpFactory,
    ILogger<DirectoryLeaveWorkflowPolicy> logger) : ILeaveWorkflowPolicy
{
    public async Task<bool> IsRhValidationRequiredAsync(CancellationToken ct = default)
    {
        try
        {
            var http = httpFactory.CreateClient("directory-org");
            var dto = await http.GetFromJsonAsync<LeaveWorkflowResponse>("/api/iam/leave-workflow", ct);
            return dto?.RhValidationRequired ?? true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IAM leave-workflow indisponible — étape RH conservée.");
            return true;
        }
    }

    private sealed record LeaveWorkflowResponse(bool RhValidationRequired);
}
