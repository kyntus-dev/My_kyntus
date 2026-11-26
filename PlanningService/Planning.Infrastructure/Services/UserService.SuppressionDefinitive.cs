using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Planning.Application.Users;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence.JournalActions;

namespace Planning.Infrastructure.Services;

/// <summary>v67 — Suppression définitive d'un compte (voir <see cref="IUtilisateurSuppressionDefinitive"/>).</summary>
public partial class UserService : IUtilisateurSuppressionDefinitive
{
    /// <summary>Liens supprimés avec le compte (responsabilités de cellules / services).</summary>
    private static readonly HashSet<string> TablesDeLiens = new(StringComparer.OrdinalIgnoreCase)
    {
        "UserSubServices", "UserManagedServices",
    };

    public async Task<SuppressionDefinitiveResultat> SupprimerDefinitivementAsync(int id, CancellationToken ct = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.SubService)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return new SuppressionDefinitiveResultat(SuppressionDefinitiveIssue.Introuvable, [], new Dictionary<string, string?>());

        var identite = new Dictionary<string, string?>
        {
            ["Nom"] = user.LastName,
            ["Prénom"] = user.FirstName,
            ["E-mail"] = user.Email,
            ["Rôle"] = JournalActeur.NormaliserRole(user.Role?.Name),
            ["Cellule"] = user.SubService?.Name,
            ["Date d'embauche"] = user.HireDate.ToString("dd/MM/yyyy"),
            ["État"] = user.IsActive ? "Actif" : "Désactivé",
        };
        var nom = JournalEcriture.NomComplet(user.LastName, user.FirstName);

        // 1. Règles de conservation : données protégées par la base (planning, demandes, renforts…).
        var bloquants = await DonneesAConserverAsync(user.Id, ct);
        if (bloquants.Count > 0)
            return new SuppressionDefinitiveResultat(SuppressionDefinitiveIssue.DonneesAConserver, bloquants, identite, nom);

        // 2. Suppression du compte et de ses réglages personnels, en une transaction. La ligne du compte
        //    n'est pas tracée en détail (elle l'est en une seule ligne, par le contrôleur).
        var employeeGuid = user.Guid;
        var authUserId = user.AuthUserId;
        await using (var tx = await _context.Database.BeginTransactionAsync(ct))
        {
            try
            {
                using (JournalScope.Silence(typeof(User), typeof(UserHrProfile), typeof(UserSubService), typeof(UserManagedService)))
                {
                    _context.UserSubServices.RemoveRange(_context.UserSubServices.Where(us => us.UserId == user.Id));
                    _context.UserManagedServices.RemoveRange(_context.UserManagedServices.Where(us => us.UserId == user.Id));
                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                await tx.RollbackAsync(ct);
                _context.ChangeTracker.Clear();
                var table = (ex.InnerException as PostgresException)?.ConstraintName is { } contrainte
                    ? TableDeContrainte(contrainte)
                    : null;
                _logger.LogWarning(ex, "Suppression définitive refusée pour {Email} (id {Id}) : données rattachées.", user.Email, user.Id);
                return new SuppressionDefinitiveResultat(
                    SuppressionDefinitiveIssue.DonneesAConserver,
                    [SuppressionDefinitiveRegles.Libelle(table)],
                    identite,
                    nom);
            }
        }

        // 3. Annuaire et compte de connexion (le compte Planning n'existe plus : une erreur ici est
        //    seulement consignée, comme pour l'annulation d'une création).
        await _directoryEmployeeWrite.TryDeleteEmployeeAsync(employeeGuid, ct);
        if (authUserId.HasValue)
        {
            try
            {
                var reponse = await _httpClient.DeleteAsync($"api/auth/users/from-planning/{authUserId.Value}", ct);
                if (!reponse.IsSuccessStatusCode && reponse.StatusCode != System.Net.HttpStatusCode.NotFound)
                    _logger.LogWarning("Auth : compte {AuthId} non supprimé ({Status}).", authUserId.Value, reponse.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auth indisponible : compte {AuthId} non supprimé.", authUserId.Value);
            }
        }

        _logger.LogWarning("Suppression définitive du compte {Email} (id {Id}).", user.Email, user.Id);
        return new SuppressionDefinitiveResultat(SuppressionDefinitiveIssue.Supprime, [], identite, nom);
    }

    /// <summary>
    /// Données que la base protège (clé étrangère sans suppression en cascade) encore rattachées au
    /// compte : lues dans le catalogue PostgreSQL, table par table. Base non PostgreSQL (tests) : aucune.
    /// </summary>
    private async Task<IReadOnlyList<string>> DonneesAConserverAsync(int userId, CancellationToken ct)
    {
        if (!_context.Database.IsNpgsql())
            return [];
        var connexion = _context.Database.GetDbConnection();
        var ouverte = connexion.State == ConnectionState.Open;
        if (!ouverte) await connexion.OpenAsync(ct);
        try
        {
            var references = new List<(string Table, string Colonne)>();
            await using (var cmd = connexion.CreateCommand())
            {
                cmd.CommandText =
                    """
                    SELECT c.conrelid::regclass::text, a.attname
                    FROM pg_constraint c
                    JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
                    WHERE c.contype = 'f'
                      AND c.confrelid = '"Users"'::regclass
                      AND array_length(c.conkey, 1) = 1
                      AND c.confdeltype IN ('a', 'r')
                    """;
                await using var lecteur = await cmd.ExecuteReaderAsync(ct);
                while (await lecteur.ReadAsync(ct))
                    references.Add((lecteur.GetString(0), lecteur.GetString(1)));
            }

            var bloquants = new List<string>();
            foreach (var (table, colonne) in references)
            {
                var nomTable = table.Trim('"');
                if (TablesDeLiens.Contains(nomTable)) continue;
                await using var cmd = connexion.CreateCommand();
                // Noms lus dans le catalogue (table déjà citée par regclass) ; colonne entre guillemets.
                var colonneCitee = "\"" + colonne.Replace("\"", "\"\"") + "\"";
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM " + table + " WHERE " + colonneCitee + " = @id)";
                var p = cmd.CreateParameter();
                p.ParameterName = "id";
                p.Value = userId;
                cmd.Parameters.Add(p);
                if (await cmd.ExecuteScalarAsync(ct) is true)
                {
                    var libelle = SuppressionDefinitiveRegles.Libelle(nomTable);
                    if (!bloquants.Contains(libelle)) bloquants.Add(libelle);
                }
            }
            return bloquants;
        }
        finally
        {
            if (!ouverte) await connexion.CloseAsync();
        }
    }

    /// <summary>« FK_ShiftAssignments_Users_UserId » ou « PlanningConsultations_UserId_fkey » → table.</summary>
    internal static string? TableDeContrainte(string contrainte)
    {
        var c = contrainte.Trim();
        if (c.StartsWith("FK_", StringComparison.Ordinal))
        {
            var reste = c[3..];
            var i = reste.IndexOf('_');
            return i > 0 ? reste[..i] : reste;
        }
        var j = c.IndexOf('_');
        return j > 0 ? c[..j] : null;
    }
}
