using Planning.Application.DTOs;

namespace Planning.Application.Abstractions;

public interface IPlanningCongeService
{
    Task<IReadOnlyList<PlanningCongeListItemDto>> GetBySubServiceAsync(
        int subServiceId,
        string? weekStart,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlanningNewEmployeeDto>> GetNewEmployeesAsync(
        int subServiceId,
        CancellationToken ct = default);

    Task<PlanningCongeListItemDto> CreateAsync(CreateCongeDto dto, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>v64 : une absence (null si introuvable).</summary>
    Task<PlanningCongeListItemDto?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>v64 : absences d'un employé, de la plus récente à la plus ancienne.</summary>
    Task<IReadOnlyList<PlanningCongeListItemDto>> GetByUserAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// v64 : modifie une absence saisie dans le planning (employé, dates, type, motif), avec les mêmes
    /// contrôles qu'à la création ; le planning est resynchronisé sur l'ancienne et la nouvelle période.
    /// Une absence issue d'une demande de congé (workflow Congés) n'est pas modifiable ici.
    /// </summary>
    Task<PlanningCongeListItemDto?> UpdateAsync(int id, CreateCongeDto dto, CancellationToken ct = default);

    /// <summary>
    /// v64 : cellules (sous-services) dans lesquelles le compte peut gérer les absences — la sienne,
    /// celles qui lui sont rattachées et celles des services qu'il encadre.
    /// </summary>
    Task<IReadOnlySet<int>> SousServicesGerablesAsync(Guid compte, CancellationToken ct = default);

    /// <summary>v64 : cellule (sous-service) de l'employé d'une absence, ou d'un employé.</summary>
    Task<int?> SousServiceDeAsync(int? congeId, int? userId, CancellationToken ct = default);

    Task<SetSaturdaySlotResultDto> SetSaturdaySlotAsync(SetSaturdaySlotDto dto, CancellationToken ct = default);

    Task<BulkAbsenceDaysResponseDto> GetBulkAbsenceDaysAsync(
        BulkAbsenceDaysRequestDto request,
        CancellationToken ct = default);
}
