using Conge.Application.Abstractions;
using Conge.Application.Commands.ModifierConge;
using Conge.Application.Contracts;
using Conge.Application.Services;
using Conge.Domain.Entities;
using Conge.Domain.Enums;
using Conge.Domain.Exceptions;
using Conge.Domain.Interfaces;
using Kyntus.Messaging.Contracts;
using Xunit;

namespace Conge.UnitTests;

/// <summary>v64.2 — modification des dates d'une demande de congé (en attente ou validée). Données fictives.</summary>
public class ModifierCongeTests
{
    /// <summary>Année des congés de test : toujours dans le futur ; novembre est hors des mois interdits par défaut.</summary>
    private static readonly int An = DateTime.Today.Year + 1;

    private static EmployeSnapshot Employe() => EmployeSnapshot.Creer(
        Guid.NewGuid(), "Agent", "Test", "agent.test@example.com",
        Guid.NewGuid(), Guid.NewGuid(), "Service test",
        DateTime.UtcNow.AddYears(-3));

    /// <summary>Lundi 2 → vendredi 6 novembre de l'année de test, validé (5 jours ouvrables débités).</summary>
    private static (DemandeConge Demande, SoldeConge Solde) CongeValide(EmployeSnapshot emp, double soldeAnnuel = 18)
    {
        var solde = SoldeConge.Initialiser(emp.EmployeId, soldeAnnuel, An);
        var debut = LundiDeNovembre();
        var demande = DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, debut.AddDays(4), solde, emp);
        demande.ValiderParSuperviseur(emp.ManagerId, "ok", etapeRh: false);
        solde.DeduireSolde(demande.NombreJours);
        return (demande, solde);
    }

    private static DateTime LundiDeNovembre()
    {
        var d = new DateTime(An, 11, 1);
        while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
        return d;
    }

    // ── Domaine ──────────────────────────────────────────────────────────────

    [Fact]
    public void Conge_valide_modifiable_duree_recalculee_et_trace_dans_l_historique()
    {
        var emp = Employe();
        var (demande, _) = CongeValide(emp);
        var debut = demande.DateDebut;

        var ancien = demande.Modifier(debut, debut.AddDays(11), Guid.NewGuid(), "RH Test", "RH", "Prolongation");

        Assert.Equal(StatutDemande.Validee, demande.Statut);
        Assert.Equal(5, ancien.AncienNombreJours);
        Assert.Equal(10, demande.NombreJours);
        Assert.Equal(debut.AddDays(11), demande.DateFin);
        var trace = Assert.Single(demande.Decisions, d => d.Action == DemandeCongeDecisionActions.Modification);
        Assert.Equal(StatutDemande.Validee, trace.StatutAvant);
        Assert.Equal(StatutDemande.Validee, trace.StatutApres);
        Assert.Contains("Prolongation", trace.Commentaire);
        Assert.Contains("(10 j)", trace.Commentaire);
    }

    [Theory]
    [InlineData(StatutDemande.Refusee)]
    [InlineData(StatutDemande.Annulee)]
    public void Demande_refusee_ou_annulee_non_modifiable(StatutDemande statut)
    {
        var emp = Employe();
        var solde = SoldeConge.Initialiser(emp.EmployeId, 18, An);
        var debut = LundiDeNovembre();
        var demande = DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, debut.AddDays(4), solde, emp);
        if (statut == StatutDemande.Refusee) demande.Refuser(emp.ManagerId, "non");
        else demande.Annuler(emp.EmployeId);

        Assert.Throws<InvalidOperationException>(() =>
            demande.Modifier(debut.AddDays(7), debut.AddDays(9), Guid.NewGuid()));
    }

    [Fact]
    public void Conge_exceptionnel_la_fin_suit_la_duree_fixe()
    {
        var emp = Employe();
        var debut = LundiDeNovembre();
        var demande = DemandeConge.CreerCongeExceptionnel(emp.EmployeId, emp.ManagerId, TypeCongeExceptionnel.Mariage, debut);

        demande.Modifier(debut.AddDays(7), dateFin: null, Guid.NewGuid());

        Assert.Equal(debut.AddDays(10), demande.DateFin);
        Assert.Equal(4, demande.NombreJours);
    }

    [Fact]
    public void Debut_deplace_dans_le_passe_refuse_mais_conge_commence_peut_etre_raccourci()
    {
        var emp = Employe();
        var (demande, _) = CongeValide(emp);
        var debut = demande.DateDebut;
        var pendantLeConge = debut.AddDays(2);

        Assert.Throws<ArgumentException>(() =>
            demande.Modifier(debut.AddDays(1), debut.AddDays(4), Guid.NewGuid(), aujourdhui: pendantLeConge));

        demande.Modifier(debut, debut.AddDays(2), Guid.NewGuid(), aujourdhui: pendantLeConge);
        Assert.Equal(3, demande.NombreJours);
    }

    [Fact]
    public void Dates_identiques_ou_periode_sans_jour_ouvrable_refusees()
    {
        var emp = Employe();
        var (demande, _) = CongeValide(emp);
        var debut = demande.DateDebut;

        Assert.Throws<InvalidOperationException>(() => demande.Modifier(debut, debut.AddDays(4), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => demande.Modifier(debut.AddDays(5), debut.AddDays(6), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => demande.Modifier(debut.AddDays(3), debut.AddDays(1), Guid.NewGuid()));
        Assert.Empty(demande.Decisions.Where(d => d.Action == DemandeCongeDecisionActions.Modification));
    }

    // ── Commande (solde, planning, contrôles) ───────────────────────────────

    [Fact]
    public async Task Conge_valide_prolonge_ecart_debite_planning_et_journal_prevenus()
    {
        var emp = Employe();
        var (demande, solde) = CongeValide(emp);
        var ctx = new Contexte(emp, demande, solde);
        var debut = demande.DateDebut;

        var r = await ctx.Handler().Handle(
            new ModifierCongeCommand(demande.Id, debut, debut.AddDays(11), "Prolongation", Guid.NewGuid(), "RH"),
            CancellationToken.None);

        Assert.Equal(10, r.NombreJours);
        Assert.Equal(5, r.AncienNombreJours);
        Assert.Equal(18 - 10, solde.SoldeRestant);
        Assert.Equal(1, ctx.UnitOfWork.Saves);
        var modif = Assert.Single(ctx.Publisher.Modifies);
        Assert.Equal((debut, debut.AddDays(4)), (modif.AncienDebut, modif.AncienneFin));
        var journal = Assert.Single(ctx.Publisher.Journal);
        Assert.Equal("Modification de la demande de congé", journal.Operation);
        Assert.Equal("5", journal.AnciennesValeurs!["Nombre de jours"]);
        Assert.Equal("10", journal.NouvellesValeurs!["Nombre de jours"]);
    }

    [Fact]
    public async Task Conge_valide_raccourci_jours_restitues()
    {
        var emp = Employe();
        var (demande, solde) = CongeValide(emp);
        var ctx = new Contexte(emp, demande, solde);
        var debut = demande.DateDebut;

        await ctx.Handler().Handle(
            new ModifierCongeCommand(demande.Id, debut, debut.AddDays(1), null, Guid.NewGuid(), "Admin"),
            CancellationToken.None);

        Assert.Equal(18 - 2, solde.SoldeRestant);
    }

    [Fact]
    public async Task Solde_insuffisant_rien_n_est_enregistre_ni_publie()
    {
        var emp = Employe();
        var (demande, solde) = CongeValide(emp, soldeAnnuel: 6);
        var ctx = new Contexte(emp, demande, solde);
        var debut = demande.DateDebut;

        await Assert.ThrowsAsync<SoldeInsuffisantException>(() => ctx.Handler().Handle(
            new ModifierCongeCommand(demande.Id, debut, debut.AddDays(11), null, Guid.NewGuid(), "RH"),
            CancellationToken.None));

        Assert.Equal(0, ctx.UnitOfWork.Saves);
        Assert.Empty(ctx.Publisher.Modifies);
        Assert.Empty(ctx.Publisher.Journal);
    }

    [Fact]
    public void Chevauchement_avec_une_autre_demande_detecte_la_demande_modifiee_exclue()
    {
        var emp = Employe();
        var (demande, _) = CongeValide(emp);
        var solde = SoldeConge.Initialiser(emp.EmployeId, 18, An);
        var autre = DemandeConge.CreerCongeAnnuel(
            emp.EmployeId, emp.ManagerId, demande.DateDebut.AddDays(14), demande.DateDebut.AddDays(18), solde, emp);

        Assert.False(ModifierCongeHandler.ChevaucheAutreDemande([demande, autre], demande));
        demande.Modifier(demande.DateDebut, demande.DateDebut.AddDays(15), Guid.NewGuid());
        Assert.True(ModifierCongeHandler.ChevaucheAutreDemande([demande, autre], demande));

        autre.Annuler(emp.EmployeId);
        Assert.False(ModifierCongeHandler.ChevaucheAutreDemande([demande, autre], demande));
    }

    [Fact]
    public async Task Demande_en_attente_modifiee_sans_toucher_au_solde_ni_au_planning()
    {
        var emp = Employe();
        var solde = SoldeConge.Initialiser(emp.EmployeId, 18, DateTime.Today.Year);
        var debut = LundiDeNovembre();
        var demande = DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, debut.AddDays(4), solde, emp);
        var ctx = new Contexte(emp, demande, solde);

        await ctx.Handler().Handle(
            new ModifierCongeCommand(demande.Id, debut.AddDays(7), debut.AddDays(9), null, Guid.NewGuid(), "RH"),
            CancellationToken.None);

        Assert.Equal(StatutDemande.EnAttente, demande.Statut);
        Assert.Equal(3, demande.NombreJours);
        Assert.Equal(18, solde.SoldeRestant);
        Assert.Empty(ctx.Publisher.Modifies);
        Assert.Single(ctx.Publisher.Journal);
    }

    [Theory]
    [InlineData("Super Admin", "Admin")]
    [InlineData("SuperAdmin", "Admin")]
    [InlineData("RH", "RH")]
    public void Role_super_admin_presente_comme_admin(string role, string attendu)
        => Assert.Equal(attendu, ModifierCongeHandler.NormaliserRole(role));

    // ── Doublures ────────────────────────────────────────────────────────────

    private sealed class Contexte
    {
        public FakeDemandeRepo Demandes { get; } = new();
        public FakeSoldeRepo Soldes { get; } = new();
        public FakeEmployeRepo Employes { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakePublisher Publisher { get; } = new();

        public Contexte(EmployeSnapshot emp, DemandeConge demande, SoldeConge solde)
        {
            Employes.Employees.Add(emp);
            Demandes.Items.Add(demande);
            Soldes.Items.Add(solde);
        }

        public ModifierCongeHandler Handler()
        {
            var regles = new CongeReglesService(new FakePeriodeRepo(), new FakeQuotaRepo(), Demandes, Employes);
            var finalizer = new CongeValidationFinalizer(
                Demandes, Soldes, Employes, UnitOfWork, Publisher, new FakeWorkflow());
            return new ModifierCongeHandler(Demandes, Soldes, Employes, UnitOfWork, Publisher, regles, finalizer);
        }
    }

    private sealed class FakeWorkflow : ILeaveWorkflowPolicy
    {
        public Task<bool> IsRhValidationRequiredAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakePublisher : ICongeEventPublisher
    {
        public List<(Guid DemandeId, DateTime AncienDebut, DateTime AncienneFin)> Modifies { get; } = new();
        public List<JournalActionMessage> Journal { get; } = new();

        public Task PublishCongeValideAsync(
            Guid employeId, Guid demandeId, DateTime dateDebut, DateTime dateFin,
            double nombreJours, string typeConge, string? typeExceptionnel = null,
            Guid? validateurId = null, string? validateurNom = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task PublishCongeModifieAsync(
            Guid employeId, Guid demandeId, DateTime dateDebut, DateTime dateFin,
            double nombreJours, string typeConge, string? typeExceptionnel,
            DateTime ancienneDateDebut, DateTime ancienneDateFin,
            Guid? acteurId = null, string? acteurNom = null, CancellationToken ct = default)
        {
            Modifies.Add((demandeId, ancienneDateDebut, ancienneDateFin));
            return Task.CompletedTask;
        }

        public Task PublishJournalAsync(JournalActionMessage message, CancellationToken ct = default)
        {
            Journal.Add(message);
            return Task.CompletedTask;
        }

        public Task PublishCongeRefuseAsync(
            Guid employeId, Guid demandeId, string motif,
            Guid? validateurId = null, string? validateurNom = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task PublishCongeDemandeAsync(
            Guid employeId, Guid demandeId, Guid managerId,
            DateTime dateDebut, DateTime dateFin, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakePeriodeRepo : IPeriodeInterditeRepository
    {
        private readonly PeriodeInterditeConge _row = PeriodeInterditeConge.CreerParDefaut();
        public Task<PeriodeInterditeConge> GetOrCreateAsync(CancellationToken ct = default) => Task.FromResult(_row);
        public Task<PeriodeInterditeConge?> GetByScopeAsync(string scopeKind, string? scopeId, CancellationToken ct = default)
            => Task.FromResult<PeriodeInterditeConge?>(
                PeriodeInterditeScopeKinds.Normalize(scopeKind) == PeriodeInterditeScopeKinds.General ? _row : null);
        public Task<IReadOnlyList<PeriodeInterditeConge>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PeriodeInterditeConge>>([_row]);
        public Task AddAsync(PeriodeInterditeConge config, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(PeriodeInterditeConge config) { }
        public void Remove(PeriodeInterditeConge config) { }
    }

    private sealed class FakeQuotaRepo : IQuotaCongeServiceRepository
    {
        public Task<QuotaCongeService?> GetByServiceIdAsync(string serviceId, CancellationToken ct = default)
            => Task.FromResult<QuotaCongeService?>(null);
        public Task<IReadOnlyList<QuotaCongeService>> GetByServiceIdsAsync(IEnumerable<string> serviceIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QuotaCongeService>>([]);
        public Task AddAsync(QuotaCongeService quota, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(QuotaCongeService quota) { }
    }

    private sealed class FakeSoldeRepo : ISoldeCongeRepository
    {
        public List<SoldeConge> Items { get; } = new();

        public Task<SoldeConge?> GetByEmployeAndAnneeAsync(Guid employeId, int annee, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(s => s.EmployeId == employeId && s.Annee == annee));

        public Task<IEnumerable<SoldeConge>> GetAllByEmployeAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Items.Where(s => s.EmployeId == employeId).AsEnumerable());

        public Task AddAsync(SoldeConge solde, CancellationToken ct = default)
        {
            Items.Add(solde);
            return Task.CompletedTask;
        }

        public void Update(SoldeConge solde) { }
    }

    private sealed class FakeEmployeRepo : IEmployeSnapshotRepository
    {
        public List<EmployeSnapshot> Employees { get; } = new();

        public Task<EmployeSnapshot?> GetByEmployeIdAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Employees.FirstOrDefault(e => e.EmployeId == employeId));
        public Task<EmployeSnapshot?> GetByEmployeIdOrEmailAsync(Guid employeId, string? email, CancellationToken ct = default)
            => GetByEmployeIdAsync(employeId, ct);
        public Task<IEnumerable<EmployeSnapshot>> GetByManagerIdAsync(Guid managerId, CancellationToken ct = default)
            => Task.FromResult(Employees.Where(e => e.ManagerId == managerId).AsEnumerable());
        public Task<IReadOnlyList<EmployeSnapshot>> GetByPerimeterAsync(
            Guid managerId, IReadOnlyList<string>? orgNodeIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());
        public Task<IReadOnlyList<EmployeSnapshot>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());
        public Task<IReadOnlyList<EmployeSnapshot>> GetByServiceIdAsync(Guid serviceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.Where(e => e.ServiceId == serviceId).ToList());
        public Task<IReadOnlyList<EmployeSnapshot>> GetByOrgNodeIdAsync(string orgNodeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());
        public Task AddAsync(EmployeSnapshot employe, CancellationToken ct = default)
        {
            Employees.Add(employe);
            return Task.CompletedTask;
        }
        public void Update(EmployeSnapshot employe) { }
        public void Remove(EmployeSnapshot employe) => Employees.Remove(employe);
        public Task<EmployeSnapshot?> GetAdminOuRhAsync(CancellationToken ct = default)
            => Task.FromResult<EmployeSnapshot?>(null);
        public Task<bool> ExistsAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Employees.Any(e => e.EmployeId == employeId));
    }

    private sealed class FakeDemandeRepo : IDemandeCongeRepository
    {
        public List<DemandeConge> Items { get; } = new();

        public Task<DemandeConge?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(d => d.Id == id));
        public Task<IEnumerable<DemandeConge>> GetByEmployeIdAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Items.Where(d => d.EmployeId == employeId).AsEnumerable());
        public Task<IEnumerable<DemandeConge>> GetByManagerIdAsync(
            Guid managerId, IEnumerable<string>? validationNodeIds = null, CancellationToken ct = default)
            => Task.FromResult(Items.Where(d => d.ManagerId == managerId).AsEnumerable());
        public Task<IEnumerable<DemandeConge>> GetByStatutAsync(StatutDemande statut, CancellationToken ct = default)
            => Task.FromResult(Items.Where(d => d.Statut == statut).AsEnumerable());
        public Task<IEnumerable<DemandeConge>> GetHistoriqueAsync(Guid employeId, int annee, CancellationToken ct = default)
            => Task.FromResult(Items.AsEnumerable());
        public Task<IEnumerable<DemandeConge>> GetByAnneeAsync(int annee, CancellationToken ct = default)
            => Task.FromResult(Items.AsEnumerable());
        public Task AddAsync(DemandeConge demande, CancellationToken ct = default)
        {
            Items.Add(demande);
            return Task.CompletedTask;
        }
        public void Update(DemandeConge demande) { }
        public Task<bool> ExistsCongeEnChevauchementAsync(
            Guid employeId, DateTime debut, DateTime fin, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<IReadOnlyList<DemandeConge>> GetOccupyingQuotaAsync(
            IEnumerable<Guid> employeIds, DateTime debut, DateTime fin,
            Guid? excludeDemandeId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DemandeConge>>(Items
                .Where(d => employeIds.Contains(d.EmployeId)
                    && CongeQuotaStatuts.Occupants.Contains(d.Statut)
                    && d.DateDebut <= fin && d.DateFin >= debut
                    && (!excludeDemandeId.HasValue || d.Id != excludeDemandeId.Value))
                .ToList());
    }
}
