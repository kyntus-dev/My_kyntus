using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;
using Planning.Application.DTOs;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services;

public class FloorService : IFloorService
{
    private readonly AppDbContext _context;

    public FloorService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<FloorDto>> GetAllFloorsAsync()
    {
        return await _context.Floors
            .Include(f => f.Services)
            .Where(f => !f.IsArchived)
            .Select(f => new FloorDto
            {
                Id = f.Id,
                Name = f.Name,
                FloorNumber = f.FloorNumber,
                Description = f.Description,
                ServicesCount = f.Services.Count(s => !s.IsArchived)
            })
            .OrderBy(f => f.FloorNumber)
            .ToListAsync();
    }

    public async Task<FloorDto?> GetFloorByIdAsync(int id)
    {
        var floor = await _context.Floors
            .Include(f => f.Services)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (floor == null)
            return null;

        return new FloorDto
        {
            Id = floor.Id,
            Name = floor.Name,
            FloorNumber = floor.FloorNumber,
            Description = floor.Description,
            ServicesCount = floor.Services.Count
        };
    }

    public async Task<FloorDetailDto?> GetFloorWithServicesAsync(int id)
    {
        var floor = await _context.Floors
            .Include(f => f.Services)
                .ThenInclude(s => s.SubServices)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (floor == null)
            return null;

        return new FloorDetailDto
        {
            Id = floor.Id,
            Name = floor.Name,
            FloorNumber = floor.FloorNumber,
            Description = floor.Description,
            Services = floor.Services.Where(s => !s.IsArchived).Select(s => new ServiceDto
            {
                Id = s.Id,
                FloorId = s.FloorId,
                FloorName = floor.Name,
                Name = s.Name,
                Code = s.Code,
                SubServicesCount = s.SubServices.Count(ss => !ss.IsArchived)
            }).ToList()
        };
    }

    public async Task<FloorDto> CreateFloorAsync(CreateFloorDto dto)
    {
        var floor = new Floor
        {
            Name = dto.Name,
            FloorNumber = dto.FloorNumber,
            Description = dto.Description
        };

        _context.Floors.Add(floor);
        await _context.SaveChangesAsync();

        return new FloorDto
        {
            Id = floor.Id,
            Name = floor.Name,
            FloorNumber = floor.FloorNumber,
            Description = floor.Description,
            ServicesCount = 0
        };
    }

    public async Task<FloorDto?> UpdateFloorAsync(int id, UpdateFloorDto dto)
    {
        var floor = await _context.Floors
            .Include(f => f.Services)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (floor == null)
            return null;

        floor.Name = dto.Name;
        floor.FloorNumber = dto.FloorNumber;
        floor.Description = dto.Description;

        await _context.SaveChangesAsync();

        return new FloorDto
        {
            Id = floor.Id,
            Name = floor.Name,
            FloorNumber = floor.FloorNumber,
            Description = floor.Description,
            ServicesCount = floor.Services.Count
        };
    }

    public async Task<bool> DeleteFloorAsync(int id)
    {
        var floor = await _context.Floors
            .Include(f => f.Services)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (floor == null)
            return false;

        // Vérifier si l'étage a des services
        if (floor.Services.Any())
        {
            throw new InvalidOperationException(
                "Impossible de supprimer cet étage car il contient des services. " +
                "Veuillez d'abord supprimer ou déplacer les services.");
        }

        _context.Floors.Remove(floor);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> FloorExistsAsync(int id)
    {
        return await _context.Floors.AnyAsync(f => f.Id == id);
    }
}