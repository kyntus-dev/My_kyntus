using EmployeeDirectory.Application.Dtos;

namespace EmployeeDirectory.Infrastructure.Services;

internal sealed class EmployeesPageCache
{
    public List<EmployeeDto> Items { get; set; } = [];
    public int Total { get; set; }
}
