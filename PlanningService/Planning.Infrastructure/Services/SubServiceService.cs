using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;
using Planning.Application.DTOs;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services;

public class SubServiceService : ISubServiceService
{
    private readonly AppDbContext _context;

    public SubServiceService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<SubServiceDto>> GetAllSubServicesAsync()
    {
        return await _context.SubServices
            .Include(ss => ss.Service)
            .Include(ss => ss.Users)
            .Where(ss => !ss.IsArchived)
            .Select(ss => new SubServiceDto
            {
                Id = ss.Id,
                ServiceId = ss.ServiceId,
                ServiceName = ss.Service.Name,
                Name = ss.Name,
                Code = ss.Code,
                PrimeServiceId = ss.PrimeServiceId,
                EmployeesCount = ss.Users.Count(u => u.IsActive)
            })
            .OrderBy(ss => ss.ServiceName)
            .ThenBy(ss => ss.Name)
            .ToListAsync();
    }

    public async Task<List<SubServiceDto>> GetSubServicesByServiceIdAsync(int serviceId)
    {
        return await _context.SubServices
            .Include(ss => ss.Service)
            .Include(ss => ss.Users)
            .Where(ss => ss.ServiceId == serviceId && !ss.IsArchived)
            .Select(ss => new SubServiceDto
            {
                Id = ss.Id,
                ServiceId = ss.ServiceId,
                ServiceName = ss.Service.Name,
                Name = ss.Name,
                Code = ss.Code,
                PrimeServiceId = ss.PrimeServiceId,
                EmployeesCount = ss.Users.Count(u => u.IsActive)
            })
            .OrderBy(ss => ss.Name)
            .ToListAsync();
    }

    public async Task<SubServiceDto?> GetSubServiceByIdAsync(int id)
    {
        var subService = await _context.SubServices
            .Include(ss => ss.Service)
            .Include(ss => ss.Users)
            .FirstOrDefaultAsync(ss => ss.Id == id);

        if (subService == null)
            return null;

        return new SubServiceDto
        {
            Id = subService.Id,
            ServiceId = subService.ServiceId,
            ServiceName = subService.Service.Name,
            Name = subService.Name,
            Code = subService.Code,
            PrimeServiceId = subService.PrimeServiceId,
            EmployeesCount = subService.Users.Count
        };
    }

