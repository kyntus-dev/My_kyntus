using System.Globalization;
using System.Text;

namespace Planning.Application.Users;

/// <summary>
/// v47 — Rapprochement d'un fichier « Nom ; Prénom ; E-mail » avec les collaborateurs créés sans
/// mail interne. Logique pure (aucune dépendance base / réseau), testée unitairement.
/// <para>
/// Règle d'or : <b>aucune attribution hasardeuse</b>. Seule une correspondance exacte du nom complet
/// est proposée cochée ; tout le reste (quasi-correspondance, homonymes, introuvable, adresse déjà
/// utilisée, doublon dans le fichier) demande une décision humaine ou est rejeté.
/// </para>
/// </summary>
public static class RapprochementMails
{
    /// <summary>Score mini pour proposer un candidat à l'écran.</summary>
    public const double SeuilCandidat = 0.70;

    /// <summary>Score mini d'une quasi-correspondance (à confirmer).</summary>
    public const double SeuilProbable = 0.85;

    /// <summary>Écart mini entre le 1er et le 2e score pour lever l'ambiguïté.</summary>
    public const double EcartAmbiguite = 0.05;

    private const double ScoreTokenMin = 0.65;
    private const int LongueurNomMin = 2;

    public const string StatutExact = "exact";
    public const string StatutProbable = "probable";
    public const string StatutAmbigu = "ambigu";
    public const string StatutIntrouvable = "introuvable";
    public const string StatutDejaAttribue = "deja";
    public const string StatutEmailUtilise = "utilise";
    public const string StatutDoublon = "doublon";
    public const string StatutInvalide = "invalide";

    /// <summary>Collaborateur candidat (sans mail interne) fourni par le service.</summary>
    public sealed record Agent(int UserId, string Nom, string Prenom);

    /// <summary>Ligne du fichier importé.</summary>
    public sealed record Ligne(int Numero, string Nom, string Prenom, string Email);

    public sealed record Candidat(int UserId, string Nom, string Prenom, double Score);

    public sealed record Resultat(
        int Numero,
        string Nom,
        string Prenom,
        string Email,
        string Statut,
        int? UserId,
        string? Message,
        IReadOnlyList<Candidat> Candidats);

    /// <param name="lignes">Lignes du fichier.</param>
    /// <param name="agents">Collaborateurs sans mail interne.</param>
    /// <param name="emailsUtilises">Adresses déjà prises (clé minuscule) → nom du titulaire.</param>
    public static IReadOnlyList<Resultat> Rapprocher(
        IReadOnlyList<Ligne> lignes,
        IReadOnlyList<Agent> agents,
        IReadOnlyDictionary<string, string> emailsUtilises)
    {
        var resultats = new List<Resultat>(lignes.Count);

        // 1er passage : doublons d'adresse dans le fichier.
        var occurrences = lignes
            .GroupBy(l => MailInterne.Cle(l.Email))
            .Where(g => g.Key.Length > 0 && g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var ligne in lignes)
        {
            var nom = (ligne.Nom ?? string.Empty).Trim();
            var prenom = (ligne.Prenom ?? string.Empty).Trim();
            var email = (ligne.Email ?? string.Empty).Trim();
            var cle = MailInterne.Cle(email);

            if (nom.Length == 0 && prenom.Length == 0)
            {
                resultats.Add(Rejet(ligne, StatutInvalide, "Nom et prénom absents."));
                continue;
            }
            if (!MailInterne.FormatValide(email))
            {
                resultats.Add(Rejet(ligne, StatutInvalide, "Adresse e-mail invalide."));
                continue;
            }
            if (MailInterne.EstProvisoire(email))
            {
                resultats.Add(Rejet(ligne, StatutInvalide, "Domaine réservé à l'application."));
                continue;
            }
            if (occurrences.Contains(cle))
            {
                resultats.Add(Rejet(ligne, StatutDoublon, "Adresse présente plusieurs fois dans le fichier."));
                continue;
            }
            if (emailsUtilises.TryGetValue(cle, out var titulaire))
            {
                resultats.Add(Rejet(ligne, StatutEmailUtilise, $"Adresse déjà utilisée par {titulaire}."));
                continue;
            }

            var scores = agents
                .Select(a => new Candidat(a.UserId, a.Nom, a.Prenom, Score(prenom, nom, a.Prenom, a.Nom)))
                .Where(c => c.Score >= SeuilCandidat)
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Nom, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var exacts = scores.Where(c => c.Score >= 0.999).ToList();
            if (exacts.Count == 1)
            {
                resultats.Add(new Resultat(
                    ligne.Numero, nom, prenom, email, StatutExact, exacts[0].UserId, null, new List<Candidat> { exacts[0] }));
                continue;
            }
            if (exacts.Count > 1)
            {
                resultats.Add(new Resultat(
                    ligne.Numero, nom, prenom, email, StatutAmbigu, null,
                    "Plusieurs collaborateurs portent exactement ce nom.", Top(exacts)));
                continue;
            }

            if (scores.Count == 0)
            {
                resultats.Add(new Resultat(
                    ligne.Numero, nom, prenom, email, StatutIntrouvable, null,
                    "Aucun collaborateur sans mail interne ne correspond.", new List<Candidat>()));
                continue;
            }

            var meilleur = scores[0];
            var second = scores.Count > 1 ? scores[1].Score : 0;
            if (meilleur.Score >= SeuilProbable && meilleur.Score - second >= EcartAmbiguite)
            {
                resultats.Add(new Resultat(
                    ligne.Numero, nom, prenom, email, StatutProbable, meilleur.UserId,
                    "Correspondance approchante — à confirmer.", Top(scores)));
                continue;
            }

            resultats.Add(new Resultat(
                ligne.Numero, nom, prenom, email, StatutAmbigu, null,
                "Plusieurs correspondances possibles — choisissez le collaborateur.", Top(scores)));
        }

        // 2e passage : deux lignes qui désignent le même collaborateur → aucune n'est appliquée.
        var parAgent = resultats
            .Where(r => r.UserId.HasValue)
            .GroupBy(r => r.UserId!.Value)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(r => r.Numero))
            .ToHashSet();

