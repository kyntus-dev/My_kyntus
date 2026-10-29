import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { KyntusRoleNames } from '../org/kyntus-role-names';

export type AppGuideKind = 'Video' | 'Document' | 'Link';

export interface AppGuide {
  id: number;
  title: string;
  description?: string | null;
  kind: AppGuideKind | string;
  mediaAssetId?: number | null;
  mediaContentType?: string | null;
  mediaFileName?: string | null;
  externalUrl?: string | null;
  sortOrder: number;
  viewed: boolean;
  viewedAt?: string | null;
}

export interface MyAppGuides {
  role: string;
  guides: AppGuide[];
  viewedCount: number;
  total: number;
}

export interface AppGuideAdmin {
  id: number;
  title: string;
  description?: string | null;
  kind: AppGuideKind | string;
  mediaAssetId?: number | null;
  mediaContentType?: string | null;
  mediaFileName?: string | null;
  externalUrl?: string | null;
  targetRoles: string[];
  sortOrder: number;
  isActive: boolean;
  viewCount: number;
  createdAt: string;
  updatedAt?: string | null;
}

export interface SaveAppGuideRequest {
  title: string;
  description?: string | null;
  kind: AppGuideKind;
  mediaAssetId?: number | null;
  externalUrl?: string | null;
  targetRoles: string[];
  sortOrder: number;
  isActive: boolean;
}

/** Rôles proposés pour cibler un guide (noms canoniques Planning). */
export const APP_GUIDE_TARGET_ROLES: readonly string[] = [
  KyntusRoleNames.Pilote,
  KyntusRoleNames.ReferentTechnique,
  KyntusRoleNames.Superviseur,
  KyntusRoleNames.ChefDeProjet,
  KyntusRoleNames.Manager,
  KyntusRoleNames.RH,
  KyntusRoleNames.Admin,
  KyntusRoleNames.Audit,
  KyntusRoleNames.Formateur,
  KyntusRoleNames.Qualiticien,
  KyntusRoleNames.Comptabilite,
];

/** Taille maximale acceptée par la passerelle pour un envoi depuis le navigateur (~28 Mo). */
export const APP_GUIDE_BROWSER_UPLOAD_MAX_BYTES = 28 * 1024 * 1024;

const ONBOARDING_SKIP_KEY = 'kyntus.appGuide.onboardingSkipped';

/**
 * Espace Aide › Guide de l'application : appels API et état partagé
 * (nombre de guides non consultés pour le badge du menu, report de l'accueil guidé).
 */
@Injectable({ providedIn: 'root' })
export class AppGuideService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/planning/app-guides`;

  private readonly mine = signal<MyAppGuides | null>(null);
  private readonly loading = signal(false);

  /** Guides du parcours de l'utilisateur connecté (null tant que non chargé). */
  readonly myGuides = this.mine.asReadonly();
  readonly isLoading = this.loading.asReadonly();
  readonly pendingCount = computed(() => {
    const m = this.mine();
    return m ? Math.max(0, m.total - m.viewedCount) : 0;
  });

  /** Recharge le parcours ; en cas d'erreur, l'espace Aide reste utilisable sans badge. */
  refresh(): Observable<MyAppGuides | null> {
    this.loading.set(true);
    return this.http.get<MyAppGuides>(`${this.base}/mine`).pipe(
      tap((m) => this.mine.set(m)),
      catchError(() => of(null)),
      finalize(() => this.loading.set(false)),
    );
  }

  markViewed(guideId: number): Observable<void> {
    return this.http.post<void>(`${this.base}/${guideId}/viewed`, {}).pipe(
      tap(() => {
        const m = this.mine();
        if (!m) return;
        const now = new Date().toISOString();
        const guides = m.guides.map((g) => (g.id === guideId && !g.viewed ? { ...g, viewed: true, viewedAt: now } : g));
        this.mine.set({ ...m, guides, viewedCount: guides.filter((g) => g.viewed).length });
      }),
    );
  }

  listAll(): Observable<AppGuideAdmin[]> {
    return this.http.get<AppGuideAdmin[]>(this.base);
  }

  create(body: SaveAppGuideRequest): Observable<AppGuideAdmin> {
    return this.http.post<AppGuideAdmin>(this.base, body);
  }

  update(id: number, body: SaveAppGuideRequest): Observable<AppGuideAdmin> {
    return this.http.put<AppGuideAdmin>(`${this.base}/${id}`, body);
  }

  /** L'accueil guidé n'est proposé qu'une fois par session tant qu'il reste des guides à voir. */
  shouldOfferOnboarding(): boolean {
    return this.pendingCount() > 0 && !this.readSkip();
  }

  skipOnboardingForSession(): void {
    try {
      sessionStorage.setItem(ONBOARDING_SKIP_KEY, '1');
    } catch {
      /* stockage indisponible : sans conséquence */
    }
  }

  private readSkip(): boolean {
    try {
      return sessionStorage.getItem(ONBOARDING_SKIP_KEY) === '1';
    } catch {
      return true;
    }
  }
}
