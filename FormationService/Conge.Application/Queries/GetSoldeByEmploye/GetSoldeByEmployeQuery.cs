using Conge.Application.Abstractions;
using Conge.Application.DTOs;
using Conge.Domain.Entities;
using Conge.Domain.Exceptions;
using Conge.Domain.Interfaces;
using MediatR;

namespace Conge.Application.Queries.GetSoldeByEmploye;

public record GetSoldeByEmployeQuery(
    Guid EmployeId,
    int? Annee = null
) : IRequest<SoldeCongeDto>;

public class GetSoldeByEmployeHandler : IRequestHandler<GetSoldeByEmployeQuery, SoldeCongeDto>
{
    private readonly ISoldeCongeRepository _repo;
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmployeSnapshotEnsure _ensure;

    public GetSoldeByEmployeHandler(
        ISoldeCongeRepository repo,
        IEmployeSnapshotRepository employeRepo,
        IUnitOfWork unitOfWork,
        IEmployeSnapshotEnsure ensure)
    {
        _repo = repo;
        _employeRepo = employeRepo;
        _unitOfWork = unitOfWork;
        _ensure = ensure;
    }

    public async Task<SoldeCongeDto> Handle(GetSoldeByEmployeQuery request, CancellationToken ct)
    {
        var annee = request.Annee ?? DateTime.Today.Year;

        var solde = await _repo.GetByEmployeAndAnneeAsync(request.EmployeId, annee, ct);
        if (solde is null)
        {
            var employe = await _ensure.EnsureAsync(
                    new EmployeSnapshotEnsureRequest(
                        request.EmployeId,
                        Email: null,
                        Role: null,
                        ActorSubjectId: null,
                        AllowJwtStub: false),
                    ct)
                ?? await _employeRepo.GetByEmployeIdAsync(request.EmployeId, ct)
                ?? throw new SoldeNotFoundException(request.EmployeId, annee);

            solde = await _repo.GetByEmployeAndAnneeAsync(employe.EmployeId, annee, ct);
            if (solde is null)
            {
                var initial = PolitiqueConge.CalculerSoldeAnnuel(
                    employe.GetAncienneteAnnees(),
                    employe.EstMineur);
                solde = SoldeConge.Initialiser(employe.EmployeId, initial, annee);
                await _repo.AddAsync(solde, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }
        }

        var snapshot = await _employeRepo.GetByEmployeIdAsync(solde.EmployeId, ct);
        return new SoldeCongeDto(
            solde.EmployeId,
            solde.Annee,
            solde.SoldeInitial,
            solde.SoldeUtilise,
            solde.SoldeRestant,
            snapshot?.DateEligibiliteCongeAnnuel()
        );
    }
}