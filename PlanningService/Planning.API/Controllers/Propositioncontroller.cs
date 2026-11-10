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
    public class PropositionController : ControllerBase
    {
        private readonly IPropositionService _service;

        public PropositionController(IPropositionService service)
        {
            _service = service;
        }

        // POST /api/proposition � Soumettre (tous les r�les)
        [HttpPost]
        [Authorize(Roles = "Employee,Ingénieur,Support IT,RH,Manager,Coach,RP,Admin,Audit,EquipeFormation")]
        public async Task<ActionResult<PropositionDto>> Soumettre(
            [FromBody] CreatePropositionDto dto)
        {
            var result = await _service.SoumettreAsync(dto, UserId, UserNom, UserRole);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        // GET /api/proposition/mes-demandes (tous)
        [HttpGet("mes-demandes")]
        [Authorize(Roles = "Employee,Ingénieur,Support IT,RH,Manager,Coach,RP,Admin,Audit,EquipeFormation")]
        public async Task<ActionResult<PaginatedResult<PropositionDto>>> MesDemandes(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _service.GetMesDemandesAsync(UserId, page, pageSize);
            return Ok(result);
        }

        // GET /api/proposition/{id}
        [HttpGet("{id:int}")]
        [Authorize(Roles = "Employee,Ingénieur,Support IT,RH,Manager,Coach,RP,Admin,Audit,EquipeFormation")]
        public async Task<ActionResult<PropositionDetailDto>> GetById(int id)
        {
            var result = await _service.GetByIdAsync(id, UserId);
            return result is null ? NotFound() : Ok(result);
        }

        // GET /api/proposition (RH, Manager, RP, Admin)
        [HttpGet]
        [Authorize(Roles = "RH,Manager,RP,Admin")]
        public async Task<ActionResult<PaginatedResult<PropositionDto>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null)
        {
            var result = await _service.GetAllAsync(page, pageSize, status);
            return Ok(result);
        }

        // PUT /api/proposition/{id}/evaluer (RH, Manager, RP, Admin)
        [HttpPut("{id:int}/evaluer")]
        [Authorize(Roles = "RH,Manager,RP,Admin")]
        public async Task<IActionResult> Evaluer(
            int id, [FromBody] UpdatePropositionStatusDto dto)
        {
            await _service.EvaluerAsync(id, dto, UserId, UserNom);
            return NoContent();
        }

        // PUT /api/proposition/{id}/assigner (RH, Manager, RP, Admin)
        [HttpPut("{id:int}/assigner")]
        [Authorize(Roles = "RH,Manager,RP,Admin")]
        public async Task<IActionResult> Assigner(
            int id, [FromBody] AssignPropositionDto dto)
        {
            await _service.AssignerAsync(id, dto, UserId, UserNom);
            return NoContent();
        }

        // PUT /api/proposition/{id}/prioriser (RH, Manager, RP, Admin)
        [HttpPut("{id:int}/prioriser")]
        [Authorize(Roles = "RH,Manager,RP,Admin")]
        public async Task<IActionResult> Prioriser(
            int id, [FromBody] PrioriserPropositionDto dto)
        {
            await _service.PrioriserAsync(id, dto, UserId, UserNom);
            return NoContent();
        }

        // GET /api/proposition/reporting (RH, Manager, RP, Admin, Audit)
        [HttpGet("reporting")]
        [Authorize(Roles = "RH,Manager,RP,Admin,Audit")]
        public async Task<ActionResult<SatisfactionReportDto>> Reporting(
            [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            var result = await _service.GetReportSatisfactionAsync(from, to);
            return Ok(result);
        }

        // GET /api/proposition/historique (RP, Admin, Audit)
        [HttpGet("historique")]
        [Authorize(Roles = "RP,Admin,Audit")]
        public async Task<ActionResult<PaginatedResult<PropositionDetailDto>>> Historique(
            [FromQuery] int? propositionId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var result = await _service.GetHistoriqueAsync(propositionId, page, pageSize);
            return Ok(result);
        }

        // POST /api/proposition/{id}/satisfaction (auteur uniquement)
        [HttpPost("{id:int}/satisfaction")]
        [Authorize(Roles = "Employee,Ingénieur,Support IT,RH,Manager,Coach,RP,Admin,Audit,EquipeFormation")]
        public async Task<IActionResult> NoteSatisfaction(int id, [FromBody] SatisfactionDto dto)
        {
            try
            {
                await _service.NoteSatisfactionAsync(id, dto, UserId);
                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        private string UserNom => User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        private string UserRole => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    }
}