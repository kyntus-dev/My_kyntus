import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
  effect,
  input,
} from '@angular/core';
import { PrimeCardComponent } from '../../components/prime-card.component';
import { RpPrimeService } from '../../services/rp-prime.service';
import { HierarchyDrillService } from '../../state/hierarchy-drill.service';

@Component({
  selector: 'app-rp-team-performance',
  standalone: true,
  imports: [PrimeCardComponent],
  template: `
    <div class="space-y-6">
      <div>
        <h2 class="text-2xl font-bold text-primary tracking-tight">Performance de l’équipe</h2>
        <p class="text-muted mt-1">Suivi detaille des taches et objectifs par membre.</p>
      </div>

      <app-prime-card title="Membres du projet">
        <div class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <tr class="border-b border-default">
                <th class="text-left py-3 text-muted font-medium">Nom</th>
                <th class="text-left py-3 text-muted font-medium">Projet</th>
                <th class="text-left py-3 text-muted font-medium">Taches completees</th>
                <th class="text-left py-3 text-muted font-medium">Objectifs atteints</th>
                <th class="text-left py-3 text-muted font-medium">Score</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.employeeId + row.projectName) {
                <tr class="border-b border-default/60">
                  <td class="py-3 text-primary">{{ row.employeeName }}</td>
                  <td class="py-3 text-muted">{{ row.projectName }}</td>
                  <td class="py-3 text-primary">{{ row.completedTasks }}/{{ row.totalTasks }}</td>
                  <td class="py-3 text-primary">{{ row.objectivesReached }}/{{ row.totalObjectives }}</td>
                  <td class="py-3 font-semibold text-[var(--info-text)]">{{ rowScore(row) }}%</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </app-prime-card>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RpTeamPerformanceComponent {
  readonly rpUserId = input.required<string>();
  readonly hierarchy = inject(HierarchyDrillService);
  readonly rows = signal<
    Array<{
      employeeId: string;
      employeeName: string;
      projectName: string;
      completedTasks: number;
      totalTasks: number;
      objectivesReached: number;
      totalObjectives: number;
    }>
  >([]);

  constructor() {
    effect(() => {
      const id = this.rpUserId();
      const drill = this.hierarchy.drill();
      RpPrimeService.getTeamPerformanceByProject(id, drill).then((data) => this.rows.set(data));
    });
  }

  rowScore(row: {
    completedTasks: number;
    totalTasks: number;
    objectivesReached: number;
    totalObjectives: number;
  }): number {
    return Math.round(
      (row.completedTasks / row.totalTasks) * 60 + (row.objectivesReached / row.totalObjectives) * 40,
    );
  }
}
