import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PlatformSectionService } from '../platform-section.service';
import { PlatformAuditService, type ServiceHealthRow } from '../services/platform-audit.service';

@Component({
  selector: 'app-admin-overview',
  standalone: true,
  imports: [],
  template: `
    <div class="ov-health">
      <h3>Santé des services</h3>
      <div class="ov-health-grid">
        @for (h of health(); track h.id) {
          <div class="ov-health-item" [class.ok]="h.ok" [class.ko]="!h.ok">
            <strong>{{ h.label }}</strong>
            <span>{{ h.ok ? 'OK' : h.detail }}</span>
          </div>
        }
      </div>
    </div>
    <div class="ov-grid">
      @for (card of cards; track card.title) {
        <button type="button" class="ov-card" (click)="open(card)">
          <strong>{{ card.title }}</strong>
          <span>{{ card.text }}</span>
        </button>
      }
    </div>
  `,
  styles: [`
    .ov-health { margin-bottom: 16px; }
    .ov-health h3 { margin: 0 0 10px; color: var(--text-primary); font-size: 0.95rem; }
    .ov-health-grid { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }
    .ov-health-item {
      border: 1px solid var(--border-color); background: var(--bg-card); border-radius: 0.6rem;
      padding: 8px 12px; min-width: 9rem;
    }
    .ov-health-item strong { display: block; color: var(--text-primary); font-size: 0.82rem; }
    .ov-health-item span { font-size: 0.78rem; color: var(--text-muted); }
    .ov-health-item.ok span { color: #10b981; }
    .ov-health-item.ko span { color: #f43f5e; }
    .ov-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 12px; }
    .ov-card {
      text-align: left; border: 1px solid var(--border-color); background: var(--bg-card);
      border-radius: 0.875rem; padding: 16px; cursor: pointer;
    }
    .ov-card strong { display: block; color: var(--text-primary); margin-bottom: 6px; }
    .ov-card span { color: var(--text-muted); font-size: 0.85rem; }
    .ov-card:hover strong { color: var(--electric-blue); }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminOverviewComponent implements OnInit {
  private readonly platform = inject(PlatformSectionService);
  private readonly router = inject(Router);
  private readonly audit = inject(PlatformAuditService);
  readonly health = signal<ServiceHealthRow[]>([]);

  readonly cards = [
    { title: 'Accès par rôle', text: 'Quelles interfaces chaque rôle peut voir et quelles actions il peut faire.', section: 'access' as const },
    { title: 'Organisation & sync', text: 'Santé du miroir Planning ↔ Directory et actions de réconciliation.', section: 'sync' as const },
    { title: 'Inbox anomalies', text: 'Clôturer ou ignorer les anomalies Prime depuis la console globale.', section: 'anomalies' as const },
    { title: 'Configuration modules', text: 'Liens vers les réglages métier (types de documents, horaires, barèmes…).', section: 'modules' as const },
    { title: 'Utilisateurs', text: 'Fiches employés, rôles JWT, import et champs personnalisés.', route: '/users' },
  ];

  ngOnInit(): void {
    void firstValueFrom(this.audit.loadServiceHealth()).then((rows) => this.health.set(rows));
  }

  async open(card: (typeof this.cards)[number]): Promise<void> {
    if ('section' in card && card.section) {
      await this.platform.goAdmin(card.section);
      return;
    }
    if ('route' in card && card.route) {
      await this.router.navigateByUrl(card.route);
    }
  }
}
