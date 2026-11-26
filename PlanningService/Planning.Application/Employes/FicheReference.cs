using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Planning.Domain.Entities;

namespace Planning.Application.Employes;

/// <summary>
/// v63 — Fichier RH de référence des employés (« BDD salariés ») : colonnes, lecture des valeurs,
/// contrôles et complétude des fiches. Règles pures, sans base de données, testées à part.
///
/// Toutes les colonnes du fichier sont reprises dans la fiche employé :
/// N° Ordre, Agents, Date d'ancienneté, Date d'embauche, Date de sortie, Nomination de poste,
/// Type contrat, Nationalité, Ville de naissance, Sexe, Situation familiale, CIN,
/// N° d'immatriculation, Date de naissance, Adresse, TEL 1, TEL 2 (+ N° BOX des feuilles « LISTE BOX »).
/// </summary>
public static partial class FicheReference
{
    /// <summary>Colonne du fichier : clé interne, intitulé du fichier, variantes d'intitulé acceptées.</summary>
    /// <summary>
    /// v64 : <paramref name="Profil"/> dit dans quel fichier la colonne est attendue — commune aux deux,
    /// propre au fichier des salariés, propre au fichier des entrepreneurs (sous-traitants), ou facultative.
    /// Les colonnes « manquantes » ne sont signalées que pour le profil du fichier lu.
    /// </summary>
    public sealed record Colonne(string Cle, string Intitule, IReadOnlyList<string> Variantes, string Profil = ProfilCommun);

    public const string ProfilCommun = "commun";
    public const string ProfilSalaries = "salaries";
    public const string ProfilSousTraitants = "sousTraitants";
    public const string ProfilFacultatif = "facultatif";

    public const string CleNumeroOrdre = "numeroOrdre";
    public const string CleAgents = "agents";
    public const string CleBox = "numeroBox";

    /// <summary>Colonnes dans l'ordre du fichier de référence.</summary>
    public static readonly IReadOnlyList<Colonne> Colonnes =
    [
        new(CleNumeroOrdre, "N° Ordre", ["n ordre", "no ordre", "numero ordre", "numero d ordre", "n d ordre", "matricule",
            // v64 : fichier des entrepreneurs (« Référence »).
            "reference", "ref", "n reference", "numero reference"]),
        new(CleAgents, "Agents", ["agents", "agent", "salaries", "salarie", "nom et prenom", "nom prenom", "employe"]),
        new("dateAnciennete", "Date d'ancienneté", ["date d anciennete", "date d anciennte", "date anciennete", "anciennete",
            "date d ancienntee", "date d anciennetee", "date ancienntee"]),
        // v64 : « Date d'Entrée » (fichier des entrepreneurs).
        new("dateEntree", "Date d'Entrée", ["date d entree", "date entree", "entree", "date d integration", "date integration"], ProfilSousTraitants),
        new("dateEmbauche", "Date d'Embauche", ["date d embauche", "date embauche", "embauche"], ProfilSalaries),
        new("dateSortie", "Date de Sortie", ["date de sortie", "date sortie", "sortie"]),
        new("poste", "Nomination de poste", ["nomination de poste", "nomination poste", "poste", "fonction", "intitule du poste"], ProfilSalaries),
        new("typeContrat", "Type Contrat", ["type contrat", "type de contrat", "contrat"], ProfilSalaries),
        new("nationalite", "Nationalité", ["nationalite"]),
        new("villeNaissance", "Ville de Naissance", ["ville de naissance", "ville naissance", "lieu de naissance"], ProfilSalaries),
        new("sexe", "Sexe", ["sexe", "genre"]),
        new("situationFamiliale", "Situation Familiale", ["situation familiale", "situation de famille"]),
        new("cin", "CIN", ["cin", "cni", "carte d identite"], ProfilSalaries),
        new("immatriculationCnss", "N° d'Immatriculation", ["n d immatriculation", "n immatriculation", "numero d immatriculation", "immatriculation", "cnss", "n cnss"], ProfilSalaries),
        new("dateNaissance", "Date de Naissance", ["date de naissance", "date naissance", "naissance"]),
        new("adresse", "Adresse", ["adresse", "domicile"]),
        new("telephone1", "TEL 1", ["tel 1", "tel1", "telephone 1", "telephone", "tel", "gsm"]),
        new("telephone2", "TEL 2", ["tel 2", "tel2", "telephone 2", "telephone urgence", "tel urgence"]),
        new(CleBox, "N° BOX", ["n box", "no box", "numero box", "box"], ProfilSalaries),
        // v64 : colonnes du fichier des entrepreneurs (sous-traitants) et type d'employé.
        new("carteImmatriculation", "Carte d'Immatriculation", ["carte d immatriculation", "carte d immatricualation",
            "carte immatriculation", "n carte d immatriculation", "carte de sejour", "carte de residence"], ProfilSousTraitants),
        new("passeport", "N° Passeport", ["n passeport", "no passeport", "numero passeport", "n de passeport", "passeport"], ProfilSousTraitants),
        new("registreNational", "N° d'inscription au registre national", ["n d inscription au registre national",
            "n inscription au registre national", "n d inscription registre national", "inscription au registre national",
            "registre national", "n registre national", "n carte auto entrepreneur", "carte auto entrepreneur",
            "carte autoentrepreneur", "numero carte autoentrepreneur", "n carte autoentrepreneur"], ProfilSousTraitants),
        new("typeEmploye", "Type d'employé", ["type d employe", "type employe", "type de salarie", "statut employe",
            "type d agent", "salarie ou sous traitant"], ProfilFacultatif),
        // v66 : e-mail personnel (fichier RH de l'écran « Liaisons HTEL », et toute colonne d'e-mail).
        new("emailPersonnel", "E-mail personnel", ["email", "e mail", "mail", "email personnel", "e mail personnel",
            "mail personnel", "adresse mail", "adresse e mail", "adresse email", "courriel"], ProfilFacultatif),
    ];

