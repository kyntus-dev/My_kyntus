import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { timer, finalize } from 'rxjs';
import { suggestHtelIdentity } from '../../services/htel-identity.util';
import { UserService } from '../../services/user.service';
import { resolveUserGuid } from '../../../../core/lib/user-guid.util';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AlertTriangle, ChevronLeft, ChevronRight, Link2, RefreshCw, Search, Unlink, X } from 'lucide';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { formatHttpErrorMessage } from '../../../../core/lib/http-error-message.util';
import {
  HtelAmbiguousMatchDto,
  HtelMissingEmployeeDto,
  HtelIdentityReviewRequest,
  HtelApiService,
  HtelLiaisonsReportDto,
  HtelLinkedEmployeeDto,
  HtelOrphanTechnicienDto,
  HtelSyncReportDto,
  HtelUnlinkedEmployeeDto,
} from '../../services/htel-api.service';

type HtelListKey = 'linked' | 'orphans' | 'ambiguous' | 'missing';

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

  private readonly destroyRef = inject(DestroyRef);
  private readonly userService = inject(UserService);
  acting = false;
  sourceUnavailable = false;
  identityReview: HtelIdentityReviewRequest | null = null;
  missingPage = 1;
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
    timer(300_000, 300_000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!document.hidden && !this.loading && !this.syncing && !this.acting && !this.identityReview) this.reload(false);
    });
  }

  get canAct(): boolean { return !!this.report && !this.loading && !this.syncing && !this.acting && !this.sourceUnavailable; }
  get newActiveCount(): number { return this.orphans.filter(t => t.actif === 1).length; }
  get inactiveHtelCount(): number { return this.linked.filter(e => e.htelActif === 0 && e.isActive).length; }
  get missing(): HtelMissingEmployeeDto[] { return this.report?.missingFromHtel ?? []; }
  get filteredMissing(): HtelMissingEmployeeDto[] {
    const q = this.normalize(this.searchTerm);
    return !q ? this.missing : this.missing.filter(r => this.matches(q, r.lastName, r.firstName, r.email, String(r.idTechnicien ?? '')));
  }
  get pagedMissing(): HtelMissingEmployeeDto[] { return this.slicePage(this.filteredMissing, this.missingPage); }

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

  reload(resetPages = true): void {
    if (this.loading) return;
    this.loading = true;
    this.error = null;
    if (resetPages) this.identityReview = null;
    this.htelApi.getLiaisons().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (report) => {
        this.report = report;
        this.sourceUnavailable = false;
        if (resetPages) this.resetPages();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.sourceUnavailable = true;
        this.error = formatHttpErrorMessage(err, 'Impossible de charger les liaisons HTEL.');
        this.loading = false;
        this.cdr.detectChanges();
      },
    });
  }

  sync(): void {
    if (!this.canAct || this.identityReview) return;
    this.syncing = true;
    this.error = null;
    this.syncReport = null;
    this.htelApi.sync().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
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
    if (!this.canAct) return;
    const name = `${row.lastName} ${row.firstName}`.trim() || row.email;
    const ok = await this.confirmService.confirm({
      title: 'Délier l’employé',
      message: `Êtes-vous sûr de délier l’employé ${name} ?`,
      confirmLabel: 'Délier',
      cancelLabel: 'Annuler',
      variant: 'danger',
    });
    if (!ok || !this.canAct) return;

    this.acting = true;
    this.htelApi.unlink(row.employeeId).pipe(finalize(() => { this.acting = false; this.cdr.detectChanges(); })).subscribe({
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
    void this.linkCandidate(employeeId, idTechnicien);
  }

  async linkCandidate(employeeId: string, idTechnicien: number): Promise<void> {
    if (!this.canAct) return;
    const source = [...this.orphans, ...this.ambiguous].find(t => t.idTechnicien === idTechnicien);
    const employee = this.unlinked.find(e => e.employeeId === employeeId);
    const ok = await this.confirmService.confirm({
      title: 'Confirmer la liaison HTEL',
      message: `Relier ${employee ? `${employee.lastName} ${employee.firstName} (${employee.email})` : 'cette fiche'} au technicien ${idTechnicien}${source ? ` — ${source.technicien}` : ''} ? Vérifiez qu’il s’agit de la même personne.`,
      confirmLabel: 'Confirmer la liaison', cancelLabel: 'Annuler', variant: 'warning',
    });
    if (!ok || !this.canAct) return;
    this.acting = true;
    this.htelApi.link(employeeId, idTechnicien).pipe(finalize(() => { this.acting = false; this.cdr.detectChanges(); })).subscribe({
      next: () => { this.linkEmployeeId = ''; this.linkIdTechnicien = null; this.reload(); },
      error: err => { this.error = formatHttpErrorMessage(err, 'Liaison impossible.'); this.cdr.detectChanges(); },
    });
  }

  createEmployeeFromOrphan(orphan: HtelOrphanTechnicienDto): void {
    if (!this.canAct || orphan.actif !== 1) return;
    void this.router.navigate(['/users/create'], { queryParams: { idTechnicien: orphan.idTechnicien } });
  }

  async createDespiteNameMatch(row: HtelAmbiguousMatchDto): Promise<void> {
    if (!this.canAct || row.actif !== 1 || row.linkedEmployeeId) return;
    const ok = await this.confirmService.confirm({
      title: 'Vérifier un possible doublon',
      message: 'Des fiches MyKyntus ressemblent à ce collaborateur HTEL. Avez-vous vérifié qu’il s’agit d’une autre personne ?',
      confirmLabel: 'Créer une autre personne', cancelLabel: 'Revenir aux correspondances', variant: 'warning',
    });
    if (ok) this.createEmployeeFromOrphan(row);
  }

  reviewIdentity(row: HtelLinkedEmployeeDto): void {
    if (!this.canAct || !row.existsInHtel || !row.htelTechnicienName) return;
    const proposal = suggestHtelIdentity(row.htelTechnicienName, row.firstName, row.lastName);
    this.identityReview = { employeeId: row.employeeId, idTechnicien: row.idTechnicien,
      expectedFirstName: row.firstName, expectedLastName: row.lastName, expectedTechnicien: row.htelTechnicienName,
      ...proposal };
    this.cdr.detectChanges();
    const panel = document.getElementById('htel-identity-review');
    panel?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    panel?.querySelector<HTMLInputElement>('input')?.focus({ preventScroll: true });
  }

  saveIdentity(): void {
    if (!this.canAct || !this.identityReview) return;
    this.acting = true;
    this.htelApi.reviewIdentity(this.identityReview).pipe(finalize(() => { this.acting = false; this.cdr.detectChanges(); })).subscribe({
      next: () => { this.identityReview = null; this.reload(false); },
      error: err => { this.error = formatHttpErrorMessage(err, 'Identité non modifiée.'); this.cdr.detectChanges(); },
    });
  }

  openEmployee(employeeId: string): void {
    if (!this.canAct) return;
    this.acting = true;
    this.userService.getAllUsers().pipe(finalize(() => { this.acting = false; this.cdr.detectChanges(); })).subscribe({
      next: users => {
        const user = users.find(u => resolveUserGuid(u).toLowerCase() === employeeId.toLowerCase());
        if (user) void this.router.navigate(['/users', user.id]);
        else { this.error = 'La fiche existe dans l’annuaire mais sa projection Planning est indisponible. Contactez l’administrateur pour la réconcilier.'; this.cdr.detectChanges(); }
      },
      error: err => { this.error = formatHttpErrorMessage(err, 'Impossible d’ouvrir la fiche.'); this.cdr.detectChanges(); },
    });
  }

  private slicePage<T>(items: T[], page: number): T[] {
    const safePage = Math.min(Math.max(page, 1), Math.max(1, Math.ceil(items.length / this.pageSize) || 1));
    const start = (safePage - 1) * this.pageSize;
    return items.slice(start, start + this.pageSize);
  }

  private filteredTotal(list: HtelListKey): number {
    switch (list) {
      case 'missing': return this.filteredMissing.length;
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
      case 'missing': return this.missingPage;
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
      case 'missing': this.missingPage = clamped; break;
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
    this.missingPage = 1;
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