        if (parAgent.Count == 0)
            return resultats;

        return resultats
            .Select(r => parAgent.Contains(r.Numero)
                ? r with
                {
                    Statut = StatutDoublon,
                    UserId = null,
                    Message = "Plusieurs lignes désignent le même collaborateur.",
                }
                : r)
            .ToList();
    }

    private static IReadOnlyList<Candidat> Top(IEnumerable<Candidat> candidats) =>
        candidats.Take(5).ToList();

    private static Resultat Rejet(Ligne ligne, string statut, string message) =>
        new(ligne.Numero,
            (ligne.Nom ?? string.Empty).Trim(),
            (ligne.Prenom ?? string.Empty).Trim(),
            (ligne.Email ?? string.Empty).Trim(),
            statut,
            null,
            message,
            new List<Candidat>());

    // ── Normalisation et score (aligné sur HtelNameNormalizer / referral-candidate-match) ──

    public static string Normaliser(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var formD = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else
                sb.Append(' ');
        }
        return string.Join(
            ' ',
            sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Score [0,1] entre « nom prénom » du fichier et « nom prénom » d'un collaborateur.</summary>
    public static double Score(string? prenomFichier, string? nomFichier, string? prenomAgent, string? nomAgent)
    {
        var (prenomF, nomF) = Identite(prenomFichier, nomFichier);
        var (prenomA, nomA) = Identite(prenomAgent, nomAgent);
        if (nomF.Length == 0 || nomA.Length == 0)
            return 0;

        // Nom complet : les deux ordres possibles, de chaque côté.
        var gauche = new[] { $"{nomF} {prenomF}".Trim(), $"{prenomF} {nomF}".Trim() };
        var droite = new[] { $"{nomA} {prenomA}".Trim(), $"{prenomA} {nomA}".Trim() };
        var best = 0.0;
        foreach (var g in gauche)
            foreach (var d in droite)
                best = Math.Max(best, ScoreChaines(g, d));

        if (prenomF.Length >= LongueurNomMin && prenomA.Length >= LongueurNomMin)
        {
            var nomScore = ScoreChaines(nomF, nomA);
            var prenomScore = ScoreChaines(prenomF, prenomA);
            if (nomScore >= ScoreTokenMin && prenomScore >= ScoreTokenMin)
                best = Math.Max(best, (nomScore + prenomScore) / 2.0);

            // Nom et prénom inversés dans le fichier.
            var croiseNom = ScoreChaines(nomF, prenomA);
            var croisePrenom = ScoreChaines(prenomF, nomA);
            if (croiseNom >= ScoreTokenMin && croisePrenom >= ScoreTokenMin)
                best = Math.Max(best, (croiseNom + croisePrenom) / 2.0);
        }

        return best;
    }

    /// <summary>Un seul champ rempli contenant « NOM Prénom » : on le découpe.</summary>
    private static (string Prenom, string Nom) Identite(string? prenom, string? nom)
    {
        var p = Normaliser(prenom);
        var n = Normaliser(nom);
        if (p.Length < LongueurNomMin && n.Split(' ').Length >= 2)
        {
            var parts = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (string.Join(' ', parts.Skip(1)), parts[0]);
        }
        if (n.Length < LongueurNomMin && p.Split(' ').Length >= 2)
        {
            var parts = p.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (string.Join(' ', parts.Skip(1)), parts[0]);
        }
        return (p, n);
    }

    public static double ScoreChaines(string? left, string? right)
    {
        var a = Normaliser(left);
        var b = Normaliser(right);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;

        var court = a.Length <= b.Length ? a : b;
        var longue = a.Length <= b.Length ? b : a;
        var ratio = (double)court.Length / longue.Length;

        if (ratio >= 0.72)
        {
            if (longue.Contains(court, StringComparison.Ordinal))
                return Math.Min(0.995, 0.9 + 0.09 * ratio);
            if (longue.StartsWith(court, StringComparison.Ordinal))
                return Math.Min(0.99, 0.85 + 0.12 * ratio);
        }
        else if (ratio >= 0.5 && court.Length >= 4 && longue.Contains(court, StringComparison.Ordinal))
        {
            return 0.55 + 0.35 * ratio;
        }

        var maxLen = Math.Max(a.Length, b.Length);
        return Math.Max(0, 1.0 - (double)Levenshtein(a, b) / maxLen);
    }

    private static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) row[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            var prev = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var next = Math.Min(Math.Min(row[j] + 1, prev + 1), row[j - 1] + cost);
                row[j - 1] = prev;
                prev = next;
            }
            row[b.Length] = prev;
        }
        return row[b.Length];
    }
}
