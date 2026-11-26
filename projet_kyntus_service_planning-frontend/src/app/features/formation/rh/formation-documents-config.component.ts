import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FormationTrainingService } from '../../../core/services/formation-training.service';
import type { FormationDocumentDefinitionDto } from '../../../core/models/formation-training.models';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';

@Component({
  selector: 'app-formation-documents-config',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent],
  template: `
    <section class="ky-page-shell">
      <app-kyntus-page-header
        title="Documents de formation"
        subtitle="Configurez la checklist des pièces que les employés en formation doivent apporter."
      />

      <div class="ky-card p-4 space-y-4">
        <form class="grid gap-2 md:grid-cols-4 items-end" *ngIf="droits.autorise('formation.checklist', 'Configure')" (ngSubmit)="save()">
          <div class="md:col-span-2">
            <label class="text-xs text-muted">Titre</label>
            <input class="ky-input w-full" [(ngModel)]="draft.title" name="title" required placeholder="Ex. Copie CNI" />
          </div>
          <div>
            <label class="text-xs text-muted">Ordre</label>
            <input class="ky-input w-full" type="number" [(ngModel)]="draft.sortOrder" name="sortOrder" />
          </div>
          <div class="flex flex-wrap gap-2">
            <label class="inline-flex items-center gap-2 text-sm">
              <input type="checkbox" [(ngModel)]="draft.isActive" name="isActive" />
              Actif
            </label>
            <button type="submit" class="ky-btn-primary">{{ editingId ? 'Enregistrer' : 'Ajouter' }}</button>
            @if (editingId) {
              <button type="button" class="ky-btn-secondary" (click)="resetDraft()">Annuler</button>
            }
          </div>
        </form>

        @if (error()) {
          <p class="text-rose-300 text-sm m-0">{{ error() }}</p>
        }

        <table class="prime-table w-full">
          <thead>
            <tr>
              <th>Ordre</th>
              <th>Titre</th>
              <th>Statut</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (d of rows(); track d.id) {
              <tr>
                <td>{{ d.sortOrder }}</td>
                <td>{{ d.title }}</td>
                <td>{{ d.isActive ? 'Actif' : 'Inactif' }}</td>
                <td class="td-actions">
                  @if (droits.autorise('formation.checklist', 'Configure')) {
                  <button type="button" class="ky-btn-secondary" (click)="edit(d)">Modifier</button>
                  <button type="button" class="ky-btn-secondary text-rose-300" (click)="remove(d)">
                    {{ d.isActive ? 'Désactiver' : 'Supprimer' }}
                  </button>
                  }
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="4" class="text-muted">Aucun document configuré.</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormationDocumentsConfigComponent implements OnInit {
  /** v72 — Droits « Accès par rôle / par compte » : une action n'apparaît que si le serveur l'accepte. */
  readonly droits = inject(PlatformAccessService);
  private readonly api = inject(FormationTrainingService);
  private readonly confirmService = inject(KyntusConfirmService);
  readonly rows = signal<FormationDocumentDefinitionDto[]>([]);
  readonly error = signal<string | null>(null);
  editingId: string | null = null;
  draft = { title: '', sortOrder: 1, isActive: true };

  ngOnInit(): void {
    void this.droits.ensureLoaded();
    void this.reload();
  }

  private async reload(): Promise<void> {
    this.rows.set(await this.api.listDocumentDefinitions());
    if (!this.editingId) {
      this.draft.sortOrder = (this.rows().at(-1)?.sortOrder ?? 0) + 1;
    }
  }

  resetDraft(): void {
    this.editingId = null;
    this.draft = { title: '', sortOrder: (this.rows().at(-1)?.sortOrder ?? 0) + 1, isActive: true };
    this.error.set(null);
  }

  edit(d: FormationDocumentDefinitionDto): void {
    this.editingId = d.id;
    this.draft = { title: d.title, sortOrder: d.sortOrder, isActive: d.isActive };
  }

  async save(): Promise<void> {
    this.error.set(null);
    const title = this.draft.title.trim();
    if (!title) {
      this.error.set('Le titre est obligatoire.');
      return;
    }
    try {
      if (this.editingId) {
        await this.api.updateDocumentDefinition(this.editingId, {
          title,
          sortOrder: Number(this.draft.sortOrder) || 0,
          isActive: this.draft.isActive,
        });
      } else {
        await this.api.createDocumentDefinition({
          title,
          sortOrder: Number(this.draft.sortOrder) || 0,
          isActive: this.draft.isActive,
        });
      }
      this.resetDraft();
      await this.reload();
    } catch (e: any) {
      this.error.set(e?.message || 'Échec de l’enregistrement');
    }
  }

  async remove(d: FormationDocumentDefinitionDto): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: d.isActive ? 'Désactiver le document' : 'Supprimer le document',
      message: d.isActive ? `Désactiver « ${d.title} » ?` : `Supprimer « ${d.title} » ?`,
      confirmLabel: d.isActive ? 'Désactiver' : 'Supprimer',
      variant: d.isActive ? 'warning' : 'danger',
    });
    if (!ok) return;
    await this.api.deleteDocumentDefinition(d.id);
    await this.reload();
  }
}
