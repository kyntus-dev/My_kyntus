using Planning.Domain.Entities;
using Planning.Domain.Enums;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v64 — Rotation équitable des heures de fin de journée.
/// <para>
/// Après la génération (quotas, niveaux, alternance), les horaires sont échangés deux à deux entre
/// agents du même jour, du même mode et du même niveau, pour que les fins tardives tournent : celui qui
/// a déjà cumulé le plus de fins tardives (8 semaines d'historique + semaine en cours) cède son horaire
/// tardif à celui qui en a eu le moins. Un échange ne change ni les quotas par créneau, ni l'équilibre
/// des niveaux ; il est refusé s'il crée deux jours de suite sur le même horaire, s'il dépasse le
/// maximum d'un même horaire dans la semaine, ou s'il touche une journée figée, corrigée à la main,
/// une demande exceptionnelle, un renfort, un switch de mode ou un agent en formation plateau
/// (jamais ouverture / fermeture).
/// </para>
/// <para>
/// Charge d'un agent = somme des écarts (en heures) entre son heure de fin et l'heure de fin moyenne
/// des horaires de la semaine. Un échange n'est fait que s'il réduit strictement l'écart entre les deux
/// agents (L_x − L_y &gt; δ), ce qui garantit l'arrêt et évite de favoriser toujours les mêmes.
/// </para>
/// </summary>
public static class RotationFinJournee
{
    private const int PassesMax = 12;

    /// <summary>Heure de fin d'un horaire, en minutes depuis minuit (pause comprise).</summary>
    public static double FinMinutes(SubServiceShiftConfig c) =>
        c.StartTime.Hour * 60 + c.StartTime.Minute + c.WorkHours * 60 + Math.Max(0, c.BreakDurationMinutes);

    /// <summary>Poids d'un horaire : écart (heures) entre sa fin et la fin moyenne des horaires donnés.</summary>
    public static Dictionary<int, double> Poids(IEnumerable<SubServiceShiftConfig> configs)
    {
        var liste = configs.ToList();
        if (liste.Count == 0) return new Dictionary<int, double>();
        var moyenne = liste.Average(FinMinutes);
        return liste.ToDictionary(c => c.Id, c => (FinMinutes(c) - moyenne) / 60.0);
    }

    /// <summary>Journée qu'aucun échange ne doit toucher.</summary>
    public static bool Figee(ShiftAssignment a) =>
        a.IsManagerOverride || a.IsExceptionalRequest || a.IsModeOverride || a.IsReinforcement
        || a.IsHalfDaySaturday || a.IsSaturday || a.IsOnLeave || a.IsHoliday || a.SubServiceShiftConfigId is null;

