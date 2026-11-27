import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MICROSERVICES } from '../../../../core/navigation/microservices.config';
import { apercuMenu } from '../../../../core/navigation/menu-apercu.util';
import { libelleRole } from '../../../../core/org/role-libelle';
import {
  PLATFORM_ROLES,
  PLATFORM_SCREENS,
  defaultScopeForRole,
} from '../../../../core/platform-access/platform-capability.catalog';
import { PlatformIamApiService, type IamPermissionDto } from '../../../../core/platform-access/platform-iam-api.service';
import { RolesAdminService } from '../../../../core/services/roles-admin.service';
import { KyTrTiroirComponent } from '../../../../shared/components/ky-traitement/ky-traitement.components';
import { aJoker, clesDesLignes, lignesDuRole } from '../../../platform/pages/role-access.util';
import { rolesMatrice } from '../../../platform/roles/roles-iam.util';
import { apercuRole } from './voir-comme.util';

/**
 * v88 — « Voir comme » (Super Admin, depuis l'accueil) : accueil et menu d'un rôle, en lecture seule.
 * Mêmes données et mêmes règles que l'aperçu de « Accès par rôle » (lecture des droits du rôle) ;
 * la session reste celle du Super Admin, rien n'est modifié, aucune donnée d'un autre compte n'est lue.
 */
@Component({
  selector: 'app-voir-comme',
  standalone: true,
  imports: [KyTrTiroirComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ky-tr-tiroir [ouvert]="ouvert" titre="Voir comme…" sousTitre="Accueil et menu d’un rôle — lecture seule" [avatar]="false" (fermer)="fermer.emit()">
      <label class="vc-choix">
        <small>Rôle</small>
        <select class="ky-input" [value]="role()" (change)="role.set($any($event.target).value)" aria-label="Rôle à afficher">
          @for (r of roles(); track r) { <option [value]="r">{{ libelle(r) }}</option> }
        </select>
      </label>

      @if (chargement()) {
        <p class="ky-tr-note">Chargement des droits…</p>
      } @else if (erreur()) {
        <p class="ky-tr-alerte" data-ton="bad">{{ erreur() }}</p>
      } @else if (apercu(); as a) {
        <p class="ky-tr-alerte">Aperçu sans chiffres : les valeurs dépendent du périmètre de chaque compte. Les droits
          accordés compte par compte (« Accès par compte ») peuvent ajouter ou retirer des pages.</p>
        <section>
          <h4>Accueil</h4>
          <div class="vc-bandeau"><strong>{{ a.titre }}</strong><small>{{ a.sousTitre }}</small></div>
        </section>
        <section>
          <h4>Indicateurs ({{ a.indicateurs.length }})</h4>
          <div class="vc-puces">@for (i of a.indicateurs; track i) { <span>{{ i }}</span> } @empty { <em>Aucun</em> }</div>
        </section>
        <section>
          <h4>Raccourcis par défaut</h4>
          <ul class="vc-liste">@for (r of a.raccourcis; track r) { <li>{{ r }}</li> } @empty { <li><em>Aucun</em></li> }</ul>
        </section>
        <section>
          <h4>Menu ({{ a.menu.length }} modules · {{ a.nbEntrees }} pages)</h4>
          @for (g of a.menu; track g.id) {
            <details class="vc-groupe">
              <summary>{{ g.label }} <span>{{ g.module ? 'menu propre au module' : g.entrees.length }}</span></summary>
              <ul class="vc-liste">@for (e of g.entrees; track e) { <li>{{ e }}</li> }</ul>
            </details>
          }
        </section>
      }
    </ky-tr-tiroir>
  `,
  styles: [`
    .vc-choix { display: flex; flex-direction: column; gap: 4px; }
    .vc-choix small { font-size: 11.5px; color: var(--text-muted); }
    .vc-bandeau { display: flex; flex-direction: column; gap: 3px; padding: 12px 14px; border-radius: 12px; background: var(--bg-input); border: 1px solid var(--border-color); }
    .vc-bandeau small { color: var(--text-muted); font-size: 12.5px; }
    .vc-puces { display: flex; flex-wrap: wrap; gap: 6px; }
    .vc-puces span { padding: 4px 10px; border-radius: 999px; font-size: 12.5px; background: var(--bg-input); border: 1px solid var(--border-color); }
    .vc-liste { margin: 0; padding-left: 18px; display: flex; flex-direction: column; gap: 3px; font-size: 13px; }
    .vc-groupe { border-bottom: 1px solid var(--border-color); padding: 7px 0; }
    .vc-groupe summary { cursor: pointer; font-size: 13.5px; font-weight: 600; display: flex; justify-content: space-between; gap: 8px; }
    .vc-groupe summary span { font-weight: 500; color: var(--text-muted); font-size: 12px; }
    .vc-groupe .vc-liste { margin-top: 6px; }
  `],
})
export class VoirCommeComponent implements OnChanges {
  @Input() ouvert = false;
  @Output() fermer = new EventEmitter<void>();

  private readonly api = inject(PlatformIamApiService);
  private readonly rolesAdmin = inject(RolesAdminService);

  readonly roles = signal<string[]>([...PLATFORM_ROLES]);
  readonly role = signal<string>('Superviseur');
  readonly permissions = signal<IamPermissionDto[] | null>(null);
  readonly chargement = signal(false);
  readonly erreur = signal('');

  readonly apercu = computed(() => {
    const perms = this.permissions();
    if (!perms) return null;
    const r = this.role();
    const menu = aJoker(perms, r)
      ? apercuMenu(r, ['*:Global'], MICROSERVICES)
      : apercuMenu(r, clesDesLignes(lignesDuRole(r, perms, PLATFORM_SCREENS, (s) => defaultScopeForRole(r, s))), MICROSERVICES);
    return apercuRole(r, menu);
  });

  libelle(r: string): string {
    return libelleRole(r);
  }

  ngOnChanges(): void {
    if (this.ouvert && !this.permissions() && !this.chargement()) void this.charger();
  }

  /** Lecture des droits par rôle et de la liste des rôles (mêmes appels que « Accès par rôle »). */
  private async charger(): Promise<void> {
    this.chargement.set(true);
    this.erreur.set('');
    try {
      const [perms, roles] = await Promise.all([
        firstValueFrom(this.api.listPermissions()),
        firstValueFrom(this.rolesAdmin.lister()).catch(() => []),
      ]);
      this.roles.set(rolesMatrice(PLATFORM_ROLES, roles.filter((x) => !x.systeme).map((x) => x.name)));
      this.permissions.set(perms);
    } catch {
      this.erreur.set('Impossible de lire les droits des rôles (service IAM indisponible).');
    } finally {
      this.chargement.set(false);
    }
  }
}
