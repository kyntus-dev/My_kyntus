import { Injectable, computed, inject, signal } from '@angular/core';
import type { Employee, Role } from '../models';
import { PRIME_AUTHORIZED_ROLES } from '../models';
import {
  employeesForUiRole,
  pickDefaultEmployeeForRole,
  resolveEmployeeForRole,
} from '../lib/prime-demo-users';
import { KyntusSessionService } from '../../../core/session/kyntus-session.service';
import { mapJwtRoleToPrimeRole } from '../../../core/session/kyntus-role-ui.config';
import { findEmployeeByLoginEmail } from '../lib/prime-demo-users';
import { primeApiGet } from '../services/prime-http';

@Injectable({ providedIn: 'root' })
export class RoleService {
  private readonly session = inject(KyntusSessionService);
  readonly currentRole = signal<Role>('Superviseur');
  readonly employees = signal<Employee[]>([]);
  /** Utilisateur courant aligné sur l’email JWT (plus de sessionStorage démo). */
  readonly selectedUserId = signal<string | null>(null);

  readonly employeesForCurrentRole = computed(() =>
    employeesForUiRole(this.employees(), this.currentRole()),
  );

  readonly currentUser = computed<Employee>(() => {
    const role = this.currentRole();
    const list = this.employees();
    return resolveEmployeeForRole(list, role, this.selectedUserId(), this.session.getEmail());
  });

  private employeesRequested = false;

  constructor() {
    this.currentRole.set(this.readInitialRole());
  }

  /** Charge l’annuaire Prime une seule fois, au premier accès au module (pas au démarrage du shell). */
  ensureEmployeesLoaded(): void {
    if (this.employeesRequested) return;
    this.employeesRequested = true;
    void primeApiGet<Employee[]>('/api/prime/employees').then((rows) => {
      this.employees.set(rows);
      this.ensureUserMatchesRole();
    });
  }

  setRole(role: Role): void {
    // Rôle UI = JWT uniquement en prod ; setRole reste pour compat écrans internes RH.
    const fromJwt = mapJwtRoleToPrimeRole(this.session.getRole());
    if (fromJwt && PRIME_AUTHORIZED_ROLES.includes(fromJwt)) {
      this.currentRole.set(fromJwt);
    } else {
      this.currentRole.set(role);
    }
    this.applyDefaultUserForRole(this.currentRole());
  }

  setUserId(userId: string): void {
    const id = userId.trim();
    this.selectedUserId.set(id || null);
  }

  /** Rôle par défaut pour l’écran Organisation RH. */
  preferRhForOrgScreen(): void {
    if (this.currentRole() !== 'RH') this.setRole('RH');
  }

  private applyDefaultUserForRole(role: Role): void {
    const list = this.employees();
    if (list.length === 0) return;
    const email = this.session.getEmail();
    const byEmail = email ? findEmployeeByLoginEmail(list, email) : undefined;
    if (byEmail) {
      this.setUserId(byEmail.id);
      return;
    }
    const picked = pickDefaultEmployeeForRole(list, role);
    if (picked) this.setUserId(picked.id);
  }

  private ensureUserMatchesRole(): void {
    const role = this.currentRole();
    const list = this.employees();
    if (list.length === 0) return;
    const email = this.session.getEmail();
    const byEmail = email ? findEmployeeByLoginEmail(list, email) : undefined;
    if (byEmail) {
      this.setUserId(byEmail.id);
      return;
    }
    const resolved = resolveEmployeeForRole(list, role, this.selectedUserId(), email);
    this.setUserId(resolved.id);
  }

  private readInitialRole(): Role {
    const fromJwt = mapJwtRoleToPrimeRole(this.session.getRole());
    if (fromJwt && PRIME_AUTHORIZED_ROLES.includes(fromJwt)) return fromJwt;
    return 'Superviseur';
  }
}
