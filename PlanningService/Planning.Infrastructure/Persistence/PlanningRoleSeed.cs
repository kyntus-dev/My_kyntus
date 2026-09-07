using Microsoft.EntityFrameworkCore;
using Planning.Domain.Entities;
using Kyntus.Messaging.Contracts;

namespace Planning.Infrastructure.Persistence;

/// <summary>
/// Catalogue des rôles Planning requis par l'import employés et le module.
/// Idempotent — n'insère que les noms absents (pas de données métier démo).
/// </summary>
internal static class PlanningRoleSeed
{
    private static readonly (string Name, string Description)[] Catalog =
    [
        (KyntusRoleNames.Pilote, "Pilote opérationnel"),
        (KyntusRoleNames.ReferentTechnique, "Référent technique"),
        (KyntusRoleNames.Superviseur, "Superviseur de cellule"),
        (KyntusRoleNames.ChefDeProjet, "Chef de projet"),
        (KyntusRoleNames.Manager, "Manager département Support"),
        ("RH", "Ressources humaines"),
        ("Admin", "Administrateur système"),
        ("Audit", "Auditeur interne"),
        ("EquipeFormation", "Équipe formation"),
        (KyntusRoleNames.Qualiticien, "Qualiticien — formation continue"),
        ("Comptabilité", "Comptabilité — synthèse globale / paiement"),
    ];

    internal static async Task EnsureCatalogAsync(AppDbContext context, CancellationToken ct = default)
    {
        var existing = await context.Roles
            .Select(r => r.Name)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var (name, description) in Catalog)
        {
            if (existingSet.Contains(name))
                continue;

            context.Roles.Add(new Role
            {
                Name = name,
                Description = description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            existingSet.Add(name);
            added++;
        }

        if (added > 0)
        {
            await context.SaveChangesAsync(ct);
            Console.WriteLine($"✅ Catalogue rôles Planning : {added} rôle(s) ajouté(s).");
        }
    }
}
