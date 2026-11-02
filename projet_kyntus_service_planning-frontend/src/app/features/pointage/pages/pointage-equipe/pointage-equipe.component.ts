import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PointageService } from '../../../../core/services/pointage.service';
import type { PointageEquipeDto } from '../../../../core/models/pointage.models';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { filtrerEquipe, formatDuree, lignesExport, periode, type PeriodePreset } from '../../lib/pointage.util';

/**
 * Pointages de l'équipe : superviseur / référent technique / chef de projet (son périmètre),
 * RH et Admin (toute l'entreprise). Totaux de la période par collaborateur et export Excel.
 */
@Component({
  selector: 'app-pointage-equipe',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, KyntusPageHeaderComponent],
  templateUrl: './pointage-equipe.component.html',
  styleUrls: ['../pointage-suivi/pointage-suivi.component.css', './pointage-equipe.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PointageEquipeComponent implements OnInit {
  private readonly svc = inject(PointageService);
  private readonly toast = inject(KyntusToastService);

  readonly formatDuree = formatDuree;
  readonly preset = signal<PeriodePreset | 'perso'>('mois');
  readonly du = signal(periode('mois').du);
  readonly au = signal(periode('mois').au);
  readonly data = signal<PointageEquipeDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly recherche = signal('');
  readonly cellule = signal('');
  readonly seulementRetards = signal(false);

  readonly cellules = computed(() =>
    [...new Set((this.data()?.lignes ?? []).map((l) => l.cellule ?? '').filter(Boolean))]
      .sort((a, b) => a.localeCompare(b, 'fr', { sensitivity: 'base' })));
  readonly lignes = computed(() =>
    filtrerEquipe(this.data()?.lignes ?? [], this.recherche(), this.cellule(), this.seulementRetards()));
  readonly totalRetard = computed(() => this.lignes().reduce((n, l) => n + l.retardTotalMinutes, 0));
  readonly nonRelies = computed(() => (this.data()?.lignes ?? []).filter((l) => !l.relie).length);

  ngOnInit(): void {
    this.load();
  }

  choisirPreset(p: PeriodePreset): void {
    this.preset.set(p);
    const r = periode(p);
    this.du.set(r.du);
    this.au.set(r.au);
    this.load();
  }

  appliquerPeriode(): void {
    this.preset.set('perso');
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.svc.equipe(this.du(), this.au()).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.status === 403
          ? 'Cette vue est réservée aux responsables d’équipe, à RH et à l’Admin.'
          : err?.error?.message ?? 'Impossible de charger les pointages de l’équipe.');
      },
    });
  }

  async exporter(): Promise<void> {
    const rows = lignesExport(this.lignes());
    try {
      const mod = await import('exceljs');
      const ExcelJS = ((mod as unknown) as { default?: typeof import('exceljs') }).default ?? mod;
      const wb = new ExcelJS.Workbook();
      const ws = wb.addWorksheet('Pointages');
      ws.addRow([`Pointages du ${this.du()} au ${this.au()} — ${this.data()?.perimetre ?? ''}`]);
      ws.addRow([]);
      rows.forEach((r, i) => {
        const row = ws.addRow(r);
        if (i === 0) row.font = { bold: true };
      });
      ws.columns.forEach((c) => { c.width = 18; });
      ws.getColumn(1).width = 32;
      const buffer = await wb.xlsx.writeBuffer();
      const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `pointages-${this.du()}-${this.au()}.xlsx`;
      a.click();
      URL.revokeObjectURL(a.href);
    } catch {
      this.toast.error('Export Excel impossible.');
    }
  }
}
