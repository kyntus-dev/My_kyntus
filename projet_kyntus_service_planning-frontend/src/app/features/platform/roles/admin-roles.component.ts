import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { RolesAdminService, type RoleAdminDto } from '../../../core/services/roles-admin.service';
import { PlatformIamApiService } from '../../../core/platform-access/platform-iam-api.service';
import { PlatformSectionService } from '../platform-section.service';
import { KyntusToastService } from '../../../shared/components/ui/kyntus-toast.service';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { libelleRole } from '../../../core/org/role-libelle';
import { retraitDroits, transfertDroits } from './roles-iam.util';

type Erreur = { error?: { message?: string } };

/**
 * Gestion des rôles (Admin) : créer, renommer, décrire, supprimer. Un nouveau rôle n'a aucun
 * droit : ils se cochent dans « Accès par rôle ». À la suppression, les comptes sont basculés
 * automatiquement vers le rôle de remplacement (fiche, connexion, annuaire).
 */
@Component({
  selector: 'app-admin-roles',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './admin-roles.component.html',
  styleUrls: ['./admin-roles.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminRolesComponent implements OnInit {
  private readonly svc = inject(RolesAdminService);
  private readonly iam = inject(PlatformIamApiService);
  private readonly platform = inject(PlatformSectionService);
  private readonly toast = inject(KyntusToastService);
  private readonly confirm = inject(KyntusConfirmService);

  readonly libelleRole = libelleRole;
  readonly roles = signal<RoleAdminDto[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly enCours = signal(false);
  readonly recherche = signal('');

  // Création
  readonly nouveauNom = signal('');
  readonly nouvelleDescription = signal('');

  // Modification en ligne
  readonly edition = signal<number | null>(null);
  readonly editionNom = signal('');
  readonly editionDescription = signal('');

  // Suppression
  readonly suppression = signal<RoleAdminDto | null>(null);
  readonly remplacementId = signal<number | null>(null);

  readonly visibles = computed(() => {
    const q = this.recherche().trim().toLowerCase();
    return this.roles().filter((r) =>
      !q || libelleRole(r.name).toLowerCase().includes(q) || (r.description ?? '').toLowerCase().includes(q));
  });
  readonly crees = computed(() => this.roles().filter((r) => !r.systeme));
  readonly totalComptes = computed(() => this.roles().reduce((n, r) => n + r.nbComptes, 0));
  readonly remplacements = computed(() => {
    const s = this.suppression();
    return this.roles().filter((r) => r.id !== s?.id);
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.lister().subscribe({
      next: (r) => {
        this.roles.set(r);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403 ? 'Réservé à l’Admin.' : err?.error?.message ?? 'Impossible de charger les rôles.');
      },
    });
  }

  async creer(): Promise<void> {
    const name = this.nouveauNom().trim();
    if (!name || this.enCours()) return;
    this.enCours.set(true);
    try {
      const r = await firstValueFrom(this.svc.creer({ name, description: this.nouvelleDescription().trim() || null }));
      this.nouveauNom.set('');
      this.nouvelleDescription.set('');
      this.toast.success(`Rôle « ${r.name} » créé, sans aucun droit. Cochez ses accès dans « Accès par rôle ».`, 6000);
      this.load();
    } catch (err) {
      this.toast.error((err as Erreur)?.error?.message ?? 'Création impossible.');
    } finally {
      this.enCours.set(false);
    }
  }

  modifier(r: RoleAdminDto): void {
    this.suppression.set(null);
    this.edition.set(r.id);
    this.editionNom.set(r.name);
    this.editionDescription.set(r.description ?? '');
  }

  annuler(): void {
    this.edition.set(null);
    this.suppression.set(null);
  }

  async enregistrer(r: RoleAdminDto): Promise<void> {
    if (this.enCours()) return;
    const name = r.systeme ? r.name : this.editionNom().trim();
    this.enCours.set(true);
    try {
      const maj = await firstValueFrom(this.svc.modifier(r.id, { name, description: this.editionDescription().trim() || null }));
      if (maj.name !== r.name) {
        // Les droits suivent le nouveau nom dans « Accès par rôle ».
        const rows = await firstValueFrom(this.iam.listPermissions(r.name));
        const items = transfertDroits(rows, r.name, maj.name);
        if (items.length) await firstValueFrom(this.iam.upsertBulk(items));
      }
      this.toast.success(maj.name !== r.name
        ? `Rôle renommé en « ${maj.name} ». Les comptes concernés sont mis à jour automatiquement.`
        : 'Description enregistrée.');
      this.edition.set(null);
      this.load();
    } catch (err) {
      this.toast.error((err as Erreur)?.error?.message ?? 'Modification impossible.');
    } finally {
      this.enCours.set(false);
    }
  }

  demanderSuppression(r: RoleAdminDto): void {
    this.edition.set(null);
    this.suppression.set(r);
    this.remplacementId.set(null);
  }

  async supprimer(): Promise<void> {
    const r = this.suppression();
    if (!r || this.enCours()) return;
    const cible = this.roles().find((x) => x.id === this.remplacementId()) ?? null;
    if (r.nbComptes > 0 && !cible) {
      this.toast.error('Choisissez le rôle qui remplace celui-ci.');
      return;
    }
    const ok = await this.confirm.confirm({
      title: `Supprimer le rôle « ${r.name} » ?`,
      message: r.nbComptes > 0
        ? `${r.nbComptes} compte(s) passeront au rôle « ${libelleRole(cible!.name)} » (fiche, connexion et annuaire), puis le rôle sera supprimé.`
        : 'Aucun compte n’a ce rôle. Il sera supprimé avec ses droits.',
      confirmLabel: 'Supprimer',
      cancelLabel: 'Annuler',
      variant: 'danger',
    });
    if (!ok) return;
    this.enCours.set(true);
    try {
      const res = await firstValueFrom(this.svc.supprimer(r.id, cible?.id ?? null));
      try {
        const rows = await firstValueFrom(this.iam.listPermissions(r.name));
        const items = retraitDroits(rows, r.name);
        if (items.length) await firstValueFrom(this.iam.upsertBulk(items));
      } catch {
        // Les droits d'un rôle supprimé ne servent plus à personne : sans conséquence.
      }
      this.toast.success(res.comptesReaffectes
        ? `Rôle supprimé : ${res.comptesReaffectes} compte(s) basculé(s) vers « ${libelleRole(res.remplacement)} ».`
        : 'Rôle supprimé.');
      this.suppression.set(null);
      this.load();
    } catch (err) {
      this.toast.error((err as Erreur)?.error?.message ?? 'Suppression impossible.');
    } finally {
      this.enCours.set(false);
    }
  }

  ouvrirAcces(): void {
    void this.platform.goAdmin('access');
  }
}