    /// <summary>Champs exigés pour qu'une fiche de salarié actif soit complète (fichier des salariés).</summary>
    public static readonly IReadOnlyList<string> ChampsObligatoires =
    [
        "immatriculationInterne", "dateAnciennete", "dateEmbauche", "poste", "typeContrat", "nationalite",
        "villeNaissance", "sexe", "situationFamiliale", "cin", "immatriculationCnss", "dateNaissance",
        "adresse", "telephone1",
    ];

    /// <summary>
    /// v64 : champs exigés pour un sous-traitant (fichier des entrepreneurs). Pas de CNSS, de type de
    /// contrat ni de poste : à la place, l'inscription au registre national et une pièce d'identité
    /// (CIN, carte d'immatriculation ou passeport : <see cref="ChampPieceIdentite"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> ChampsObligatoiresSousTraitant =
    [
        "immatriculationInterne", "dateAnciennete", "dateEntree", "nationalite", "sexe", "situationFamiliale",
        ChampPieceIdentite, "registreNational", "dateNaissance", "adresse", "telephone1",
    ];

    /// <summary>Champ virtuel : au moins une pièce d'identité (CIN, carte d'immatriculation ou passeport).</summary>
    public const string ChampPieceIdentite = "pieceIdentite";
    public const string LibellePieceIdentite = "Pièce d'identité (CIN, carte d'immatriculation ou passeport)";

    /// <summary>Champs obligatoires selon le type d'employé de la fiche.</summary>
    public static IReadOnlyList<string> ChampsObligatoiresPour(string typeEmploye) =>
        typeEmploye == TypesEmploye.SousTraitant ? ChampsObligatoiresSousTraitant : ChampsObligatoires;

    // ── Normalisation ────────────────────────────────────────────────────────────────────────

