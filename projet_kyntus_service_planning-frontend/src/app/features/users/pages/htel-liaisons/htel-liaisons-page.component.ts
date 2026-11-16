import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AlertTriangle, ChevronLeft, ChevronRight, Link2, RefreshCw, Search, Unlink, X } from 'lucide';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { formatHttpErrorMessage } from '../../../../core/lib/http-error-message.util';
import { libelleMail } from '../../lib/mail-interne.util';
import {
  HtelAmbiguousMatchDto,
  HtelApiService,
  HtelLiaisonsReportDto,
  HtelLinkedEmployeeDto,
  HtelOrphanTechnicienDto,
  HtelSyncReportDto,
  HtelUnlinkedEmployeeDto,
} from '../../services/htel-api.service';

type HtelListKey = 'linked' | 'orphans' | 'ambiguous';

@Component({
  selector: 'app-htel-liaisons-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, LucideIconComponent, KyntusPageHeaderComponent],
  templateUrl: './htel-liaisons-page.component.html',
  styleUrls: ['./htel-liaisons-page.component.css'],
})
export class HtelLiaisonsPageComponent implements OnInit {
  readonly icons = {
    sync: RefreshCw,
    warn: AlertTriangle,
    link: Link2,
    unlink: Unlink,
    search: Search,
    clear: X,
    prev: ChevronLeft,
    next: ChevronRight,
  };

  readonly pageSize = 10;

  private readonly htelApi = inject(HtelApiService);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly confirmService = inject(KyntusConfirmService);

  loading = false;
  syncing = false;
  error: string | null = null;
  syncReport: HtelSyncReportDto | null = null;
  report: HtelLiaisonsReportDto | null = null;
  searchTerm = '';

  linkedPage = 1;
  orphansPage = 1;
  ambiguousPage = 1;

  linkEmployeeId = '';
  linkIdTechnicien: number | null = null;

  ngOnInit(): void {
    this.reload();
  }

  /** v47 : une adresse provisoire s'affiche « Sans mail interne ». */
  libelleMail(email: string | null | undefined): string {
    return libelleMail(email);
  }

  get linked(): HtelLinkedEmployeeDto[] {
    return this.report?.linked ?? [];
  }

  get orphans(): HtelOrphanTechnicienDto[] {
    return this.report?.orphansHtel ?? [];
  }

  get ambiguous(): HtelAmbiguousMatchDto[] {
    return this.report?.ambiguous ?? [];
  }

  get unlinked(): HtelUnlinkedEmployeeDto[] {
    return this.report?.unlinkedEmployees ?? [];
  }

  get filteredLinked(): HtelLinkedEmployeeDto[] {
    const q = this.normalize(this.searchTerm);
    if (!q) return this.linked;
    return this.linked.filter((r) =>
      this.matches(q, r.lastName, r.firstName, r.email, String(r.idTechnicien), r.htelCode, r.htelTechnicienName),
    );
  }

  get filteredOrphans(): HtelOrphanTechnicienDto[] {
    const q = this.normalize(this.searchTerm);
    if (!q) return this.orphans;
    return this.orphans.filter((r) =>
      this.matches(q, String(r.idTechnicien), r.technicien, r.code, r.actif === 1 ? 'oui' : 'non'),
    );
  }

  get filteredAmbiguous(): HtelAmbiguousMatchDto[] {
    const q = this.normalize(this.searchTerm);
    if (!q) return this.ambiguous;
    return this.ambiguous.filter(
      (r) =>
        this.matches(q, String(r.idTechnicien), r.technicien, r.code) ||
        r.candidates.some((c) => this.matches(q, c.lastName, c.firstName, c.email)),
    );
  }

  get filteredUnlinked(): HtelUnlinkedEmployeeDto[] {
    const q = this.normalize(this.searchTerm);
    if (!q) return this.unlinked;
    return this.unlinked.filter((r) => this.matches(q, r.lastName, r.firstName, r.email));
  }

  get pagedLinked(): HtelLinkedEmployeeDto[] {
    return this.slicePage(this.filteredLinked, this.linkedPage);
  }

  get pagedOrphans(): HtelOrphanTechnicienDto[] {
    return this.slicePage(this.filteredOrphans, this.orphansPage);
  }

  get pagedAmbiguous(): HtelAmbiguousMatchDto[] {
    return this.slicePage(this.filteredAmbiguous, this.ambiguousPage);
  }

  get hasSearch(): boolean {
    return this.normalize(this.searchTerm).length > 0;
  }

  onSearchChange(): void {
    this.resetPages();
  }

  clearSearch(): void {
    this.searchTerm = '';
    this.resetPages();
    this.cdr.detectChanges();
  }

  pageCount(list: HtelListKey): number {
    const total = this.filteredTotal(list);
    return Math.max(1, Math.ceil(total / this.pageSize));
  }

  currentPage(list: HtelListKey): number {
    return this.clampPage(list, this.rawPage(list));
  }

  pageLabel(list: HtelListKey): string {
    const total = this.filteredTotal(list);
    if (total === 0) return '0 / 0';
    const page = this.currentPage(list);
    const from = (page - 1) * this.pageSize + 1;
    const to = Math.min(page * this.pageSize, total);
    return `${from}–${to} / ${total}`;
  }

