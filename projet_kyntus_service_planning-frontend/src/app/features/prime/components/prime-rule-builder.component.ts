import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  signal,
} from '@angular/core';
import type { Department, PrimeType } from '../models';

interface RuleDraft {
  primeTypeId: string;
  departmentId: string;
  conditionField: string;
  conditionType: string;
  targetValue: string;
  calculationMethod: string;
  amount: string;
  period: string;
}

@Component({
  selector: 'app-prime-rule-builder',
  standalone: true,
  template: `
    <form (ngSubmit)="submit($event)" class="space-y-6">
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div>
          <label class="block text-sm font-medium text-muted mb-1">Type de prime</label>
          <select
            class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-app text-primary"
            required
            [value]="rule().primeTypeId"
            (change)="patch('primeTypeId', $any($event.target).value)"
          >
            <option value="">Sélectionner un type</option>
            @for (t of types; track t.id) {
              <option [value]="t.id">{{ t.name }}</option>
            }
          </select>
        </div>
        <div>
          <label class="block text-sm font-medium text-muted mb-1">Département</label>
          <select
            class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-app text-primary"
            [value]="rule().departmentId"
            (change)="patch('departmentId', $any($event.target).value)"
          >
            <option value="">Tous les départements</option>
            @for (d of departments; track d.id) {
              <option [value]="d.id">{{ d.name }}</option>
            }
          </select>
        </div>
      </div>

      <div class="bg-app p-4 rounded-xl border border-default space-y-4">
        <h4 class="text-sm font-semibold text-primary">Condition</h4>
        <div class="flex flex-col sm:flex-row gap-3 items-center">
          <div class="w-full sm:w-1/3">
            <select
              class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-card text-primary"
              required
              [value]="rule().conditionField"
              (change)="patch('conditionField', $any($event.target).value)"
            >
              <option value="">Sélectionner un champ</option>
              <option value="tickets_resolved">Tickets résolus</option>
              <option value="csat_score">Score CSAT</option>
              <option value="errors">Erreurs</option>
              <option value="attendance_rate">Taux de présence</option>
            </select>
          </div>
          <div class="w-full sm:w-1/4">
            <select
              class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-card text-primary"
              required
              [value]="rule().conditionType"
              (change)="patch('conditionType', $any($event.target).value)"
            >
              <option value=">">Supérieur à (&gt;)</option>
              <option value="<">Inférieur à (&lt;)</option>
              <option value=">=">Supérieur ou égal (&gt;=)</option>
              <option value="<=">Inférieur ou égal (&lt;=)</option>
              <option value="==">Égal à (==)</option>
            </select>
          </div>
          <div class="w-full sm:w-1/3">
            <input
              type="number"
              class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-card text-primary placeholder:text-muted"
              placeholder="Valeur cible"
              required
              [value]="rule().targetValue"
              (input)="patch('targetValue', $any($event.target).value)"
            />
          </div>
        </div>
      </div>

      <div class="bg-app p-4 rounded-xl border border-default space-y-4">
        <h4 class="text-sm font-semibold text-primary">Récompense</h4>
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div>
            <label class="block text-sm font-medium text-muted mb-1">Méthode</label>
            <select
              class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-card text-primary"
              required
              [value]="rule().calculationMethod"
              (change)="patch('calculationMethod', $any($event.target).value)"
            >
              <option value="Fixed">Montant fixe</option>
              <option value="Percentage">Pourcentage</option>
            </select>
          </div>
          <div>
            <label class="block text-sm font-medium text-muted mb-1">Montant / %</label>
            <input
              type="number"
              class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-card text-primary placeholder:text-muted"
              placeholder="Ex. : 300"
              required
              [value]="rule().amount"
              (input)="patch('amount', $any($event.target).value)"
            />
          </div>
          <div>
            <label class="block text-sm font-medium text-muted mb-1">Période</label>
            <select
              class="w-full px-3 py-2 border border-default rounded-lg focus:ring-blue-500 focus:border-blue-500 bg-card text-primary"
              required
              [value]="rule().period"
              (change)="patch('period', $any($event.target).value)"
            >
              <option value="Monthly">Mensuel</option>
              <option value="Quarterly">Trimestriel</option>
              <option value="Yearly">Annuel</option>
            </select>
          </div>
        </div>
      </div>

      <div class="flex justify-end gap-3 pt-2">
        <button
          type="button"
          (click)="cancel.emit()"
          class="px-4 py-2 text-primary hover:bg-app rounded-lg font-medium transition-colors border border-default"
        >
          Annuler
        </button>
        <button
          type="submit"
          class="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg font-medium transition-colors shadow-sm"
        >
          Enregistrer la règle
        </button>
      </div>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrimeRuleBuilderComponent {
  @Input() types: PrimeType[] = [];
  @Input() departments: Department[] = [];
  @Output() save = new EventEmitter<RuleDraft>();
  @Output() cancel = new EventEmitter<void>();

  readonly rule = signal<RuleDraft>({
    primeTypeId: '',
    departmentId: '',
    conditionField: '',
    conditionType: '>',
    targetValue: '',
    calculationMethod: 'Fixed',
    amount: '',
    period: 'Monthly',
  });

  patch(key: keyof RuleDraft, value: string): void {
    this.rule.update((current) => ({ ...current, [key]: value }));
  }

  submit(event: Event): void {
    event.preventDefault();
    this.save.emit(this.rule());
  }
}
