import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';
import { PlatformSectionService } from '../platform-section.service';
import { AuditDashboardComponent } from './audit-dashboard.component';
import { AuditJournalComponent } from './audit-journal.component';
import { AuditAccessHistoryComponent } from './audit-access-history.component';
import { AuditAnomaliesComponent } from './audit-anomalies.component';
import { AuditReportingComponent } from './audit-reporting.component';

@Component({
  selector: 'app-audit-root',
  standalone: true,
  imports: [
    KyntusPageHeaderComponent,
    AuditDashboardComponent,
    AuditJournalComponent,
    AuditAccessHistoryComponent,
    AuditAnomaliesComponent,
    AuditReportingComponent,
  ],
  template: `
    <section class="plat-page">
      <app-kyntus-page-header
        title="Audit et conformité"
        subtitle="Lecture seule — journal unifié, anomalies et reporting, avec filtre par module."
      />
      <nav class="plat-tabs" aria-label="Sections audit">
        @for (tab of visibleTabs(); track tab.id) {
          <button
            type="button"
            class="plat-tab"
            [class.active]="platform.auditSection() === tab.id"
            (click)="platform.goAudit(tab.id)"
          >
            {{ tab.label }}
          </button>
        }
      </nav>
      @switch (platform.auditSection()) {
        @case ('dashboard') { <app-audit-dashboard /> }
        @case ('journal') { <app-audit-journal /> }
        @case ('access-history') { <app-audit-access-history /> }
        @case ('anomalies') { <app-audit-anomalies /> }
        @case ('reporting') { <app-audit-reporting /> }
        @default { <app-audit-dashboard /> }
      }
    </section>
  `,
  styles: [`
    .plat-page { display: flex; flex-direction: column; gap: 1.25rem; padding: 8px 4px 32px; }
    .plat-tabs { display: flex; flex-wrap: wrap; gap: 8px; }
    .plat-tab {
      border: 1px solid var(--border-color);
      background: var(--bg-card);
      color: var(--text-muted);
      border-radius: 0.6rem;
      padding: 8px 14px;
      font-size: 0.85rem;
      cursor: pointer;
    }
    .plat-tab.active {
      border-color: var(--blue-600);
      color: var(--electric-blue);
      font-weight: 600;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditRootComponent {
  readonly platform = inject(PlatformSectionService);
  private readonly access = inject(PlatformAccessService);
  readonly tabs = [
    { id: 'dashboard' as const, label: 'Tableau de bord', capability: 'audit.dashboard' },
    { id: 'journal' as const, label: 'Journal', capability: 'audit.journal' },
    { id: 'access-history' as const, label: 'Historique d’accès', capability: 'audit.access-history' },
    { id: 'anomalies' as const, label: 'Anomalies', capability: 'audit.anomalies' },
    { id: 'reporting' as const, label: 'Reporting', capability: 'audit.reporting' },
  ];

  visibleTabs() {
    return this.tabs.filter((t) => this.access.canView(t.capability));
  }
}
