namespace EmployeeDirectory.Application.Iam;

/// <summary>Un verbe d'une rubrique pour un compte : droit du rôle, exception du compte, résultat.</summary>
public sealed record UserAccessVerbDto(string Verb, bool Role, bool? Exception, bool Effectif);

public sealed record UserAccessScreenDto(string Id, string Label, string Module, IReadOnlyList<UserAccessVerbDto> Verbs);

public sealed record UserAccessDto(
    Guid SubjectId,
    string Role,
    bool Admin,
    int Exceptions,
    DateTime? ModifieLe,
    string? ModifiePar,
    IReadOnlyList<UserAccessScreenDto> Screens);

/// <summary>Changement demandé : « herite » (supprime l'exception), « accorde » ou « retire ».</summary>
public sealed record UserAccessChange(string Action, string Etat);

public sealed record UserAccessUpdateRequest(string Role, IReadOnlyList<UserAccessChange> Changes);

public sealed record UserAccessSummaryDto(Guid SubjectId, int Accordes, int Retires, DateTime ModifieLe);

public interface IIamUserAccessService
{
    Task<UserAccessDto> GetAsync(Guid subjectId, string role, CancellationToken ct = default);
    Task<UserAccessDto> UpdateAsync(Guid subjectId, UserAccessUpdateRequest request, string? auteur, CancellationToken ct = default);
    Task<IReadOnlyList<UserAccessSummaryDto>> SummaryAsync(CancellationToken ct = default);
}
