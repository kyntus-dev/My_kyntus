namespace Planning.Application.DTOs
{
    public class CreateCongeDto
    {
        public int UserId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public string? Reason { get; set; }
        public string AbsenceType { get; set; } = "CongesPayes";
    }

    public record SetSaturdaySlotDto(
        int UserId,
        int Slot  // 1 = Matin (8h-12h) | 2 = Apres-midi (12h-16h)
    );

    public record PlanningCongeListItemDto(
        int Id,
        int UserId,
        string FullName,
        DateOnly StartDate,
        DateOnly EndDate,
        string Reason,
        string AbsenceType,
        string Status,
        // v64 : « Planning » (saisie ici, modifiable) ou « Demande » (workflow Congés, lecture seule ici).
        string Origine = "Planning");

    public record PlanningNewEmployeeDto(
        int Id,
        string FullName,
        DateTime HireDate,
        int MonthsHere,
        bool IsNewEmployee,
        int SaturdaySlot,
        string SaturdaySlotLabel);

    public record SetNewEmployeeStatusResultDto(
        int UserId,
        string FullName,
        bool IsNewEmployee,
        DateTime HireDate);

    public record SetSaturdaySlotResultDto(int UserId, int Slot, string SlotLabel);

    public sealed class BulkAbsenceDaysRequestDto
    {
        public string Period { get; set; } = "";
        public List<string> EmployeeGuids { get; set; } = [];
    }

    public record BulkAbsenceDaysItemDto(string EmployeeGuid, int AbsenceDayCount);

    public sealed class BulkAbsenceDaysResponseDto
    {
        public List<BulkAbsenceDaysItemDto> Items { get; set; } = [];
    }
}
