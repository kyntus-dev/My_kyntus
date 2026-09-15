import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, DestroyRef, inject, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, exhaustMap, filter, of, timer } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { HtelApiService, HtelLiaisonsReportDto } from '../services/htel-api.service';

@Component({
  selector: 'app-htel-inbox', standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    @if (enabled) {
      <section class="ky-card htel-inbox" aria-label="Suivi des collaborateurs HTEL" aria-live="polite">
        <div><strong>Collaborateurs HTEL</strong>
          @if (report && !failed) {
            <p>{{ newCount }} nouveau(x) à compléter · {{ report.ambiguous.length }} correspondance(s) à vérifier ·
              {{ report.missingFromHtel.length }} fiche(s) absente(s) ou sans correspondance · {{ inactiveCount }} statut(s) inactif HTEL à vérifier.</p>
            <small>Comparaison du {{ report.checkedAtUtc | date:'dd/MM/yyyy HH:mm' }}. Actualisation toutes les 5 minutes sur cette page.</small>
          } @else if (failed) {
            <p>Comparaison HTEL indisponible. Ouvrez le suivi pour actualiser ; aucune absence n’est déduite de cette erreur.</p>
          } @else { <p>Recherche des collaborateurs à intégrer…</p> }
        </div>
        <a class="ky-btn-secondary" routerLink="/users/htel-liaisons">Ouvrir le suivi HTEL</a>
      </section>
    }
  `,
  styles: [`.htel-inbox { display:flex; justify-content:space-between; align-items:center; gap:1rem; padding:1rem; margin-bottom:1rem; flex-wrap:wrap; } p { margin:.35rem 0; }`],
})
export class HtelInboxComponent implements OnInit {
  private readonly api = inject(HtelApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);
  readonly enabled = ['admin', 'rh'].includes(inject(AuthService).getRole().toLowerCase());
  report: HtelLiaisonsReportDto | null = null;
  failed = false;
  get newCount(): number { return this.report?.orphansHtel.filter(t => t.actif === 1).length ?? 0; }
  get inactiveCount(): number { return this.report?.linked.filter(e => e.isActive && e.htelActif === 0).length ?? 0; }
  ngOnInit(): void {
    if (!this.enabled) return;
    timer(0, 300_000).pipe(
      filter(() => !document.hidden),
      exhaustMap(() => this.api.getLiaisons().pipe(catchError(() => of(null)))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(report => {
      this.failed = !report;
      this.report = report;
      this.cdr.detectChanges();
    });
  }
}
