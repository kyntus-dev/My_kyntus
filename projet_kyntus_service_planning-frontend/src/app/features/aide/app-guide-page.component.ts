import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { KyntusPageHeaderComponent } from '../../shared/components/ui/kyntus-page-header.component';
import { KyntusSessionService } from '../../core/session/kyntus-session.service';
import { MediaService } from '../../core/services/media.service';
import { roleNamesMatch } from '../../core/org/org-role-assignment';
import { KyntusRoleNames } from '../../core/org/kyntus-role-names';
import {
  APP_GUIDE_BROWSER_UPLOAD_MAX_BYTES,
  APP_GUIDE_TARGET_ROLES,
  AppGuide,
  AppGuideAdmin,
  AppGuideKind,
  AppGuideService,
  SaveAppGuideRequest,
} from '../../core/help/app-guide.service';

interface GuideForm {
  id: number | null;
  title: string;
  description: string;
  kind: AppGuideKind;
  mediaAssetId: number | null;
  mediaFileName: string | null;
  externalUrl: string;
  targetRoles: string[];
  sortOrder: number;
  isActive: boolean;
}

const KIND_LABELS: Record<string, string> = { Video: 'Vidéo', Document: 'Document', Link: 'Lien' };

function emptyForm(): GuideForm {
  return {
    id: null,
    title: '',
    description: '',
    kind: 'Video',
    mediaAssetId: null,
    mediaFileName: null,
    externalUrl: '',
    targetRoles: [],
    sortOrder: 0,
    isActive: true,
  };
}

