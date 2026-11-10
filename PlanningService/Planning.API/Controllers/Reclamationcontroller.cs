using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.DTOs;
using Planning.Application.Abstractions;
using System.Security.Claims;

namespace Planning.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReclamationController : ControllerBase
    {
        private readonly IReclamationService _service;

        public ReclamationController(IReclamationService service)
        {
            _service = service;
        }

        // --------------------------------------------------------------
        // POST /api/reclamation ó Soumettre (tous les rÙles)
        // --------------------------------------------------------------
        [HttpPost]
        [Authorize(Roles = "Employee,Ing√©nieur,Support IT,RH,Manager,Coach,RP,Admin,Audit,EquipeFormation")]
        public async Task<ActionResult<ReclamationDto>> Soumettre([FromBody] CreateReclamationDto dto)
        {
            // ? AJOUTEZ CES DEUX LIGNES
            Console.WriteLine($"?? UserId claim: '{UserId}'");
            Console.WriteLine($"?? UserNom claim: '{UserNom}'");

            var result = await _service.SoumettreAsync(dto, UserId, UserNom, UserRole);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        // --------------------------------------------------------------
        // GET /api/reclamation/mes-demandes ó Suivre ses demandes (tous)
        // --------------------------------------------------------------
        [HttpGet("mes-demandes")]
        [Authorize(Roles = "Employee,Ing√©nieur,Support IT,RH,Manager,Coach,RP,Admin,Audit,Equipe_Formation")]
        public async Task<ActionResult<PaginatedResult<ReclamationDto>>> MesDemandes(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _service.GetMesDemandesAsync(UserId, page, pageSize);
            return Ok(result);
        }

        // --------------------------------------------------------------
        // GET /api/reclamation/{id}
        // --------------------------------------------------------------
        [HttpGet("{id:int}")]
        [Authorize(Roles = "Employee,Ing√©nieur,Support IT,RH,Manager,Coach,RP,Admin,Audit,EquipeFormation")]
        public async Task<ActionResult<ReclamationDetailDto>> GetById(int id)
        {
            var result = await _service.GetByIdAsync(id, UserId);
            return result is null ? NotFound() : Ok(result);
        }

        // --------------------------------------------------------------
        // GET /api/reclamation ó Toutes (RH, Manager, RP, Admin)
        // --------------------------------------------------------------
        [HttpGet]
        [Authorize(Roles = "RH,Manager,RP,Admin")]
        public async Task<ActionResult<PaginatedResult<ReclamationDto>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null,
            [FromQuery] string? priorite = null)
        {
            var result = await _service.GetAllAsync(page, pageSize, status, priorite);
            return Ok(result);
        }

        // --------------------------------------------------------------
        // PUT /api/reclamation/{id}/traiter (RH, Manager, RP, Admin)
        // --------------------------------------------------------------
        [HttpPut("{id:int}/traiter")]
        [Authorize(Roles = "RH,Manager,RP,Admin")]
        public async Task<IActionResult> Traiter(
            int id, [FromBody] UpdateReclamationStatusDto dto)
        {
            await _service.TraiterAsync(id, dto, UserId, UserNom);
            return NoContent();
        }

        // --------------------------------------------------------------
        // PUT /api/reclamation/{id}/assigner (RH, Manager, RP, Admin)
        // --------------------------------------------------------------
        [HttpPut("{id:int}/assigner")]
        [Authorize(Roles = "RH,Manager,RP,Admin")]
        public async Task<IActionResult> Assigner(
            int id, [FromBody] AssignReclamationDto dto)
        {
            await _service.AssignerAsync(id, dto, UserId, UserNom);
            return NoContent();
        }

        // --------------------------------------------------------------
        // PUT /api/reclamation/{id}/prioriser (RH, Manager, RP, Admin)
        // --------------------------------------------------------------
        [HttpPut("{id:int}/prioriser")]
        [Authorize(Roles = "RH,Manager,RP,Admin")]
        public async Task<IActionResult> Prioriser(
            int id, [FromBody] PrioriserReclamationDto dto)
        {
            await _service.PrioriserAsync(id, dto, UserId, UserNom);
            return NoContent();
        }

        // --------------------------------------------------------------
        // GET /api/reclamation/reporting (RH, Manager, RP, Admin, Audit)
        // --------------------------------------------------------------
        [HttpGet("reporting")]
        [Authorize(Roles = "RH,Manager,RP,Admin,Audit")]
        public async Task<ActionResult<SatisfactionReportDto>> Reporting(
            [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            var result = await _service.GetReportSatisfactionAsync(from, to);
            return Ok(result);
        }

        // --------------------------------------------------------------
        // GET /api/reclamation/historique (RP, Admin, Audit)
        // --------------------------------------------------------------
        [HttpGet("historique")]
        [Authorize(Roles = "RP,Admin,Audit")]
        public async Task<ActionResult<PaginatedResult<ReclamationDetailDto>>> Historique(
            [FromQuery] int? reclamationId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var result = await _service.GetHistoriqueAsync(reclamationId, page, pageSize);
            return Ok(result);
        }

        // --------------------------------------------------------------
        // POST /api/reclamation/{id}/satisfaction (auteur uniquement)
        // --------------------------------------------------------------
        [HttpPost("{id:int}/satisfaction")]
        [Authorize(Roles = "Employee,Ing√©nieur,Support IT,RH,Manager,Coach,RP,Admin,Audit,EquipeFormation")]
        public async Task<IActionResult> NoteSatisfaction(int id, [FromBody] SatisfactionDto dto)
        {
            try
            {
                await _service.NoteSatisfactionAsync(id, dto, UserId);
                return NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(); // 403
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message }); // 400
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message }); // 404
            }
        }
        // --------------------------------------------------------------
        // Claims helpers
        // --------------------------------------------------------------
        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        private string UserNom => User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        private string UserRole => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    }
}