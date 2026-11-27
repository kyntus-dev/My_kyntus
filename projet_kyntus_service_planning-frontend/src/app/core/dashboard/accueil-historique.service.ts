import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { compteursParRoute, jourLocal, valeursDuJour, type JourIndicateurs } from './accueil-historique.util';

const CLE_COMPTEURS = 'ky.menu.compteurs';
const CLE_ENVOI = 'ky.accueil.dernierEnvoi';

/**
 * v87 — Historique personnel des indicateurs de l'accueil (tendances, courbes) et compteurs du menu.
 * - L'historique est lu / écrit sur /api/planning/accueil/indicateurs : uniquement les valeurs de la
 *   personne connectée, telles que son accueil les affiche. En cas d'erreur, l'accueil s'affiche sans
 *   tendance (rien n'est bloqué).
 * - Les compteurs du menu reprennent les actions en attente calculées par l'accueil (aucun appel en plus) ;
 *   ils sont gardés pour la session de l'onglet et rafraîchis à chaque passage sur l'accueil.
 */
@Injectable({ providedIn: 'root' })
export class AccueilHistoriqueService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/planning/accueil/indicateurs`;

  readonly historique = signal<JourIndicateurs[]>([]);
  readonly compteursMenu = signal<Record<string, number>>(lireCompteurs());

  async charger(jours = 35): Promise<void> {
    try {
      const rows = await firstValueFrom(this.http.get<JourIndicateurs[]>(this.base, { params: { jours } }));
      this.historique.set(Array.isArray(rows) ? rows.map((r) => ({ jour: String(r.jour).slice(0, 10), valeurs: r.valeurs ?? {} })) : []);
    } catch {
      this.historique.set([]);
    }
  }

  /** Enregistre les valeurs affichées aujourd'hui (au plus une fois toutes les 15 minutes si rien ne change). */
  async enregistrer(kpis: readonly { label: string; value: string | number }[]): Promise<void> {
    const valeurs = valeursDuJour(kpis);
    if (!Object.keys(valeurs).length) return;
    const empreinte = `${jourLocal(new Date())}|${JSON.stringify(valeurs)}`;
    try {
      const prec = JSON.parse(sessionStorage.getItem(CLE_ENVOI) ?? 'null') as { e: string; t: number } | null;
      if (prec && prec.e === empreinte && Date.now() - prec.t < 15 * 60_000) return;
    } catch { /* stockage indisponible : on envoie */ }
    try {
      await firstValueFrom(this.http.post(this.base, { valeurs }));
      try { sessionStorage.setItem(CLE_ENVOI, JSON.stringify({ e: empreinte, t: Date.now() })); } catch { /* ignoré */ }
    } catch { /* l'accueil reste utilisable sans historique */ }
  }

  /** Met à jour les compteurs du menu à partir des actions en attente de l'accueil. */
  publierCompteurs(actions: readonly { route?: string; count?: number }[]): void {
    const c = compteursParRoute(actions);
    this.compteursMenu.set(c);
    try { sessionStorage.setItem(CLE_COMPTEURS, JSON.stringify(c)); } catch { /* ignoré */ }
  }
}

function lireCompteurs(): Record<string, number> {
  try {
    const v = JSON.parse(sessionStorage.getItem(CLE_COMPTEURS) ?? '{}');
    return v && typeof v === 'object' ? v : {};
  } catch {
    return {};
  }
}
