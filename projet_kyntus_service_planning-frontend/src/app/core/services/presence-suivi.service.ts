import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of, shareReplay, tap } from 'rxjs';
import type {
  AvertissementDto,
  AvertissementRequest,
  CategorieType,
  DossierDocumentDto,
  DossierDto,
  EvpDto,
  EvpRequest,
  PresenceAgentRefDto,
  SuiviCatalogues,
} from '../models/presence.models';

/** Présences et paie, lot B : avertissements, éléments variables de paie, dossier administratif. */
@Injectable({ providedIn: 'root' })
export class PresenceSuiviService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/planning/presences';
  private catalogues$?: Observable<SuiviCatalogues>;

  catalogues(): Observable<SuiviCatalogues> {
    return (this.catalogues$ ??= this.http.get<SuiviCatalogues>(`${this.base}/catalogues`).pipe(shareReplay(1)));
  }

  /** v31 : ajoute un type à la main ; renvoie les listes à jour (gardées en cache). */
  ajouterType(categorie: CategorieType, libelle: string): Observable<SuiviCatalogues> {
    return this.http.post<SuiviCatalogues>(`${this.base}/types`, { categorie, libelle })
      .pipe(tap((c) => (this.catalogues$ = of(c))));
  }

  /** v31 : supprime un type ajouté à la main (les saisies gardent leur libellé). */
  supprimerType(id: number): Observable<SuiviCatalogues> {
    return this.http.delete<SuiviCatalogues>(`${this.base}/types/${id}`)
      .pipe(tap((c) => (this.catalogues$ = of(c))));
  }

  /** Agents du périmètre du demandeur (listes de choix). */
  agents(): Observable<PresenceAgentRefDto[]> {
    return this.http.get<PresenceAgentRefDto[]>(`${this.base}/agents`);
  }

  avertissements(du: string, au: string): Observable<AvertissementDto[]> {
    return this.http.get<AvertissementDto[]>(`${this.base}/avertissements`, { params: periode(du, au) });
  }

  ajouterAvertissement(body: AvertissementRequest): Observable<AvertissementDto> {
    return this.http.post<AvertissementDto>(`${this.base}/avertissements`, body);
  }

  supprimerAvertissement(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/avertissements/${id}`);
  }

  evp(du: string, au: string): Observable<EvpDto[]> {
    return this.http.get<EvpDto[]>(`${this.base}/evp`, { params: periode(du, au) });
  }

  ajouterEvp(body: EvpRequest): Observable<EvpDto> {
    return this.http.post<EvpDto>(`${this.base}/evp`, body);
  }

  modifierEvp(id: number, body: EvpRequest): Observable<EvpDto> {
    return this.http.put<EvpDto>(`${this.base}/evp/${id}`, body);
  }

  supprimerEvp(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/evp/${id}`);
  }

  dossier(userId: number): Observable<DossierDto> {
    return this.http.get<DossierDto>(`${this.base}/dossiers/${userId}`);
  }

  ajouterDocument(userId: number, fichier: File, categorie: string, commentaire: string | null): Observable<DossierDocumentDto> {
    const form = new FormData();
    form.append('fichier', fichier, fichier.name);
    form.append('categorie', categorie);
    if (commentaire) form.append('commentaire', commentaire);
    return this.http.post<DossierDocumentDto>(`${this.base}/dossiers/${userId}/documents`, form);
  }

  telechargerDocument(id: number): Observable<Blob> {
    return this.http.get(`${this.base}/documents/${id}`, { responseType: 'blob' });
  }

  supprimerDocument(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/documents/${id}`);
  }
}

function periode(du: string, au: string): HttpParams {
  return new HttpParams().set('du', du).set('au', au);
}