    /// <summary>Majuscules, sans accents ni ponctuation, espaces réduits : « Faress  Mehdí » → « FARESS MEHDI ».</summary>
    public static string NormaliserTexte(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return string.Empty;
        var decompose = texte.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decompose.Length);
        foreach (var c in decompose)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (c == 'Œ' || c == 'œ') { sb.Append("OE"); continue; }
            if (c == 'Æ' || c == 'æ') { sb.Append("AE"); continue; }
            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }
        return Espaces().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Clé de rapprochement d'un nom : mots normalisés, triés (l'ordre nom / prénom ne compte pas).</summary>
    public static string CleNom(string? nomComplet) =>
        string.Join(' ', NormaliserTexte(nomComplet).Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(m => m, StringComparer.Ordinal));

    public static string CleNom(string? nom, string? prenom) => CleNom($"{nom} {prenom}");

    /// <summary>Clé interne d'une colonne d'après son intitulé (tolère accents, ponctuation, espaces doubles).</summary>
    public static string? ColonneDe(string? intitule)
    {
        var n = NormaliserTexte(intitule).ToLowerInvariant();
        if (n.Length == 0) return null;
        foreach (var c in Colonnes)
            if (NormaliserTexte(c.Intitule).ToLowerInvariant() == n || c.Variantes.Contains(n))
                return c.Cle;
        return null;
    }

    /// <summary>
    /// v64 : profil du fichier lu — « entrepreneurs » s'il porte plus de colonnes propres aux sous-traitants
    /// que de colonnes propres aux salariés, sinon « salariés ».
    /// </summary>
    public static string ProfilFichier(IEnumerable<string> clesReconnues)
    {
        var cles = clesReconnues.ToHashSet(StringComparer.Ordinal);
        int Nb(string profil) => Colonnes.Count(c => c.Profil == profil && cles.Contains(c.Cle));
        return Nb(ProfilSousTraitants) > Nb(ProfilSalaries) ? ProfilSousTraitants : ProfilSalaries;
    }

    /// <summary>Colonnes attendues et absentes du fichier, selon son profil (les facultatives ne sont jamais signalées).</summary>
    public static IReadOnlyList<string> ColonnesManquantes(IReadOnlyCollection<string> clesReconnues)
    {
        var profil = ProfilFichier(clesReconnues);
        return Colonnes
            .Where(c => c.Profil == ProfilCommun || c.Profil == profil)
            .Where(c => !clesReconnues.Contains(c.Cle))
            .Select(c => c.Intitule)
            .ToList();
    }

    /// <summary>
    /// Sépare « NOM PRÉNOM » : le premier mot est le nom, avec ses particules (EL, BEN, AIT, OULD…) ;
    /// le reste est le prénom. Utilisé seulement pour créer une fiche.
    /// </summary>
    public static (string Nom, string Prenom) SeparerNom(string nomComplet)
    {
        var mots = Espaces().Replace(nomComplet.Trim(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (mots.Length == 0) return (string.Empty, string.Empty);
        if (mots.Length == 1) return (mots[0], string.Empty);
        var particules = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EL", "AL", "BEN", "BEL", "BENT", "AIT", "OULD", "ABOU", "ABDEL", "LA", "LE", "DE", "DU" };
        var i = 0;
        while (i < mots.Length - 2 && particules.Contains(mots[i])) i++;
        var nom = string.Join(' ', mots[..(i + 1)]);
        var prenom = string.Join(' ', mots[(i + 1)..]);
        return (nom, prenom);
    }

    // ── Lecture des valeurs ──────────────────────────────────────────────────────────────────

    public static string? LireTexte(object? valeur)
    {
        var t = valeur switch
        {
            null => null,
            DateTime d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            double n when Math.Abs(n % 1) < 1e-9 => ((long)n).ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => valeur.ToString(),
        };
        if (t is null) return null;
        t = Espaces().Replace(t.Trim(), " ");
        return t.Length == 0 ? null : t;
    }

    /// <summary>Date du fichier : cellule date, numéro de série Excel, « jj/mm/aaaa » ou « aaaa-mm-jj ».</summary>
    public static (DateOnly? Date, string? Anomalie) LireDate(object? valeur)
    {
        switch (valeur)
        {
            case null:
                return (null, null);
            case DateTime d:
                return Plausible(DateOnly.FromDateTime(d));
            case DateOnly d:
                return Plausible(d);
            case double or int or long or decimal:
            {
                var n = Convert.ToDouble(valeur, CultureInfo.InvariantCulture);
                // Numéro de série Excel (1940 → 2100) ; un grand nombre est autre chose (n° d'immatriculation…).
                if (n is >= 14611 and <= 73051)
                    return Plausible(DateOnly.FromDateTime(DateTime.FromOADate(n)));
                return (null, $"« {LireTexte(valeur)} » n'est pas une date");
            }
        }
        var texte = LireTexte(valeur);
        if (texte is null) return (null, null);
        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yy", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss"];
        if (DateTime.TryParseExact(texte, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var lue))
            return Plausible(DateOnly.FromDateTime(lue));
        if (double.TryParse(texte, NumberStyles.Float, CultureInfo.InvariantCulture, out var serie))
            return LireDate(serie);
        return (null, $"« {texte} » n'est pas une date reconnue");
    }

    private static (DateOnly? Date, string? Anomalie) Plausible(DateOnly d) =>
        d.Year is >= 1930 and <= 2100 ? (d, null) : (null, $"date improbable ({d:dd/MM/yyyy})");

    /// <summary>Sexe : F / Femme / Féminin → « F » ; H / Homme / M / Masculin → « M » (valeurs de la fiche).</summary>
    public static (string? Sexe, string? Anomalie) LireSexe(object? valeur)
    {
        var n = NormaliserTexte(LireTexte(valeur));
        if (n.Length == 0) return (null, null);
        return n switch
        {
            "F" or "FEMME" or "FEMININ" or "FEMININE" => ("F", null),
            "H" or "M" or "HOMME" or "MASCULIN" => ("M", null),
            _ => (null, $"sexe « {LireTexte(valeur)} » non reconnu (F, H ou M attendu)"),
        };
    }

    /// <summary>
    /// Situation familiale : « C », « M+2 », « D+1 », « V » ou en toutes lettres → code de la fiche
    /// (CELIBATAIRE, MARIE, DIVORCE, VEUF) et nombre d'enfants.
    /// </summary>
    public static (string? Situation, int? Enfants, string? Anomalie) LireSituation(object? valeur)
    {
        var texte = LireTexte(valeur);
        var n = NormaliserTexte(texte);
        if (n.Length == 0) return (null, null, null);
        var m = SituationCourte().Match(texte!.Trim());
        if (m.Success)
        {
            var enfants = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            var code = char.ToUpperInvariant(m.Groups[1].Value[0]) switch
            {
                'C' => "CELIBATAIRE",
                'M' => "MARIE",
                'D' => "DIVORCE",
                _ => "VEUF",
            };
            return (code, enfants, null);
        }
        if (n.StartsWith("CELIB", StringComparison.Ordinal)) return ("CELIBATAIRE", null, null);
        if (n.StartsWith("MARI", StringComparison.Ordinal)) return ("MARIE", null, null);
        if (n.StartsWith("DIVOR", StringComparison.Ordinal)) return ("DIVORCE", null, null);
        if (n.StartsWith("VEU", StringComparison.Ordinal)) return ("VEUF", null, null);
        return (null, null, $"situation familiale « {texte} » non reconnue (C, M, D, V, avec +n enfants)");
    }

    /// <summary>« 06-02914370 (SA SŒUR) » → numéro « 06-02914370 », relation « Sa sœur ».</summary>
    public static (string? Numero, string? Relation) LireTelephoneUrgence(object? valeur)
    {
        var texte = LireTexte(valeur);
        if (texte is null) return (null, null);
        var numero = Telephone().Match(texte);
        var relation = Parentheses().Match(texte) is { Success: true } p
            ? p.Groups[1].Value.Trim()
            : (numero.Success ? texte.Replace(numero.Value, string.Empty).Trim(' ', '-', ',', ';', ':') : null);
        relation = string.IsNullOrWhiteSpace(relation) ? null : Capitaliser(relation);
        return (numero.Success ? numero.Value.Trim() : texte, relation);
    }

    /// <summary>Numéro (immatriculation…) : chiffres seulement ; null s'il n'y en a pas.</summary>
    public static string? LireNumero(object? valeur)
    {
        if (valeur is DateTime) return null;
        var texte = LireTexte(valeur);
        if (texte is null) return null;
        var chiffres = new string(texte.Where(char.IsDigit).ToArray());
        return chiffres.Length == 0 ? null : chiffres;
    }

    /// <summary>N° d'ordre : texte tel quel ; un nombre est complété à 6 chiffres (« 1 » → « 000001 »).</summary>
    public static string? LireNumeroOrdre(object? valeur)
    {
        if (valeur is double or int or long or decimal)
        {
            var n = Convert.ToInt64(valeur, CultureInfo.InvariantCulture);
            return n.ToString("D6", CultureInfo.InvariantCulture);
        }
        return LireTexte(valeur);
    }

    public static string Capitaliser(string texte)
    {
        var bas = texte.Trim().ToLower(CultureInfo.GetCultureInfo("fr-FR"));
        return bas.Length == 0 ? bas : char.ToUpper(bas[0], CultureInfo.GetCultureInfo("fr-FR")) + bas[1..];
    }

    // ── Interprétation d'une ligne ───────────────────────────────────────────────────────────

    /// <summary>Valeurs d'une ligne du fichier, prêtes à comparer à la fiche (null = vide dans le fichier).</summary>
    public sealed class Valeurs
    {
        public string? NumeroOrdre { get; set; }
        public string? Agents { get; set; }
        public DateOnly? DateAnciennete { get; set; }
        public DateOnly? DateEmbauche { get; set; }
        public DateOnly? DateSortie { get; set; }
        public string? Poste { get; set; }
        public string? TypeContrat { get; set; }
        public string? Nationalite { get; set; }
        public string? VilleNaissance { get; set; }
        public string? Sexe { get; set; }
        public string? SituationFamiliale { get; set; }
        public int? NombreEnfants { get; set; }
        public string? Cin { get; set; }
        public string? ImmatriculationCnss { get; set; }
        public DateOnly? DateNaissance { get; set; }
        public string? Adresse { get; set; }
        public string? Telephone1 { get; set; }
        public string? TelephoneUrgence { get; set; }
        public string? RelationUrgence { get; set; }
        public string? NumeroBox { get; set; }
        /// <summary>v64 : « Date d'Entrée » (fichier des entrepreneurs).</summary>
        public DateOnly? DateEntree { get; set; }
        public string? CarteImmatriculation { get; set; }
        public string? NumeroPasseport { get; set; }
        /// <summary>v64 : N° d'inscription au registre national = N° de carte d'auto-entrepreneur.</summary>
        public string? RegistreNational { get; set; }
        /// <summary>v64 : type d'employé lu dans le fichier ou déduit de ses colonnes (null = inconnu).</summary>
        public string? TypeEmploye { get; set; }
        /// <summary>v66 : e-mail personnel.</summary>
        public string? EmailPersonnel { get; set; }

        /// <summary>Valeurs refusées (non appliquées) : à corriger dans le fichier.</summary>
        public List<string> Anomalies { get; } = [];
        /// <summary>Valeurs appliquées mais à vérifier.</summary>
        public List<string> Avertissements { get; } = [];

        /// <summary>Ligne sans agent (« BOX DIRECTION ») ou vide : ignorée.</summary>
        public bool SansAgent =>
            string.IsNullOrWhiteSpace(Agents) || NormaliserTexte(Agents).StartsWith("BOX ", StringComparison.Ordinal);

        public bool Sorti(DateOnly aujourdhui) => DateSortie is DateOnly s && s <= aujourdhui;
    }

    /// <summary>Lit une ligne (clé de colonne → valeur brute de la cellule) et contrôle sa cohérence.</summary>
    public static Valeurs Interpreter(IReadOnlyDictionary<string, object?> brut, DateOnly aujourdhui)
    {
        object? V(string cle) => brut.TryGetValue(cle, out var v) ? v : null;
        var r = new Valeurs
        {
            NumeroOrdre = LireNumeroOrdre(V(CleNumeroOrdre)),
            Agents = LireTexte(V(CleAgents)),
            Poste = LireTexte(V("poste")),
            TypeContrat = LireTexte(V("typeContrat")),
            VilleNaissance = LireTexte(V("villeNaissance")),
            Adresse = LireTexte(V("adresse")),
            Telephone1 = LireTexte(V("telephone1")),
            NumeroBox = LireTexte(V(CleBox)),
            CarteImmatriculation = LireTexte(V("carteImmatriculation"))?.Replace(" ", string.Empty).ToUpperInvariant(),
            NumeroPasseport = LireTexte(V("passeport"))?.Replace(" ", string.Empty).ToUpperInvariant(),
            RegistreNational = LireTexte(V("registreNational"))?.Replace(" ", string.Empty),
        };

        // v66 : e-mail personnel — une valeur qui n'a pas la forme d'une adresse est signalée, jamais appliquée.
        var email = LireTexte(V("emailPersonnel"))?.Replace(" ", string.Empty);
        if (email is not null)
        {
            if (EmailValide(email)) r.EmailPersonnel = email.ToLowerInvariant();
            else r.Anomalies.Add($"E-mail personnel « {email} » non reconnu (adresse attendue) : non appliqué");
        }

        var nationalite = LireTexte(V("nationalite"));
        r.Nationalite = nationalite is null ? null : Capitaliser(nationalite);

        var cin = LireTexte(V("cin"));
        if (cin is not null)
        {
            r.Cin = cin.Replace(" ", string.Empty).ToUpperInvariant();
            if (!CinValide().IsMatch(r.Cin))
                r.Avertissements.Add($"CIN « {r.Cin} » de forme inhabituelle (à vérifier)");
        }

        void Date(string cle, string libelle, Action<DateOnly?> poser)
        {
            var (d, anomalie) = LireDate(V(cle));
            if (anomalie is not null) r.Anomalies.Add($"{libelle} : {anomalie}");
            else poser(d);
        }
        Date("dateAnciennete", "Date d'ancienneté", d => r.DateAnciennete = d);
        Date("dateEmbauche", "Date d'embauche", d => r.DateEmbauche = d);
        Date("dateSortie", "Date de sortie", d => r.DateSortie = d);
        Date("dateEntree", "Date d'entrée", d => r.DateEntree = d);

        // Date de naissance et n° d'immatriculation inversés (un nombre en naissance, une date en immatriculation).
        var naissanceBrute = V("dateNaissance");
        var immatBrute = V("immatriculationCnss");
        var inverses = immatBrute is DateTime && naissanceBrute is double or int or long;
        if (inverses)
        {
            r.Anomalies.Add("Date de naissance et N° d'immatriculation semblent inversés : aucun des deux n'est appliqué");
        }
        else
        {
            Date("dateNaissance", "Date de naissance", d => r.DateNaissance = d);
            if (immatBrute is DateTime)
            {
                r.Anomalies.Add("N° d'immatriculation : une date a été saisie à la place du numéro");
            }
            else
            {
                r.ImmatriculationCnss = LireNumero(immatBrute);
                if (r.ImmatriculationCnss is { Length: < 6 or > 12 } court)
                    r.Avertissements.Add($"N° d'immatriculation « {court} » de longueur inhabituelle (à vérifier)");
            }
        }

        var (sexe, aSexe) = LireSexe(V("sexe"));
        if (aSexe is not null) r.Anomalies.Add(Capitaliser(aSexe)); else r.Sexe = sexe;

        var (situation, enfants, aSituation) = LireSituation(V("situationFamiliale"));
        if (aSituation is not null) r.Anomalies.Add(Capitaliser(aSituation));
        else { r.SituationFamiliale = situation; r.NombreEnfants = enfants; }

        var (numero, relation) = LireTelephoneUrgence(V("telephone2"));
        r.TelephoneUrgence = numero;
        r.RelationUrgence = relation;

        // v64 : type d'employé — colonne du fichier, sinon déduit : une inscription au registre national
        // (carte d'auto-entrepreneur) désigne un sous-traitant ; un N° CNSS ou un type de contrat, un salarié.
        var typeLu = LireTexte(V("typeEmploye"));
        if (typeLu is not null)
        {
            r.TypeEmploye = TypesEmploye.Normaliser(typeLu);
            if (r.TypeEmploye is null)
                r.Anomalies.Add($"Type d'employé « {typeLu} » non reconnu (Salarié ou Sous-traitant attendu)");
        }
        r.TypeEmploye ??= r.RegistreNational is not null ? TypesEmploye.SousTraitant
            : r.ImmatriculationCnss is not null || r.TypeContrat is not null ? TypesEmploye.Salarie
            : null;

        // Cohérence entre dates.
        if (r.DateNaissance is DateOnly n)
        {
            var reference = r.DateEmbauche ?? r.DateEntree ?? r.DateAnciennete ?? aujourdhui;
            var age = reference.Year - n.Year - (reference < n.AddYears(reference.Year - n.Year) ? 1 : 0);
            if (age is < 15 or > 75)
            {
                r.Anomalies.Add($"Date de naissance {n:dd/MM/yyyy} incohérente (âge de {age} ans à l'embauche) : non appliquée");
                r.DateNaissance = null;
            }
        }
        if (r.DateSortie is DateOnly s && (r.DateEmbauche ?? r.DateEntree ?? r.DateAnciennete) is DateOnly debut && s < debut)
        {
            r.Anomalies.Add($"Date de sortie {s:dd/MM/yyyy} antérieure à l'embauche {debut:dd/MM/yyyy} : non appliquée");
            r.DateSortie = null;
        }
        if (r.DateAnciennete is DateOnly anc && r.DateEmbauche is DateOnly emb && anc > emb)
            r.Avertissements.Add($"Date d'ancienneté {anc:dd/MM/yyyy} postérieure à l'embauche {emb:dd/MM/yyyy} (à vérifier)");

        // Longueurs admises par la fiche et l'annuaire : une valeur trop longue est refusée, jamais tronquée.
        string? Borne(string? valeur, int max, string libelle)
        {
            if (valeur is null || valeur.Length <= max) return valeur;
            r.Anomalies.Add($"{libelle} : valeur trop longue ({valeur.Length} caractères, {max} au plus) — non appliquée");
            return null;
        }
        r.NumeroOrdre = Borne(r.NumeroOrdre, 50, "N° d'ordre");
        r.Poste = Borne(r.Poste, 160, "Nomination de poste");
        r.TypeContrat = Borne(r.TypeContrat, 60, "Type de contrat");
        r.Nationalite = Borne(r.Nationalite, 100, "Nationalité");
        r.VilleNaissance = Borne(r.VilleNaissance, 128, "Ville de naissance");
        r.Cin = Borne(r.Cin, 32, "CIN");
        r.ImmatriculationCnss = Borne(r.ImmatriculationCnss, 50, "N° d'immatriculation");
        r.Adresse = Borne(r.Adresse, 512, "Adresse");
        r.Telephone1 = Borne(r.Telephone1, 32, "TEL 1");
        r.TelephoneUrgence = Borne(r.TelephoneUrgence, 32, "TEL 2");
        r.RelationUrgence = Borne(r.RelationUrgence, 100, "Relation (TEL 2)");
        r.NumeroBox = Borne(r.NumeroBox, 40, "N° BOX");
        r.CarteImmatriculation = Borne(r.CarteImmatriculation, 32, "Carte d'immatriculation");
        r.NumeroPasseport = Borne(r.NumeroPasseport, 32, "N° de passeport");
        r.RegistreNational = Borne(r.RegistreNational, 64, "N° d'inscription au registre national");
        r.EmailPersonnel = Borne(r.EmailPersonnel, 256, "E-mail personnel");
        return r;
    }

    /// <summary>v66 : forme d'une adresse e-mail (un « @ », un domaine avec un point, sans espace).</summary>
    public static bool EmailValide(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && System.Text.RegularExpressions.Regex.IsMatch(email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    // ── Champs de la fiche : lecture, écriture, comparaison ─────────────────────────────────

    /// <summary>Champ de la fiche alimenté par le fichier (valeurs comparées en texte normalisé).</summary>
    public sealed record Champ(
        string Cle,
        string Libelle,
        Func<UserHrProfile, string?> Lire,
        Action<UserHrProfile, string?> Ecrire,
        Func<Valeurs, string?> Proposer);

    private static string? D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly? VersDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private static string? Vide(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static readonly IReadOnlyList<Champ> Champs =
    [
        new("immatriculationInterne", "N° d'ordre (matricule)", p => p.ImmatriculationInterne, (p, v) => p.ImmatriculationInterne = Vide(v), v => v.NumeroOrdre),
        new("dateAnciennete", "Date d'ancienneté", p => D(p.DateAnciennete), (p, v) => p.DateAnciennete = VersDate(v), v => D(v.DateAnciennete)),
        new("dateEmbauche", "Date d'embauche", p => D(p.DateEmbauche), (p, v) => p.DateEmbauche = VersDate(v), v => D(v.DateEmbauche)),
        new("dateSortie", "Date de sortie", p => D(p.DateSortie), (p, v) => p.DateSortie = VersDate(v), v => D(v.DateSortie)),
        new("poste", "Nomination de poste", p => p.Poste, (p, v) => p.Poste = Vide(v), v => v.Poste),
        new("typeContrat", "Type de contrat", p => p.TypeContrat, (p, v) => p.TypeContrat = Vide(v), v => v.TypeContrat),
        new("nationalite", "Nationalité", p => p.Nationalite, (p, v) => p.Nationalite = Vide(v), v => v.Nationalite),
        new("villeNaissance", "Ville de naissance", p => p.VilleNaissance, (p, v) => p.VilleNaissance = Vide(v), v => v.VilleNaissance),
        new("sexe", "Sexe", p => p.Sexe, (p, v) => p.Sexe = Vide(v), v => v.Sexe),
        new("situationFamiliale", "Situation familiale", p => p.SituationFamiliale, (p, v) => p.SituationFamiliale = Vide(v), v => v.SituationFamiliale),
        new("nombreEnfants", "Nombre d'enfants",
            p => p.NombreEnfants?.ToString(CultureInfo.InvariantCulture),
            (p, v) => p.NombreEnfants = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null,
            v => v.NombreEnfants?.ToString(CultureInfo.InvariantCulture)),
        new("cin", "CIN", p => p.Cin, (p, v) => p.Cin = Vide(v), v => v.Cin),
        new("immatriculationCnss", "N° d'immatriculation", p => p.ImmatriculationCnss, (p, v) => p.ImmatriculationCnss = Vide(v), v => v.ImmatriculationCnss),
        new("dateNaissance", "Date de naissance", p => D(p.DateNaissance), (p, v) => p.DateNaissance = VersDate(v), v => D(v.DateNaissance)),
        new("adresse", "Adresse", p => p.Adresse, (p, v) => p.Adresse = Vide(v), v => v.Adresse),
        new("telephone1", "TEL 1", p => p.Telephone1, (p, v) => p.Telephone1 = Vide(v), v => v.Telephone1),
        new("telephoneUrgence", "TEL 2 (urgence)", p => p.TelephoneUrgence, (p, v) => p.TelephoneUrgence = Vide(v), v => v.TelephoneUrgence),
        new("relationUrgence", "Relation (TEL 2)", p => p.RelationUrgence, (p, v) => p.RelationUrgence = Vide(v), v => v.RelationUrgence),
        new("numeroBox", "N° BOX", p => p.NumeroBox, (p, v) => p.NumeroBox = Vide(v), v => v.NumeroBox),
        // v66 : e-mail personnel (fiche RH) — vérifié et mis à jour comme les autres champs.
        new("emailPersonnel", "E-mail personnel", p => p.EmailPersonnel, (p, v) => p.EmailPersonnel = Vide(v), v => v.EmailPersonnel),
        // v64 : fichier des entrepreneurs et type d'employé.
        new("dateEntree", "Date d'entrée", p => D(p.DateEntree), (p, v) => p.DateEntree = VersDate(v), v => D(v.DateEntree)),
        new("carteImmatriculation", "Carte d'immatriculation", p => p.CarteImmatriculation, (p, v) => p.CarteImmatriculation = Vide(v), v => v.CarteImmatriculation),
        new("numeroPasseport", "N° de passeport", p => p.NumeroPasseport, (p, v) => p.NumeroPasseport = Vide(v), v => v.NumeroPasseport),
        new("registreNational", "N° d'inscription au registre national (carte d'auto-entrepreneur)",
            p => p.NumeroCarteAutoentrepreneur, (p, v) => p.NumeroCarteAutoentrepreneur = Vide(v), v => v.RegistreNational),
        new("typeEmploye", "Type d'employé",
            p => p.TypeEmploye is null ? null : TypesEmploye.LibelleDe(TypesEmploye.Code(p)),
            (p, v) => p.TypeEmploye = TypesEmploye.Normaliser(v),
            v => v.TypeEmploye is null ? null : TypesEmploye.LibelleDe(v.TypeEmploye)),
    ];

    /// <summary>Champs complétés seulement s'ils sont vides (jamais remplacés).</summary>
    public static readonly IReadOnlyList<Champ> ChampsSiVides =
    [
        // Sans colonne « Date d'Entrée » : la date d'entrée vide reprend l'ancienneté (ou l'embauche).
        new("dateEntree", "Date d'entrée", p => D(p.DateEntree), (p, v) => p.DateEntree = VersDate(v),
            v => v.DateEntree is null ? D(v.DateAnciennete ?? v.DateEmbauche) : null),
    ];

    public static string? Libelle(string cle) =>
        Champs.Concat(ChampsSiVides).FirstOrDefault(c => c.Cle == cle)?.Libelle
        ?? (cle == "ancienPoste" ? "Ancien poste" : null);

    /// <summary>Une modification à appliquer à une fiche.</summary>
    public sealed record Modification(string Cle, string Libelle, string? Ancienne, string? Nouvelle);

    /// <summary>
    /// Modifications d'une fiche : toute valeur non vide du fichier qui diffère de la fiche la remplace ;
    /// une case vide du fichier ne vide jamais la fiche. Un changement de poste garde l'ancien poste.
    /// </summary>
    public static List<Modification> Comparer(UserHrProfile? fiche, Valeurs v)
    {
        var actuelle = fiche ?? new UserHrProfile();
        var modifs = new List<Modification>();
        foreach (var c in Champs)
        {
            var nouvelle = Vide(c.Proposer(v));
            if (nouvelle is null) continue;
            var ancienne = Vide(c.Lire(actuelle));
            if (!Egales(ancienne, nouvelle))
                modifs.Add(new Modification(c.Cle, c.Libelle, ancienne, nouvelle));
        }
        foreach (var c in ChampsSiVides)
        {
            var nouvelle = Vide(c.Proposer(v));
            if (nouvelle is not null && Vide(c.Lire(actuelle)) is null)
                modifs.Add(new Modification(c.Cle, c.Libelle, null, nouvelle));
        }
        var poste = modifs.FirstOrDefault(m => m.Cle == "poste");
        if (poste is { Ancienne: not null } && !Egales(actuelle.AncienPoste, poste.Ancienne))
            modifs.Add(new Modification("ancienPoste", "Ancien poste", Vide(actuelle.AncienPoste), poste.Ancienne));
        return modifs;
    }

    /// <summary>Même valeur, à la casse, aux accents et aux espaces près.</summary>
    public static bool Egales(string? a, string? b) =>
        NormaliserTexte(a) == NormaliserTexte(b) && (a is null) == (b is null);

    /// <summary>Applique une valeur (texte normalisé) au champ de la fiche.</summary>
    public static void Ecrire(UserHrProfile fiche, string cle, string? valeur)
    {
        if (cle == "ancienPoste") { fiche.AncienPoste = Vide(valeur); return; }
        var champ = Champs.Concat(ChampsSiVides).FirstOrDefault(c => c.Cle == cle)
                    ?? throw new ArgumentException($"Champ inconnu : {cle}", nameof(cle));
        champ.Ecrire(fiche, valeur);
    }

    public static string? Lire(UserHrProfile fiche, string cle) =>
        cle == "ancienPoste" ? Vide(fiche.AncienPoste)
        : Vide(Champs.Concat(ChampsSiVides).FirstOrDefault(c => c.Cle == cle)?.Lire(fiche));

    /// <summary>
    /// Champs obligatoires encore vides (libellés), pour une fiche d'employé actif. v64 : la liste
    /// dépend du type d'employé (salarié / sous-traitant).
    /// </summary>
    public static List<string> ChampsManquants(UserHrProfile? fiche)
    {
        var f = fiche ?? new UserHrProfile();
        return ChampsObligatoiresPour(TypesEmploye.Code(f))
            .Where(cle => cle == ChampPieceIdentite
                ? Vide(f.Cin) is null && Vide(f.CarteImmatriculation) is null && Vide(f.NumeroPasseport) is null
                : Lire(f, cle) is null)
            .Select(cle => cle == ChampPieceIdentite ? LibellePieceIdentite : Libelle(cle) ?? cle)
            .ToList();
    }

    /// <summary>Champs obligatoires vides d'une ligne du fichier (contrôle des nouveaux entrants).</summary>
    public static List<string> ChampsManquants(Valeurs v)
    {
        var fiche = new UserHrProfile();
        foreach (var c in Champs.Concat(ChampsSiVides))
        {
            var valeur = c.Proposer(v);
            if (valeur is not null) c.Ecrire(fiche, valeur);
        }
        return ChampsManquants(fiche);
    }

    /// <summary>
    /// Rôle donné à une fiche créée depuis le fichier, d'après l'intitulé du poste
    /// (modifiable ensuite sur la fiche). Par défaut : « Employee ».
    /// </summary>
    public static string RoleSuggere(string? poste)
    {
        var n = NormaliserTexte(poste);
        if (n.Contains("CHEF DE PROJET", StringComparison.Ordinal)) return "Chef de projet";
        if (n.Contains("SUPERVIS", StringComparison.Ordinal)) return "Superviseur";
        if (n.Contains("REFERENT", StringComparison.Ordinal)) return "Référent technique";
        if (n.Contains("PILOTE", StringComparison.Ordinal)) return "Pilote";
        if (n.Contains("COACH", StringComparison.Ordinal)) return "Coach";
        if (n.Contains("QUALITICIEN", StringComparison.Ordinal)) return "Qualiticien";
        return "Employee";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espaces();

    [GeneratedRegex(@"^\s*([CMDVcmdv])\s*(?:\+\s*(\d{1,2}))?\s*$")]
    private static partial Regex SituationCourte();

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex Parentheses();

    [GeneratedRegex(@"\+?\d[\d\s.\-]{6,}\d")]
    private static partial Regex Telephone();

    [GeneratedRegex(@"^[A-Z]{1,3}\d{3,8}$")]
    private static partial Regex CinValide();
}
