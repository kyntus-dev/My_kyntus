// features/planning/services/conge.service.ts

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface CongeItem {
  id:          number;
  userId:      number;
  fullName:    string;
  startDate:   string;
  endDate:     string;
  reason:      string;
  absenceType: string; // ✅
  status:      string;
  /** v64 : « Planning » (saisie ici, modifiable) ou « Demande » (workflow Congés, lecture seule ici). */
  origine?:    string;
}

/** v64 : droits de l'utilisateur sur les absences du planning (Accès par rôle). */
export interface DroitsConges {
  consulter: boolean;
  ajouter:   boolean;
  modifier:  boolean;
}

export interface CreateCongeDto {
  userId:      number;
  startDate:   string;
  endDate:     string;
  reason:      string;
  absenceType: string; // ✅ AJOUTÉ
}

export interface SetSaturdaySlotDto {
  userId: number;
  slot:   number; // 1 = 8h-12h | 2 = 12h-16h
}

export interface EmployeeSimple {
  id:                number;
  fullName:          string;
  isNewEmployee:     boolean;
  hireDate:          string;
  monthsHere:        number;
  saturdaySlot:      number;
  saturdaySlotLabel: string;
}

export const ABSENCE_TYPES = [
  { value: 'AbsenceAutorisee',    label: 'Absence autorisée' },
  { value: 'AbsenceNonAutorisee', label: 'Absence non autorisée' },
  { value: 'ArretMaladie',        label: 'Arrêt maladie' },
  { value: 'NeTravaillePlus',     label: 'Ne travaille plus' },
  { value: 'AccidentDeTravail',   label: 'Accident de travail' },
  { value: 'CongesPayes',         label: 'Congés payés' },
  { value: 'CongesSansSolde',     label: 'Congés sans solde' },
  { value: 'DeuilFamilial',       label: 'Deuil familial' },
  { value: 'EnfantMalade',        label: 'Enfant malade' },
  { value: 'Maternite',           label: 'Maternité' },
  { value: 'Paternite',           label: 'Paternité' },
  { value: 'Recup',               label: 'Récupération' },
];

@Injectable({ providedIn: 'root' })
export class CongeService {

  private base = `${environment.apiUrl}/Conges`;

  constructor(private http: HttpClient) {}

  getBySubService(subServiceId: number, weekStart?: string): Observable<CongeItem[]> {
    const params = weekStart ? `?weekStart=${weekStart}` : '';
    return this.http.get<CongeItem[]>(`${this.base}/subservice/${subServiceId}${params}`);
  }

  droits(): Observable<DroitsConges> {
    return this.http.get<DroitsConges>(`${this.base}/droits`);
  }

  getById(id: number): Observable<CongeItem> {
    return this.http.get<CongeItem>(`${this.base}/${id}`);
  }

  setNewEmployeeStatus(userId: number, isNewEmployee: boolean): Observable<any> {
    return this.http.put(`${environment.apiUrl}/Users/${userId}/new-employee`, { isNewEmployee });
  }

  create(dto: CreateCongeDto): Observable<CongeItem> {
    return this.http.post<CongeItem>(this.base, {
      userId: dto.userId,
      startDate: dto.startDate,
      endDate: dto.endDate,
      reason: dto.reason ?? '',
      absenceType: dto.absenceType,
    });
  }

  update(id: number, dto: CreateCongeDto): Observable<CongeItem> {
    return this.http.put<CongeItem>(`${this.base}/${id}`, {
      userId: dto.userId,
      startDate: dto.startDate,
      endDate: dto.endDate,
      reason: dto.reason ?? '',
      absenceType: dto.absenceType,
    });
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  getNewEmployees(subServiceId: number): Observable<EmployeeSimple[]> {
    return this.http.get<EmployeeSimple[]>(`${this.base}/new-employees/${subServiceId}`);
  }

  setSaturdaySlot(dto: SetSaturdaySlotDto): Observable<any> {
    return this.http.post(`${this.base}/saturday-slot`, dto);
  }

  getByUser(userId: number): Observable<CongeItem[]> {
    return this.http.get<CongeItem[]>(`${this.base}/user/${userId}`);
  }
}