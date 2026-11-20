import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** v63 — Fiches employés : mise à jour depuis le fichier RH de référence. */
export const FICHES_REFERENCE_API = '/api/users/fiches-reference';

export type StatutLigneFiche =
  | 'identifie'
  | 'a-verifier'
  | 'ambigu'
  | 'nouveau'
  | 'sorti-inconnu'
  | 'historique'
  | 'ignore';

export interface ColonneFichierDto {
  cle: string;
  intitule: string;
  enTeteFichier: string | null;
  lettre: string | null;
}

export interface ModificationFicheDto {
  cle: string;
  libelle: string;
  ancienne: string | null;
  nouvelle: string | null;
}

export interface CandidatFicheDto {
  userId: number;
  nom: string;
  prenom: string;
  matricule: string | null;
  actif: boolean;
  raison: string;
}

export interface LigneFicheReferenceDto {
  numero: number;
  agent: string | null;
  numeroOrdre: string | null;
  statut: StatutLigneFiche;
  message: string | null;
  userId: number | null;
  ficheApp: string | null;
  ficheActive: boolean;
  sorti: boolean;
  dateSortie: string | null;
  roleSiCreation: string | null;
  choixParDefaut: string;
  candidats: CandidatFicheDto[];
  modifications: ModificationFicheDto[];
  anomalies: string[];
  avertissements: string[];
  champsManquants: string[];
}

export interface AgentAbsentDto {
  userId: number;
  nom: string;
  prenom: string;
  matricule: string | null;
  service: string | null;
}

export interface CompteursAnalyseFichesDto {
  lignesLues: number;
  identifies: number;
  aMettreAJour: number;
  sansChangement: number;
  nouveaux: number;
  aVerifier: number;
  ambigus: number;
  historiques: number;
  sortisInconnus: number;
  ignores: number;
  avecAnomalies: number;
  fichesIncompletes: number;
  champsAModifier: number;
}

export interface AnalyseFichesReferenceDto {
  jeton: string;
  fichier: string;
  feuille: string;
  analyseLe: string;
  expireLe: string;
  colonnesReconnues: ColonneFichierDto[];
  colonnesManquantes: string[];
  colonnesNonReprises: string[];
  feuillesBox: string[];
  compteurs: CompteursAnalyseFichesDto;
  lignes: LigneFicheReferenceDto[];
  actifsAbsentsDuFichier: AgentAbsentDto[];
}

export interface AppliquerFichesReferenceRequest {
  jeton: string;
  choix: Record<number, string>;
  desactiverSortis: boolean;
}

export interface LigneAppliqueeFicheDto {
  numero: number;
  agent: string | null;
  resultat: 'mis-a-jour' | 'sans-changement' | 'cree' | 'desactive' | 'ignore' | 'erreur' | string;
  userId: number | null;
  message: string | null;
  annuaireSynchronise: boolean;
  modifications: ModificationFicheDto[];
  anomalies: string[];
  champsManquants: string[];
}

export interface CompteursApplicationFichesDto {
  misesAJour: number;
  sansChangement: number;
  crees: number;
  desactives: number;
  ignores: number;
  erreurs: number;
  champsModifies: number;
  fichesIncompletes: number;
  annuaireNonSynchronise: number;
}

export interface ApplicationFichesReferenceDto {
  importId: string;
  fichier: string;
  appliqueLe: string;
  compteurs: CompteursApplicationFichesDto;
  lignes: LigneAppliqueeFicheDto[];
}

/** v63.2 — Avancement d'une application lancée en arrière-plan. */
export interface SuiviApplicationFichesDto {
  importId: string;
  fichier: string;
  statut: 'en-cours' | 'termine' | 'echec' | string;
  total: number;
  traitees: number;
  demarreLe: string;
  resultat: ApplicationFichesReferenceDto | null;
  erreur: string | null;
}

export interface ImportFichesReferenceDto {
  importId: string;
  le: string;
  fichier: string;
  auteur: string;
  fiches: number;
  champs: number;
  creations: number;
  desactivations: number;
  annule: boolean;
}

export interface AnnulationFichesReferenceDto {
  importId: string;
  valeursRestaurees: number;
  valeursConservees: number;
  fichesDesactivees: number;
  message: string;
}

export interface FicheIncompleteDto {
  userId: number;
  nom: string;
  prenom: string;
  matricule: string | null;
  service: string | null;
  role: string;
  champsManquants: string[];
}

@Injectable({ providedIn: 'root' })
export class FichesReferenceApiService {
  private readonly http = inject(HttpClient);

  analyser(fichier: File): Observable<AnalyseFichesReferenceDto> {
    const form = new FormData();
    form.append('file', fichier, fichier.name);
    return this.http.post<AnalyseFichesReferenceDto>(`${FICHES_REFERENCE_API}/analyser`, form);
  }

  /** Lance l'application (arrière-plan) : rend le suivi à interroger avec {@link suivi}. */
  appliquer(demande: AppliquerFichesReferenceRequest): Observable<SuiviApplicationFichesDto> {
    return this.http.post<SuiviApplicationFichesDto>(`${FICHES_REFERENCE_API}/appliquer`, demande);
  }

  suivi(importId: string): Observable<SuiviApplicationFichesDto> {
    return this.http.get<SuiviApplicationFichesDto>(`${FICHES_REFERENCE_API}/application/${importId}`);
  }

  historique(): Observable<ImportFichesReferenceDto[]> {
    return this.http.get<ImportFichesReferenceDto[]>(`${FICHES_REFERENCE_API}/historique`);
  }

  detail(importId: string): Observable<LigneAppliqueeFicheDto[]> {
    return this.http.get<LigneAppliqueeFicheDto[]>(`${FICHES_REFERENCE_API}/historique/${importId}`);
  }

  annuler(importId: string): Observable<AnnulationFichesReferenceDto> {
    return this.http.post<AnnulationFichesReferenceDto>(`${FICHES_REFERENCE_API}/historique/${importId}/annuler`, {});
  }

  incompletes(): Observable<FicheIncompleteDto[]> {
    return this.http.get<FicheIncompleteDto[]>(`${FICHES_REFERENCE_API}/incompletes`);
  }

  exportIncompletes(): Observable<Blob> {
    return this.http.get(`${FICHES_REFERENCE_API}/incompletes/export`, { responseType: 'blob' });
  }
}
