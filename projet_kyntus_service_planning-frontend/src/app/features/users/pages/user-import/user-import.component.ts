// features/users/pages/user-import/user-import.component.ts

import { Component, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../../environments/environment';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { firstValueFrom } from 'rxjs';
import { EmployeeRosterService } from '../../services/employee-roster.service';
import { EMPLOYEE_STATUS_SCOPES, type EmployeeStatusScope } from '../../lib/employee-list-filters';

interface ImportResult {
  totalLignes: number;
  importes:    number;
  erreurs:     number;
  details:     string[];
}

@Component({
  selector: 'app-user-import',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './user-import.component.html',
  styleUrls: ['./user-import.component.css'],
})
export class UserImportComponent {

  selectedFile:  File | null = null;
  importing    = false;
  isDragOver   = false;
  importResult: ImportResult | null = null;
  exporting: EmployeeStatusScope | null = null;
  readonly exportScopes = EMPLOYEE_STATUS_SCOPES;

  private base = `${environment.apiUrl}/Users`;

  constructor(
    private http: HttpClient,
    private cdr: ChangeDetectorRef,
    private toastService: KyntusToastService,
    private roster: EmployeeRosterService,
  ) {}

  // ── Template ──
  downloadTemplate(): void {
    window.open(`${this.base}/template`, '_blank');
  }

  // ── Export : tous, actifs ou inactifs, avec l'organisation telle qu'affichée dans Employés ──
  async exportUsers(scope: EmployeeStatusScope): Promise<void> {
    if (this.exporting) return;
    this.exporting = scope;
    this.cdr.detectChanges();
    try {
      const roster = await firstValueFrom(this.roster.load());
      const count = await this.roster.exportExcel(roster, scope);
      if (count === 0) this.toastService.error('Aucun employé à exporter pour ce statut.');
      else this.toastService.success(`${count} employé(s) exporté(s).`);
    } catch {
      this.toastService.error("Échec de l'export Excel.");
    } finally {
      this.exporting = null;
      this.cdr.detectChanges();
    }
  }

  // ── Drag & Drop ──
  onDragOver(e: DragEvent): void {
    e.preventDefault();
    this.isDragOver = true;
  }

  onDragLeave(): void {
    this.isDragOver = false;
  }

  onDrop(e: DragEvent): void {
    e.preventDefault();
    this.isDragOver = false;
    const file = e.dataTransfer?.files[0];
    if (file) this.setFile(file);
  }

  onFileSelected(e: Event): void {
    const input = e.target as HTMLInputElement;
    const file  = input.files?.[0];
    if (file) this.setFile(file);
  }

  setFile(file: File): void {
    const allowed = ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
                     'application/vnd.ms-excel', 'text/csv'];
    if (!allowed.includes(file.type) && !file.name.match(/\.(xlsx|xls|csv)$/i)) {
      this.toastService.error('Format non supporté. Utilisez .xlsx, .xls ou .csv');
      return;
    }
    this.selectedFile  = file;
    this.importResult  = null;
    this.cdr.detectChanges();
  }

  removeFile(e: Event): void {
    e.stopPropagation();
    this.selectedFile = null;
    this.importResult = null;
    this.cdr.detectChanges();
  }

  // ── Import ──
  importFile(): void {
    if (!this.selectedFile) return;

    this.importing = true;
    this.cdr.detectChanges();

    const formData = new FormData();
    formData.append('file', this.selectedFile);

    this.http.post<ImportResult>(`${this.base}/import`, formData).subscribe({
      next: result => {
        this.importResult = result;
        this.importing    = false;
        this.selectedFile = null;
        this.cdr.detectChanges();
      },
      error: err => {
        console.error('Erreur import:', err);
        this.importing = false;
        this.importResult = {
          totalLignes: 0,
          importes:    0,
          erreurs:     1,
          details:     ['Erreur serveur — vérifiez que le backend est démarré']
        };
        this.cdr.detectChanges();
      }
    });
  }

  reset(): void {
    this.selectedFile = null;
    this.importResult = null;
    this.cdr.detectChanges();
  }

  // ── Helpers ──
  getFileSize(bytes: number): string {
    if (bytes < 1024)       return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  getSuccessPercent(): number {
    if (!this.importResult || this.importResult.totalLignes === 0) return 0;
    return (this.importResult.importes / this.importResult.totalLignes) * 100;
  }
}