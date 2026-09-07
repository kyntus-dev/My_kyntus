namespace Conge.Domain.Entities;

/// <summary>
/// Portée d’une période interdite : générale (défaut) ou exception org.
/// La plus spécifique gagne : Service &gt; Cellule &gt; Pôle &gt; Département &gt; Général.
/// </summary>
public static class PeriodeInterditeScopeKinds
{
    public const string General = "General";
    public const string Department = "Department";
    public const string Pole = "Pole";
    public const string Cellule = "Cellule";
    public const string Service = "Service";

    public static string Normalize(string? kind)
    {
        var k = kind?.Trim() ?? string.Empty;
        if (k.Equals(Service, StringComparison.OrdinalIgnoreCase)) return Service;
        if (k.Equals(Cellule, StringComparison.OrdinalIgnoreCase)) return Cellule;
        if (k.Equals(Pole, StringComparison.OrdinalIgnoreCase)) return Pole;
        if (k.Equals(Department, StringComparison.OrdinalIgnoreCase)
            || k.Equals("Departement", StringComparison.OrdinalIgnoreCase)
            || k.Equals("OperationalDepartment", StringComparison.OrdinalIgnoreCase))
            return Department;
        return General;
    }

    public static string NormalizeScopeId(string scopeKind, string? scopeId)
    {
        if (Normalize(scopeKind) == General) return string.Empty;
        return (scopeId ?? string.Empty).Trim();
    }

    public static int Rank(string? kind) => Normalize(kind) switch
    {
        Service => 4,
        Cellule => 3,
        Pole => 2,
        Department => 1,
        _ => 0
    };

    public static string LabelFr(string? kind) => Normalize(kind) switch
    {
        Service => "Service",
        Cellule => "Cellule",
        Pole => "Pôle",
        Department => "Département",
        _ => "Général"
    };
}

/// <summary>
/// Mois calendaires où les congés sont interdits (récurrents chaque année).
/// Une ligne « General » (défaut sept–oct) + exceptions optionnelles par nœud org.
/// </summary>
public class PeriodeInterditeConge
{
    public static readonly Guid GeneralId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeee0001");

    public Guid Id { get; private set; }

    /// <summary>Mois interdits (1–12), sérialisés en JSON.</summary>
    public string MoisInterditsJson { get; private set; } = "[9,10]";

    public string ScopeKind { get; private set; } = PeriodeInterditeScopeKinds.General;

    /// <summary>Id Directory du nœud (vide pour la config générale).</summary>
    public string ScopeId { get; private set; } = string.Empty;

    public DateTime UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    private PeriodeInterditeConge() { }

    public bool IsGeneral =>
        PeriodeInterditeScopeKinds.Normalize(ScopeKind) == PeriodeInterditeScopeKinds.General;

    public static PeriodeInterditeConge CreerParDefaut()
    {
        return new PeriodeInterditeConge
        {
            Id = GeneralId,
            ScopeKind = PeriodeInterditeScopeKinds.General,
            ScopeId = string.Empty,
            MoisInterditsJson = "[9,10]",
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static PeriodeInterditeConge CreerPourScope(
        string scopeKind,
        string? scopeId,
        IEnumerable<int> mois,
        Guid? updatedBy = null)
    {
        var kind = PeriodeInterditeScopeKinds.Normalize(scopeKind);
        var id = PeriodeInterditeScopeKinds.NormalizeScopeId(kind, scopeId);
        if (kind != PeriodeInterditeScopeKinds.General && string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Un nœud org est requis pour une exception.", nameof(scopeId));

        var row = new PeriodeInterditeConge
        {
            Id = kind == PeriodeInterditeScopeKinds.General ? GeneralId : Guid.NewGuid(),
            ScopeKind = kind,
            ScopeId = id,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = updatedBy
        };
        row.MettreAJour(mois, updatedBy);
        return row;
    }

    public IReadOnlyList<int> GetMois()
    {
        try
        {
            var months = System.Text.Json.JsonSerializer.Deserialize<int[]>(MoisInterditsJson)
                ?? Array.Empty<int>();
            return months
                .Where(m => m is >= 1 and <= 12)
                .Distinct()
                .OrderBy(m => m)
                .ToList();
        }
        catch
        {
            return IsGeneral ? new List<int> { 9, 10 } : new List<int>();
        }
    }

    public void MettreAJour(IEnumerable<int> mois, Guid? updatedBy = null)
    {
        var cleaned = mois
            .Where(m => m is >= 1 and <= 12)
            .Distinct()
            .OrderBy(m => m)
            .ToArray();
        MoisInterditsJson = System.Text.Json.JsonSerializer.Serialize(cleaned);
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public bool ChevauchePeriode(DateTime debut, DateTime fin)
    {
        var mois = GetMois();
        if (mois.Count == 0) return false;

        var d = debut.Date;
        var f = fin.Date;
        if (f < d) (d, f) = (f, d);

        for (var day = d; day <= f; day = day.AddDays(1))
        {
            if (mois.Contains(day.Month))
                return true;
        }

        return false;
    }

    public bool MatchesEmploye(EmployeSnapshot employe)
    {
        var kind = PeriodeInterditeScopeKinds.Normalize(ScopeKind);
        if (kind == PeriodeInterditeScopeKinds.General) return true;

        var id = (ScopeId ?? string.Empty).Trim();
        if (id.Length == 0) return false;

        return kind switch
        {
            PeriodeInterditeScopeKinds.Service =>
                string.Equals(employe.OrgServiceId, id, StringComparison.OrdinalIgnoreCase)
                || (employe.ServiceId != Guid.Empty
                    && string.Equals(employe.ServiceId.ToString(), id, StringComparison.OrdinalIgnoreCase)),
            PeriodeInterditeScopeKinds.Cellule =>
                string.Equals(employe.CelluleId, id, StringComparison.OrdinalIgnoreCase),
            PeriodeInterditeScopeKinds.Pole =>
                string.Equals(employe.PoleId, id, StringComparison.OrdinalIgnoreCase),
            PeriodeInterditeScopeKinds.Department =>
                employe.BusinessDepartmentId is Guid dept
                && string.Equals(dept.ToString(), id, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>Choisit l’exception la plus spécifique, sinon la config générale.</summary>
    public static PeriodeInterditeConge PickForEmploye(
        IReadOnlyList<PeriodeInterditeConge> rows,
        EmployeSnapshot? employe,
        PeriodeInterditeConge general)
    {
        if (employe is null) return general;

        PeriodeInterditeConge? best = null;
        var bestRank = -1;
        foreach (var row in rows)
        {
            var rank = PeriodeInterditeScopeKinds.Rank(row.ScopeKind);
            if (rank <= bestRank) continue;
            if (!row.MatchesEmploye(employe)) continue;
            best = row;
            bestRank = rank;
        }

        return best ?? general;
    }
}
