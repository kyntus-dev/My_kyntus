using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services;

public sealed record DirectoryEmployeeCreateResult(
    Guid EmployeeId,
    bool Success,
    string? ErrorMessage = null,
    bool Retryable = true);

public sealed record DirectoryEmployeeBulkCreateItem(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? PrimeServiceId,
    DateTime HireDate);

public interface IDirectoryEmployeeWriteClient
{
    Task<DirectoryEmployeeCreateResult> TryCreateEmployeeAsync(
        string firstName,
        string lastName,
        string email,
        string role,
        string? primeServiceId,
        DateTime hireDate,
        CancellationToken ct = default,
        int? idTechnicien = null);

    Task<IReadOnlyList<DirectoryEmployeeCreateResult>> TryCreateEmployeesBulkAsync(
        IReadOnlyList<DirectoryEmployeeBulkCreateItem> items,
        CancellationToken ct = default);

    Task<bool> IsEmailUsedInDirectoryAsync(string email, Guid? excludeEmployeeId = null, CancellationToken ct = default);

    Task<bool> TryUpdateEmployeeAsync(User user, CancellationToken ct = default);
    Task<bool> TryLinkAuthSubjectAsync(Guid employeeId, Guid authSubjectId, CancellationToken ct = default);
    Task<bool> TryDeleteEmployeeAsync(Guid employeeGuid, CancellationToken ct = default);
}

