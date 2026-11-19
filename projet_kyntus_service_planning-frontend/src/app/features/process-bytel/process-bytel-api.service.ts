import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** v48 — Bibliothèque « Process Bytel » : documents déposés par l'encadrement. */
export interface ProcessDocumentDto {
  id: number;
  dossier: string;
  titre: string;
  nomFichier: string;
  contentType: string;
  taille: number;
  commentaire: string | null;
  pourTous: boolean;
  activites: string[];
  auteur: string | null;
  creeLe: string;
}

export interface ProcessBibliothequeDto {
  dossiers: string[];
  documents: ProcessDocumentDto[];
  peutDeposer: boolean;
  /** Pôle du demandeur (sert au filtre de visibilité côté serveur). */
  monPole: string | null;
}

/** Contrat (activité) et ses pôles — destinataires possibles d'un document. */
export interface ActiviteRef {
  id: string;
  name: string;
  kind?: string;
  isActive?: boolean;
  poleIds?: string[];
}

export interface DepotProcess {
  fichier: File;
  dossier: string;
  titre: string;
  commentaire: string;
  pourTous: boolean;
  activites: string[];
  poles: string[];
}

@Injectable({ providedIn: 'root' })
export class ProcessBytelApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/planning/process-bytel';

  bibliotheque(): Observable<ProcessBibliothequeDto> {
    return this.http.get<ProcessBibliothequeDto>(this.base);
  }

  /** Activités (contrats) et leurs pôles, pour choisir les destinataires. */
  activites(): Observable<ActiviteRef[]> {
    return this.http.get<ActiviteRef[]>('/api/directory/business-departments');
  }

  deposer(d: DepotProcess): Observable<ProcessDocumentDto> {
    const form = new FormData();
    form.append('fichier', d.fichier, d.fichier.name);
    form.append('dossier', d.dossier);
    form.append('titre', d.titre);
    form.append('commentaire', d.commentaire);
    form.append('pourTous', String(d.pourTous));
    form.append('activites', d.activites.join('|'));
    form.append('poles', d.poles.join('|'));
    return this.http.post<ProcessDocumentDto>(this.base, form);
  }

  telecharger(id: number): Observable<Blob> {
    return this.http.get(`${this.base}/${id}/fichier`, { responseType: 'blob' });
  }

  supprimer(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
