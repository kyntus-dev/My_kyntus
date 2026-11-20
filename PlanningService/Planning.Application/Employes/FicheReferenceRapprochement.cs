namespace Planning.Application.Employes;

/// <summary>
/// v63 — Rapprochement des lignes du fichier RH de référence avec les fiches existantes.
///
/// L'identifiant est le nom de l'employé, normalisé (casse, accents, espaces, ordre nom / prénom).
/// Le N° d'ordre du fichier, s'il est déjà porté par une fiche, sert à départager des homonymes.
/// Règle de prudence : dès qu'un doute existe (homonymes, nom approchant, N° d'ordre contradictoire,
/// deux lignes visant la même fiche), la ligne n'est pas appliquée d'office — elle est signalée et
/// la personne qui importe choisit la fiche à mettre à jour.
/// </summary>
public static class FicheReferenceRapprochement
{
    public const string Identifie = "identifie";
    public const string AVerifier = "a-verifier";
    public const string Ambigu = "ambigu";
    public const string Nouveau = "nouveau";
    public const string SortiInconnu = "sorti-inconnu";
    public const string Historique = "historique";
    public const string Ignore = "ignore";

    public sealed record Ligne(int Numero, string? NomComplet, string? NumeroOrdre, bool Sorti, DateOnly? DateEmbauche);

    public sealed record Agent(int UserId, string Nom, string Prenom, string? Matricule, bool Actif);

    public sealed record Candidat(int UserId, string Nom, string Prenom, string? Matricule, bool Actif, string Raison);

    public sealed record Resultat(int Numero, string Statut, int? UserId, string? Message, IReadOnlyList<Candidat> Candidats);

    public static IReadOnlyList<Resultat> Rapprocher(IReadOnlyList<Ligne> lignes, IReadOnlyList<Agent> agents)
    {
        var parCle = agents
            .GroupBy(a => FicheReference.CleNom(a.Nom, a.Prenom))
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => g.ToList());
        var parMatricule = agents
            .Where(a => !string.IsNullOrWhiteSpace(a.Matricule))
            .GroupBy(a => NormaliserMatricule(a.Matricule))
            .ToDictionary(g => g.Key, g => g.ToList());

        var resultats = new Dictionary<int, Resultat>();
        var homonymesFichier = new HashSet<int>();

        // 1. Lignes sans agent (« BOX DIRECTION », lignes vides) : ignorées.
        var utiles = new List<Ligne>();
        foreach (var l in lignes)
        {
            if (FicheReference.CleNom(l.NomComplet).Length == 0
                || FicheReference.NormaliserTexte(l.NomComplet).StartsWith("BOX ", StringComparison.Ordinal))
                resultats[l.Numero] = new Resultat(l.Numero, Ignore, null, "Ligne sans employé (intitulé de box ou ligne vide).", []);
            else
                utiles.Add(l);
        }

        // 2. Même nom plusieurs fois dans le fichier : une ancienne embauche (sortie) suivie d'une
        //    réembauche n'est qu'un historique ; deux lignes actives sont des homonymes à départager
        //    par le N° d'ordre.
        var principales = new List<Ligne>();
        foreach (var groupe in utiles.GroupBy(l => FicheReference.CleNom(l.NomComplet)))
        {
            var g = groupe.ToList();
            if (g.Count == 1) { principales.Add(g[0]); continue; }
            var actives = g.Where(l => !l.Sorti).ToList();
            if (actives.Count == 1)
            {
                principales.Add(actives[0]);
                foreach (var h in g.Where(l => l != actives[0]))
                    resultats[h.Numero] = new Resultat(h.Numero, Historique, null,
                        $"Ancienne embauche (sortie) du même employé : la ligne {actives[0].Numero} est la situation en cours.", []);
                continue;
            }
            if (actives.Count == 0)
            {
                var derniere = g.OrderByDescending(l => l.DateEmbauche ?? DateOnly.MinValue).ThenByDescending(l => l.Numero).First();
                principales.Add(derniere);
                foreach (var h in g.Where(l => l != derniere))
                    resultats[h.Numero] = new Resultat(h.Numero, Historique, null,
                        $"Embauche plus ancienne du même employé : la ligne {derniere.Numero} est la plus récente.", []);
                continue;
            }
            // Plusieurs lignes actives au même nom : chacune doit être départagée par son N° d'ordre.
            foreach (var l in g.Where(x => x.Sorti))
                resultats[l.Numero] = new Resultat(l.Numero, Historique, null,
                    "Ancienne embauche (sortie) d'un employé dont le nom apparaît plusieurs fois.", []);
            principales.AddRange(actives);
            foreach (var l in actives) homonymesFichier.Add(l.Numero);
        }

