import { ChangeDetectionStrategy, Component, ElementRef, ViewChild, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, forkJoin } from 'rxjs';
import { PlanningExportService, type PlanningExportDto } from '../../core/services/planning-export.service';
import { PlanningItService } from '../../core/services/planning-it.service';
import type { PlanningItSemaineDto } from '../../core/models/planning-it.models';
import { KyntusToastService } from '../components/ui/kyntus-toast.service';
import {
  type CaseExport,
  type ModePeriode,
  type PeriodeExport,
  type TableauExport,
  jours,
  lundis,
  moisDePaie,
  periodePerso,
  semaine,
  telechargerPlanning,
  ymd,
} from './planning-export.util';

/**
 * Bouton « Exporter (Excel) » des plannings RH et IT : choix d'une semaine, d'un mois de paie
 * (24 → 23) ou d'une période personnalisée, puis fichier Excel (une ligne par collaborateur,
 * une colonne par jour, totaux).
 */
@Component({
  selector: 'app-planning-export-button',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" class="pex-btn" (click)="ouvrir()">
      <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" d="M12 3v12m0 0-4-4m4 4 4-4M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2"/></svg>
      Exporter (Excel)
    </button>

    <dialog #modal class="pex-modal" [attr.aria-label]="titre()">
      <form (ngSubmit)="exporter()">
        <h2>{{ titre() }}</h2>

        <div class="pex-seg" role="tablist">
          <button type="button" [class.on]="mode() === 'semaine'" (click)="choisirMode('semaine')">Semaine</button>
          <button type="button" [class.on]="mode() === 'paie'" (click)="choisirMode('paie')">Mois de paie</button>
          <button type="button" [class.on]="mode() === 'perso'" (click)="choisirMode('perso')">Personnalisée</button>
        </div>

        @if (mode() !== 'perso') {
          <div class="pex-nav">
            <button type="button" (click)="decaler(-1)" aria-label="Précédent">‹</button>
            <span>{{ periode()?.libelle }}</span>
            <button type="button" (click)="decaler(1)" aria-label="Suivant">›</button>
          </div>
        } @else {
          <div class="pex-dates">
            <label><span>Du</span><input type="date" [ngModel]="du()" (ngModelChange)="du.set($event)" [ngModelOptions]="{ standalone: true }" /></label>
            <label><span>Au</span><input type="date" [ngModel]="au()" (ngModelChange)="au.set($event)" [ngModelOptions]="{ standalone: true }" /></label>
          </div>
          @if (trop()) { <p class="pex-err">93 jours au plus.</p> }
        }

        <p class="pex-aide">
          Une ligne par collaborateur{{ type() === 'rh' ? ' de votre périmètre' : ' de la cellule IT' }}, une colonne par jour
          {{ type() === 'rh' ? '(horaires du planning publié, congés, fériés)' : '(heure d’arrivée, repos, congé)' }} et les totaux.
        </p>

        <footer>
          <button type="button" class="pex-sec" (click)="fermer()">Annuler</button>
          <button type="submit" class="pex-pri" [disabled]="!periode() || trop() || enCours()">
            {{ enCours() ? 'Préparation…' : 'Télécharger' }}
          </button>
        </footer>
      </form>
    </dialog>
  `,
  styles: [`
    :host { display: inline-block; }
    .pex-btn {
      display: inline-flex; align-items: center; gap: 6px; min-height: 36px; padding: 0 14px; border-radius: var(--radius-md, 8px);
      border: 1px solid var(--border-color); background: var(--bg-card); color: var(--text-primary); font: inherit; font-weight: 600;
      font-size: 0.86rem; cursor: pointer;
    }
    .pex-btn:hover { border-color: var(--ky-blue); color: var(--ky-blue); }
    .pex-modal {
      padding: 0; border: 1px solid var(--border-color); border-radius: var(--radius-lg, 12px); width: min(460px, calc(100vw - 32px));
      background: var(--bg-card); color: var(--text-primary); box-shadow: var(--shadow-3);
    }
    .pex-modal::backdrop { background: rgb(15 23 42 / 0.35); }
    form { display: flex; flex-direction: column; gap: 14px; padding: 20px; }
    h2 { margin: 0; font-size: 1.05rem; }
    .pex-seg { display: grid; grid-template-columns: repeat(3, 1fr); gap: 2px; padding: 3px; border-radius: var(--radius-md, 8px); background: var(--bg-input, var(--bg-primary)); }
    .pex-seg button { min-height: 32px; border: 0; border-radius: 6px; background: transparent; color: var(--text-muted); font: inherit; font-weight: 600; font-size: 0.82rem; cursor: pointer; }
    .pex-seg button.on { background: var(--bg-card); color: var(--ky-blue); box-shadow: var(--shadow-1); }
    .pex-nav { display: grid; grid-template-columns: 36px 1fr 36px; align-items: center; gap: 8px; text-align: center; font-weight: 600; font-size: 0.88rem; }
    .pex-nav button { height: 36px; border: 1px solid var(--border-color); border-radius: var(--radius-md, 8px); background: var(--bg-card); color: var(--text-primary); font-size: 1.1rem; cursor: pointer; }
    .pex-dates { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .pex-dates label { display: flex; flex-direction: column; gap: 4px; font-size: 0.74rem; font-weight: 600; color: var(--text-muted); }
    .pex-dates input { min-height: 36px; padding: 0 10px; border: 1px solid var(--border-color); border-radius: var(--radius-md, 8px); background: var(--bg-input, var(--bg-card)); color: var(--text-primary); font: inherit; }
    .pex-aide { margin: 0; font-size: 0.78rem; color: var(--text-muted); }
    .pex-err { margin: 0; font-size: 0.78rem; color: var(--danger); font-weight: 600; }
    footer { display: flex; justify-content: flex-end; gap: 8px; }
    .pex-sec, .pex-pri { min-height: 36px; padding: 0 16px; border-radius: var(--radius-md, 8px); font: inherit; font-weight: 600; cursor: pointer; }
    .pex-sec { border: 1px solid var(--border-color); background: var(--bg-card); color: var(--text-primary); }
    .pex-pri { border: 0; background: var(--ky-blue); color: #fff; }
    .pex-pri:disabled { opacity: 0.55; cursor: default; }
  `],
})
export class PlanningExportButtonComponent {
  private readonly exportRh = inject(PlanningExportService);
  private readonly planningIt = inject(PlanningItService);
  private readonly toast = inject(KyntusToastService);
  @ViewChild('modal') private modal?: ElementRef<HTMLDialogElement>;

  /** « rh » : planning publié ; « it » : planning IT. */
  readonly type = input<'rh' | 'it'>('rh');

  readonly mode = signal<ModePeriode>('semaine');
  readonly decalage = signal(0);
  readonly du = signal('');
  readonly au = signal('');
  readonly enCours = signal(false);

  readonly titre = computed(() => (this.type() === 'rh' ? 'Exporter le planning' : 'Exporter le planning IT'));
  readonly periode = computed<PeriodeExport | null>(() => {
    if (this.mode() === 'semaine') return semaine(this.decalage());
    if (this.mode() === 'paie') return moisDePaie(this.decalage());
    return periodePerso(this.du(), this.au());
  });
  readonly trop = computed(() => {
    const p = this.periode();
    return !!p && jours(p.du, p.au).length > 93;
  });

  ouvrir(): void {
    if (!this.du()) {
      const s = semaine(0);
      this.du.set(s.du);
      this.au.set(s.au);
    }
    this.modal?.nativeElement.showModal();
  }

  fermer(): void {
    this.modal?.nativeElement.close();
  }

  choisirMode(m: ModePeriode): void {
    this.mode.set(m);
    this.decalage.set(0);
  }

  decaler(delta: number): void {
    this.decalage.set(this.decalage() + delta);
  }

  async exporter(): Promise<void> {
    const p = this.periode();
    if (!p || this.trop() || this.enCours()) return;
    this.enCours.set(true);
    try {
      const tableau = this.type() === 'rh' ? await this.tableauRh(p) : await this.tableauIt(p);
      if (!tableau.lignes.length) {
        this.toast.info('Aucun collaborateur à exporter sur cette période.');
        return;
      }
      const prefixe = this.type() === 'rh' ? 'planning' : 'planning-it';
      await telechargerPlanning(`${prefixe}_${p.du}_${p.au}.xlsx`, `${p.du} au ${p.au}`, tableau);
      this.toast.success(`Planning exporté : ${tableau.lignes.length} collaborateur(s).`);
      this.fermer();
    } catch (err) {
      const e = err as { status?: number; error?: { message?: string } };
      this.toast.error(e?.status === 403
        ? 'Export réservé aux rôles autorisés dans « Accès par rôle ».'
        : e?.error?.message ?? 'Export impossible.');
    } finally {
      this.enCours.set(false);
    }
  }

  private async tableauRh(p: PeriodeExport): Promise<TableauExport> {
    const d: PlanningExportDto = await firstValueFrom(this.exportRh.planningRh(p.du, p.au));
    const liste = jours(d.du.slice(0, 10), d.au.slice(0, 10));
    return {
      titre: `Planning — ${p.libelle} — ${d.perimetre}`,
      colonnesInfo: ['Pôle', 'Cellule', 'Service'],
      jours: liste,
      lignes: d.agents.map((a) => {
        const parJour = new Map(a.jours.map((j) => [j.jour.slice(0, 10), j]));
        return {
          nom: a.nom,
          infos: [a.pole ?? '', a.cellule ?? '', a.service ?? ''],
          cases: liste.map((jour): CaseExport => {
            const j = parJour.get(jour);
            if (!j || (!j.libelle && !j.debut && !j.conge && !j.ferie)) return { texte: 'Repos', nature: 'repos', minutes: 0 };
            if (j.conge) return { texte: 'Congé', nature: 'conge', minutes: 0 };
            if (j.ferie) return { texte: 'Férié', nature: 'ferie', minutes: 0 };
            const horaire = j.debut ? `${j.debut}–${j.fin ?? ''}` : j.libelle ?? '';
            return { texte: j.renfort ? `${horaire} (renfort)` : horaire, nature: 'travail', minutes: j.minutes };
          }),
        };
      }),
    };
  }

  private async tableauIt(p: PeriodeExport): Promise<TableauExport> {
    const semaines: PlanningItSemaineDto[] = await firstValueFrom(forkJoin(lundis(p.du, p.au).map((l) => this.planningIt.semaine(l))));
    const liste = jours(p.du, p.au);
    const membres = new Map<number, { nom: string; role: string; cases: Map<string, CaseExport> }>();
    for (const s of semaines) {
      for (const m of s.membres) {
        const ligne = membres.get(m.userId) ?? { nom: m.collaborateur, role: m.role ?? '', cases: new Map<string, CaseExport>() };
        for (const j of m.jours) {
          const jour = j.jour.slice(0, 10);
          if (j.statut === 'Travail') ligne.cases.set(jour, { texte: j.arrivee ?? 'Travail', nature: 'travail', minutes: 0 });
          else if (j.statut === 'Conge') ligne.cases.set(jour, { texte: 'Congé', nature: 'conge', minutes: 0 });
          else if (j.statut === 'Repos') ligne.cases.set(jour, { texte: 'Repos', nature: 'repos', minutes: 0 });
        }
        membres.set(m.userId, ligne);
      }
    }
    return {
      titre: `Planning IT — ${p.libelle} — extrait le ${ymd(new Date()).split('-').reverse().join('/')}`,
      colonnesInfo: ['Rôle'],
      jours: liste,
      sansHeures: true,
      lignes: [...membres.values()]
        .sort((a, b) => a.nom.localeCompare(b.nom, 'fr'))
        .map((m) => ({
          nom: m.nom,
          infos: [m.role],
          cases: liste.map((jour) => m.cases.get(jour) ?? { texte: '', nature: 'vide', minutes: 0 }),
        })),
    };
  }
}
