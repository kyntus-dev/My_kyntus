namespace Kyntus.Caching;

/// <summary>
/// Cache partagé Kyntus — L1 mémoire + L2 Redis (si configuré).
/// Convention de clés : <c>{service}:{entity}:{id}:{version}</c>
/// </summary>
public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Supprime toutes les clés correspondant au préfixe (best-effort ; L1 exact + L2 si Redis).</summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}

/// <summary>TTL recommandés pour la plateforme.</summary>
public static class KyntusCacheTtl
{
    public static readonly TimeSpan Referential = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Entity = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Computation = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Short = TimeSpan.FromMinutes(1);
}

/// <summary>Helpers de construction de clés cache.</summary>
public static class KyntusCacheKeys
{
    public static string DirectoryOrgOverview() => "directory:org:overview:v1";
    public static string DirectoryEmployees(string? role, string? poleId) =>
        $"directory:employees:v1:{role ?? "_"}:{poleId ?? "_"}";
    public static string DirectoryEmployee(Guid id) => $"directory:employee:v1:{id:N}";
    public static string DirectoryIam(Guid subjectId, string role) =>
        $"directory:iam:v1:{subjectId:N}:{role}";
    public static string PrimePonderation(string scope, string scopeId, string templateId, string day) =>
        $"prime:ponderation:{scope}:{scopeId}:{templateId}:{day}";
    public static string PrimeScope(string userId, string period) =>
        $"prime:scope:{userId}:{period}";
    public static string FormationCatalog() => "formation:catalog:published:v1";
    public static string CongeOrgCatalog() => "conge:directory:org:catalog:v1";
    public static string DocUserLookup(string key) => $"documentation:user:{key}";
    public static string AuthRoleCatalog() => "auth:roles:catalog:v1";
    public static string PlanningPerimeter(int userId) => $"planning:perimeter:v1:{userId}";
    public static string PlanningDirectoryParent(Guid employeeId) => $"planning:directory:parent:v1:{employeeId:N}";
    public static string PrimeSupervisedCellules(string supervisorUserId) =>
        $"prime:scope:cellules:v2:{supervisorUserId}";
    public static string PrimeAbsences(string period, string employeeHash) =>
        $"prime:absences:v1:{period}:{employeeHash}";
}