  canPrev(list: HtelListKey): boolean {
    return this.currentPage(list) > 1;
  }

  canNext(list: HtelListKey): boolean {
    return this.currentPage(list) < this.pageCount(list);
  }

  prevPage(list: HtelListKey): void {
    if (!this.canPrev(list)) return;
    this.setPage(list, this.currentPage(list) - 1);
  }

  nextPage(list: HtelListKey): void {
    if (!this.canNext(list)) return;
    this.setPage(list, this.currentPage(list) + 1);
  }

  reload(): void {
    this.loading = true;
    this.error = null;
    this.htelApi.getLiaisons().subscribe({
      next: (report) => {
        this.report = report;
        this.resetPages();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = formatHttpErrorMessage(err, 'Impossible de charger les liaisons HTEL.');
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
  }

  sync(): void {
    this.syncing = true;
    this.error = null;
    this.syncReport = null;
    this.htelApi.sync().subscribe({
      next: (report) => {
        this.syncReport = report;
        this.syncing = false;
        this.reload();
      },
      error: (err) => {
        this.error = formatHttpErrorMessage(err, 'Synchronisation HTEL impossible.');
        this.syncing = false;
        this.cdr.detectChanges();
      },
    });
  }

  async unlink(row: HtelLinkedEmployeeDto): Promise<void> {
    const name = `${row.lastName} ${row.firstName}`.trim() || row.email;
    const ok = await this.confirmService.confirm({
      title: 'Délier l’employé',
      message: `Êtes-vous sûr de délier l’employé ${name} ?`,
      confirmLabel: 'Délier',
      cancelLabel: 'Annuler',
      variant: 'danger',
    });
    if (!ok) return;

    this.htelApi.unlink(row.employeeId).subscribe({
      next: () => this.reload(),
      error: (err) => {
        this.error = formatHttpErrorMessage(err, 'Impossible de délier.');
        this.cdr.detectChanges();
      },
    });
  }

  linkManual(): void {
    const employeeId = this.linkEmployeeId.trim();
    const idTechnicien = this.linkIdTechnicien;
    if (!employeeId || !idTechnicien || idTechnicien <= 0) {
      this.error = 'Sélectionnez un employé et un id technicien HTEL.';
      return;
    }
    this.htelApi.link(employeeId, idTechnicien).subscribe({
      next: () => {
        this.linkEmployeeId = '';
        this.linkIdTechnicien = null;
        this.reload();
      },
      error: (err) => {
        this.error = formatHttpErrorMessage(err, 'Liaison impossible.');
        this.cdr.detectChanges();
      },
    });
  }

  linkCandidate(employeeId: string, idTechnicien: number): void {
    this.htelApi.link(employeeId, idTechnicien).subscribe({
      next: () => this.reload(),
      error: (err) => {
        this.error = formatHttpErrorMessage(err, 'Liaison impossible.');
        this.cdr.detectChanges();
      },
    });
  }

  createEmployeeFromOrphan(orphan: HtelOrphanTechnicienDto): void {
    const parts = orphan.technicien.trim().split(/\s+/);
    const lastName = parts[0] ?? '';
    const firstName = parts.slice(1).join(' ');
    void this.router.navigate(['/users/create'], {
      queryParams: {
        lastName,
        firstName,
        idTechnicien: orphan.idTechnicien,
      },
    });
  }

  private slicePage<T>(items: T[], page: number): T[] {
    const safePage = Math.min(Math.max(page, 1), Math.max(1, Math.ceil(items.length / this.pageSize) || 1));
    const start = (safePage - 1) * this.pageSize;
    return items.slice(start, start + this.pageSize);
  }

  private filteredTotal(list: HtelListKey): number {
    switch (list) {
      case 'linked':
        return this.filteredLinked.length;
      case 'orphans':
        return this.filteredOrphans.length;
      case 'ambiguous':
        return this.filteredAmbiguous.length;
    }
  }

  private rawPage(list: HtelListKey): number {
    switch (list) {
      case 'linked':
        return this.linkedPage;
      case 'orphans':
        return this.orphansPage;
      case 'ambiguous':
        return this.ambiguousPage;
    }
  }

  private setPage(list: HtelListKey, page: number): void {
    const clamped = this.clampPage(list, page);
    switch (list) {
      case 'linked':
        this.linkedPage = clamped;
        break;
      case 'orphans':
        this.orphansPage = clamped;
        break;
      case 'ambiguous':
        this.ambiguousPage = clamped;
        break;
    }
    this.cdr.detectChanges();
  }

  private clampPage(list: HtelListKey, page: number): number {
    return Math.min(Math.max(page, 1), this.pageCount(list));
  }

  private resetPages(): void {
    this.linkedPage = 1;
    this.orphansPage = 1;
    this.ambiguousPage = 1;
  }

  private normalize(value: string | null | undefined): string {
    return (value ?? '')
      .trim()
      .normalize('NFD')
      .replace(/\p{M}/gu, '')
      .toLowerCase();
  }

  private matches(query: string, ...parts: Array<string | null | undefined>): boolean {
    const haystack = this.normalize(parts.filter(Boolean).join(' '));
    return haystack.includes(query);
  }
}
