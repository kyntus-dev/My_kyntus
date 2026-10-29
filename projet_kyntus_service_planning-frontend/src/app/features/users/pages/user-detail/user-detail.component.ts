import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { UserService } from '../../services/user.service';
import { User, type EmployeeLifecycleStatus } from '../../users-module';
import { copyTextToClipboard } from '../../../../core/lib/clipboard.util';
import { EmployeeFieldService } from '../../services/employee-field.service';
import type { EmployeeImportFieldConfig } from '../../services/employee-import.service';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { ArrowLeft, History, KeyRound, Pencil, Trash2 } from 'lucide';
import type { Department } from '../../../prime/models';
import { PrimeOrgApiService, type OrgAssignmentsOverview } from '../../../prime/services/prime-org-api.service';
import type { SubService } from '../../../sub-services/sub-services-module';
import { SubServiceService } from '../../../sub-services/services/sub-service.service';
import { ParrainageApiService } from '../../../parrainage/services/parrainage-api.service';
import type { Referral } from '../../../parrainage/models/referral.model';
import { ContractService, type ContractResponse } from '../../../contract/services/contract.service';
import { resolveUserGuid } from '../../../../core/lib/user-guid.util';
import { formatHttpErrorMessage } from '../../../../core/lib/http-error-message.util';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import {
  enrichUserOrgPerimeter,
  orgCellLabel,
  orgDepartmentLabel,
  orgPerimeterSummary,
  orgServiceIsCelluleMirror,
  type BusinessDepartmentRef,
  type DirectoryEmployeeOrgRef,
  type UserOrgPerimeterView,
} from '../../../../core/org/user-org-perimeter';
import {
  buildContractDisplayRows,
  buildEmployeeDetailSections,
  contractLevelLabel,
  expertiseLevelLabel,
  formatSeniorityDuration,
  seniorityReferenceDate,
  type EmployeeDetailSection,
} from '../../../../core/hr/user-hr-display.util';
import { PilotRotationHistoryModalComponent } from '../../../prime/components/pilot-rotation-history-modal.component';