    /// <summary>
    /// Équilibre les fins de journée des <paramref name="aTraiter"/> (journées régénérées, lundi-vendredi).
    /// <paramref name="semaine"/> : toutes les journées de la semaine (régénérées et figées) pour les règles
    /// d'alternance ; <paramref name="historique"/> : charge passée par agent. Retourne le nombre d'échanges.
    /// </summary>
    public static int Equilibrer(
        IReadOnlyList<ShiftAssignment> aTraiter,
        IReadOnlyList<ShiftAssignment> semaine,
        IReadOnlyDictionary<int, SubServiceShiftConfig> configs,
        IReadOnlyDictionary<int, User> agents,
        IReadOnlyDictionary<int, double> historique)
    {
        var poids = Poids(configs.Values.Where(c => !c.AppliesToSaturday));
        if (poids.Count < 2) return 0;

        double W(ShiftAssignment a) =>
            a.SubServiceShiftConfigId is int id && poids.TryGetValue(id, out var w) ? w : 0;

        // Charge : historique + semaine en cours (jours ouvrés travaillés).
        var charge = new Dictionary<int, double>();
        foreach (var a in semaine.Where(x => !x.IsSaturday && !x.IsOnLeave && !x.IsHoliday && x.SubServiceShiftConfigId != null))
            charge[a.UserId] = charge.GetValueOrDefault(a.UserId) + W(a);
        foreach (var (uid, h) in historique)
            charge[uid] = charge.GetValueOrDefault(uid) + h;

        var parAgentJour = semaine
            .Where(x => !x.IsSaturday)
            .GroupBy(x => (x.UserId, x.AssignedDate))
            .ToDictionary(g => g.Key, g => g.First());
        var shiftsParMode = configs.Values
            .Where(c => !c.AppliesToSaturday)
            .GroupBy(c => c.ShiftModeProfileId ?? 0)
            .ToDictionary(g => g.Key, g => g.Count());

        int MaxMeme(int? modeId, int joursTravailles) =>
            ShiftDispersionSelector.EffectiveMaxSame(shiftsParMode.GetValueOrDefault(modeId ?? 0, configs.Count), joursTravailles);

        bool Respecte(int userId, DateOnly jour, int nouveauConfig, int? modeId)
        {
            // Pas deux jours de suite sur le même horaire.
            foreach (var voisin in new[] { jour.AddDays(-1), jour.AddDays(1) })
            {
                if (parAgentJour.TryGetValue((userId, voisin), out var v) && v.SubServiceShiftConfigId == nouveauConfig
                    && !v.IsOnLeave && !v.IsHoliday)
                    return false;
            }
            // Maximum d'un même horaire dans la semaine.
            var siens = semaine.Where(x => x.UserId == userId && !x.IsSaturday && !x.IsOnLeave && !x.IsHoliday
                                           && x.SubServiceShiftConfigId != null).ToList();
            var meme = siens.Count(x => x.AssignedDate != jour && x.SubServiceShiftConfigId == nouveauConfig) + 1;
            return meme <= MaxMeme(modeId, siens.Count);
        }

        bool PlateauOk(int userId, int configId) =>
            !(agents.TryGetValue(userId, out var u) && u.IsPlateauTraining
              && configs.TryGetValue(configId, out var c) && c.ShiftKind is ShiftKind.Opening or ShiftKind.Closing);

        var echanges = 0;
        var jours = aTraiter.Where(a => !Figee(a)).GroupBy(a => a.AssignedDate).OrderBy(g => g.Key).ToList();
        for (var passe = 0; passe < PassesMax; passe++)
        {
            var avant = echanges;
            foreach (var jour in jours)
            {
                var groupes = jour
                    .Where(a => !Figee(a))
                    .GroupBy(a => (Mode: a.ShiftModeProfileId ?? 0, Niveau: agents.TryGetValue(a.UserId, out var u) ? u.Level : 0));
                foreach (var groupe in groupes)
                {
                    // Le plus chargé sur l'horaire le plus tardif ↔ le moins chargé sur un horaire plus tôt.
                    var membres = groupe.OrderByDescending(a => charge.GetValueOrDefault(a.UserId)).ThenBy(a => a.UserId).ToList();
                    for (var i = 0; i < membres.Count; i++)
                    {
                        var x = membres[i];
                        ShiftAssignment? meilleur = null;
                        var meilleurGain = 0.0;
                        for (var j = membres.Count - 1; j > i; j--)
                        {
                            var y = membres[j];
                            var delta = W(x) - W(y);
                            if (delta <= 1e-9 || x.SubServiceShiftConfigId == y.SubServiceShiftConfigId) continue;
                            var ecart = charge.GetValueOrDefault(x.UserId) - charge.GetValueOrDefault(y.UserId);
                            if (ecart - delta <= 1e-9) continue; // l'échange doit réduire l'écart
                            var cx = x.SubServiceShiftConfigId!.Value;
                            var cy = y.SubServiceShiftConfigId!.Value;
                            if (!PlateauOk(y.UserId, cx) || !PlateauOk(x.UserId, cy)) continue;
                            if (!Respecte(x.UserId, x.AssignedDate, cy, x.ShiftModeProfileId)
                                || !Respecte(y.UserId, y.AssignedDate, cx, y.ShiftModeProfileId)) continue;
                            var gain = delta * (ecart - delta);
                            if (gain > meilleurGain)
                            {
                                meilleurGain = gain;
                                meilleur = y;
                            }
                        }
                        if (meilleur is null) continue;
                        var dx = W(x) - W(meilleur);
                        (x.SubServiceShiftConfigId, meilleur.SubServiceShiftConfigId) = (meilleur.SubServiceShiftConfigId, x.SubServiceShiftConfigId);
                        charge[x.UserId] = charge.GetValueOrDefault(x.UserId) - dx;
                        charge[meilleur.UserId] = charge.GetValueOrDefault(meilleur.UserId) + dx;
                        echanges++;
                    }
                }
            }
            if (echanges == avant) break;
        }
        return echanges;
    }
}
