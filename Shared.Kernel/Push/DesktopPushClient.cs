using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Shared.Kernel.Push;

public sealed class DesktopPushClient(HttpClient http, ILogger<DesktopPushClient> logger) : IDesktopPushClient
{
    public async Task NotifyAsync(DesktopPushRequest request, CancellationToken ct = default)
    {
        if (request.AuthUserIds.Count == 0 && request.SubjectIds.Count == 0)
            return;

        try
        {
            using var response = await http.PostAsJsonAsync("api/Auth/push/notify", new
            {
                authUserIds = request.AuthUserIds,
                subjectIds = request.SubjectIds,
                title = request.Title,
                body = request.Body,
                tag = request.Tag,
                url = request.Url,
            }, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Desktop push notify failed: {Status} {Reason}",
                    (int)response.StatusCode,
                    response.ReasonPhrase);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Desktop push notify skipped (Auth unreachable).");
        }
    }
}