public sealed class DirectoryEmployeeWriteClient(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    AppDbContext context,
    IConfiguration configuration,
    ILogger<DirectoryEmployeeWriteClient> logger) : IDirectoryEmployeeWriteClient
{
    private const int MaxRetries = 3;

    public Task<DirectoryEmployeeCreateResult> TryCreateEmployeeAsync(
        string firstName,
        string lastName,
        string email,
        string role,
        string? primeServiceId,
        DateTime hireDate,
        CancellationToken ct = default,
        int? idTechnicien = null) =>
        ExecuteWithRetriesAsync<DirectoryEmployeeCreateResult>(
            async () =>
            {
                var payload = new
                {
                    employeeId = (Guid?)null,
                    firstName,
                    lastName,
                    email,
                    role,
                    serviceId = primeServiceId,
                    parentId = (Guid?)null,
                    hireDate,
                    idTechnicien,
                };

                var client = CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/directory/employees")
                {
                    Content = JsonContent.Create(payload),
                };
                AttachAuth(request);

                var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    var errorMessage = TryParseDirectoryError(body);
                    var retryable = IsRetryableStatus(response.StatusCode);
                    logger.LogWarning(
                        "Directory create échec {Status} pour {Email}: {Body}",
                        response.StatusCode,
                        email,
                        string.IsNullOrWhiteSpace(errorMessage) ? body : errorMessage);
                    return new DirectoryEmployeeCreateResult(Guid.Empty, false, errorMessage, retryable);
                }

                var created = await response.Content.ReadFromJsonAsync<DirectoryEmployeeJson>(cancellationToken: ct);
                if (created is null || !Guid.TryParse(created.Id, out var employeeId))
                    return new DirectoryEmployeeCreateResult(Guid.Empty, false, "Réponse Directory invalide.", Retryable: false);

                logger.LogInformation("Directory create OK pour {Email} guid={Guid}", email, employeeId);
                return new DirectoryEmployeeCreateResult(employeeId, true);
            },
            $"create {email}",
            new DirectoryEmployeeCreateResult(Guid.Empty, false),
            ct);

    public async Task<IReadOnlyList<DirectoryEmployeeCreateResult>> TryCreateEmployeesBulkAsync(
        IReadOnlyList<DirectoryEmployeeBulkCreateItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return Array.Empty<DirectoryEmployeeCreateResult>();

        var bulkPayload = items.Select(item => new
        {
            employeeId = (Guid?)null,
            firstName = item.FirstName,
            lastName = item.LastName,
            email = item.Email,
            role = item.Role,
            serviceId = item.PrimeServiceId,
            parentId = (Guid?)null,
            hireDate = item.HireDate,
        }).ToList();

        try
        {
            // Bulk séquentiel côté Directory : 30s est trop court pour des lots denses.
            var client = CreateClient(timeout: TimeSpan.FromMinutes(5));
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/directory/employees/bulk")
            {
                Content = JsonContent.Create(new { items = bulkPayload }),
            };
            AttachAuth(request);

            var response = await client.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.LogInformation("Directory bulk endpoint absent (404), repli créations individuelles parallèles.");
                return await CreateEmployeesIndividuallyParallelAsync(items, ct);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var errorMessage = TryParseDirectoryError(body) ?? "Bulk create Directory échoué.";
                logger.LogWarning("Directory bulk échec {Status}: {Body}", response.StatusCode, body);
                return items.Select(item => new DirectoryEmployeeCreateResult(
                    Guid.Empty,
                    false,
                    errorMessage,
                    IsRetryableStatus(response.StatusCode))).ToList();
            }

            var bulkResults = await response.Content.ReadFromJsonAsync<List<DirectoryBulkCreateJson>>(cancellationToken: ct);
            if (bulkResults is null || bulkResults.Count != items.Count)
            {
                logger.LogWarning("Directory bulk réponse invalide (count={Count}, attendu={Expected}).",
                    bulkResults?.Count ?? 0, items.Count);
                return await CreateEmployeesIndividuallyParallelAsync(items, ct);
            }

            return bulkResults.Select(r =>
            {
                var employeeId = r.EmployeeId.HasValue && r.EmployeeId.Value != Guid.Empty
                    ? r.EmployeeId.Value
                    : Guid.Empty;
                return new DirectoryEmployeeCreateResult(
                    employeeId,
                    r.Success,
                    r.Error,
                    Retryable: !r.Success && string.IsNullOrWhiteSpace(r.Error));
            }).ToList();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout HttpClient : ne pas rejouer le lot en individuel (cascade « Email déjà utilisé »
            // si Directory a déjà créé une partie des employés côté serveur).
            logger.LogWarning(ex, "Directory bulk timeout ({Count} item(s)), pas de repli individuel.", items.Count);
            const string timeoutMessage = "Timeout création Directory (lot). Relancez l'import.";
            return items.Select(_ => new DirectoryEmployeeCreateResult(
                Guid.Empty, false, timeoutMessage, Retryable: false)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Directory bulk exception, repli créations individuelles parallèles.");
            return await CreateEmployeesIndividuallyParallelAsync(items, ct);
        }
    }

    private async Task<IReadOnlyList<DirectoryEmployeeCreateResult>> CreateEmployeesIndividuallyParallelAsync(
        IReadOnlyList<DirectoryEmployeeBulkCreateItem> items,
        CancellationToken ct)
    {
        const int maxParallelism = 6;
        using var gate = new SemaphoreSlim(maxParallelism, maxParallelism);
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await TryCreateEmployeeAsync(
                    item.FirstName,
                    item.LastName,
                    item.Email,
                    item.Role,
                    item.PrimeServiceId,
                    item.HireDate,
                    ct);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    public Task<bool> IsEmailUsedInDirectoryAsync(string email, Guid? excludeEmployeeId = null, CancellationToken ct = default) =>
        ExecuteWithRetriesAsync(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(email)) return false;

                var client = CreateClient();
                var query = $"api/directory/employees/check-email?email={Uri.EscapeDataString(email.Trim())}";
                if (excludeEmployeeId.HasValue && excludeEmployeeId.Value != Guid.Empty)
                    query += $"&excludeId={excludeEmployeeId.Value:D}";

                using var request = new HttpRequestMessage(HttpMethod.Get, query);
                AttachAuth(request);

                var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    return false;

                var payload = await response.Content.ReadFromJsonAsync<DirectoryEmailCheckJson>(cancellationToken: ct);
                return payload is not null && !payload.IsUnique;
            },
            $"check-email {email}",
            false,
            ct);

    public Task<bool> TryUpdateEmployeeAsync(User user, CancellationToken ct = default) =>
        ExecuteWithRetriesAsync(
            async () =>
            {
                string? primeServiceId = null;
                if (user.SubServiceId.HasValue)
                {
                    primeServiceId = await context.SubServices.AsNoTracking()
                        .Where(ss => ss.Id == user.SubServiceId.Value)
                        .Select(ss => ss.PrimeServiceId)
                        .FirstOrDefaultAsync(ct);
                }

                var hr = await context.UserHrProfiles.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == user.Id, ct);

                var payload = new
                {
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    email = user.Email,
                    role = user.Role?.Name ?? KyntusRoleNames.Employee,
                    serviceId = primeServiceId,
                    parentId = hr?.SuperviseurId,
                    isActive = user.IsActive,
                    hireDate = user.HireDate,
                    chefDeProjetId = hr?.ChefDeProjetId,
                    superviseurId = hr?.SuperviseurId,
                    referentTechniqueId = hr?.ReferentTechniqueId,
                    hrProfile = hr is null ? null : new
                    {
                        hr.DateNaissance,
                        hr.VilleNaissance,
                        hr.Nationalite,
                        hr.NumeroCarteAutoentrepreneur,
                        hr.Sexe,
                        hr.SituationFamiliale,
                        hr.NombreEnfants,
                        hr.Cin,
                        hr.Adresse,
                        hr.EmailPersonnel,
                        hr.Telephone1,
                        hr.TelephoneUrgence,
                        hr.RelationUrgence,
                        hr.Rib,
                        hr.ImmatriculationInterne,
                        hr.ImmatriculationCnss,
                        hr.DateEntree,
                        hr.DateEmbauche,
                        hr.DateAnciennete,
                        hr.DateSortie,
                        hr.DateEvolutionPoste,
                        hr.AncienPoste,
                        hr.AncienService,
                        hr.NiveauScolaire,
                        hr.IntitulesEtudes,
                        hr.EnFormation,
                        hr.DateDebutFormation,
                        hr.DateFinFormationPrevue,
                        niveauExpertiseMetier = hr.NiveauExpertiseMetier,
                    },
                };

                var client = CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Put, $"api/directory/employees/{user.Guid:D}")
                {
                    Content = JsonContent.Create(payload),
                };
                AttachAuth(request);

                var response = await client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                    return true;

                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Directory update échec {Status} pour {Email}: {Body}", response.StatusCode, user.Email, body);
                return false;
            },
            $"update {user.Email}",
            false,
            ct);

    public Task<bool> TryLinkAuthSubjectAsync(Guid employeeId, Guid authSubjectId, CancellationToken ct = default)
    {
        if (employeeId == Guid.Empty || authSubjectId == Guid.Empty)
            return Task.FromResult(true);

        return ExecuteWithRetriesAsync(
            async () =>
            {
                var client = CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Patch, $"api/directory/employees/{employeeId:D}/auth-subject")
                {
                    Content = JsonContent.Create(new { authSubjectId }),
                };
                AttachAuth(request);

                var response = await client.SendAsync(request, ct);
                return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;
            },
            $"link-auth guid={employeeId}",
            false,
            ct);
    }

    public Task<bool> TryDeleteEmployeeAsync(Guid employeeGuid, CancellationToken ct = default)
    {
        if (employeeGuid == Guid.Empty) return Task.FromResult(true);
        return ExecuteWithRetriesAsync(
            async () =>
            {
                var client = CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/directory/employees/{employeeGuid:D}");
                AttachAuth(request);
                var response = await client.SendAsync(request, ct);
                return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;
            },
            $"delete guid={employeeGuid}",
            false,
            ct);
    }

    private async Task<T> ExecuteWithRetriesAsync<T>(
        Func<Task<T>> action,
        string label,
        T failureValue,
        CancellationToken ct)
    {
        T? lastResult = default;
        var hasLastResult = false;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var result = await action();
                if (result is bool b && !b)
                {
                    lastResult = result;
                    hasLastResult = true;
                }
                else if (result is DirectoryEmployeeCreateResult r && !r.Success)
                {
                    lastResult = result;
                    hasLastResult = true;
                    if (!r.Retryable)
                        return result;
                }
                else
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Directory {Label} tentative {Attempt}/{Max}", label, attempt, MaxRetries);
                if (failureValue is DirectoryEmployeeCreateResult)
                {
                    lastResult = (T)(object)new DirectoryEmployeeCreateResult(
                        Guid.Empty, false, ex.Message, Retryable: true);
                    hasLastResult = true;
                }
            }

            if (attempt < MaxRetries)
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
        }

        logger.LogError("Directory {Label} échoué après {Max} tentatives", label, MaxRetries);
        return hasLastResult && lastResult is not null ? lastResult : failureValue;
    }

    private HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var baseUrl = configuration["Directory:BaseUrl"]?.Trim()
            ?? "http://employee-directory-backend:8080/";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        var client = httpClientFactory.CreateClient("DirectorySync");
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = timeout ?? TimeSpan.FromSeconds(60);
        return client;
    }

    private void AttachAuth(HttpRequestMessage request)
    {
        var authHeader = DirectoryHttpAuthContext.AuthorizationHeader.Value
            ?? httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authHeader);
    }

    private sealed class DirectoryEmployeeJson
    {
        public string Id { get; set; } = "";
    }

    private sealed class DirectoryBulkCreateJson
    {
        public string Email { get; set; } = "";
        public bool Success { get; set; }
        public Guid? EmployeeId { get; set; }
        public string? Error { get; set; }
    }

    private sealed class DirectoryEmailCheckJson
    {
        public bool IsUnique { get; set; }
    }

    private sealed class DirectoryErrorJson
    {
        public string? Error { get; set; }
        public string? Message { get; set; }
    }

    private static readonly JsonSerializerOptions ErrorJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string? TryParseDirectoryError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<DirectoryErrorJson>(body, ErrorJsonOptions);
            return parsed?.Error ?? parsed?.Message;
        }
        catch
        {
            return body.Length <= 500 ? body : body[..500];
        }
    }

    private static bool IsRetryableStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode is not (
            System.Net.HttpStatusCode.BadRequest
            or System.Net.HttpStatusCode.Conflict
            or System.Net.HttpStatusCode.NotFound
            or System.Net.HttpStatusCode.UnprocessableEntity);
}
