import { Injectable, Inject, PLATFORM_ID } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable, BehaviorSubject, tap, catchError, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { isPlatformBrowser } from '@angular/common';
import { environment } from '../environments/environment';
import { AuthResponse } from '../models/auth-response.model';
import { LoginRequest, RegisterRequest, RefreshTokenRequest } from '../models/auth-response.model';
import { User } from '../models/user.model';
import { KYNTUS_PUBLIC_URLS } from '../config/kyntus-public-urls';

@Injectable({
  providedIn: 'root'
})
export class AuthService {

private apiUrl = `${environment.apiUrl}/Auth`;

  private currentUserSubject = new BehaviorSubject<User | null>(null);
  public currentUser$ = this.currentUserSubject.asObservable();

  private loadingSubject = new BehaviorSubject<boolean>(false);
  public loading$ = this.loadingSubject.asObservable();

  constructor(
    private http: HttpClient,
    private router: Router,
    @Inject(PLATFORM_ID) private platformId: Object
  ) {
    // Charger utilisateur seulement côté navigateur
    if (isPlatformBrowser(this.platformId)) {
      this.loadUserFromStorage();
    }
  }

  // ==============================
  // UTILITAIRES STORAGE
  // ==============================

  private isBrowser(): boolean {
    return isPlatformBrowser(this.platformId);
  }

  private loadUserFromStorage(): void {
    if (!this.isBrowser()) return;

    const savedUser = localStorage.getItem('currentUser');
    if (savedUser) {
      try {
        this.currentUserSubject.next(JSON.parse(savedUser));
      } catch (error) {
        console.error('Erreur chargement utilisateur', error);
        this.clearStorage();
      }
    }
  }

  private handleAuthResponse(response: AuthResponse): void {
    if (!this.isBrowser()) return;

    localStorage.setItem('accessToken', response.accessToken);
    localStorage.setItem('refreshToken', response.refreshToken);
    localStorage.setItem('currentUser', JSON.stringify(response.user));

    this.currentUserSubject.next(response.user);
  }

  private clearStorage(): void {
    if (this.isBrowser()) {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('currentUser');
    }

    this.currentUserSubject.next(null);
  }

  getToken(): string | null {
    if (!this.isBrowser()) return null;
    return localStorage.getItem('accessToken');
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  getCurrentUser(): User | null {
    return this.currentUserSubject.value;
  }

  // ==============================
  // AUTHENTIFICATION
  // ==============================

  register(request: RegisterRequest): Observable<AuthResponse> {
    this.loadingSubject.next(true);

    return this.http.post<AuthResponse>(`${this.apiUrl}/register`, request).pipe(
      tap(response => {
        this.handleAuthResponse(response);
        this.loadingSubject.next(false);
      }),
      catchError(error => {
        this.loadingSubject.next(false);
        return throwError(() => error);
      })
    );
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    this.loadingSubject.next(true);

    return this.http.post<AuthResponse>(`${this.apiUrl}/login`, request).pipe(
      tap(response => {
        this.handleAuthResponse(response);
        this.loadingSubject.next(false);
      }),
      catchError(error => {
        this.loadingSubject.next(false);
        return throwError(() => error);
      })
    );
  }

  completeFirstLogin(changeToken: string, password: string, confirmation: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/complete-first-login`, {
      changeToken, newPassword: password, confirmPassword: confirmation
    });
  }

logout(): Observable<any> {

  if (!this.isBrowser()) {
    this.clearStorage();
    return throwError(() => new Error('Logout non disponible côté serveur'));
  }

  const refreshToken = localStorage.getItem('refreshToken');
  const token = this.getToken();

  const headers = new HttpHeaders({
    'Authorization': `Bearer ${token}`,
    'Content-Type': 'application/json'
  });

  return this.http.post(`${this.apiUrl}/logout`, { refreshToken }, { headers }).pipe(
    tap(() => {
      // ✅ Efface les clés des DEUX frontends
      this.clearStorage();
      localStorage.removeItem('access_token');
      localStorage.removeItem('refresh_token');
      localStorage.removeItem('user');
      localStorage.removeItem('token_type');
      // ✅ Redirection propre vers login
      window.location.href = KYNTUS_PUBLIC_URLS.authLogin;
    }),
    catchError(error => {
      // ✅ Même chose en cas d'erreur API
      this.clearStorage();
      localStorage.removeItem('access_token');
      localStorage.removeItem('refresh_token');
      localStorage.removeItem('user');
      localStorage.removeItem('token_type');
      window.location.href = KYNTUS_PUBLIC_URLS.authLogin;
      return throwError(() => error);
    })
  );
}
  refreshToken(): Observable<AuthResponse> {

    if (!this.isBrowser()) {
      return throwError(() => new Error('Refresh non disponible côté serveur'));
    }

    const refreshToken = localStorage.getItem('refreshToken');

    if (!refreshToken) {
      return throwError(() => new Error('Pas de refresh token disponible'));
    }

    const request: RefreshTokenRequest = { refreshToken };

    return this.http.post<AuthResponse>(`${this.apiUrl}/refresh`, request).pipe(
      tap(response => this.handleAuthResponse(response)),
      catchError(error => {
        this.clearStorage();
        this.router.navigate(['/login']);
        return throwError(() => error);
      })
    );
  }

  // ==============================
  // VALIDATION
  // ==============================

  checkEmail(email: string): Observable<{ exists: boolean }> {
    return this.http.get<{ exists: boolean }>(
      `${this.apiUrl}/check-email?email=${email}`
    );
  }

  checkUsername(username: string): Observable<{ exists: boolean }> {
    return this.http.get<{ exists: boolean }>(
      `${this.apiUrl}/check-username?username=${username}`
    );
  }
 

}
