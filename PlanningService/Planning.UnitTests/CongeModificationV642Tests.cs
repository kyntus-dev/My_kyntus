using System.Reflection;
using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Messaging.Consumers;
using Planning.Infrastructure.Persistence;
using Xunit;

namespace Planning.UnitTests;

/// <summary>
/// v64.2 — Congé validé dont les dates sont modifiées dans Congés : l'absence du planning est mise à jour
/// (même ligne, repérée par l'identifiant de la demande), l'ancienne période est rendue avant de poser la
/// nouvelle. Données fictives.
/// </summary>
public class CongeModificationV642Tests
{
    [Fact]
    public async Task Dates_modifiees_meme_absence_mise_a_jour_ancienne_periode_rendue_puis_nouvelle_posee()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("conge-v642-" + Guid.NewGuid()).Options);
        var employe = Guid.NewGuid();
        var demande = Guid.NewGuid();
        db.Users.Add(new User { Id = 5, Guid = employe, FirstName = "Agent", LastName = "Test" });
        db.Conges.Add(new Conge
        {
            Id = 1, UserId = 5, SourceDemandeId = demande, Status = CongeStatus.Approved,
            StartDate = new DateOnly(2026, 12, 21), EndDate = new DateOnly(2027, 1, 4),
        });
        await db.SaveChangesAsync();
        var impact = new ImpactEnregistre();

        await new CongeValideConsumer(db, impact, NullLogger<CongeValideConsumer>.Instance)
            .Consume(Contexte(new CongeValideMessage
            {
                DemandeId = demande, EmployeId = employe, NombreJours = 5, TypeConge = "Annuel",
                DateDebut = new DateTime(2026, 12, 21), DateFin = new DateTime(2026, 12, 25),
                Modification = true,
                AncienneDateDebut = new DateTime(2026, 12, 21), AncienneDateFin = new DateTime(2027, 1, 4),
            }));

        var conge = Assert.Single(db.Conges);
        Assert.Equal(1, conge.Id);
        Assert.Equal((new DateOnly(2026, 12, 21), new DateOnly(2026, 12, 25)), (conge.StartDate, conge.EndDate));
        Assert.Contains("dates modifiées", conge.Reason);
        Assert.Equal(2, impact.Appels.Count);
        Assert.Equal((5, new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 4), true), impact.Appels[0]);
        Assert.Equal((5, new DateOnly(2026, 12, 21), new DateOnly(2026, 12, 25), false), impact.Appels[1]);
    }

    [Fact]
    public async Task Premiere_validation_une_seule_synchronisation()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("conge-v642-" + Guid.NewGuid()).Options);
        var employe = Guid.NewGuid();
        db.Users.Add(new User { Id = 5, Guid = employe, FirstName = "Agent", LastName = "Test" });
        await db.SaveChangesAsync();
        var impact = new ImpactEnregistre();

        await new CongeValideConsumer(db, impact, NullLogger<CongeValideConsumer>.Instance)
            .Consume(Contexte(new CongeValideMessage
            {
                DemandeId = Guid.NewGuid(), EmployeId = employe, NombreJours = 3, TypeConge = "Annuel",
                DateDebut = new DateTime(2026, 12, 21), DateFin = new DateTime(2026, 12, 23),
            }));

        Assert.Single(db.Conges);
        var appel = Assert.Single(impact.Appels);
        Assert.False(appel.Retire);
    }

    private sealed class ImpactEnregistre : IPlanningLeaveImpactService
    {
        public List<(int UserId, DateOnly Debut, DateOnly Fin, bool Retire)> Appels { get; } = new();

        public Task SyncAfterAbsenceChangeAsync(int userId, DateOnly start, DateOnly end, bool absenceRemoved, CancellationToken ct = default)
        {
            Appels.Add((userId, start, end, absenceRemoved));
            return Task.CompletedTask;
        }
    }

    private static ConsumeContext<CongeValideMessage> Contexte(CongeValideMessage message)
    {
        var ctx = DispatchProxy.Create<ConsumeContext<CongeValideMessage>, ContexteMinimal>();
        ((ContexteMinimal)(object)ctx).Message = message;
        return ctx;
    }

    /// <summary>Contexte MassTransit réduit au message et au jeton d'annulation (le reste n'est pas utilisé).</summary>
    public class ContexteMinimal : DispatchProxy
    {
        public object? Message { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            if (targetMethod.Name == "get_Message") return Message;
            if (targetMethod.Name == "get_CancellationToken") return CancellationToken.None;
            var type = targetMethod.ReturnType;
            if (type == typeof(Task)) return Task.CompletedTask;
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }
}