    public async Task<SubServiceDetailDto?> GetSubServiceWithEmployeesAsync(int id)
    {
        var subService = await _context.SubServices
            .Include(ss => ss.Service)
                .ThenInclude(s => s.Floor)
            .Include(ss => ss.Users)
                .ThenInclude(u => u.Role)
            .FirstOrDefaultAsync(ss => ss.Id == id);

        if (subService == null)
            return null;

        return new SubServiceDetailDto
        {
            Id = subService.Id,
            ServiceId = subService.ServiceId,
            ServiceName = subService.Service.Name,
            FloorName = subService.Service.Floor.Name,
            Name = subService.Name,
            Code = subService.Code,
            Employees = subService.Users.Select(u => new UserSimpleDto
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                RoleName = u.Role.Name
            }).ToList()
        };
    }

    public async Task<List<UserSimpleDto>> GetEmployeesBySubServiceAsync(int id)
    {
        var detail = await GetSubServiceWithEmployeesAsync(id);
        return detail?.Employees ?? [];
    }

    public async Task<SubServiceDto> CreateSubServiceAsync(CreateSubServiceDto dto)
    {
        // Vérifier que le service existe
        var serviceExists = await _context.Services.AnyAsync(s => s.Id == dto.ServiceId);
        if (!serviceExists)
        {
            throw new InvalidOperationException($"Le service avec l'ID {dto.ServiceId} n'existe pas.");
        }

        // Vérifier l'unicité du code
        var codeExists = await _context.SubServices.AnyAsync(ss => ss.Code == dto.Code);
        if (codeExists)
        {
            throw new InvalidOperationException($"Le code '{dto.Code}' est déjà utilisé par un autre sous-service.");
        }

        var subService = new SubService
        {
            ServiceId = dto.ServiceId,
            Name = dto.Name,
            Code = dto.Code,
            PrimeServiceId = dto.PrimeServiceId?.Trim(),
        };

        if (string.IsNullOrWhiteSpace(subService.PrimeServiceId))
        {
            throw new InvalidOperationException(
                "Un sous-service doit être lié à l'Organisation RH (PrimeServiceId requis). " +
                "Créez le service via Organisation RH ou exécutez la synchronisation org.");
        }

        _context.SubServices.Add(subService);
        await _context.SaveChangesAsync();

        // Recharger avec les relations
        var service = await _context.Services.FindAsync(dto.ServiceId);

        return new SubServiceDto
        {
            Id = subService.Id,
            ServiceId = subService.ServiceId,
            ServiceName = service?.Name ?? "",
            Name = subService.Name,
            Code = subService.Code,
            PrimeServiceId = subService.PrimeServiceId,
            EmployeesCount = 0
        };
    }

    public async Task<SubServiceDto?> UpdateSubServiceAsync(int id, UpdateSubServiceDto dto)
    {
        var subService = await _context.SubServices
            .Include(ss => ss.Service)
            .Include(ss => ss.Users)
            .FirstOrDefaultAsync(ss => ss.Id == id);

        if (subService == null)
            return null;

        // Vérifier que le nouveau service existe
        var serviceExists = await _context.Services.AnyAsync(s => s.Id == dto.ServiceId);
        if (!serviceExists)
        {
            throw new InvalidOperationException($"Le service avec l'ID {dto.ServiceId} n'existe pas.");
        }

        // Vérifier l'unicité du code (sauf pour le sous-service actuel)
        var codeExists = await _context.SubServices
            .AnyAsync(ss => ss.Code == dto.Code && ss.Id != id);
        if (codeExists)
        {
            throw new InvalidOperationException($"Le code '{dto.Code}' est déjà utilisé par un autre sous-service.");
        }

        subService.ServiceId = dto.ServiceId;
        subService.Name = dto.Name;
        subService.Code = dto.Code;

        await _context.SaveChangesAsync();

        return new SubServiceDto
        {
            Id = subService.Id,
            ServiceId = subService.ServiceId,
            ServiceName = subService.Service.Name,
            Name = subService.Name,
            Code = subService.Code,
            PrimeServiceId = subService.PrimeServiceId,
            EmployeesCount = subService.Users.Count
        };
    }

    public async Task<bool> DeleteSubServiceAsync(int id)
    {
        var subService = await _context.SubServices
            .Include(ss => ss.Users)
            .Include(ss => ss.WeeklyPlannings)
            .FirstOrDefaultAsync(ss => ss.Id == id);

        if (subService == null)
            return false;

        // Vérifier si le sous-service a des employés
        if (subService.Users.Any())
        {
            throw new InvalidOperationException(
                "Impossible de supprimer ce sous-service car il contient des employés. " +
                "Veuillez d'abord déplacer ou supprimer les employés.");
        }

        // Vérifier si le sous-service a des plannings
        if (subService.WeeklyPlannings.Any())
        {
            throw new InvalidOperationException(
                "Impossible de supprimer ce sous-service car il a des plannings associés. " +
                "Veuillez d'abord supprimer les plannings.");
        }

        _context.SubServices.Remove(subService);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> SubServiceExistsAsync(int id)
    {
        return await _context.SubServices.AnyAsync(ss => ss.Id == id);
    }

    public async Task<bool> IsCodeUniqueAsync(string code, int? excludeId = null)
    {
        if (excludeId.HasValue)
        {
            return !await _context.SubServices.AnyAsync(ss => ss.Code == code && ss.Id != excludeId.Value);
        }

        return !await _context.SubServices.AnyAsync(ss => ss.Code == code);
    }
}