        // 3. Rapprochement de chaque ligne retenue.
        foreach (var l in principales)
            resultats[l.Numero] = RapprocherLigne(l, parCle, parMatricule, agents, homonymesFichier.Contains(l.Numero));

        // 4. Deux lignes ne peuvent pas mettre à jour la même fiche.
        foreach (var conflit in resultats.Values
                     .Where(r => r.UserId is not null && r.Statut is Identifie or AVerifier)
                     .GroupBy(r => r.UserId!.Value)
                     .Where(g => g.Count() > 1))
        {
            var numeros = string.Join(", ", conflit.Select(r => r.Numero).OrderBy(n => n));
            foreach (var r in conflit)
                resultats[r.Numero] = r with
                {
                    Statut = Ambigu,
                    UserId = null,
                    Message = $"Les lignes {numeros} désignent la même fiche : choisissez la bonne fiche pour chacune.",
                    Candidats = r.Candidats.Count > 0 ? r.Candidats : CandidatsDe(agents.Where(a => a.UserId == conflit.Key), "même fiche visée"),
                };
        }

        return resultats.Values.OrderBy(r => r.Numero).ToList();
    }

    private static Resultat RapprocherLigne(
        Ligne l,
        Dictionary<string, List<Agent>> parCle,
        Dictionary<string, List<Agent>> parMatricule,
        IReadOnlyList<Agent> agents,
        bool homonymeDansLeFichier)
    {
        var cle = FicheReference.CleNom(l.NomComplet);
        var matricule = NormaliserMatricule(l.NumeroOrdre);
        var memeNom = parCle.TryGetValue(cle, out var liste) ? liste : [];
        var memeMatricule = matricule.Length > 0 && parMatricule.TryGetValue(matricule, out var m) ? m : [];

        // a. Un seul employé porte ce nom.
        if (memeNom.Count == 1)
        {
            var a = memeNom[0];
            var matriculeFiche = NormaliserMatricule(a.Matricule);
            if (homonymeDansLeFichier)
                return matricule.Length > 0 && matriculeFiche == matricule
                    ? new Resultat(l.Numero, Identifie, a.UserId, "Homonymes du fichier départagés par le N° d'ordre.", CandidatsDe([a], "même nom et même N° d'ordre"))
                    : new Resultat(l.Numero, Ambigu, null,
                        "Ce nom figure sur plusieurs lignes actives du fichier : choisissez la fiche correspondant à cette ligne (ou créez-en une).",
                        CandidatsDe(memeNom, "même nom"));
            if (matricule.Length > 0 && matriculeFiche.Length > 0 && matriculeFiche != matricule)
                return new Resultat(l.Numero, AVerifier, a.UserId,
                    $"Même nom, mais N° d'ordre différent (fiche : {a.Matricule}, fichier : {l.NumeroOrdre}) : confirmez qu'il s'agit bien de la même personne.",
                    CandidatsDe([a], "même nom, N° d'ordre différent"));
            return new Resultat(l.Numero, Identifie, a.UserId, null, CandidatsDe([a], "même nom"));
        }

        // b. Homonymes dans l'application : le N° d'ordre départage.
        if (memeNom.Count > 1)
        {
            var parNumero = memeNom.Where(a => matricule.Length > 0 && NormaliserMatricule(a.Matricule) == matricule).ToList();
            if (parNumero.Count == 1)
                return new Resultat(l.Numero, Identifie, parNumero[0].UserId,
                    "Homonymes départagés par le N° d'ordre.", CandidatsDe(memeNom, "même nom"));
            return new Resultat(l.Numero, Ambigu, null,
                $"{memeNom.Count} employés portent ce nom : choisissez la fiche à mettre à jour.",
                CandidatsDe(memeNom, "même nom"));
        }

        // c. Aucun nom identique : nom approchant ou même N° d'ordre → à vérifier, jamais appliqué d'office.
        var approchants = agents
            .Where(a => NomsApprochants(cle, FicheReference.CleNom(a.Nom, a.Prenom)))
            .Select(a => (a, raison: "nom approchant"))
            .Concat(memeMatricule.Select(a => (a, raison: "même N° d'ordre")))
            .GroupBy(x => x.a.UserId)
            .Select(g => (g.First().a, raison: string.Join(", ", g.Select(x => x.raison).Distinct())))
            .ToList();
        if (approchants.Count > 0)
        {
            var candidats = approchants
                .Select(x => new Candidat(x.a.UserId, x.a.Nom, x.a.Prenom, x.a.Matricule, x.a.Actif, x.raison))
                .ToList();
            return new Resultat(l.Numero, approchants.Count == 1 ? AVerifier : Ambigu,
                approchants.Count == 1 ? approchants[0].a.UserId : null,
                approchants.Count == 1
                    ? "Aucun nom identique ; une fiche proche existe : confirmez qu'il s'agit de la même personne."
                    : "Aucun nom identique ; plusieurs fiches proches : choisissez la bonne fiche ou créez-en une.",
                candidats);
        }

        // d. Inconnu.
        if (l.Sorti)
            return new Resultat(l.Numero, SortiInconnu, null,
                "Employé sorti, sans fiche dans l'application : aucune fiche n'est créée.", []);
        return new Resultat(l.Numero, Nouveau, null, "Aucune fiche à ce nom : une fiche peut être créée.", []);
    }

    private static List<Candidat> CandidatsDe(IEnumerable<Agent> agents, string raison) =>
        agents.Select(a => new Candidat(a.UserId, a.Nom, a.Prenom, a.Matricule, a.Actif, raison)).ToList();

    public static string NormaliserMatricule(string? matricule)
    {
        var n = FicheReference.NormaliserTexte(matricule).Replace(" ", string.Empty);
        // « 000123 » et « 123 » désignent le même N° d'ordre.
        if (n.Length == 0 || !n.All(char.IsDigit)) return n;
        var t = n.TrimStart('0');
        return t.Length > 0 ? t : "0";
    }

    /// <summary>
    /// Noms proches : mêmes mots à une faute de frappe près (mots de 4 lettres ou plus), ou l'un des
    /// noms contient tous les mots de l'autre (prénom composé, second nom omis…), avec au moins 2 mots communs.
    /// </summary>
    public static bool NomsApprochants(string cleA, string cleB)
    {
        if (cleA.Length == 0 || cleB.Length == 0 || cleA == cleB) return false;
        var a = cleA.Split(' ');
        var b = cleB.Split(' ');
        if (a.Length == b.Length && a.Zip(b).All(p => p.First == p.Second || (Math.Min(p.First.Length, p.Second.Length) >= 4 && Distance(p.First, p.Second) <= 1)))
            return true;
        if (a.Length != b.Length)
        {
            var (petit, grand) = a.Length < b.Length ? (a, b) : (b, a);
            if (petit.Length >= 2 && petit.All(grand.Contains)) return true;
        }
        // Particules attachées ou détachées (« ELHASSANI » / « EL HASSANI », « BENALI » / « BEN ALI »).
        var sa = SansParticules(a);
        var sb = SansParticules(b);
        return sa.Contains(' ') && sa == sb;
    }

    private static readonly string[] Particules = ["EL", "AL", "BEN", "BEL", "BENT", "AIT", "OULD", "ABOU", "ABD", "ABDEL"];

    private static string SansParticules(string[] mots) =>
        string.Join(' ', mots
            .Where(m => !Particules.Contains(m))
            .Select(m => Particules.Where(p => m.Length - p.Length >= 3 && m.StartsWith(p, StringComparison.Ordinal))
                                   .Select(p => m[p.Length..]).FirstOrDefault() ?? m)
            .OrderBy(m => m, StringComparer.Ordinal));

    public static int Distance(string s, string t)
    {
        var d = new int[s.Length + 1, t.Length + 1];
        for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++) d[0, j] = j;
        for (var i = 1; i <= s.Length; i++)
            for (var j = 1; j <= t.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (s[i - 1] == t[j - 1] ? 0 : 1));
        return d[s.Length, t.Length];
    }
}
