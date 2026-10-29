import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, forkJoin, map, type Observable } from 'rxjs';
import {
  enrichUserOrgPerimeter,
  type BusinessDepartmentRef,
  type DirectoryEmployeeOrgRef,
  type UserOrgPerimeterView,
} from '../../../core/org/user-org-perimeter';
import type { OrgAssignmentsOverview } from '../../prime/services/prime-org-api.service';
import { PrimeOrgApiService } from '../../prime/services/prime-org-api.service';
import { SubServiceService } from '../../sub-services/services/sub-service.service';
import type { User } from '../users-module';
import { UserService } from './user.service';
import {
  buildEmployeeExportRows,
  downloadEmployeesExcel,
} from '../lib/employee-export';
import type { EmployeeStatusScope } from '../lib/employee-list-filters';

export type EmployeeRoster = {
  users: User[];
  overview: OrgAssignmentsOverview | null;
  perimeters: Map<number, UserOrgPerimeterView>;
};

/**
 * Employés + organisation résolue (activité, pôle, cellule) : une seule source pour la liste,
 * la fiche et l'export, afin que les libellés soient partout identiques.
 */
@Injectable({ providedIn: 'root' })
export class EmployeeRosterService {
  private readonly http = inject(HttpClient);
  private readonly userService = inject(UserService);
  private readonly orgApi = inject(PrimeOrgApiService);
  private readonly subServiceService = inject(SubServiceService);

  load(): Observable<EmployeeRoster> {
    return forkJoin({
      users: this.userService.getAllUsers(),
      overview: this.orgApi.loadOverview(),
      subServices: this.subServiceService.getAllSubServices(),
      directoryEmployees: this.http.get<DirectoryEmployeeOrgRef[]>('/api/directory/employees'),
      businessDepartments: this.http.get<BusinessDepartmentRef[]>('/api/directory/business-departments'),
    }).pipe(
      map(({ users, overview, subServices, directoryEmployees, businessDepartments }) =>
        buildRoster(users, overview ?? null, subServices ?? [], directoryEmployees ?? [], businessDepartments ?? [])),
      // Organisation indisponible : la liste reste utilisable avec les libellés enregistrés côté Planning.
      catchError(() => this.userService.getAllUsers().pipe(map((users) => buildRoster(users, null, [], [], [])))),
    );
  }

  /** Télécharge l'Excel des employés : tous, actifs seulement ou inactifs seulement. */
  async exportExcel(roster: EmployeeRoster, scope: EmployeeStatusScope): Promise<number> {
    const rows = buildEmployeeExportRows(
      roster.users,
      (u) => roster.perimeters.get(u.id) ?? enrichUserOrgPerimeter(u, [], null, []),
      scope,
    );
    await downloadEmployeesExcel(rows, scope);
    return rows.length;
  }
}

function buildRoster(
  users: User[],
  overview: OrgAssignmentsOverview | null,
  subServices: readonly { id: number; primeServiceId?: string | null }[],
  directoryEmployees: readonly DirectoryEmployeeOrgRef[],
  businessDepartments: readonly BusinessDepartmentRef[],
): EmployeeRoster {
  const departments = overview?.departments?.length ? overview.departments : [];
  const perimeters = new Map<number, UserOrgPerimeterView>();
  for (const user of users) {
    perimeters.set(
      user.id,
      enrichUserOrgPerimeter(user, departments, overview, subServices, directoryEmployees, businessDepartments),
    );
  }
  return { users, overview, perimeters };
}
