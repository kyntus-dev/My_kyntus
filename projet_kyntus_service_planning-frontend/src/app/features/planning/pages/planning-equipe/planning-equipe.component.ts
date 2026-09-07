import { Component, OnInit, ViewEncapsulation, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { Router } from '@angular/router';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import {
  PlanningService,
  EquipePlanningSummary,
} from '../../services/planning.service';
import { UserService } from '../../../users/services/user.service';
import type { User } from '../../../users/users-module';
import { formatWeekLabel } from '../../utils/week-code.util';
import { formatPersonDisplayName } from '../../../../core/session/person-display-name.util';

interface AgentOption {
  id: number;
  label: string;
}

@Component({
  selector: 'app-planning-equipe',
  standalone: true,
  imports: [CommonModule, FormsModule, KyntusPageHeaderComponent],
  templateUrl: './planning-equipe.component.html',
  styleUrls: ['./planning-equipe.component.css'],
  encapsulation: ViewEncapsulation.None,
})
export class PlanningEquipeComponent implements OnInit {
  private readonly planningService = inject(PlanningService);
  private readonly userService = inject(UserService);
  private readonly session = inject(KyntusSessionService);
  private readonly router = inject(Router);

  equipePlannings: EquipePlanningSummary[] = [];
  equipeLoading = false;
  authUserId = 0;
  error = '';
  agentFilterId: number | null = null;
  agentSearch = '';
  private usersById = new Map<number, User>();

  readonly formatWeekLabel = formatWeekLabel;

  ngOnInit(): void {
    this.authUserId = this.session.getAuthUserId();
    if (!this.authUserId) {
      this.error = 'Utilisateur non identifié.';
      return;
    }
    this.userService.getAllUsers().subscribe({
      next: (users) => {
        this.usersById = new Map(users.filter((u) => u.isActive).map((u) => [u.id, u]));
      },
    });
    this.loadEquipePlannings();
  }

  loadEquipePlannings(): void {
    this.equipeLoading = true;
    this.error = '';
    this.planningService.getEquipePlannings(this.authUserId).subscribe({
      next: (data) => {
        this.equipePlannings = data ?? [];
        this.equipeLoading = false;
        if (this.equipePlannings.length === 0) {
          this.error = 'Aucun planning publié pour les services que vous gérez.';
        }
      },
      error: () => {
        this.equipePlannings = [];
        this.equipeLoading = false;
        this.error = 'Impossible de charger les plannings équipe.';
      },
    });
  }

  get agentOptions(): AgentOption[] {
    const ids = new Set<number>();
    for (const p of this.equipePlannings) {
      for (const uid of p.assignedUserIds ?? []) ids.add(uid);
    }
    const q = this.agentSearch.trim().toLowerCase();
    return [...ids]
      .map((id) => {
        const u = this.usersById.get(id);
        const label = u ? formatPersonDisplayName(u) : `Agent #${id}`;
        return { id, label };
      })
      .filter((o) => !q || o.label.toLowerCase().includes(q))
      .sort((a, b) => a.label.localeCompare(b.label, 'fr'));
  }

  get filteredEquipePlannings(): EquipePlanningSummary[] {
    if (this.agentFilterId == null) return this.equipePlannings;
    const agentId = this.agentFilterId;
    return this.equipePlannings.filter((p) => (p.assignedUserIds ?? []).includes(agentId));
  }

  clearAgentFilter(): void {
    this.agentFilterId = null;
    this.agentSearch = '';
  }

  /** Ouvre la même grille RH en lecture seule. */
  openPlanning(p: EquipePlanningSummary): void {
    const queryParams: Record<string, string | number> = {
      from: 'equipe',
      weekCode: p.weekCode,
    };
    if (this.agentFilterId != null) {
      queryParams['highlightUserId'] = this.agentFilterId;
    }
    void this.router.navigate(['/planning/view', p.id], { queryParams });
  }

  statusLabel(status: string): string {
    return status === 'Published' ? 'Publié' : status === 'Draft' ? 'Brouillon' : status;
  }
}
