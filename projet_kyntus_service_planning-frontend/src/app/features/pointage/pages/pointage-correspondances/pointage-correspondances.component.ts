import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { PointageService } from '../../../../core/services/pointage.service';
import type {
  PointageCorrespondanceDto,
  PointageCorrespondanceStatut,
  PointageEtatDto,
} from '../../../../core/models/pointage.models';
import { UserService } from '../../../users/services/user.service';
import type { User } from '../../../users/users-module';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { AuthService } from '../../../../core/services/auth.service';

const STATUTS: Record<PointageCorrespondanceStatut, string> = {
  AValider: 'À traiter',
  Auto: 'Proposé (par le nom)',
  Valide: 'Validé',
  Ignore: 'Ignoré',
};

/**
 * Correspondance pointeuses (RH / Admin) : relie chaque ID pointeuse ZKBio Time à un collaborateur.
 * Les rapprochements par le nom sont proposés automatiquement ; RH les valide et traite le reste.
 */
@Component({
  selector: 'app-pointage-correspondances',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent],
  templateUrl: './pointage-correspondances.component.html',
  styleUrls: ['../pointage-suivi/pointage-suivi.component.css', './pointage-correspondances.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PointageCorrespondancesComponent implements OnInit {
  private readonly svc = inject(PointageService);
  private readonly users = inject(UserService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);
  private readonly auth = inject(AuthService);

  readonly statutsLabels = STATUTS;
  readonly rows = signal<PointageCorrespondanceDto[]>([]);
  readonly collaborateurs = signal<User[]>([]);
  readonly etat = signal<PointageEtatDto | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly filtre = signal<PointageCorrespondanceStatut | ''>('AValider');
  readonly recherche = signal('');
  /** Choix en cours par ID pointeuse (avant enregistrement). */
  readonly choix = signal<Record<string, string>>({});

  readonly isAdmin = computed(() => (this.auth.getRole() || '').toLowerCase() === 'admin');
  readonly compte = computed(() => {
    const c: Record<string, number> = { AValider: 0, Auto: 0, Valide: 0, Ignore: 0 };
    for (const r of this.rows()) c[r.statut] = (c[r.statut] ?? 0) + 1;
    return c;
  });
  readonly affichees = computed(() => {
    const q = this.recherche().trim().toLowerCase();
    return this.rows().filter((r) =>
      (!this.filtre() || r.statut === this.filtre())
      && (!q || `${r.empCode} ${r.nomBioTime ?? ''} ${r.collaborateur ?? ''} ${r.departementBioTime ?? ''}`.toLowerCase().includes(q)));
  });
  readonly options = computed(() =>
    this.collaborateurs()
      .filter((u) => u.isActive !== false)
      .map((u) => ({ id: u.id, label: `${u.lastName} ${u.firstName}`.trim() + (u.subServiceName ? ` — ${u.subServiceName}` : '') }))
      .sort((a, b) => a.label.localeCompare(b.label, 'fr', { sensitivity: 'base' })));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    forkJoin({
      rows: this.svc.correspondances(),
      users: this.users.getAllUsers(),
      etat: this.svc.etat(),
    }).subscribe({
      next: ({ rows, users, etat }) => {
        this.rows.set(rows);
        this.collaborateurs.set(users);
        this.etat.set(etat);
        this.choix.set({});
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.error(err?.error?.message ?? 'Impossible de charger les correspondances.');
      },
    });
  }

  valeur(r: PointageCorrespondanceDto): string {
    return this.choix()[r.empCode] ?? (r.userId != null ? String(r.userId) : '');
  }

  choisir(r: PointageCorrespondanceDto, value: string): void {
    this.choix.set({ ...this.choix(), [r.empCode]: value });
  }

  enregistrer(r: PointageCorrespondanceDto): void {
    const v = this.valeur(r);
    this.update(r, { userId: v ? Number(v) : null, ignorer: false }, v ? 'Correspondance validée.' : 'Remis à traiter.');
  }

  ignorer(r: PointageCorrespondanceDto): void {
    this.update(r, { userId: null, ignorer: true }, 'ID pointeuse ignoré.');
  }

  async validerTout(): Promise<void> {
    const n = this.compte()['Auto'] ?? 0;
    if (!n) return;
    const ok = await this.confirm.confirm({
      title: 'Valider les propositions',
      message: `${n} rapprochement(s) par le nom seront validés. Vérifiez d’abord la liste « Proposé » en cas de doute.`,
      confirmLabel: 'Valider',
    });
    if (!ok) return;
    this.busy.set(true);
    this.svc.validerAuto().subscribe({
      next: (r) => {
        this.busy.set(false);
        this.toast.success(`${r.validees} correspondance(s) validée(s).`);
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.toast.error(err?.error?.message ?? 'Validation impossible.');
      },
    });
  }

  synchroniser(): void {
    this.busy.set(true);
    this.svc.synchroniser().subscribe({
      next: (r) => {
        this.busy.set(false);
        this.toast.success(`${r.ajoutes} nouveau(x) pointage(s).`);
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.toast.error(err?.error?.message ?? 'Synchronisation impossible.');
      },
    });
  }

  formatDateTime(value: string | null): string {
    if (!value) return '—';
    const d = new Date(value);
    return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
  }

  private update(r: PointageCorrespondanceDto, body: { userId: number | null; ignorer: boolean }, message: string): void {
    this.busy.set(true);
    this.svc.updateCorrespondance(r.empCode, body).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.rows.set(this.rows().map((x) => (x.empCode === updated.empCode ? updated : x)));
        const { [r.empCode]: _, ...rest } = this.choix();
        this.choix.set(rest);
        this.toast.success(message);
      },
      error: (err) => {
        this.busy.set(false);
        this.toast.error(err?.error?.message ?? 'Enregistrement impossible.');
      },
    });
  }
}
