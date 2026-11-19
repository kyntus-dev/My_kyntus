import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { LigneMailImport } from '../lib/mail-interne.util';

export interface AgentSansMailDto {
  userId: number;
  guid: string;
  nom: string;
  prenom: string;
  role: string;
  service: string | null;
  actif: boolean;
  idTechnicien: number | null;
  htelCode: string | null;
  creeLe: string;
}

export interface CandidatMailDto {
  userId: number;
  nom: string;
  prenom: string;
  score: number;
}

export interface LigneMailApercuDto {
  numero: number;
  nom: string;
  prenom: string;
  email: string;
  statut: string;
  userId: number | null;
  message: string | null;
  candidats: CandidatMailDto[];
}

export interface ApercuMailsResultDto {
  lignes: LigneMailApercuDto[];
  total: number;
  exacts: number;
  probables: number;
  ambigus: number;
  introuvables: number;
  rejetes: number;
  agentsSansMail: number;
}

export interface AttributionMailResultDto {
  userId: number;
  nom: string;
  prenom: string;
  email: string;
  ok: boolean;
  message: string | null;
  motDePasse: string | null;
}

export interface AppliquerMailsResultDto {
  resultats: AttributionMailResultDto[];
  appliques: number;
  echecs: number;
}

/** v47 — Mails internes : agents sans adresse, aperçu de l'import, application. */
@Injectable({ providedIn: 'root' })
export class MailsInternesApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/users/mails-internes';

  sansMail(): Observable<AgentSansMailDto[]> {
    return this.http.get<AgentSansMailDto[]>(`${this.base}/sans-mail`);
  }

  apercu(lignes: LigneMailImport[]): Observable<ApercuMailsResultDto> {
    return this.http.post<ApercuMailsResultDto>(`${this.base}/apercu`, { lignes });
  }

  appliquer(
    attributions: { userId: number; email: string }[],
    creerComptes: boolean,
  ): Observable<AppliquerMailsResultDto> {
    return this.http.post<AppliquerMailsResultDto>(`${this.base}/appliquer`, {
      attributions,
      creerComptes,
    });
  }
}
