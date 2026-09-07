using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.DTOs;
using Planning.Application.Abstractions;

namespace Planning.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServicesController : ControllerBase
{
    private readonly IServiceService _serviceService;

    public ServicesController(IServiceService serviceService)
    {
        _serviceService = serviceService;
    }

    /// <summary>
    /// Récupérer tous les services
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ServiceDto>>> GetAllServices()
    {
        var services = await _serviceService.GetAllServicesAsync();
        return Ok(services);
    }

    /// <summary>
    /// Récupérer les services d'un étage spécifique
    /// </summary>
    [HttpGet("by-floor/{floorId}")]
    public async Task<ActionResult<List<ServiceDto>>> GetServicesByFloor(int floorId)
    {
        var services = await _serviceService.GetServicesByFloorIdAsync(floorId);
        return Ok(services);
    }

    /// <summary>
    /// Récupérer un service par ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ServiceDto>> GetServiceById(int id)
    {
        var service = await _serviceService.GetServiceByIdAsync(id);

        if (service == null)
            return NotFound(new { message = $"Le service avec l'ID {id} n'existe pas." });

        return Ok(service);
    }

    /// <summary>
    /// Récupérer un service avec ses sous-services
    /// </summary>
    [HttpGet("{id}/details")]
    public async Task<ActionResult<ServiceDetailDto>> GetServiceWithSubServices(int id)
    {
        var service = await _serviceService.GetServiceWithSubServicesAsync(id);

        if (service == null)
            return NotFound(new { message = $"Le service avec l'ID {id} n'existe pas." });

        return Ok(service);
    }

    /// <summary>
    /// Créer un nouveau service
    /// </summary>
    [HttpPost]
    public IActionResult CreateService([FromBody] CreateServiceDto dto) =>
        StatusCode(403, new { message = "Structure en lecture seule. Gérer dans Organisation RH (Prime)." });

    [HttpPut("{id}")]
    public IActionResult UpdateService(int id, [FromBody] UpdateServiceDto dto) =>
        StatusCode(403, new { message = "Structure en lecture seule. Gérer dans Organisation RH (Prime)." });
    /// <summary>
    /// Récupérer tous les services avec leurs sous-services (pour formulaire employee)
    /// </summary>
    [HttpGet("with-subservices")]
    public async Task<ActionResult<List<ServiceDetailDto>>> GetAllServicesWithSubServices()
    {
        var services = await _serviceService.GetAllServicesWithSubServicesAsync();
        return Ok(services);
    }

    /// <summary>
    /// Supprimer un service
    /// </summary>
    [HttpDelete("{id}")]
    public IActionResult DeleteService(int id) =>
        StatusCode(403, new { message = "Structure en lecture seule. Gérer dans Organisation RH (Prime)." });

    /// <summary>
    /// Vérifier si un code de service est unique
    /// </summary>
    [HttpGet("check-code/{code}")]
    //[Authorize(Roles = "RH")]
    public async Task<ActionResult<bool>> CheckCodeUnique(string code, [FromQuery] int? excludeId = null)
    {
        var isUnique = await _serviceService.IsCodeUniqueAsync(code, excludeId);
        return Ok(new { isUnique });
    }
}