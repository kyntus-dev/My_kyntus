using System.Collections.Concurrent;
using Planning.Application.DTOs;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v63.2 — Suivi des applications de fichier RH lancées en arrière-plan (une à la fois).
/// L'application de plusieurs centaines de fiches dure plusieurs minutes (base + annuaire par fiche) :
/// elle ne dépend plus de la requête HTTP, que la passerelle coupe au bout de 90 s.
/// </summary>
public sealed class FichesReferenceSuivi
{
    public sealed class Etat
    {
        public Guid ImportId { get; init; }
        public string Fichier { get; init; } = string.Empty;
        public int Total { get; init; }
        public int Traitees;
        public volatile string Statut = EnCours;
        public ApplicationFichesReferenceDto? Resultat { get; set; }
        public string? Erreur { get; set; }
        public DateTime DemarreLe { get; init; } = DateTime.UtcNow;
    }

    public const string EnCours = "en-cours";
    public const string Termine = "termine";
    public const string Echec = "echec";

    private readonly ConcurrentDictionary<Guid, Etat> _etats = new();
    private readonly object _verrou = new();

    /// <summary>Enregistre une application ; null si une autre est déjà en cours.</summary>
    public Etat? Demarrer(Guid importId, string fichier, int total)
    {
        lock (_verrou)
        {
            foreach (var ancien in _etats.Values.Where(e => e.DemarreLe < DateTime.UtcNow.AddHours(-12)).ToList())
                _etats.TryRemove(ancien.ImportId, out _);
            if (_etats.Values.Any(e => e.Statut == EnCours && e.DemarreLe > DateTime.UtcNow.AddHours(-2)))
                return null;
            var etat = new Etat { ImportId = importId, Fichier = fichier, Total = total };
            _etats[importId] = etat;
            return etat;
        }
    }

    public Etat? Lire(Guid importId) => _etats.TryGetValue(importId, out var e) ? e : null;

    public static SuiviApplicationFichesDto VersDto(Etat e) =>
        new(e.ImportId, e.Fichier, e.Statut, e.Total, Volatile.Read(ref e.Traitees), e.DemarreLe, e.Resultat, e.Erreur);
}
