import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, catchError, of } from 'rxjs';

/** v64 — Journal d'actions (Audit et conformité). */
export interface JournalLigne {
  id: number;
  horodatage: string;
  acteurId: number | null;
  acteurNom: string;
  acteurRole: string | null;
  module: string;
  operation: string;
  entite: string;
  entiteId: string | null;
  element: string;
  cibleUserId: number | null;
  anciennesValeurs: Record<string, string | null> | null;
  nouvellesValeurs: Record<string, string | null> | null;
  details: string | null;
  source: string | null;
}

export interface JournalPage {
  total: number;
  page: number;
  taille: number;
  lignes: JournalLigne[];
}

export interface JournalFiltres {
  modules: string[];
  operations: string[];
  roles: string[];
  acteurs: { id: number | null; nom: string; role: string | null }[];
}

export interface JournalRecherche {
  du?: string | null;
  au?: string | null;
  module?: string | null;
  operation?: string | null;
  acteur?: number | null;
  role?: string | null;
  employe?: number | null;
  q?: string | null;
  page?: number;
  taille?: number;
}

@Injectable({ providedIn: 'root' })
export class JournalActionsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/planning/journal';

  private params(r: JournalRecherche): HttpParams {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(r)) {
      if (v !== null && v !== undefined && v !== '') p = p.set(k, String(v));
    }
    return p;
  }

  rechercher(r: JournalRecherche): Observable<JournalPage> {
    return this.http.get<JournalPage>(this.base, { params: this.params(r) });
  }

  filtres(): Observable<JournalFiltres> {
    return this.http.get<JournalFiltres>(`${this.base}/filtres`);
  }

  exporter(r: JournalRecherche): Observable<Blob> {
    return this.http.get(`${this.base}/export`, { params: this.params(r), responseType: 'blob' });
  }

  /**
   * Déclare un export produit dans le navigateur (paie, planning, employés…) pour qu'il figure au
   * journal. Ne bloque jamais l'export : une erreur est ignorée.
   */
  declarerExport(element: string, details?: string): void {
    this.http
      .post(`${this.base}/evenements`, { element, details: details ?? null })
      .pipe(catchError(() => of(null)))
      .subscribe();
  }
}