/** Espace Aide › Guide de l'application : parcours de prise en main par rôle. */
@Component({
  selector: 'app-guide-page',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent],
  templateUrl: './app-guide-page.component.html',
  styleUrl: './app-guide-page.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppGuidePageComponent implements OnInit {
  private readonly guides = inject(AppGuideService);
  private readonly media = inject(MediaService);
  private readonly session = inject(KyntusSessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly roleOptions = APP_GUIDE_TARGET_ROLES;

  readonly my = this.guides.myGuides;
  readonly loading = this.guides.isLoading;
  readonly pending = this.guides.pendingCount;
  readonly selectedId = signal<number | null>(null);
  readonly videoUrl = signal<string | null>(null);
  readonly videoLoading = signal(false);
  readonly marking = signal(false);
  readonly error = signal<string | null>(null);

  readonly isManager = computed(() => {
    const role = this.session.getRole() || '';
    return roleNamesMatch(role, KyntusRoleNames.Admin) || roleNamesMatch(role, KyntusRoleNames.RH);
  });

  readonly roleLabel = computed(() => this.my()?.role || this.session.getRole() || '—');

  readonly selected = computed<AppGuide | null>(() => {
    const list = this.my()?.guides ?? [];
    const id = this.selectedId();
    return list.find((g) => g.id === id) ?? null;
  });

  // ── Administration (Admin / RH) ──
  readonly adminList = signal<AppGuideAdmin[]>([]);
  readonly form = signal<GuideForm | null>(null);
  readonly saving = signal(false);
  readonly uploading = signal(false);
  readonly formError = signal<string | null>(null);

  ngOnInit(): void {
    this.reload();
    if (this.isManager()) this.reloadAdmin();
  }

  reload(): void {
    this.guides
      .refresh()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((m) => {
        if (!m) {
          this.error.set('Le guide est momentanément indisponible. Réessayez dans quelques instants.');
          return;
        }
        this.error.set(null);
        const current = this.selectedId();
        if (current == null || !m.guides.some((g) => g.id === current)) {
          const first = m.guides.find((g) => !g.viewed) ?? m.guides[0];
          this.select(first ?? null);
        }
      });
  }

  select(guide: AppGuide | null): void {
    this.selectedId.set(guide?.id ?? null);
    this.videoUrl.set(null);
    if (!guide || guide.kind !== 'Video') return;
    if (guide.mediaAssetId) {
      this.videoLoading.set(true);
      this.media
        .blobUrl(guide.mediaAssetId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (url) => {
            if (this.selectedId() === guide.id) this.videoUrl.set(url);
            this.videoLoading.set(false);
          },
          error: () => {
            this.videoLoading.set(false);
            this.error.set('La vidéo n’a pas pu être chargée.');
          },
        });
    } else if (guide.externalUrl && this.isDirectVideoUrl(guide.externalUrl)) {
      this.videoUrl.set(guide.externalUrl);
    }
  }

  isDirectVideoUrl(url: string): boolean {
    return /\.(mp4|webm|mov)(\?|#|$)/i.test(url);
  }

  markViewed(guide: AppGuide): void {
    if (guide.viewed || this.marking()) return;
    this.marking.set(true);
    this.guides
      .markViewed(guide.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.marking.set(false);
          this.selectNextPending(guide.id);
        },
        error: () => {
          this.marking.set(false);
          this.error.set('Impossible d’enregistrer la consultation. Réessayez.');
        },
      });
  }

  /** Fin de la vidéo : elle compte comme consultée. */
  onVideoEnded(guide: AppGuide): void {
    this.markViewed(guide);
  }

  openFile(guide: AppGuide): void {
    if (guide.mediaAssetId) {
      this.media
        .blobUrl(guide.mediaAssetId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe((url) => window.open(url, '_blank', 'noopener'));
    } else if (guide.externalUrl) {
      window.open(guide.externalUrl, '_blank', 'noopener');
    }
  }

  skipForNow(): void {
    this.guides.skipOnboardingForSession();
    void this.router.navigateByUrl('/home');
  }

  private selectNextPending(afterId: number): void {
    const list = this.my()?.guides ?? [];
    const next = list.find((g) => !g.viewed && g.id !== afterId);
    if (next) this.select(next);
  }

  // ── Administration ──

  reloadAdmin(): void {
    this.guides
      .listAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (list) => this.adminList.set(list), error: () => this.adminList.set([]) });
  }

  newGuide(): void {
    this.formError.set(null);
    const nextOrder = this.adminList().reduce((max, g) => Math.max(max, g.sortOrder), 0) + 1;
    this.form.set({ ...emptyForm(), sortOrder: nextOrder });
  }

  editGuide(g: AppGuideAdmin): void {
    this.formError.set(null);
    this.form.set({
      id: g.id,
      title: g.title,
      description: g.description ?? '',
      kind: (g.kind as AppGuideKind) ?? 'Video',
      mediaAssetId: g.mediaAssetId ?? null,
      mediaFileName: g.mediaFileName ?? null,
      externalUrl: g.externalUrl ?? '',
      targetRoles: [...g.targetRoles],
      sortOrder: g.sortOrder,
      isActive: g.isActive,
    });
  }

  cancelForm(): void {
    this.form.set(null);
    this.formError.set(null);
  }

  patchForm(patch: Partial<GuideForm>): void {
    const f = this.form();
    if (f) this.form.set({ ...f, ...patch });
  }

  toggleRole(role: string, checked: boolean): void {
    const f = this.form();
    if (!f) return;
    const roles = checked ? [...new Set([...f.targetRoles, role])] : f.targetRoles.filter((r) => r !== role);
    this.form.set({ ...f, targetRoles: roles });
  }

  hasRole(role: string): boolean {
    return this.form()?.targetRoles.some((r) => roleNamesMatch(r, role)) ?? false;
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (file.size > APP_GUIDE_BROWSER_UPLOAD_MAX_BYTES) {
      this.formError.set(
        `Fichier de ${(file.size / 1024 / 1024).toFixed(0)} Mo : au-delà de 28 Mo, publiez-le avec la commande serveur ` +
          '(procédure « Publier une vidéo de guide ») ou indiquez un lien.',
      );
      return;
    }
    this.uploading.set(true);
    this.formError.set(null);
    this.media
      .upload(file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (asset) => {
          this.uploading.set(false);
          this.patchForm({ mediaAssetId: asset.id, mediaFileName: asset.fileName });
        },
        error: (err) => {
          this.uploading.set(false);
          this.formError.set(err?.error?.message ?? 'Envoi du fichier impossible.');
        },
      });
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving()) return;
    const body: SaveAppGuideRequest = {
      title: f.title.trim(),
      description: f.description.trim() || null,
      kind: f.kind,
      mediaAssetId: f.kind === 'Link' ? null : f.mediaAssetId,
      externalUrl: f.externalUrl.trim() || null,
      targetRoles: f.targetRoles,
      sortOrder: Number(f.sortOrder) || 0,
      isActive: f.isActive,
    };
    this.saving.set(true);
    this.formError.set(null);
    const request$ = f.id == null ? this.guides.create(body) : this.guides.update(f.id, body);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.saving.set(false);
        this.form.set(null);
        this.reloadAdmin();
        this.reload();
      },
      error: (err) => {
        this.saving.set(false);
        this.formError.set(err?.error?.message ?? 'Enregistrement impossible.');
      },
    });
  }

  toggleActive(g: AppGuideAdmin): void {
    const body: SaveAppGuideRequest = {
      title: g.title,
      description: g.description ?? null,
      kind: g.kind as AppGuideKind,
      mediaAssetId: g.mediaAssetId ?? null,
      externalUrl: g.externalUrl ?? null,
      targetRoles: g.targetRoles,
      sortOrder: g.sortOrder,
      isActive: !g.isActive,
    };
    this.guides
      .update(g.id, body)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.reloadAdmin();
          this.reload();
        },
        error: (err) => this.error.set(err?.error?.message ?? 'Mise à jour impossible.'),
      });
  }

  kindLabel(kind: string): string {
    return KIND_LABELS[kind] ?? kind;
  }

  rolesLabel(roles: string[]): string {
    return roles.length ? roles.join(', ') : 'Tous les rôles';
  }
}
