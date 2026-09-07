namespace Planning.Application.DTOs;

public class ResetPasswordResultDto
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TemporaryPassword { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class ResetPasswordsByGroupRequest
{
    /// <summary>Pole, Cellule ou Service.</summary>
    public string Scope { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    /// <summary>GUIDs Directory des employés du groupe (aperçu UI). Union avec le miroir Planning.</summary>
    public List<string>? EmployeeGuids { get; set; }
}

public class ResetPasswordsByGroupResultDto
{
    public string Scope { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public int TargetedCount { get; set; }
    public int ResetCount { get; set; }
    public int FailedCount { get; set; }
    public List<ResetPasswordResultDto> Items { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}
