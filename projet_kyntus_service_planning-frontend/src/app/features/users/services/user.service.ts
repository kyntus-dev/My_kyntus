import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, throwError, of } from 'rxjs';
import { catchError, map, shareReplay } from 'rxjs/operators';
import { User, CreateUserDto, UpdateUserDto } from '../users-module';
import { environment } from '../../../../environments/environment';



export interface RoleOption {
  id: number;
  name: string;
}

export type OrgGroupScope = 'Pole' | 'Cellule' | 'Service';

export interface ResetPasswordResult {
  userId: number;
  email: string;
  temporaryPassword: string;
  firstName?: string;
  lastName?: string;
}

export interface ResetPasswordsByGroupRequest {
  scope: OrgGroupScope;
  nodeId: string;
  employeeGuids?: string[];
}

export interface ResetPasswordsByGroupResult {
  scope: string;
  nodeId: string;
  targetedCount: number;
  resetCount: number;
  failedCount: number;
  items: ResetPasswordResult[];
  errors: string[];
}
@Injectable({ providedIn: 'root' })
export class UserService {
  private apiUrl = `${environment.apiUrl}/users`;
  private rolesUrl = `${environment.apiUrl}/roles`;
  private activeCountCache$: Observable<number> | null = null;
  private activeCountCacheAt = 0;
  private static readonly COUNT_CACHE_MS = 30 * 60_000;
  constructor(private http: HttpClient) {}

  getAllUsers(): Observable<User[]> {
    return this.http.get<User[]>(this.apiUrl).pipe(
      catchError(err => throwError(() => err))
    );
  }

  /** Compte employés actifs — cache mémoire 30 min (dashboard). */
  getActiveUsersCount(): Observable<number> {
    const now = Date.now();
    if (this.activeCountCache$ && now - this.activeCountCacheAt < UserService.COUNT_CACHE_MS) {
      return this.activeCountCache$;
    }
    this.activeCountCacheAt = now;
    this.activeCountCache$ = this.getAllUsers().pipe(
      map((users) => users.filter((u) => u.isActive !== false).length),
      catchError(() => of(0)),
      shareReplay(1),
    );
    return this.activeCountCache$;
  }

  getUsersBySubService(subServiceId: number): Observable<User[]> {
    return this.http.get<User[]>(`${this.apiUrl}/by-subservice/${subServiceId}`).pipe(
      catchError(err => throwError(() => err))
    );
  }

  getUserById(id: number): Observable<User> {
    return this.http.get<User>(`${this.apiUrl}/${id}`).pipe(
      catchError(err => throwError(() => err))
    );
  }

  createUser(dto: CreateUserDto): Observable<User> {
    return this.http.post<User>(this.apiUrl, dto).pipe(
      catchError(err => throwError(() => err))
    );
  }

  updateUser(id: number, dto: UpdateUserDto): Observable<User> {
    return this.http.put<User>(`${this.apiUrl}/${id}`, dto).pipe(
      catchError(err => throwError(() => err))
    );
  }

  /** Met à jour uniquement le niveau contractuel (1–3). */
  patchContractualLevel(id: number, level: number): Observable<User> {
    return this.http.patch<User>(`${this.apiUrl}/${id}/contractual-level`, { level }).pipe(
      catchError(err => throwError(() => err))
    );
  }
  // Ajouter cette méthode
getUserByAuthId(authId: number): Observable<User> {
  return this.http.get<User>(`${this.apiUrl}/by-auth/${authId}`).pipe(
    catchError(err => throwError(() => err))
  );
}

  getCurrentUser(): Observable<User> {
    return this.http.get<User>(`${this.apiUrl}/me`).pipe(
      catchError(err => throwError(() => err))
    );
  }

  deleteUser(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`).pipe(
      catchError(err => throwError(() => err))
    );
  }

  resetPassword(id: number): Observable<ResetPasswordResult> {
    return this.http.post<ResetPasswordResult>(
      `${this.apiUrl}/${id}/reset-password`,
      {},
    ).pipe(
      catchError(err => throwError(() => err))
    );
  }

  resetPasswordsByGroup(body: ResetPasswordsByGroupRequest): Observable<ResetPasswordsByGroupResult> {
    return this.http.post<ResetPasswordsByGroupResult>(
      `${this.apiUrl}/reset-passwords/by-group`,
      body,
    ).pipe(
      catchError(err => throwError(() => err))
    );
  }

  checkEmailUnique(email: string, excludeId?: number): Observable<{ isUnique: boolean }> {
    const params = excludeId ? `?excludeId=${excludeId}` : '';
    return this.http.get<{ isUnique: boolean }>(`${this.apiUrl}/check-email/${email}${params}`).pipe(
      catchError(err => throwError(() => err))
    );
  }
  getRoles(): Observable<RoleOption[]> {
    return this.http.get<RoleOption[]>(this.rolesUrl).pipe(
      catchError(err => throwError(() => err))
    );
  }

  /** Aligne le miroir Planning depuis Employee Directory (rapide, source canonique). */
  syncOrgMirrorFromDirectory(): Observable<unknown> {
    return this.http.post(`${environment.apiUrl}/admin/org-reconciliation/sync-from-directory`, {});
  }

  /** @deprecated Préférer syncOrgMirrorFromDirectory */
  syncOrgMirrorFromPrime(): Observable<unknown> {
    return this.http
      .post(`${environment.apiUrl}/admin/org-reconciliation/sync-from-prime`, {})
      .pipe(
        catchError((err) =>
          throwError(
            () =>
              new Error(
                err?.error?.message ??
                  'La synchronisation Organisation RH → Planning a échoué. Réessayez ou contactez l\'administrateur.',
              ),
          ),
        ),
      );
  }
}