@Component({
  selector: 'app-user-detail',
  standalone: true,
  imports: [CommonModule, LucideIconComponent, KyntusPageHeaderComponent, PilotRotationHistoryModalComponent],
  templateUrl: './user-detail.component.html',
  styleUrls: ['./user-detail.component.css']
})
export class UserDetailComponent implements OnInit {
  readonly icons = { back: ArrowLeft, edit: Pencil, trash: Trash2, history: History, key: KeyRound };
  readonly orgCellLabel = orgCellLabel;
  readonly orgDepartmentLabel = orgDepartmentLabel;
  readonly contractLevelLabel = contractLevelLabel;
  readonly expertiseLevelLabel = expertiseLevelLabel;
  user: User | null = null;
  lifecycle: EmployeeLifecycleStatus | null = null;
  detailSections: EmployeeDetailSection[] = [];
  /** Masque les champs « — » pour alléger la fiche (recommandé). */
  hideEmptyFields = true;
  linkedReferral: Referral | null = null;
  perimeter: UserOrgPerimeterView = { operationalDepartment: null, pole: null, cellule: null, service: null };
  rotationHistoryOpen = false;
  rotationHistoryEmployeeId = '';
  rotationHistoryEmployeeName = '';
  loading = false;
  error: string | null = null;
  resettingPassword = false;
  resetCredentials: { email: string; password: string } | null = null;
  showResetPassword = false;
  downloadingCredentialsExcel = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private userService: UserService,
    private orgApi: PrimeOrgApiService,
    private subServiceService: SubServiceService,
    private fieldService: EmployeeFieldService,
    private http: HttpClient,
    private contractService: ContractService,
    private parrainageApi: ParrainageApiService,
    private confirmService: KyntusConfirmService,
    private toastService: KyntusToastService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loadUser(id);
  }

  /** Service miroir de la cellule (même nom) : colonne Service masquée pour éviter le doublon. */
  get serviceIsCelluleMirror(): boolean {
    return orgServiceIsCelluleMirror(this.perimeter);
  }

  get perimeterSummary(): string {
    return orgPerimeterSummary(this.perimeter);
  }

  loadUser(id: number): void {
    this.loading = true;
    this.error = null;
    forkJoin({
      user: this.userService.getUserById(id),
      departments: this.http.get<Department[]>('/api/prime/departments'),
      overview: this.orgApi.loadOverview(),
      subServices: this.subServiceService.getAllSubServices(),
      directoryEmployees: this.http.get<DirectoryEmployeeOrgRef[]>('/api/directory/employees'),
      businessDepartments: this.http.get<BusinessDepartmentRef[]>('/api/directory/business-departments'),
      customFields: this.fieldService.getFields(true),
      contracts: this.contractService.getByUser(id).pipe(catchError(() => of([] as ContractResponse[]))),
    }).subscribe({
      next: ({ user, departments, overview, subServices, directoryEmployees, businessDepartments, customFields, contracts }) => {
        this.applyUserData(
          user,
          departments ?? [],
          overview,
          subServices ?? [],
          directoryEmployees ?? [],
          businessDepartments ?? [],
          customFields.filter((f) => f.isSystemField === false),
          contracts ?? [],
        );
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.userService.getUserById(id).subscribe({
          next: (user) => {
            this.applyUserData(user, [], null, [], [], [], [], []);
            this.loading = false;
            this.cdr.detectChanges();
          },
          error: () => {
            this.error = 'Impossible de récupérer le profil.';
            this.loading = false;
            this.cdr.detectChanges();
          },
        });
      },
    });
  }

  private applyUserData(
    user: User,
    departments: Department[],
    overview: OrgAssignmentsOverview | null,
    subServices: SubService[],
    directoryEmployees: DirectoryEmployeeOrgRef[],
    businessDepartments: BusinessDepartmentRef[],
    customFields: EmployeeImportFieldConfig[],
    contracts: ContractResponse[],
  ): void {
    this.user = user;
    const hasContract = contracts.length > 0;
    const base = user.lifecycleStatus ?? this.fallbackLifecycle(user);
    this.lifecycle = {
      ...base,
      hasContract,
      steps: [
        ...base.steps.filter((s) => s.id !== 'contract'),
        {
          id: 'contract',
          label: 'Contrat',
          state: hasContract ? 'done' : (base.enFormation ? 'pending' : 'current'),
        },
      ],
    };
    this.perimeter = enrichUserOrgPerimeter(
      user,
      departments,
      overview,
      subServices,
      directoryEmployees,
      businessDepartments,
    );

    const mentorEmployees = (overview?.employees ?? []).map((e) => ({
      id: e.id,
      firstName: e.firstName,
      lastName: e.lastName,
    }));

    const latestContract = contracts.length
      ? [...contracts].sort((a, b) => new Date(b.startDate).getTime() - new Date(a.startDate).getTime())[0]
      : null;

    const contractRows = latestContract
      ? [
          { label: 'Référence contrat', value: String(latestContract.id) },
          ...buildContractDisplayRows(latestContract),
        ]
      : [];

    const customFieldRows = customFields.map((field) => ({
      label: field.label,
      value: user.customFields?.[field.fieldKey]?.trim() ? user.customFields[field.fieldKey]!.trim() : '—',
    }));

    this.detailSections = buildEmployeeDetailSections(user, {
      mentorEmployees,
      contractRows,
      customFieldRows,
    });

    void this.loadLinkedReferral(user);
  }

  openPilotRotationHistory(): void {
    if (!this.user) return;
    const guid = resolveUserGuid(this.user);
    if (!guid) {
      this.toastService.error('Identifiant employé (GUID) introuvable — historique indisponible.');
      return;
    }
    this.rotationHistoryEmployeeId = guid;
    this.rotationHistoryEmployeeName = `${this.user.firstName ?? ''} ${this.user.lastName ?? ''}`.trim();
    this.rotationHistoryOpen = true;
  }

  closePilotRotationHistory(): void {
    this.rotationHistoryOpen = false;
    this.rotationHistoryEmployeeId = '';
    this.rotationHistoryEmployeeName = '';
  }

  private async loadLinkedReferral(user: User): Promise<void> {
    const guid = resolveUserGuid(user);
    if (!guid) return;
    try {
      const referrals = await this.parrainageApi.getReferrals();
      this.linkedReferral = referrals.find((r) => r.candidateEmployeeId === guid) ?? null;
      this.cdr.detectChanges();
    } catch {
      this.linkedReferral = null;
    }
  }

  getAnciennete(): string {
    if (!this.user) return '—';
    return formatSeniorityDuration(seniorityReferenceDate(this.user));
  }

  /** Sections affichées : champs vides filtrés / triés pour une lecture plus confortable. */
  get visibleDetailSections(): EmployeeDetailSection[] {
    return this.detailSections.map((section) => {
      const rows = this.hideEmptyFields
        ? section.rows.filter((r) => r.value !== '—')
        : [...section.rows].sort((a, b) => Number(a.value === '—') - Number(b.value === '—'));
      return { ...section, rows };
    });
  }

  isWideField(label: string): boolean {
    const wide = new Set([
      'Adresse',
      'Intitulés études',
      'Mail interne',
      'GUID employé',
      'Services supervisés',
      'Sous-services supervisés',
    ]);
    return wide.has(label);
  }

  goBack(): void { this.router.navigate(['/users']); }
  editUser(): void { this.router.navigate(['/users', 'edit', this.user?.id]); }

  async resetPassword(): Promise<void> {
    if (!this.user || this.resettingPassword) return;
    const ok = await this.confirmService.confirm({
      title: 'Réinitialiser le mot de passe',
      message: `Générer un nouveau mot de passe pour ${this.user.email} ? L'ancien ne fonctionnera plus.`,
      confirmLabel: 'Réinitialiser',
      cancelLabel: 'Annuler',
    });
    if (!ok) return;

    this.resettingPassword = true;
    this.userService.resetPassword(this.user.id).subscribe({
      next: (result) => {
        this.resetCredentials = { email: result.email, password: result.temporaryPassword };
        this.showResetPassword = true;
        this.resettingPassword = false;
        this.toastService.success('Mot de passe réinitialisé.');
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.resettingPassword = false;
        this.toastService.error(formatHttpErrorMessage(err, 'Échec de la réinitialisation.'));
        this.cdr.detectChanges();
      },
    });
  }

  closeResetCredentials(): void {
    this.resetCredentials = null;
    this.showResetPassword = false;
  }

  async copyResetPassword(): Promise<void> {
    if (!this.resetCredentials?.password) return;
    try {
      await copyTextToClipboard(this.resetCredentials.password);
      this.toastService.success('Mot de passe copié.');
    } catch (err) {
      console.error('copyResetPassword', err);
      this.toastService.error('Impossible de copier le mot de passe.');
    }
  }

  async downloadResetCredentialsExcel(): Promise<void> {
    if (!this.resetCredentials || this.downloadingCredentialsExcel) return;
    this.downloadingCredentialsExcel = true;
    try {
      const { downloadCredentialsExcel } = await import('../../../../core/lib/credentials-excel.util');
      await downloadCredentialsExcel(
        [
          {
            email: this.resetCredentials.email,
            password: this.resetCredentials.password,
            firstName: this.user?.firstName,
            lastName: this.user?.lastName,
          },
        ],
        { fileNamePrefix: 'identifiants-mykyntus-reinit' },
      );
      this.toastService.success('Excel des identifiants téléchargé.');
    } catch {
      this.toastService.error('Échec du téléchargement Excel.');
    } finally {
      this.downloadingCredentialsExcel = false;
      this.cdr.detectChanges();
    }
  }

  async deleteUser(): Promise<void> {
    if (!this.user) return;
    const ok = await this.confirmService.confirm({
      title: 'Supprimer l\'employé',
      message: 'Supprimer cet employé ?',
      confirmLabel: 'Supprimer',
      variant: 'danger',
    });
    if (!ok) return;
    this.userService.deleteUser(this.user.id).subscribe({
      next: () => this.router.navigate(['/users']),
      error: (err) => this.toastService.error(`Erreur: ${err.error?.message}`),
    });
  }

  private fallbackLifecycle(user: User): EmployeeLifecycleStatus {
    const enFormation = !!user.hrProfile?.enFormation;
    return {
      phase: !user.isActive ? 'inactive' : enFormation ? 'onboarding_formation' : 'active',
      label: !user.isActive ? 'Inactif' : enFormation ? 'En formation' : 'Actif',
      isActive: user.isActive,
      enFormation,
      authProvisioned: true,
      editDeepLink: `/users/${user.id}/edit`,
      formationDeepLink: enFormation ? `/users/${user.id}/edit` : null,
      passageProductionDeepLink: enFormation ? '/formations/passage-production' : null,
      steps: [
        { id: 'account', label: 'Compte Planning', state: user.isActive ? 'done' : 'blocked' },
        { id: 'auth', label: 'Compte Auth', state: 'done' },
        { id: 'formation', label: 'Formation initiale', state: enFormation ? 'current' : 'done' },
        { id: 'production', label: 'Passage en production', state: enFormation ? 'pending' : 'done' },
      ],
    };
  }

  openLifecycleLink(path: string | null | undefined): void {
    if (!path) return;
    void this.router.navigateByUrl(path);
  }

  lifecyclePhaseClass(phase: string | undefined): string {
    switch (phase) {
      case 'inactive':
        return 'lifecycle--inactive';
      case 'awaiting_auth':
        return 'lifecycle--warn';
      case 'onboarding_formation':
        return 'lifecycle--formation';
      default:
        return 'lifecycle--active';
    }
  }
}
