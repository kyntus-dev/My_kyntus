using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Messaging.Consumers;

public sealed class DirectoryEmployeeHrProfileProjectionConsumer(AppDbContext db) :
    IConsumer<DirectoryEmployeeHrProfileChangedMessage>
{
    public async Task Consume(ConsumeContext<DirectoryEmployeeHrProfileChangedMessage> context)
    {
        var msg = context.Message;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Guid == msg.EmployeeId, context.CancellationToken);
        if (user is null) return;

        if (msg.IsDeleted)
        {
            var existing = await db.UserHrProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, context.CancellationToken);
            if (existing is not null)
                db.UserHrProfiles.Remove(existing);
            await db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var profile = await db.UserHrProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, context.CancellationToken);
        if (profile is null)
        {
            profile = new UserHrProfile { UserId = user.Id };
            db.UserHrProfiles.Add(profile);
        }

        profile.DateNaissance = msg.DateNaissance;
        profile.VilleNaissance = msg.VilleNaissance;
        profile.Nationalite = msg.Nationalite;
        profile.NumeroCarteAutoentrepreneur = msg.NumeroCarteAutoentrepreneur;
        profile.Sexe = msg.Sexe;
        profile.SituationFamiliale = msg.SituationFamiliale;
        profile.NombreEnfants = msg.NombreEnfants;
        profile.Cin = msg.Cin;
        profile.Adresse = msg.Adresse;
        profile.EmailPersonnel = msg.EmailPersonnel;
        profile.Telephone1 = msg.Telephone1;
        profile.TelephoneUrgence = msg.TelephoneUrgence;
        profile.RelationUrgence = msg.RelationUrgence;
        profile.Rib = msg.Rib;
        profile.ImmatriculationInterne = msg.ImmatriculationInterne;
        profile.ImmatriculationCnss = msg.ImmatriculationCnss;
        profile.DateEntree = msg.DateEntree;
        profile.DateEmbauche = msg.DateEmbauche;
        profile.DateAnciennete = msg.DateAnciennete;
        profile.DateSortie = msg.DateSortie;
        profile.DateEvolutionPoste = msg.DateEvolutionPoste;
        profile.AncienPoste = msg.AncienPoste;
        profile.AncienService = msg.AncienService;
        profile.NiveauScolaire = msg.NiveauScolaire;
        profile.IntitulesEtudes = msg.IntitulesEtudes;
        profile.ChangerEnFormation(msg.EnFormation, DateOnly.FromDateTime(DateTime.UtcNow));
        profile.DateDebutFormation = msg.DateDebutFormation;
        profile.DateFinFormationPrevue = msg.DateFinFormationPrevue;
        profile.NiveauExpertiseMetier = msg.NiveauExpertiseMetier;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
