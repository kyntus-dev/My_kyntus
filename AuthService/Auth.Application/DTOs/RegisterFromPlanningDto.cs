namespace Auth.Application.DTOs;

public class RegisterFromPlanningDto
{
    public string Email { get; set; } = string.Empty;
    /// <summary>
    /// Mot de passe initial, seulement à la création du compte. Absent (null) pour une simple
    /// mise à jour du rôle : le mot de passe d'un compte existant n'est alors jamais modifié.
    /// </summary>
    public string? DefaultPassword { get; set; }
    public int RoleId { get; set; }
    public string? RoleName { get; set; }

    /// <summary>Identifiant employé Planning / Directory (aligne JWT sub et annuaire documentation).</summary>
    public Guid EmployeeId { get; set; }
}

public class RegisterFromPlanningResponseDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
}

public class RegisterFromPlanningBatchDto
{
    public List<RegisterFromPlanningDto> Items { get; set; } = [];
}

public class RegisterFromPlanningBatchItemResultDto
{
    public string Email { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int? AuthUserId { get; set; }
    public string? Message { get; set; }
}
