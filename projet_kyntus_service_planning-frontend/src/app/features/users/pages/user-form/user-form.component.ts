import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin, defer, of, Observable, throwError } from 'rxjs';
import { retry, switchMap, map, catchError } from 'rxjs/operators';
import { formatHttpErrorMessage } from '../../../../core/lib/http-error-message.util';
import { copyTextToClipboard } from '../../../../core/lib/clipboard.util';
import { resolveUserGuid } from '../../../../core/lib/user-guid.util';
import { KyntusFormDraftService } from '../../../../core/drafts/kyntus-form-draft.service';
import { KyntusObjectDraftBinder } from '../../../../core/drafts/kyntus-object-draft.binder';
import { UserService } from '../../services/user.service';
import { SubServiceService } from '../../../sub-services/services/sub-service.service';
import { ServiceService } from '../../../services/services/service';
import { CreateUserDto, UpdateUserDto, type UserHrProfile } from '../../users-module';
import { EmployeeFieldService } from '../../services/employee-field.service';
import type { EmployeeImportFieldConfig } from '../../services/employee-import.service';
import {
  HtelApiService,
  type HtelTechnicienDto,
} from '../../services/htel-api.service';
import { htelIdentityMatches, suggestHtelIdentity } from '../../services/htel-identity.util';
import { KyntusPageHeaderComponent } from '../../../../shared/components/ui/kyntus-page-header.component';
import { LucideIconComponent } from '../../../../shared/lucide-icon.component';
import { KyntusSelectSyncDirective } from '../../../../shared/directives/kyntus-select-sync.directive';
import { LockKeyhole, Search, Sparkles } from 'lucide';
import { NavigationActionsService } from '../../../../core/navigation/navigation-actions.service';
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';
import {
  PrimeOrgApiService,
  type OrgAssignmentsOverview,
  type StructuralRoleAssignmentResult,
} from '../../../prime/services/prime-org-api.service';
import type {
  OperationalDepartmentNode,
  OrgCelluleNode,
  OrgPoleNode,
  OrgServiceNode,
} from '../../../prime/models/org-tree.types';
import type { SubService } from '../../../sub-services/sub-services-module';
import type { Service } from '../../../services/services-module';
import {
  cellulesForPole,
  filterOperationalCellulesBySearch,
  filterOperationalDepartmentsBySearch,
  filterOperationalPolesBySearch,
  filterOperationalServicesBySearch,
  findOperationalSelectionByCelluleId,
  findOperationalSelectionByPoleId,
  findOperationalSelectionByServiceId,
  flattenOperationalServiceOptions,
  isUnassignedPole,
  operationalSelectionSummary,
  polesForOperationalDept,
  servicesForCellule,
  type OperationalFlatServiceOption,
} from '../../../../core/org/operational-org-picker';
import { normalizeOrgSearch } from '../../../../core/org/org-structure-filter';
import {
  resolvePlanningServiceIdByPrimeCelluleId,
  resolveSubServiceIdByPrimeServiceId,
} from '../../../../core/org/planning-org-picker';
import {
  orgAssignmentHint,
  orgAssignmentIsRequired,
  orgAssignmentRequiresCellule,
  orgAssignmentRequiresOperationalDept,
  orgAssignmentRequiresPole,
  orgAssignmentRequiresService,
  orgRoleAssignmentDepth,
  needsPrimeStructureAssignment,
  isReferentTechniqueRole,
  isSuperviseurRole,
  isSupportManagerRole,
  isChefDeProjetRole,
  isPiloteRole,
  type OrgRoleAssignmentDepth,
} from '../../../../core/org/org-role-assignment';
import {
  addOrgNodeSelection,
  clearMultiOrgSelection,
  directoryAssignmentKindForRole,
  hydrateMultiOrgSelectionFromOverview,
  removeOrgNodeSelection,
  resolveOrgNodePathLabel,
  summarizeMultiOrgSelection,
  supportsMultiOrgSelection,
  validateMultiOrgSelection,
} from '../../../../core/org/multi-org-selection';
import {
  buildCrossRoleOverwriteMessage,
  employeeDisplayName,
  findConflictingStructuralRole,
  findEmployeeStructuralRoles,
  findStructureIncumbents,
  filterSuperviseursForChefDeProjet,
  filterReferentsForSuperviseur,
  buildMultiChargeHint,
} from '../../../../core/org/org-structure-incumbent.util';
import {
  HR_EDUCATION_LEVEL_OPTIONS,
  HR_MARITAL_STATUS_OPTIONS,
  HR_MARITAL_STATUS_WITH_CHILDREN,
  HR_NATIONALITY_OPTIONS,
  defaultNationalityCode,
  nationalityLabelForCode,
  requiresAutoentrepreneur,
  syncNationalityCodeFromLabel,
} from '../../../../core/hr/hr-form-options';
import { contractLevelLabel, formatSeniorityDuration } from '../../../../core/hr/user-hr-display.util';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import { KyntusToastService } from '../../../../shared/components/ui/kyntus-toast.service';
import { ContractFieldsComponent } from '../../../../shared/components/contract-fields/contract-fields.component';
import {
  createEmptyContractFields,
  statusLabelToValue,
  type ContractFieldsModel,
} from '../../../../shared/components/contract-fields/contract-fields.model';
import { ParrainageApiService } from '../../../parrainage/services/parrainage-api.service';
import type { Referral } from '../../../parrainage/models/referral.model';
import {
  filterLinkableReferrals,
  matchReferralCandidates,
  rankReferralsByQuery,
  type ReferralMatchCandidate,
  type ReferralMatchResult,
} from '../../../parrainage/utils/referral-candidate-match.util';
import { REFERRAL_STATUS_LABELS } from '../../../parrainage/utils/referral-status.util';
import { FormationTrainingService } from '../../../../core/services/formation-training.service';
import type {
  FormationDocumentChecklistItemDto,
  InitialTrainingPathDto,
} from '../../../../core/models/formation-training.models';
import {
  ContractService,
  type CreateContractDto,
  type UpdateContractDto,
} from '../../../contract/services/contract.service';

interface RoleOption { id: number; name: string; }
interface SupportDepartmentOption { id: string; code: string; name: string; kind: string; isActive?: boolean; }
type OrgAssignmentMode = 'operational' | 'support';
type WizardStepId = 'identity' | 'position' | 'pathway' | 'finalize';

@Component({
  selector: 'app-user-form',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, LucideIconComponent, KyntusSelectSyncDirective, KyntusPageHeaderComponent, ContractFieldsComponent],
  templateUrl: './user-form.component.html',
  styleUrls: ['./user-form.component.css']
})
export class UserFormComponent implements OnInit, OnDestroy {
  private readonly formDrafts = inject(KyntusFormDraftService);
  private draftBinder?: KyntusObjectDraftBinder<{
    form: UserFormComponent['form'];
    hrProfile: UserFormComponent['hrProfile'];
    contractDraft: ContractFieldsModel;
    customFieldValues: Record<string, string>;
  }>;

  readonly icons = { lock: LockKeyhole, search: Search, detect: Sparkles };
  isEditMode = false;
  userId: number | null = null;
  subServices: SubService[] = [];
  planningServices: Service[] = [];
  operationalDepartments: OperationalDepartmentNode[] = [];
  unassignedPoles: OrgPoleNode[] = [];
  orgOverview: OrgAssignmentsOverview | null = null;
  orgLoading = false;
  orgOperationalDeptId = '';
  orgPoleId = '';
  orgCelluleId = '';
  orgServiceId = '';
  /** Périmètres multi-nœuds (Chef / Superviseur / RT) — ancre primaire séparée. */
  selectedOrgNodeIds: string[] = [];
  primaryOrgNodeId = '';
  orgMirrorWarning: string | null = null;
  orgStructureSearch = '';
  orgFilterPoleId = '';
  orgFilterCelluleId = '';
  orgMode: OrgAssignmentMode = 'operational';
  supportDepartmentId = '';
  supportDepartments: SupportDepartmentOption[] = [];
  operationalBusinessDepartmentId = '';
  operationalBusinessDepartments: SupportDepartmentOption[] = [];
  supportDeptLoading = false;
  loading = false;
  submitting = false;
  showCreateSuccess = false;
  createdUserId: number | null = null;
  createSuccessMessage: string | null = null;
  createdCredentials: { email: string; password: string } | null = null;
  showCreatedPassword = false;
  downloadingCredentialsExcel = false;

  currentWizardStep: WizardStepId = 'identity';
  showCivilDetails = true;
  showAdminDetails = true;
  showCareerDetails = true;
  error: string | null = null;
  emailError: string | null = null;
  personalEmailError: string | null = null;
  roles: RoleOption[] = [];
  private loadedManagedServiceIds: number[] = [];
  private loadedManagedSubServiceIds: number[] = [];
  private loadedUserGuid = '';
  customEmployeeFields: EmployeeImportFieldConfig[] = [];
  customFieldValues: Record<string, string> = {};

  /** Miroir HTEL (lecture seule — source de vérité HTEL). */
  htelIdTechnicien: number | null = null;
  htelCode = '';
  htelPreviewHint = '';
  htelSourceName = '';
  htelIdentityConfirmed = false;
  htelLoading = false;
  htelLoadError = '';
  htelSelectionId: number | null = null;
  htelCreationChoices: HtelTechnicienDto[] = [];
  private htelAvailableIds = new Set<number>();
  private preferredIdTechnicien: number | null = null;
  private htelTechniciensCache: HtelTechnicienDto[] = [];

  form = {
    roleId: 0,
    subServiceId: null as number | null,
    firstName: '',
    lastName: '',
    email: '',
    hireDate: this.toDateInputValue(new Date()),
    isActive: true,
    level: 1,
    chefDeProjetId: '',
    superviseurId: '',
    referentTechniqueId: '',
    niveauExpertiseMetier: null as number | null,
  };

  hrProfile = {
    dateNaissance: '',
    villeNaissance: '',
    nationalite: '',
    numeroCarteAutoentrepreneur: '',
    sexe: '',
    situationFamiliale: '',
    nombreEnfants: null as number | null,
    cin: '',
    adresse: '',
    emailPersonnel: '',
    telephone1: '',
    telephoneUrgence: '',
    relationUrgence: '',
    rib: '',
    immatriculationInterne: '',
    immatriculationCnss: '',
    dateEntree: '',
    dateAnciennete: '',
    dateSortie: '',
    dateEvolutionPoste: '',
    ancienPoste: '',
    ancienService: '',
    niveauScolaire: '',
    intitulesEtudes: '',
    enFormation: false,
    dateDebutFormation: '',
    dateFinFormationPrevue: '',
  };

  contractDraft: ContractFieldsModel = createEmptyContractFields();
  contractId: number | null = null;
  contractLoading = false;

  formationDocs: FormationDocumentChecklistItemDto[] = [];
  formationDocsLoading = false;
  /** Query ?passageProduction=pathId — confirmation RH sans changer le statut avant clic. */
  passageProductionPathId = '';
  passageProductionBusy = false;
  /** Affectation org posée par le formateur — préremplit la cascade si Directory est vide. */
  trainingPathOrg: InitialTrainingPathDto | null = null;

  selectedReferralId = '';
  linkableReferrals: Referral[] = [];
  /** Pool pour l'alerte de matching (dossiers sans employé lié). */
  matchableReferrals: Referral[] = [];
  selectedReferral: Referral | null = null;
  referralRewardAmount = 0;
  referralLoading = false;
  referralLockedFromUrl = false;
  referralPositionHint = '';
  referralSearchQuery = '';
  referralMatchResult: ReferralMatchResult | null = null;
  referralManualIdentityEdit = false;
  /** RH a ignoré l'alerte de matching pour cette saisie. */
  referralMatchDismissed = false;
  private referralMatchTimer: ReturnType<typeof setTimeout> | null = null;
  readonly referralStatusLabels = REFERRAL_STATUS_LABELS;

  niveauScolaireCode = '';
  niveauScolaireAutre = '';
  nationaliteCode = '';
  nationaliteAutre = '';
  situationAvecEnfants: '' | 'OUI' | 'NON' = '';
  ancienPosteRoleId = 0;
  ancienOrgOperationalDeptId = '';
  ancienOrgPoleId = '';
  ancienOrgCelluleId = '';
  ancienOrgServiceId = '';

  readonly maritalStatusOptions = HR_MARITAL_STATUS_OPTIONS;
  readonly educationLevelOptions = HR_EDUCATION_LEVEL_OPTIONS;
  readonly nationalityOptions = HR_NATIONALITY_OPTIONS;
  readonly contractLevelLabel = contractLevelLabel;

  get computedAncienneteLabel(): string {
    const ref = this.hrProfile.dateEntree.trim() || this.form.hireDate;
    return formatSeniorityDuration(ref);
  }

  onDateEntreeChange(value: string): void {
    this.hrProfile.dateEntree = value;
    // Ancienneté = date d'entrée (pas de saisie manuelle).
    this.hrProfile.dateAnciennete = value;
  }

  onHireDateChange(value: string): void {
    this.form.hireDate = value;
    if (!this.hrProfile.dateEntree.trim()) {
      this.hrProfile.dateEntree = value;
    }
    this.hrProfile.dateAnciennete = this.hrProfile.dateEntree.trim() || value;
  }

  private readonly defaultProbation: Record<string, number> = {
    CDI: 90, CDD: 30, Stage: 15, ANAPEC: 0,
  };

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private userService: UserService,
    private subServiceService: SubServiceService,
    private serviceService: ServiceService,
    private navActions: NavigationActionsService,
    private orgApi: PrimeOrgApiService,
    private confirmService: KyntusConfirmService,
    private toastService: KyntusToastService,
    private fieldService: EmployeeFieldService,
    private htelApi: HtelApiService,
    private http: HttpClient,
    private contractService: ContractService,
    private parrainageApi: ParrainageApiService,
    private formationTraining: FormationTrainingService,
    private session: KyntusSessionService,
    private cdr: ChangeDetectorRef
  ) {}

  get canEditExpertiseMetier(): boolean {
    const role = this.session.getRole().toUpperCase();
    return role === 'RH' || role === 'ADMIN';
  }

  ngOnInit(): void {
    this.isEditMode = !!this.route.snapshot.paramMap.get('id');
    this.fieldService.getFields(true).subscribe({
      next: (fields) => {
        this.customEmployeeFields = fields.filter((f) => f.isSystemField === false);
        for (const field of this.customEmployeeFields) {
          this.customFieldValues[field.fieldKey] ??= '';
        }
        this.cdr.detectChanges();
      },
    });
    this.loadOrgAndSubServices();
    this.loadBusinessDepartments();
    this.loadRoles();
    void this.loadLinkableReferrals();
    const q = this.route.snapshot.queryParamMap;
    const referralId = q.get('referralId');
    const passagePath = q.get('passageProduction');
    if (passagePath) {
      this.passageProductionPathId = passagePath;
      void this.loadTrainingPathOrgThenReconcile();
    }
    if (referralId) {
      this.referralLockedFromUrl = true;
      void this.prefillFromReferral(referralId);
    }
    if (!this.isEditMode) {
      const lastName = q.get('lastName');
      const firstName = q.get('firstName');
      const idTech = q.get('idTechnicien');
      if (lastName) this.form.lastName = lastName;
      if (firstName) this.form.firstName = firstName;
      if (idTech && Number.isSafeInteger(Number(idTech)) && Number(idTech) > 0) {
        this.preferredIdTechnicien = Number(idTech);
        this.htelIdTechnicien = this.preferredIdTechnicien;
        this.htelSelectionId = this.preferredIdTechnicien;
      }
      this.refreshHtelPreview();
    }
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode = true;
      this.userId = Number(id);
      this.loadUser(this.userId);
    } else {
      this.startFormDraft();
    }
    this.preloadHtelTechniciens();
  }

  ngOnDestroy(): void {
    this.draftBinder?.destroy();
  }

  private draftKey(): string {
    return this.isEditMode && this.userId
      ? `user-form-edit-${this.userId}`
      : this.preferredIdTechnicien ? `user-form-create-htel-${this.preferredIdTechnicien}` : 'user-form-create';
  }

  private startFormDraft(): void {
    this.draftBinder?.destroy();
    this.draftBinder = new KyntusObjectDraftBinder(
      this.formDrafts,
      this.draftKey(),
      () => ({
        form: { ...this.form },
        hrProfile: { ...this.hrProfile },
        contractDraft: { ...this.contractDraft },
        customFieldValues: { ...this.customFieldValues },
      }),
      (s) => {
        if (s.form) this.form = { ...this.form, ...s.form };
        if (s.hrProfile) this.hrProfile = { ...this.hrProfile, ...s.hrProfile };
        if (s.contractDraft) this.contractDraft = { ...this.contractDraft, ...s.contractDraft };
        if (s.customFieldValues) {
          this.customFieldValues = { ...this.customFieldValues, ...s.customFieldValues };
        }
      },
    );
    this.draftBinder.start();
  }

  touchDraft(): void {
    this.draftBinder?.touch();
  }

  /** Annule une création partielle (Directory/Planning/Auth) après échec d'une étape post-create. */
  private compensateFailedCreate$(userId: number, err: unknown, fallback: string): Observable<never> {
    return this.userService.deleteUser(userId).pipe(
      catchError(() => of(void 0)),
      switchMap(() => throwError(() => new Error(formatHttpErrorMessage(err, fallback)))),
    );
  }

  async resetDraftForm(): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Réinitialiser le formulaire',
      message: this.isEditMode
        ? 'Recharger les données enregistrées et ignorer les modifications non sauvegardées ?'
        : 'Effacer toutes les saisies et le brouillon en cours ?',
      confirmLabel: 'Réinitialiser',
      cancelLabel: 'Annuler',
      variant: 'warning',
    });
    if (!ok) return;

    this.error = null;
    this.draftBinder?.discard();

    if (this.isEditMode && this.userId) {
      this.loadUser(this.userId);
      return;
    }

    this.resetCreateFormState();
    this.htelIdentityConfirmed = false;
    this.startFormDraft();
    if (this.preferredIdTechnicien) Object.assign(this.form, suggestHtelIdentity(this.htelSourceName));
    this.cdr.detectChanges();
  }

  private resetCreateFormState(): void {
    this.form = {
      roleId: 0,
      subServiceId: null,
      firstName: '',
      lastName: '',
      email: '',
      hireDate: this.toDateInputValue(new Date()),
      isActive: true,
      level: 1,
      chefDeProjetId: '',
      superviseurId: '',
      referentTechniqueId: '',
      niveauExpertiseMetier: null,
    };
    this.hrProfile = {
      dateNaissance: '',
      villeNaissance: '',
      nationalite: '',
      numeroCarteAutoentrepreneur: '',
      sexe: '',
      situationFamiliale: '',
      nombreEnfants: null,
      cin: '',
      adresse: '',
      emailPersonnel: '',
      telephone1: '',
      telephoneUrgence: '',
      relationUrgence: '',
      rib: '',
      immatriculationInterne: '',
      immatriculationCnss: '',
      dateEntree: '',
      dateAnciennete: '',
      dateSortie: '',
      dateEvolutionPoste: '',
      ancienPoste: '',
      ancienService: '',
      niveauScolaire: '',
      intitulesEtudes: '',
      enFormation: false,
      dateDebutFormation: '',
      dateFinFormationPrevue: '',
    };
    this.contractDraft = createEmptyContractFields();
    this.customFieldValues = {};
    this.emailError = null;
    this.personalEmailError = null;
    this.currentWizardStep = 'identity';
    this.clearOrgAssignment();
    this.clearOrgFilters();
  }

  get htelSelectionOutsideChoices(): boolean {
    return !!this.htelSelectionId && !this.htelCreationChoices.some(t => t.idTechnicien === this.htelSelectionId);
  }

  reloadHtel(): void { this.preloadHtelTechniciens(); }

  private preloadHtelTechniciens(): void {
    if (this.isEditMode) return; // La fiche existante se rapproche dans Liaisons HTEL.
    this.htelLoading = true;
    this.htelLoadError = '';
    this.htelApi.getLiaisons().subscribe({
      next: (report) => {
        this.htelCreationChoices = report.orphansHtel.filter(t => t.actif === 1);
        this.htelTechniciensCache = [
          ...report.orphansHtel,
          ...report.ambiguous.filter(t => !t.linkedEmployeeId).map(t => ({ idTechnicien: t.idTechnicien, technicien: t.technicien, actif: t.actif, code: t.code })),
        ];
        this.htelAvailableIds = new Set(this.htelTechniciensCache.filter(t => t.actif === 1).map(t => t.idTechnicien));
        this.htelLoading = false;
        this.refreshHtelPreview();
        if (this.preferredIdTechnicien && !this.form.firstName.trim() && !this.form.lastName.trim()) {
          Object.assign(this.form, suggestHtelIdentity(this.htelSourceName));
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.htelLoading = false;
        this.htelAvailableIds.clear();
        this.htelLoadError = formatHttpErrorMessage(err, 'HTEL indisponible. Réessayez la comparaison.');
        this.cdr.detectChanges();
      },
    });
  }

  async selectHtelTechnicien(value: number | null): Promise<void> {
    if (this.referralLockedFromUrl || this.passageProductionPathId) return;
    if (value === this.preferredIdTechnicien) return;
    if (this.form.firstName.trim() || this.form.lastName.trim() || this.form.email.trim()) {
      const ok = await this.confirmService.confirm({
        title: 'Changer de collaborateur',
        message: 'Les saisies actuelles restent dans leur brouillon. Ouvrir le formulaire du collaborateur sélectionné ?',
        confirmLabel: 'Ouvrir', cancelLabel: 'Annuler', variant: 'warning',
      });
      if (!ok) { this.htelSelectionId = this.preferredIdTechnicien; this.cdr.detectChanges(); return; }
    }
    this.draftBinder?.destroy();
    this.resetCreateFormState();
    this.selectedReferralId = '';
    this.referralMatchResult = null;
    this.referralMatchDismissed = false;
    this.preferredIdTechnicien = value;
    this.htelSelectionId = value;
    this.htelIdTechnicien = value;
    this.htelIdentityConfirmed = false;
    this.htelSourceName = '';
    this.htelCode = '';
    this.startFormDraft();
    this.refreshHtelPreview();
    if (value && !this.form.firstName.trim() && !this.form.lastName.trim()) {
      Object.assign(this.form, suggestHtelIdentity(this.htelSourceName));
    }
    await this.router.navigate([], { relativeTo: this.route,
      queryParams: { idTechnicien: value, firstName: null, lastName: null },
      queryParamsHandling: 'merge', replaceUrl: true });
    this.cdr.detectChanges();
  }

  private refreshHtelPreview(): void {
    if (this.isEditMode) return;
    if (!this.preferredIdTechnicien) {
      this.htelIdTechnicien = null;
      this.htelCode = '';
      this.htelSourceName = '';
      this.htelPreviewHint = 'Choisissez un collaborateur HTEL ci-dessus ou vérifiez les rapprochements proposés.';
      return;
    }
    const tech = this.htelTechniciensCache.find(t => t.idTechnicien === this.preferredIdTechnicien);
    if (!tech || !this.htelAvailableIds.has(tech.idTechnicien)) {
      this.htelPreviewHint = 'Ce technicien est déjà lié, inactif ou absent de HTEL. Vérifiez les liaisons avant de créer une fiche.';
      return;
    }
    this.htelIdTechnicien = tech.idTechnicien;
    this.htelCode = tech.code;
    this.htelSourceName = tech.technicien;
    this.htelPreviewHint = 'Identité issue de HTEL. Vérifiez la séparation Nom / Prénom et complétez les autres champs RH.';
  }

  loadRoles(): void {
    this.userService.getRoles().subscribe({
      next: (roles) => {
        this.roles = (roles ?? []).map((r) => ({
          id: Number((r as RoleOption & { Id?: number }).id ?? (r as { Id?: number }).Id),
          name: String((r as RoleOption & { Name?: string }).name ?? (r as { Name?: string }).Name ?? ''),
        }));
        this.syncAncienPickersFromProfile();
        this.cdr.detectChanges();
      },
      error: () => {
        this.roles = [
          { id: 1, name: 'Pilote' },
          { id: 2, name: 'RH' },
          { id: 3, name: 'Superviseur' },
          { id: 4, name: 'Référent technique' },
          { id: 5, name: 'Chef de projet' },
          { id: 6, name: 'Admin' },
          { id: 7, name: 'Audit' },
          { id: 8, name: 'EquipeFormation' },
          { id: 9, name: 'Manager' },
          { id: 10, name: 'Comptabilité' },
        ];
        this.syncAncienPickersFromProfile();
        this.cdr.detectChanges();
      }
    });
  }

  loadBusinessDepartments(): void {
    this.supportDeptLoading = true;
    this.http.get<SupportDepartmentOption[]>('/api/directory/business-departments').subscribe({
      next: (depts) => {
        const active = (depts ?? []).filter((d) => d.isActive !== false);
        this.supportDepartments = active.filter(
          (d) => String(d.kind).toLowerCase() === 'support',
        );
        this.operationalBusinessDepartments = active.filter(
          (d) => String(d.kind).toLowerCase() === 'operational',
        );
        this.supportDeptLoading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.supportDepartments = [];
        this.operationalBusinessDepartments = [];
        this.supportDeptLoading = false;
        this.cdr.detectChanges();
      },
    });
  }

  setOrgMode(mode: OrgAssignmentMode): void {
    if (this.orgMode === mode) return;
    this.orgMode = mode;
    if (!this.isEditMode) {
      this.form.roleId = 0;
    }
    this.supportDepartmentId = '';
    this.operationalBusinessDepartmentId = '';
    this.clearOrgAssignment();
    this.cdr.detectChanges();
  }

  patchSupportDepartment(deptId: string): void {
    this.supportDepartmentId = deptId;
    this.cdr.detectChanges();
  }

  patchOperationalBusinessDepartment(deptId: string): void {
    this.operationalBusinessDepartmentId = deptId;
    this.cdr.detectChanges();
  }

  private resolvedRoleId(): number {
    return Number(this.form.roleId) || 0;
  }

  get selectedRoleName(): string {
    const id = this.resolvedRoleId();
    return this.roles.find((r) => Number(r.id) === id)?.name ?? '';
  }

  get isSupportMode(): boolean {
    return this.orgMode === 'support';
  }

  get filteredRoles(): RoleOption[] {
    const operationalStructureRoles = (name: string) =>
      isChefDeProjetRole(name) || isSuperviseurRole(name) || isReferentTechniqueRole(name);
    if (this.isSupportMode) {
      return this.roles.filter((r) => !operationalStructureRoles(r.name));
    }
    return this.roles;
  }

  get orgAssignmentDepth(): OrgRoleAssignmentDepth {
    return orgRoleAssignmentDepth(this.selectedRoleName);
  }

  get showOrgAssignmentBlock(): boolean {
    return !this.isSupportMode && this.orgAssignmentDepth !== 'none';
  }

  get showSupportAssignmentBlock(): boolean {
    return this.isSupportMode;
  }

  get showOperationalManagerBlock(): boolean {
    return !this.isSupportMode && isSupportManagerRole(this.selectedRoleName);
  }

  get showOrgFlatServiceSelect(): boolean {
    return false;
  }

  get showOrgCascade(): boolean {
    return this.showOrgAssignmentBlock && !this.showOrgFlatServiceSelect;
  }

  get orgFlatServiceOptions(): OperationalFlatServiceOption[] {
    return flattenOperationalServiceOptions(this.operationalDepartments, this.unassignedPoles);
  }

  get orgPoleFilterOptions(): { id: string; name: string }[] {
    const poles: { id: string; name: string }[] = [];
    for (const md of this.operationalDepartments) {
      for (const p of md.poles) poles.push({ id: p.id, name: p.name });
    }
    for (const p of this.unassignedPoles) poles.push({ id: p.id, name: p.name });
    return poles;
  }

  get orgCelluleFilterOptions(): { id: string; name: string }[] {
    if (!this.orgFilterPoleId) return [];
    return cellulesForPole(this.operationalDepartments, this.unassignedPoles, this.orgFilterPoleId).map(
      (c) => ({ id: c.id, name: c.name }),
    );
  }

  get filteredFlatServices(): { visible: OperationalFlatServiceOption[]; totalMatches: number } {
    let matched = [...this.orgFlatServiceOptions];
    const q = normalizeOrgSearch(this.orgStructureSearch);
    if (q) matched = matched.filter((o) => o.label.toLowerCase().includes(q));
    if (this.orgFilterPoleId) matched = matched.filter((o) => o.poleId === this.orgFilterPoleId);
    if (this.orgFilterCelluleId) matched = matched.filter((o) => o.celluleId === this.orgFilterCelluleId);
    return { visible: matched.slice(0, 40), totalMatches: matched.length };
  }

  get filteredOperationalDepartments(): OperationalDepartmentNode[] {
    return filterOperationalDepartmentsBySearch(this.operationalDepartments, this.orgStructureSearch);
  }

  get filteredOrgPoleOptions(): OrgPoleNode[] {
    const fromDept = this.orgOperationalDeptId
      ? polesForOperationalDept(this.operationalDepartments, this.orgOperationalDeptId)
      : [];
    const filtered = filterOperationalPolesBySearch(fromDept, this.orgStructureSearch);
    if (!this.orgOperationalDeptId && this.unassignedPoles.length) {
      return filterOperationalPolesBySearch(this.unassignedPoles, this.orgStructureSearch);
    }
    return filtered;
  }

  get filteredOrgCelluleOptions(): OrgCelluleNode[] {
    return filterOperationalCellulesBySearch(this.orgCelluleOptions, this.orgStructureSearch);
  }

  get filteredOrgServiceOptions(): OrgServiceNode[] {
    return filterOperationalServicesBySearch(this.orgServiceOptions, this.orgStructureSearch);
  }

  get roleDetectionSummary(): string {
    const depth = this.orgAssignmentDepth;
    if (depth === 'pole') return 'Affectation automatique : Département → Pôle';
    if (depth === 'cellule') return 'Affectation automatique : Département → Pôle → Cellule';
    if (depth === 'service') {
      return 'Affectation automatique : Département → Pôle → Cellule → Service';
    }
    return '';
  }

  get selectedOrgSummary(): string {
    if (this.supportsMultiOrg) {
      return summarizeMultiOrgSelection(
        this.operationalDepartments,
        this.unassignedPoles,
        this.orgAssignmentDepth,
        {
          selectedOrgNodeIds: this.selectedOrgNodeIds,
          primaryOrgNodeId: this.primaryOrgNodeId,
        },
      );
    }
    return operationalSelectionSummary(
      this.operationalDepartments,
      this.unassignedPoles,
      this.orgOperationalDeptId,
      this.orgPoleId,
      this.orgCelluleId,
      this.orgServiceId,
    );
  }

  get supportsMultiOrg(): boolean {
    return supportsMultiOrgSelection(this.selectedRoleName);
  }

  get selectedOrgChipItems(): { id: string; label: string }[] {
    return this.selectedOrgNodeIds.map((id) => ({
      id,
      label: resolveOrgNodePathLabel(
        this.operationalDepartments,
        this.unassignedPoles,
        this.orgAssignmentDepth,
        id,
      ),
    }));
  }

  /** @deprecated Conservé pour compat — la multi-sélection est maintenant éditable ici. */
  get additionalStructuralChargesHint(): string {
    if (!this.isEditMode || !this.orgOverview || !this.loadedUserGuid.trim()) return '';
    if (this.supportsMultiOrg) return '';
    return buildMultiChargeHint(findEmployeeStructuralRoles(this.orgOverview, this.loadedUserGuid));
  }

  get showOrgFilterBar(): boolean {
    return this.showOrgAssignmentBlock && !this.orgLoading;
  }

  get showOrgOperationalDeptSelect(): boolean {
    return this.showOrgCascade && orgAssignmentRequiresOperationalDept(this.orgAssignmentDepth);
  }

  get showOrgPoleSelect(): boolean {
    return this.showOrgCascade && orgAssignmentRequiresPole(this.orgAssignmentDepth);
  }

  get showOrgCelluleSelect(): boolean {
    return this.showOrgCascade && orgAssignmentRequiresCellule(this.orgAssignmentDepth);
  }

  get showOrgServiceSelect(): boolean {
    return this.showOrgCascade && orgAssignmentRequiresService(this.orgAssignmentDepth);
  }

  get orgAssignmentRequired(): boolean {
    return orgAssignmentIsRequired(this.orgAssignmentDepth);
  }

  get orgAssignmentHintText(): string {
    return orgAssignmentHint(this.selectedRoleName, this.orgAssignmentDepth);
  }

  get orgCelluleOptions(): OrgCelluleNode[] {
    return cellulesForPole(this.operationalDepartments, this.unassignedPoles, this.orgPoleId);
  }

  get orgServiceOptions(): OrgServiceNode[] {
    return servicesForCellule(this.operationalDepartments, this.unassignedPoles, this.orgCelluleId);
  }

  get hasUnassignedPoles(): boolean {
    return this.unassignedPoles.length > 0;
  }

  get chefDeProjetOptions() {
    if (!this.orgOverview) return [];
    return findStructureIncumbents(this.orgOverview, 'Chef de projet', { orgPoleId: this.orgPoleId });
  }

  get superviseurOptions() {
    if (!this.orgOverview || !this.form.chefDeProjetId.trim()) return [];
    return filterSuperviseursForChefDeProjet(
      this.orgOverview,
      this.form.chefDeProjetId,
      this.orgCelluleId || undefined,
    );
  }

  get referentTechniqueOptions() {
    if (!this.orgOverview || !this.form.superviseurId.trim()) return [];
    return filterReferentsForSuperviseur(
      this.orgOverview,
      this.form.superviseurId,
      this.orgServiceId || undefined,
    );
  }

  get showSituationEnfantsChoice(): boolean {
    return HR_MARITAL_STATUS_WITH_CHILDREN.has(this.hrProfile.situationFamiliale);
  }

  get showNombreEnfants(): boolean {
    return this.showSituationEnfantsChoice && this.situationAvecEnfants === 'OUI';
  }

  get showRequiresAutoentrepreneur(): boolean {
    return requiresAutoentrepreneur(this.nationaliteCode);
  }

  get showNiveauScolaireAutre(): boolean {
    return this.niveauScolaireCode === 'AUTRE';
  }

  get showNationaliteAutre(): boolean {
    return this.nationaliteCode === 'AUTRE';
  }

  get ancienSelectedRoleName(): string {
    return this.roles.find((r) => r.id === this.ancienPosteRoleId)?.name ?? '';
  }

  get ancienOrgAssignmentDepth(): OrgRoleAssignmentDepth {
    return orgRoleAssignmentDepth(this.ancienSelectedRoleName);
  }

  get ancienShowOrgAssignmentBlock(): boolean {
    return this.ancienOrgAssignmentDepth !== 'none';
  }

  get ancienShowOrgOperationalDeptSelect(): boolean {
    return this.ancienShowOrgAssignmentBlock
      && orgAssignmentRequiresOperationalDept(this.ancienOrgAssignmentDepth);
  }

  get ancienShowOrgPoleSelect(): boolean {
    return this.ancienShowOrgAssignmentBlock && orgAssignmentRequiresPole(this.ancienOrgAssignmentDepth);
  }

  get ancienShowOrgCelluleSelect(): boolean {
    return this.ancienShowOrgAssignmentBlock && orgAssignmentRequiresCellule(this.ancienOrgAssignmentDepth);
  }

  get ancienShowOrgServiceSelect(): boolean {
    return this.ancienShowOrgAssignmentBlock && orgAssignmentRequiresService(this.ancienOrgAssignmentDepth);
  }

  get ancienFilteredOrgPoleOptions(): OrgPoleNode[] {
    const fromDept = this.ancienOrgOperationalDeptId
      ? polesForOperationalDept(this.operationalDepartments, this.ancienOrgOperationalDeptId)
      : [];
    if (!this.ancienOrgOperationalDeptId && this.unassignedPoles.length) {
      return this.unassignedPoles;
    }
    return fromDept;
  }

  get ancienOrgCelluleOptions(): OrgCelluleNode[] {
    return cellulesForPole(this.operationalDepartments, this.unassignedPoles, this.ancienOrgPoleId);
  }

  get ancienOrgServiceOptions(): OrgServiceNode[] {
    return servicesForCellule(this.operationalDepartments, this.unassignedPoles, this.ancienOrgCelluleId);
  }

  get ancienSelectedOrgSummary(): string {
    return operationalSelectionSummary(
      this.operationalDepartments,
      this.unassignedPoles,
      this.ancienOrgOperationalDeptId,
      this.ancienOrgPoleId,
      this.ancienOrgCelluleId,
      this.ancienOrgServiceId,
    );
  }

  get ancienOrgAssignmentHintText(): string {
    return orgAssignmentHint(this.ancienSelectedRoleName, this.ancienOrgAssignmentDepth);
  }

  get canShowContractFields(): boolean {
    if (this.hrProfile.enFormation && !this.isEditMode) return false;
    if (this.isEditMode && this.hrProfile.enFormation && !this.contractId) return false;
    return true;
  }

  get wizardSteps(): { id: WizardStepId; label: string }[] {
    return [
      { id: 'identity', label: 'Identité' },
      { id: 'position', label: 'Poste & organisation' },
      { id: 'pathway', label: this.hrProfile.enFormation ? 'Formation' : 'Contrat' },
      { id: 'finalize', label: 'Finalisation' },
    ];
  }

  get isOrgReadyForResponsables(): boolean {
    if (this.isSupportMode || this.showSupportAssignmentBlock || this.showOperationalManagerBlock) {
      return false;
    }
    if (!this.showOrgAssignmentBlock) return false;
    return this.validateOrgAssignment() === null;
  }

  private toOptionalGuid(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length ? trimmed : null;
  }

  /** DateOnly côté API Planning (`yyyy-MM-dd`) — pas d’heure ISO. */
  private toOptionalDateIso(dateStr: string): string | null {
    const raw = dateStr.trim();
    if (!raw) return null;
    if (/^\d{4}-\d{2}-\d{2}$/.test(raw)) return raw;
    const d = new Date(raw);
    if (Number.isNaN(d.getTime())) return null;
    return this.toDateInputValue(d);
  }

  private dateToInputValue(dateStr: string | null | undefined): string {
    if (!dateStr?.trim()) return '';
    const d = new Date(dateStr);
    return Number.isNaN(d.getTime()) ? '' : this.toDateInputValue(d);
  }

  private applyHrProfileFromUser(profile: UserHrProfile | null | undefined): void {
    if (!profile) return;
    this.hrProfile = {
      dateNaissance: this.dateToInputValue(profile.dateNaissance),
      villeNaissance: profile.villeNaissance ?? '',
      nationalite: profile.nationalite ?? '',
      numeroCarteAutoentrepreneur: profile.numeroCarteAutoentrepreneur ?? '',
      sexe: profile.sexe ?? '',
      situationFamiliale: profile.situationFamiliale ?? '',
      nombreEnfants: profile.nombreEnfants ?? null,
      cin: profile.cin ?? '',
      adresse: profile.adresse ?? '',
      emailPersonnel: profile.emailPersonnel ?? '',
      telephone1: profile.telephone1 ?? '',
      telephoneUrgence: profile.telephoneUrgence ?? '',
      relationUrgence: profile.relationUrgence ?? '',
      rib: profile.rib ?? '',
      immatriculationInterne: profile.immatriculationInterne ?? '',
      immatriculationCnss: profile.immatriculationCnss ?? '',
      dateEntree: this.dateToInputValue(profile.dateEntree),
      dateAnciennete: this.dateToInputValue(profile.dateAnciennete),
      dateSortie: this.dateToInputValue(profile.dateSortie),
      dateEvolutionPoste: this.dateToInputValue(profile.dateEvolutionPoste),
      ancienPoste: profile.ancienPoste ?? '',
      ancienService: profile.ancienService ?? '',
      niveauScolaire: profile.niveauScolaire ?? '',
      intitulesEtudes: profile.intitulesEtudes ?? '',
      enFormation: profile.enFormation ?? false,
      dateDebutFormation: this.dateToInputValue(profile.dateDebutFormation),
      dateFinFormationPrevue: this.dateToInputValue(profile.dateFinFormationPrevue),
    };
    this.syncSituationAvecEnfantsFromProfile();
  }

  private syncSituationAvecEnfantsFromProfile(): void {
    if (!this.showSituationEnfantsChoice) {
      this.situationAvecEnfants = '';
      return;
    }
    const nb = this.hrProfile.nombreEnfants;
    if (nb === null || nb === undefined) {
      this.situationAvecEnfants = '';
    } else if (nb === 0) {
      this.situationAvecEnfants = 'NON';
    } else {
      this.situationAvecEnfants = 'OUI';
    }
  }

  private resolveNombreEnfantsForSave(): number | null {
    if (!this.showSituationEnfantsChoice) return null;
    if (this.situationAvecEnfants === 'NON') return 0;
    if (this.situationAvecEnfants === 'OUI') return this.hrProfile.nombreEnfants;
    return null;
  }

  private buildHrProfilePayload(): UserHrProfile {
    return {
      dateNaissance: this.toOptionalDateIso(this.hrProfile.dateNaissance),
      villeNaissance: this.hrProfile.villeNaissance.trim() || null,
      nationalite: this.hrProfile.nationalite.trim() || null,
      numeroCarteAutoentrepreneur: this.showRequiresAutoentrepreneur
        ? this.hrProfile.numeroCarteAutoentrepreneur.trim() || null
        : null,
      sexe: this.hrProfile.sexe.trim() || null,
      situationFamiliale: this.hrProfile.situationFamiliale.trim() || null,
      nombreEnfants: this.resolveNombreEnfantsForSave(),
      cin: this.hrProfile.cin.trim() || null,
      adresse: this.hrProfile.adresse.trim() || null,
      emailPersonnel: this.hrProfile.emailPersonnel.trim() || null,
      telephone1: this.hrProfile.telephone1.trim() || null,
      telephoneUrgence: this.hrProfile.telephoneUrgence.trim() || null,
      relationUrgence: this.hrProfile.relationUrgence.trim() || null,
      rib: this.hrProfile.rib.trim() || null,
      immatriculationInterne: this.hrProfile.immatriculationInterne.trim() || null,
      immatriculationCnss: this.hrProfile.immatriculationCnss.trim() || null,
      dateEntree: this.toOptionalDateIso(this.hrProfile.dateEntree || this.form.hireDate),
      dateEmbauche: this.toOptionalDateIso(this.form.hireDate),
      // Alignée automatiquement sur la date d'entrée (pas de champ formulaire).
      dateAnciennete: this.toOptionalDateIso(
        this.hrProfile.dateEntree || this.form.hireDate || this.hrProfile.dateAnciennete,
      ),
      dateSortie: this.isEditMode ? this.toOptionalDateIso(this.hrProfile.dateSortie) : null,
      dateEvolutionPoste: this.isEditMode
        ? this.toOptionalDateIso(this.hrProfile.dateEvolutionPoste)
        : null,
      ancienPoste: this.isEditMode ? this.hrProfile.ancienPoste.trim() || null : null,
      ancienService: this.isEditMode ? this.hrProfile.ancienService.trim() || null : null,
      niveauScolaire: this.hrProfile.niveauScolaire.trim() || null,
      intitulesEtudes: this.hrProfile.intitulesEtudes.trim() || null,
      enFormation: this.hrProfile.enFormation,
      dateDebutFormation: this.hrProfile.enFormation
        ? this.toOptionalDateIso(this.hrProfile.dateDebutFormation)
        : null,
      dateFinFormationPrevue: this.hrProfile.enFormation
        ? this.toOptionalDateIso(this.hrProfile.dateFinFormationPrevue)
        : null,
    };
  }

  patchChefDeProjet(userId: string): void {
    this.form.chefDeProjetId = userId;
    this.form.superviseurId = '';
    this.form.referentTechniqueId = '';
    this.cdr.detectChanges();
  }

  patchSuperviseur(userId: string): void {
    this.form.superviseurId = userId;
    this.form.referentTechniqueId = '';
    this.cdr.detectChanges();
  }

  patchReferentTechnique(userId: string): void {
    this.form.referentTechniqueId = userId;
    this.cdr.detectChanges();
  }

  patchEnFormation(value: boolean): void {
    this.hrProfile.enFormation = value;
    if (value) {
      if (!this.hrProfile.dateDebutFormation.trim()) {
        this.hrProfile.dateDebutFormation = this.form.hireDate;
      }
      void this.loadFormationDocs();
    } else {
      this.hrProfile.dateDebutFormation = '';
      this.hrProfile.dateFinFormationPrevue = '';
      this.formationDocs = [];
      if (!this.contractDraft.startDate.trim()) {
        this.contractDraft.startDate = this.form.hireDate;
      }
    }
    this.cdr.detectChanges();
  }

  get formationDocsReceivedCount(): number {
    return this.formationDocs.filter((d) => d.isReceived).length;
  }

  async confirmPassageProduction(): Promise<void> {
    if (!this.passageProductionPathId || this.passageProductionBusy) return;

    // Recharger la checklist du parcours (même si « En formation » est déjà décoché sur la fiche).
    try {
      this.formationDocs = await this.formationTraining.getPathChecklist(this.passageProductionPathId);
    } catch {
      /* keep existing */
    }

    if (this.formationDocs.length > 0 && this.formationDocsReceivedCount < this.formationDocs.length) {
      const cont = await this.confirmService.confirm({
        title: 'Documents incomplets',
        message: `Checklist incomplete (${this.formationDocsReceivedCount}/${this.formationDocs.length}). Vous pouvez quand même confirmer le passage en production.`,
        confirmLabel: 'Continuer',
        cancelLabel: 'Annuler',
        variant: 'warning',
      });
      if (!cont) return;
    }
    const ok = await this.confirmService.confirm({
      title: 'Confirmer le passage en production',
      message:
        'Le statut passera à « En production » et l’employé quittera la file Passage en production. Continuer ?',
      confirmLabel: 'Confirmer',
    });
    if (!ok) return;

    this.passageProductionBusy = true;
    this.cdr.detectChanges();
    try {
      await this.formationTraining.rhValidate(this.passageProductionPathId);
      this.toastService.success('Passage en production confirmé.');
      this.passageProductionPathId = '';
      await this.router.navigate(['/formations/passage-production']);
    } catch (e) {
      this.toastService.error(e instanceof Error ? e.message : 'Échec de la confirmation');
    } finally {
      this.passageProductionBusy = false;
      this.cdr.detectChanges();
    }
  }

  private async loadTrainingPathOrgThenReconcile(): Promise<void> {
    await this.loadTrainingPathOrg();
    if (this.operationalDepartments.length > 0 || this.unassignedPoles.length > 0) {
      this.reconcileOrgPickerAfterLoad();
    }
    this.cdr.detectChanges();
  }

  private async loadTrainingPathOrg(): Promise<void> {
    try {
      if (this.passageProductionPathId) {
        this.trainingPathOrg = await this.formationTraining.getInitialPath(this.passageProductionPathId);
        return;
      }
      if (!this.loadedUserGuid.trim()) {
        this.trainingPathOrg = null;
        return;
      }
      const rows = await this.formationTraining.listInitialByEmployee(this.loadedUserGuid);
      this.trainingPathOrg =
        rows.find((p) => p.status === 'AttenteValidationRh' && !!p.orgServiceId?.trim()) ??
        rows.find((p) => !!p.orgServiceId?.trim()) ??
        null;
    } catch {
      this.trainingPathOrg = null;
    }
  }

  async loadFormationDocs(): Promise<void> {
    if (!this.isEditMode || !this.hrProfile.enFormation || !this.loadedUserGuid) {
      this.formationDocs = [];
      return;
    }
    this.formationDocsLoading = true;
    this.cdr.detectChanges();
    try {
      this.formationDocs = await this.formationTraining.getEmployeeChecklist(this.loadedUserGuid);
    } catch {
      this.formationDocs = [];
    } finally {
      this.formationDocsLoading = false;
      this.cdr.detectChanges();
    }
  }

  async toggleFormationDoc(doc: FormationDocumentChecklistItemDto, isReceived: boolean): Promise<void> {
    const pathId = doc.pathId;
    if (!pathId) return;
    try {
      await this.formationTraining.updateChecklistItem(pathId, doc.id, {
        isReceived,
        receivedBy: this.session.getStoredUser()?.username || 'RH',
      });
      await this.loadFormationDocs();
    } catch {
      await this.loadFormationDocs();
    }
  }

  patchSituationFamiliale(value: string): void {
    this.hrProfile.situationFamiliale = value;
    if (!HR_MARITAL_STATUS_WITH_CHILDREN.has(value)) {
      this.hrProfile.nombreEnfants = null;
      this.situationAvecEnfants = '';
    } else {
      this.situationAvecEnfants = '';
      this.hrProfile.nombreEnfants = null;
    }
    this.cdr.detectChanges();
  }

  patchSituationAvecEnfants(value: '' | 'OUI' | 'NON'): void {
    this.situationAvecEnfants = value;
    if (value === 'NON') {
      this.hrProfile.nombreEnfants = 0;
    } else if (value !== 'OUI') {
      this.hrProfile.nombreEnfants = null;
    }
    this.cdr.detectChanges();
  }

  patchSexe(value: string): void {
    this.hrProfile.sexe = value;
    if (!value || this.nationaliteCode === 'AUTRE') {
      this.cdr.detectChanges();
      return;
    }
    if (!this.nationaliteCode || this.nationaliteCode === 'MAROCAIN' || this.nationaliteCode === 'MAROCAINE') {
      const next = defaultNationalityCode(value);
      this.nationaliteCode = next;
      this.hrProfile.nationalite = nationalityLabelForCode(next);
    }
    this.cdr.detectChanges();
  }

  patchNationaliteCode(value: string): void {
    this.nationaliteCode = value;
    if (value === 'AUTRE') {
      this.hrProfile.nationalite = this.nationaliteAutre.trim();
    } else {
      this.nationaliteAutre = '';
      this.hrProfile.nationalite = nationalityLabelForCode(value);
    }
    if (!requiresAutoentrepreneur(value)) {
      this.hrProfile.numeroCarteAutoentrepreneur = '';
    }
    this.cdr.detectChanges();
  }

  patchNationaliteAutre(value: string): void {
    this.nationaliteAutre = value;
    this.hrProfile.nationalite = value.trim();
    this.cdr.detectChanges();
  }

  patchNiveauScolaireCode(value: string): void {
    this.niveauScolaireCode = value;
    if (value !== 'AUTRE') {
      this.niveauScolaireAutre = '';
      this.hrProfile.niveauScolaire = this.educationLevelOptions.find((o) => o.value === value)?.label ?? value;
    } else {
      this.hrProfile.niveauScolaire = this.niveauScolaireAutre.trim();
    }
    this.cdr.detectChanges();
  }

  patchNiveauScolaireAutre(value: string): void {
    this.niveauScolaireAutre = value;
    if (this.niveauScolaireCode === 'AUTRE') {
      this.hrProfile.niveauScolaire = value.trim();
    }
    this.cdr.detectChanges();
  }

  patchAncienPosteRole(roleId: number): void {
    this.ancienPosteRoleId = roleId;
    const role = this.roles.find((r) => r.id === roleId);
    this.hrProfile.ancienPoste = role?.name ?? '';
    this.clearAncienOrgAssignment();
    this.cdr.detectChanges();
  }

  patchAncienOrgOperationalDept(deptId: string): void {
    this.ancienOrgOperationalDeptId = deptId;
    this.ancienOrgPoleId = '';
    this.ancienOrgCelluleId = '';
    this.ancienOrgServiceId = '';
    this.syncAncienServiceFromOrg();
    this.cdr.detectChanges();
  }

  patchAncienOrgPole(poleId: string): void {
    this.ancienOrgPoleId = poleId;
    if (isUnassignedPole(this.unassignedPoles, poleId)) {
      this.ancienOrgOperationalDeptId = '';
    } else {
      const sel = findOperationalSelectionByPoleId(this.operationalDepartments, this.unassignedPoles, poleId);
      if (sel?.operationalDeptId) {
        this.ancienOrgOperationalDeptId = sel.operationalDeptId;
      }
    }
    this.ancienOrgCelluleId = '';
    this.ancienOrgServiceId = '';
    this.syncAncienServiceFromOrg();
    this.cdr.detectChanges();
  }

  patchAncienOrgCellule(celluleId: string): void {
    this.ancienOrgCelluleId = celluleId;
    this.ancienOrgServiceId = '';
    this.syncAncienServiceFromOrg();
    this.cdr.detectChanges();
  }

  patchAncienOrgService(serviceId: string): void {
    this.ancienOrgServiceId = serviceId;
    this.syncAncienServiceFromOrg();
    this.cdr.detectChanges();
  }

  private clearAncienOrgAssignment(): void {
    this.ancienOrgOperationalDeptId = '';
    this.ancienOrgPoleId = '';
    this.ancienOrgCelluleId = '';
    this.ancienOrgServiceId = '';
    this.hrProfile.ancienService = '';
  }

  private syncAncienServiceFromOrg(): void {
    if (!this.ancienShowOrgAssignmentBlock) {
      this.hrProfile.ancienService = '';
      return;
    }
    const summary = this.ancienSelectedOrgSummary;
    this.hrProfile.ancienService = summary === '—' ? '' : summary;
  }

  private syncAncienPickersFromProfile(): void {
    const poste = this.hrProfile.ancienPoste.trim();
    if (poste) {
      const role = this.roles.find((r) => r.name.toLowerCase() === poste.toLowerCase());
      this.ancienPosteRoleId = role?.id ?? 0;
    } else {
      this.ancienPosteRoleId = 0;
    }
    this.resolveAncienOrgFromLabel();
  }

  private resolveAncienOrgFromLabel(): void {
    this.ancienOrgOperationalDeptId = '';
    this.ancienOrgPoleId = '';
    this.ancienOrgCelluleId = '';
    this.ancienOrgServiceId = '';

    const label = this.hrProfile.ancienService.trim();
    if (!label || !this.ancienPosteRoleId) return;

    const depth = this.ancienOrgAssignmentDepth;
    if (depth === 'none') return;

    const matches = (deptId: string, poleId: string, celluleId: string, serviceId: string): boolean => {
      const summary = operationalSelectionSummary(
        this.operationalDepartments,
        this.unassignedPoles,
        deptId,
        poleId,
        celluleId,
        serviceId,
      );
      return summary === label;
    };

    const apply = (deptId: string, poleId: string, celluleId: string, serviceId: string): void => {
      this.ancienOrgOperationalDeptId = deptId;
      this.ancienOrgPoleId = poleId;
      this.ancienOrgCelluleId = celluleId;
      this.ancienOrgServiceId = serviceId;
    };

    for (const md of this.operationalDepartments) {
      for (const pole of md.poles) {
        if (depth === 'pole' && matches(md.id, pole.id, '', '')) {
          apply(md.id, pole.id, '', '');
          return;
        }
        for (const cellule of pole.cellules) {
          if (depth === 'cellule' && matches(md.id, pole.id, cellule.id, '')) {
            apply(md.id, pole.id, cellule.id, '');
            return;
          }
          for (const service of cellule.services) {
            if (depth === 'service' && matches(md.id, pole.id, cellule.id, service.id)) {
              apply(md.id, pole.id, cellule.id, service.id);
              return;
            }
          }
        }
      }
    }

    for (const pole of this.unassignedPoles) {
      if (depth === 'pole' && matches('', pole.id, '', '')) {
        apply('', pole.id, '', '');
        return;
      }
      for (const cellule of pole.cellules) {
        if (depth === 'cellule' && matches('', pole.id, cellule.id, '')) {
          apply('', pole.id, cellule.id, '');
          return;
        }
        for (const service of cellule.services) {
          if (depth === 'service' && matches('', pole.id, cellule.id, service.id)) {
            apply('', pole.id, cellule.id, service.id);
            return;
          }
        }
      }
    }

    const lastPart = label.split('/').pop()?.trim().toLowerCase() ?? '';
    if (!lastPart) return;

    for (const md of this.operationalDepartments) {
      for (const pole of md.poles) {
        if (depth === 'pole' && pole.name.toLowerCase() === lastPart) {
          apply(md.id, pole.id, '', '');
          return;
        }
        for (const cellule of pole.cellules) {
          if (depth === 'cellule' && cellule.name.toLowerCase() === lastPart) {
            apply(md.id, pole.id, cellule.id, '');
            return;
          }
          for (const service of cellule.services) {
            if (depth === 'service' && service.name.toLowerCase() === lastPart) {
              apply(md.id, pole.id, cellule.id, service.id);
              return;
            }
          }
        }
      }
    }
  }

  onContractDraftChange(model: ContractFieldsModel): void {
    this.touchDraft();
    this.contractDraft = model;
    this.cdr.detectChanges();
  }

  get filteredLinkableReferrals(): Referral[] {
    return filterLinkableReferrals(this.linkableReferrals, this.referralSearchQuery);
  }

  /** Suggestions floues sous le champ recherche (max 5). */
  get referralSearchSuggestions(): ReferralMatchCandidate[] {
    const q = this.referralSearchQuery.trim();
    if (!q) return [];
    return rankReferralsByQuery(q, this.linkableReferrals).slice(0, 5);
  }

  get referralMatchBanner(): ReferralMatchResult | null {
    if (this.referralLockedFromUrl || this.referralMatchDismissed || !this.referralMatchResult) {
      return null;
    }
    // Ne pas ré-alerter si le dossier déjà sélectionné est le meilleur match.
    if (
      this.selectedReferralId &&
      this.referralMatchResult.best?.referral.id === this.selectedReferralId
    ) {
      return null;
    }
    if (!this.referralMatchResult.alertMatches.length) return null;
    return this.referralMatchResult;
  }

  /** Jusqu'à 3 meilleurs dossiers pour confirmation multi-match. */
  get referralMatchChoices(): ReferralMatchCandidate[] {
    return (this.referralMatchBanner?.alertMatches ?? []).slice(0, 3);
  }

  isReferralLinkable(referral: Referral): boolean {
    return (
      !referral.candidateEmployeeId &&
      (referral.status === 'SUBMITTED' || referral.status === 'PROCESSED')
    );
  }

  isReferralAlreadyLinked(referral: Referral): boolean {
    return !!referral.candidateEmployeeId?.trim();
  }

  onLastNameChange(value: string): void {
    this.form.lastName = value ?? '';
    this.onIdentityFieldChange();
    this.refreshHtelPreview();
  }

  onFirstNameChange(value: string): void {
    this.form.firstName = value ?? '';
    this.onIdentityFieldChange();
    this.refreshHtelPreview();
  }

  get wizardRecapReferral(): string {
    if (!this.selectedReferral) return '—';
    const status = this.referralStatusLabels[this.selectedReferral.status] ?? this.selectedReferral.status;
    const after = this.hrProfile.enFormation ? 'En cours de formation' : 'Validé';
    return `${this.selectedReferral.candidateName} (${status}) — après enregistrement : ${after}`;
  }

  referralStatusClass(status: Referral['status']): string {
    switch (status) {
      case 'SUBMITTED':
        return 'referral-badge referral-badge--submitted';
      case 'PROCESSED':
        return 'referral-badge referral-badge--processed';
      default:
        return 'referral-badge';
    }
  }

  async loadLinkableReferrals(): Promise<void> {
    if (this.isEditMode) return;
    // Isoler les erreurs : un endpoint en échec ne doit pas vider tout le pool.
    const [linkable, all] = await Promise.all([
      this.parrainageApi.getLinkableReferrals().catch(() => [] as Referral[]),
      this.parrainageApi.getReferrals().catch(() => [] as Referral[]),
    ]);
    this.linkableReferrals = linkable;
    // Alerte sur tout dossier connu (lié ou non) — le RH doit être informé du doublon.
    const byId = new Map<string, Referral>();
    for (const r of [...all, ...linkable]) {
      if (r?.id) byId.set(r.id, r);
    }
    this.matchableReferrals = byId.size > 0 ? [...byId.values()] : linkable;
    this.cdr.detectChanges();
    // Rejouer le match si le RH a déjà saisi le nom pendant le chargement.
    this.scheduleReferralMatch();
  }

  onReferralSearchChange(query: string): void {
    this.referralSearchQuery = query;
    this.cdr.detectChanges();
  }

  onIdentityFieldChange(): void {
    this.referralManualIdentityEdit = true;
    this.referralMatchDismissed = false;
    this.scheduleReferralMatch();
  }

  private scheduleReferralMatch(): void {
    if (this.isEditMode || this.referralLockedFromUrl) return;
    if (this.referralMatchTimer) clearTimeout(this.referralMatchTimer);
    this.referralMatchTimer = setTimeout(() => this.runReferralMatch(), 250);
  }

  private runReferralMatch(): void {
    const firstName = (this.form.firstName ?? '').trim();
    const lastName = (this.form.lastName ?? '').trim();
    const hasPair =
      firstName.length >= 2 && lastName.length >= 2;
    const hasFullInOneField =
      (firstName.length < 2 && lastName.split(/\s+/).filter(Boolean).length >= 2) ||
      (lastName.length < 2 && firstName.split(/\s+/).filter(Boolean).length >= 2);
    if (!hasPair && !hasFullInOneField) {
      this.referralMatchResult = null;
      this.cdr.detectChanges();
      return;
    }
    const pool =
      this.matchableReferrals.length > 0 ? this.matchableReferrals : this.linkableReferrals;
    if (pool.length === 0) {
      this.referralMatchResult = null;
      this.cdr.detectChanges();
      return;
    }
    this.referralMatchResult = matchReferralCandidates(
      {
        firstName,
        lastName,
        email: this.hrProfile.emailPersonnel?.trim() || this.form.email,
        emails: [this.hrProfile.emailPersonnel, this.form.email],
      },
      pool,
    );
    // Pas de liaison silencieuse : le RH confirme via l'alerte.
    this.cdr.detectChanges();
  }

  confirmReferralSuggestion(referralId?: string): void {
    const id =
      referralId?.trim() ||
      this.referralMatchResult?.best?.referral.id ||
      '';
    if (!id) return;
    this.referralMatchDismissed = false;
    this.referralManualIdentityEdit = false;
    this.referralSearchQuery = '';
    void this.selectReferral(id, true);
  }

  dismissReferralMatch(): void {
    this.referralMatchDismissed = true;
    this.cdr.detectChanges();
  }

  pickReferralFromSearch(referralId: string): void {
    this.referralSearchQuery = '';
    this.referralMatchDismissed = true;
    void this.selectReferral(referralId, true);
  }

  async prefillFromReferral(referralId: string): Promise<void> {
    this.referralLoading = true;
    try {
      const referral =
        this.linkableReferrals.find((r) => r.id === referralId) ??
        (await this.parrainageApi.getReferral(referralId));
      if (
        referral.candidateEmployeeId ||
        (referral.status !== 'SUBMITTED' && referral.status !== 'PROCESSED')
      ) {
        return;
      }
      this.selectedReferralId = referral.id;
      this.selectedReferral = referral;
      this.referralPositionHint = referral.position?.trim() ?? '';
      const today = this.toDateInputValue(new Date());
      this.form.hireDate = today;
      this.hrProfile.dateEntree = today;
      const parts = referral.candidateName.trim().split(/\s+/);
      if (parts.length >= 2) {
        this.form.lastName = parts[0];
        this.form.firstName = parts.slice(1).join(' ');
      } else {
        this.form.firstName = referral.candidateName;
      }
      this.hrProfile.emailPersonnel = referral.candidateEmail;
      this.hrProfile.telephone1 = referral.candidatePhone;
      const preview = await this.parrainageApi.getRewardPreview(referral.id);
      this.referralRewardAmount = preview.suggestedAmount;
      this.cdr.detectChanges();
    } finally {
      this.referralLoading = false;
      this.cdr.detectChanges();
    }
  }

  onReferralSelectionChange(referralId: string): void {
    if (!referralId) {
      this.selectedReferralId = '';
      this.selectedReferral = null;
      this.referralRewardAmount = 0;
      this.referralPositionHint = '';
      this.cdr.detectChanges();
      return;
    }
    this.referralManualIdentityEdit = false;
    void this.selectReferral(referralId, true);
  }

  private async selectReferral(referralId: string, prefillIdentity: boolean): Promise<void> {
    this.selectedReferralId = referralId;
    this.selectedReferral = this.linkableReferrals.find((r) => r.id === referralId) ?? null;
    if (prefillIdentity) {
      await this.prefillFromReferral(referralId);
    } else if (this.selectedReferral) {
      this.referralPositionHint = this.selectedReferral.position?.trim() ?? '';
      try {
        const preview = await this.parrainageApi.getRewardPreview(referralId);
        this.referralRewardAmount = preview.suggestedAmount;
      } catch {
        this.referralRewardAmount = 0;
      }
      this.cdr.detectChanges();
    }
  }

  private loadContractForUser(userId: number): void {
    this.contractLoading = true;
    this.contractService.getByUser(userId).subscribe({
      next: (contracts) => {
        const latest = contracts?.[0];
        if (!latest) {
          this.contractId = null;
          this.contractDraft = createEmptyContractFields();
          this.contractDraft.startDate = this.form.hireDate;
          this.contractLoading = false;
          this.cdr.detectChanges();
          return;
        }
        this.contractId = latest.id;
        this.contractDraft = {
          type: latest.type,
          startDate: latest.startDate?.substring(0, 10) ?? '',
          endDate: latest.endDate?.substring(0, 10) ?? '',
          probationDays: null,
          alertThresholdDays: latest.alertThresholdDays,
          notes: latest.notes ?? '',
          status: statusLabelToValue(latest.status),
        };
        this.contractLoading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.contractLoading = false;
        this.cdr.detectChanges();
      },
    });
  }

  private saveContract$(userId: number): Observable<void> {
    if (this.hrProfile.enFormation && !this.contractId) {
      return of(undefined);
    }
    if (this.contractId) {
      const dto: UpdateContractDto = {
        type: this.contractDraft.type,
        status: this.contractDraft.status,
        endDate: this.contractDraft.type !== 'CDI' ? this.contractDraft.endDate : undefined,
        probationDays: this.contractDraft.probationDays ?? undefined,
        alertThresholdDays: this.contractDraft.alertThresholdDays,
        notes: this.contractDraft.notes,
      };
      return this.contractService.update(this.contractId, dto).pipe(map(() => undefined));
    }
    const createDto = this.buildCreateContractDto(userId);
    return this.contractService.create(createDto).pipe(
      map((created) => {
        this.contractId = created.id;
      }),
    );
  }

  private completeReferralOnboarding$(employeeGuid: string): Observable<Referral | null> {
    if (!this.selectedReferralId.trim()) return of(null);
    return defer(() =>
      this.parrainageApi.completeOnboarding(this.selectedReferralId, {
        employeeId: employeeGuid,
        candidateStartDate: this.form.hireDate,
        rewardAmount: this.referralRewardAmount || this.selectedReferral?.rewardAmount || 0,
        requiresTraining: this.hrProfile.enFormation,
        trainingEndDate: this.hrProfile.enFormation ? this.hrProfile.dateFinFormationPrevue : undefined,
      }),
    );
  }

  private resetResponsables(): void {
    this.form.chefDeProjetId = '';
    this.form.superviseurId = '';
    this.form.referentTechniqueId = '';
  }

  private resetInvalidResponsables(): void {
    if (
      this.form.chefDeProjetId &&
      !this.chefDeProjetOptions.some((o) => o.userId === this.form.chefDeProjetId)
    ) {
      this.form.chefDeProjetId = '';
    }
    if (
      this.form.superviseurId &&
      !this.superviseurOptions.some((o) => o.userId === this.form.superviseurId)
    ) {
      this.form.superviseurId = '';
    }
    if (
      this.form.referentTechniqueId &&
      !this.referentTechniqueOptions.some((o) => o.userId === this.form.referentTechniqueId)
    ) {
      this.form.referentTechniqueId = '';
    }
    this.syncDefaultMentorsFromIncumbents();
  }

  private syncDefaultMentorsFromIncumbents(): void {
    if (!this.isOrgReadyForResponsables || this.isEditMode) return;
    if (this.chefDeProjetOptions.length === 1 && !this.form.chefDeProjetId.trim()) {
      this.form.chefDeProjetId = this.chefDeProjetOptions[0].userId;
    }
    if (this.superviseurOptions.length === 1 && !this.form.superviseurId.trim()) {
      this.form.superviseurId = this.superviseurOptions[0].userId;
    }
    if (this.referentTechniqueOptions.length === 1 && !this.form.referentTechniqueId.trim()) {
      this.form.referentTechniqueId = this.referentTechniqueOptions[0].userId;
    }
  }

  private validateResponsables(): string | null {
    if (!this.isOrgReadyForResponsables) return null;
    if (isPiloteRole(this.selectedRoleName)) {
      if (this.chefDeProjetOptions.length > 0 && !this.form.chefDeProjetId.trim()) {
        return 'Choisissez le chef de projet parmi les titulaires du pôle.';
      }
      if (this.superviseurOptions.length > 0 && !this.form.superviseurId.trim()) {
        return 'Choisissez le superviseur parmi les titulaires de la cellule.';
      }
      if (this.referentTechniqueOptions.length > 0 && !this.form.referentTechniqueId.trim()) {
        return 'Choisissez le référent technique parmi les titulaires du service.';
      }
    }
    if (
      this.form.chefDeProjetId &&
      !this.chefDeProjetOptions.some((o) => o.userId === this.form.chefDeProjetId)
    ) {
      return 'Le chef de projet sélectionné n\'appartient pas au pôle choisi.';
    }
    if (!this.form.chefDeProjetId.trim() && this.form.superviseurId.trim()) {
      return 'Choisissez d\'abord le chef de projet.';
    }
    if (
      this.form.superviseurId &&
      !this.superviseurOptions.some((o) => o.userId === this.form.superviseurId)
    ) {
      return 'Le superviseur sélectionné n\'est pas rattaché au chef de projet choisi.';
    }
    if (!this.form.superviseurId.trim() && this.form.referentTechniqueId.trim()) {
      return 'Choisissez d\'abord le superviseur.';
    }
    if (
      this.form.referentTechniqueId &&
      !this.referentTechniqueOptions.some((o) => o.userId === this.form.referentTechniqueId)
    ) {
      return 'Le référent technique sélectionné n\'est pas rattaché au superviseur choisi.';
    }
    return null;
  }

  private toDateInputValue(date: Date): string {
    return date.toLocaleDateString('en-CA');
  }

  private toISOString(dateStr: string): string {
    if (!dateStr) return new Date().toISOString();
    const [year, month, day] = dateStr.split('-').map(Number);
    return new Date(Date.UTC(year, month - 1, day, 12, 0, 0)).toISOString();
  }

  loadOrgAndSubServices(): void {
    this.orgLoading = true;
    this.userService.syncOrgMirrorFromDirectory().pipe(
      catchError((err) => {
        console.warn('syncOrgMirrorFromDirectory failed', err);
        this.orgMirrorWarning =
          'Synchronisation Planning ↔ Organisation RH échouée. Rechargez la page ou réessayez dans quelques secondes.';
        return of(null);
      }),
      switchMap(() =>
        forkJoin({
          overview: this.orgApi.loadOverview(),
          subServices: this.subServiceService.getAllSubServices(),
          services: this.serviceService.getAllServices(),
        }),
      ),
    ).subscribe({
      next: ({ overview, subServices, services }) => {
        this.orgOverview = overview;
        this.operationalDepartments = overview.operationalDepartments ?? [];
        this.unassignedPoles = overview.unassignedPoles ?? [];
        this.subServices = subServices ?? [];
        this.planningServices = services ?? [];
        this.orgLoading = false;
        this.reconcileOrgPickerAfterLoad();
        this.syncAncienPickersFromProfile();
        this.cdr.detectChanges();
      },
      error: () => {
        this.orgLoading = false;
        this.error = 'Impossible de charger la structure organisationnelle (Organisation RH).';
        this.cdr.detectChanges();
      },
    });
  }

  private ensureOrgPickerDefaults(): void {
    if (this.operationalDepartments.length === 0 && this.unassignedPoles.length === 0) {
      this.clearOrgAssignment();
      return;
    }

    if (this.showOrgOperationalDeptSelect) {
      if (this.orgOperationalDeptId && !this.operationalDepartments.some((d) => d.id === this.orgOperationalDeptId)) {
        this.orgOperationalDeptId = '';
      }
    }

    if (this.showOrgPoleSelect) {
      const poles = this.filteredOrgPoleOptions;
      if (this.orgPoleId && !poles.some((p) => p.id === this.orgPoleId)) {
        this.orgPoleId = '';
      }
      if (this.orgPoleId && isUnassignedPole(this.unassignedPoles, this.orgPoleId)) {
        this.orgOperationalDeptId = '';
      }
    } else {
      this.orgPoleId = '';
    }

    if (this.showOrgCelluleSelect) {
      const cellules = this.filteredOrgCelluleOptions;
      if (this.orgCelluleId && !cellules.some((c) => c.id === this.orgCelluleId)) {
        this.orgCelluleId = '';
      }
    } else {
      this.orgCelluleId = '';
    }

    if (this.showOrgServiceSelect) {
      const services = this.filteredOrgServiceOptions;
      if (this.orgServiceId && !services.some((s) => s.id === this.orgServiceId)) {
        this.orgServiceId = '';
      }
      this.syncSubServiceFromOrg();
    } else {
      this.orgServiceId = '';
      if (this.orgAssignmentDepth !== 'service') {
        this.form.subServiceId = null;
        this.orgMirrorWarning = null;
      }
    }

    this.resetInvalidResponsables();
  }

  onOrgSearchChange(value: string): void {
    this.orgStructureSearch = value;
    this.cdr.detectChanges();
  }

  patchOrgFilterPole(poleId: string): void {
    this.orgFilterPoleId = poleId;
    this.orgFilterCelluleId = '';
    this.cdr.detectChanges();
  }

  patchOrgFilterCellule(celluleId: string): void {
    this.orgFilterCelluleId = celluleId;
    this.cdr.detectChanges();
  }

  clearOrgFilters(): void {
    this.orgStructureSearch = '';
    this.orgFilterPoleId = '';
    this.orgFilterCelluleId = '';
    this.cdr.detectChanges();
  }

  selectFlatService(opt: OperationalFlatServiceOption): void {
    this.patchOrgFlatService(opt.serviceId);
  }

  isFlatServiceSelected(opt: OperationalFlatServiceOption): boolean {
    return this.orgServiceId === opt.serviceId;
  }

  patchOrgOperationalDept(deptId: string): void {
    this.orgOperationalDeptId = deptId;
    this.orgPoleId = '';
    this.orgCelluleId = '';
    this.orgServiceId = '';
    this.form.subServiceId = null;
    this.orgMirrorWarning = null;
    this.resetResponsables();
    this.ensureOrgPickerDefaults();
    this.cdr.detectChanges();
  }

  patchOrgPole(poleId: string): void {
    this.orgPoleId = poleId;
    if (isUnassignedPole(this.unassignedPoles, poleId)) {
      this.orgOperationalDeptId = '';
    } else {
      const sel = findOperationalSelectionByPoleId(this.operationalDepartments, this.unassignedPoles, poleId);
      if (sel?.operationalDeptId) {
        this.orgOperationalDeptId = sel.operationalDeptId;
      }
    }
    this.orgCelluleId = '';
    this.orgServiceId = '';
    this.form.subServiceId = null;
    this.orgMirrorWarning = null;
    this.resetResponsables();
    this.ensureOrgPickerDefaults();
    if (this.supportsMultiOrg && this.orgAssignmentDepth === 'pole' && poleId.trim()) {
      this.applyMultiOrgLeafSelection();
    }
    this.cdr.detectChanges();
  }

  patchOrgCellule(celluleId: string): void {
    this.orgCelluleId = celluleId;
    this.orgServiceId = '';
    this.form.subServiceId = null;
    this.resetResponsables();
    if (this.showOrgServiceSelect) {
      this.syncSubServiceFromOrg();
    } else {
      this.orgMirrorWarning = this.superviseurMirrorWarning();
    }
    this.resetInvalidResponsables();
    if (this.supportsMultiOrg && this.orgAssignmentDepth === 'cellule' && celluleId.trim()) {
      this.applyMultiOrgLeafSelection();
    }
    this.cdr.detectChanges();
  }

  patchOrgService(serviceId: string): void {
    this.orgServiceId = serviceId;
    this.resetInvalidResponsables();
    this.syncSubServiceFromOrg();
    if (this.supportsMultiOrg && this.orgAssignmentDepth === 'service' && serviceId.trim()) {
      this.applyMultiOrgLeafSelection();
    } else if (!this.supportsMultiOrg && serviceId.trim()) {
      this.selectedOrgNodeIds = [serviceId.trim()];
      this.primaryOrgNodeId = serviceId.trim();
    }
    this.cdr.detectChanges();
  }

  patchOrgFlatService(serviceId: string): void {
    const hit = this.orgFlatServiceOptions.find((o) => o.serviceId === serviceId);
    if (!hit) {
      this.orgOperationalDeptId = '';
      this.orgPoleId = '';
      this.orgCelluleId = '';
      this.orgServiceId = '';
    } else {
      this.orgOperationalDeptId = hit.operationalDeptId;
      this.orgPoleId = hit.poleId;
      this.orgCelluleId = hit.celluleId;
      this.orgServiceId = hit.serviceId;
    }
    this.resetInvalidResponsables();
    this.syncSubServiceFromOrg();
    if (this.supportsMultiOrg && this.orgServiceId.trim()) {
      this.applyMultiOrgLeafSelection();
    }
    this.cdr.detectChanges();
  }

  clearOrgAssignment(): void {
    this.orgOperationalDeptId = '';
    this.orgPoleId = '';
    this.orgCelluleId = '';
    this.orgServiceId = '';
    const cleared = clearMultiOrgSelection();
    this.selectedOrgNodeIds = cleared.selectedOrgNodeIds;
    this.primaryOrgNodeId = cleared.primaryOrgNodeId;
    this.form.subServiceId = null;
    this.orgMirrorWarning = null;
    this.resetResponsables();
    this.cdr.detectChanges();
  }

  addCurrentOrgNodeToSelection(): void {
    if (!this.supportsMultiOrg) return;
    const nodeId = this.currentLeafOrgNodeId();
    if (!nodeId) return;
    const next = addOrgNodeSelection(
      { selectedOrgNodeIds: this.selectedOrgNodeIds, primaryOrgNodeId: this.primaryOrgNodeId },
      nodeId,
    );
    this.selectedOrgNodeIds = next.selectedOrgNodeIds;
    this.primaryOrgNodeId = next.primaryOrgNodeId;
    this.syncCascadeFromPrimaryOrgNode();
    this.cdr.detectChanges();
  }

  removeSelectedOrgNode(nodeId: string): void {
    const next = removeOrgNodeSelection(
      { selectedOrgNodeIds: this.selectedOrgNodeIds, primaryOrgNodeId: this.primaryOrgNodeId },
      nodeId,
    );
    this.selectedOrgNodeIds = next.selectedOrgNodeIds;
    this.primaryOrgNodeId = next.primaryOrgNodeId;
    this.syncCascadeFromPrimaryOrgNode();
    this.cdr.detectChanges();
  }

  private currentLeafOrgNodeId(): string {
    const depth = this.orgAssignmentDepth;
    if (depth === 'pole') return this.orgPoleId.trim();
    if (depth === 'cellule') return this.orgCelluleId.trim();
    if (depth === 'service') return this.orgServiceId.trim();
    return '';
  }

  private canAddCurrentOrgNode(): boolean {
    if (!this.supportsMultiOrg) return false;
    const nodeId = this.currentLeafOrgNodeId();
    return !!nodeId && !this.selectedOrgNodeIds.includes(nodeId);
  }

  get canAddOrgNodeToSelection(): boolean {
    return this.canAddCurrentOrgNode();
  }

  private syncCascadeFromPrimaryOrgNode(): void {
    const primary = this.primaryOrgNodeId.trim();
    if (!primary) {
      this.syncSubServiceFromOrg();
      this.resetInvalidResponsables();
      return;
    }
    const depth = this.orgAssignmentDepth;
    if (depth === 'pole') {
      const sel = findOperationalSelectionByPoleId(
        this.operationalDepartments,
        this.unassignedPoles,
        primary,
      );
      if (sel) {
        this.orgOperationalDeptId = sel.operationalDeptId;
        this.orgPoleId = sel.poleId;
        this.orgCelluleId = '';
        this.orgServiceId = '';
      }
    } else if (depth === 'cellule') {
      const sel = findOperationalSelectionByCelluleId(
        this.operationalDepartments,
        this.unassignedPoles,
        primary,
      );
      if (sel) {
        this.orgOperationalDeptId = sel.operationalDeptId;
        this.orgPoleId = sel.poleId;
        this.orgCelluleId = sel.celluleId;
        this.orgServiceId = '';
      }
    } else if (depth === 'service') {
      const sel = findOperationalSelectionByServiceId(
        this.operationalDepartments,
        this.unassignedPoles,
        primary,
      );
      if (sel) {
        this.applyOrgSelection(sel);
      }
    }
    this.syncSubServiceFromOrg();
    this.resetInvalidResponsables();
  }

  private applyMultiOrgLeafSelection(): void {
    if (!this.supportsMultiOrg) return;
    const nodeId = this.currentLeafOrgNodeId();
    if (!nodeId) return;
    const next = addOrgNodeSelection(
      { selectedOrgNodeIds: this.selectedOrgNodeIds, primaryOrgNodeId: this.primaryOrgNodeId },
      nodeId,
    );
    this.selectedOrgNodeIds = next.selectedOrgNodeIds;
    this.primaryOrgNodeId = next.primaryOrgNodeId;
  }

  private superviseurMirrorWarning(): string | null {
    if (!isSuperviseurRole(this.selectedRoleName) || !this.orgCelluleId) return null;
    const svcId = resolvePlanningServiceIdByPrimeCelluleId(this.planningServices, this.orgCelluleId);
    return svcId
      ? null
      : 'Cette cellule existe dans Organisation RH mais n’est pas encore synchronisée dans Planning.';
  }

  private syncSubServiceFromOrg(): void {
    const needsService = this.showOrgFlatServiceSelect || this.showOrgServiceSelect;
    if (!needsService) {
      this.form.subServiceId = null;
      if (isSuperviseurRole(this.selectedRoleName)) {
        this.orgMirrorWarning = this.superviseurMirrorWarning();
      } else {
        this.orgMirrorWarning = null;
      }
      return;
    }
    const primeId = this.orgServiceId.trim();
    if (!primeId) {
      this.form.subServiceId = null;
      this.orgMirrorWarning = null;
      return;
    }
    const subId = resolveSubServiceIdByPrimeServiceId(this.subServices, primeId);
    this.form.subServiceId = subId;
    this.orgMirrorWarning = subId
      ? null
      : 'Ce service existe dans Organisation RH mais n’est pas encore synchronisé dans Planning. Attendez quelques secondes ou lancez la réconciliation.';
  }

  private applyOrgSelection(sel: {
    operationalDeptId: string;
    poleId: string;
    celluleId: string;
    serviceId: string;
  }): void {
    this.orgOperationalDeptId = sel.operationalDeptId;
    this.orgPoleId = sel.poleId;
    this.orgCelluleId = sel.celluleId;
    if (this.showOrgServiceSelect || this.showOrgFlatServiceSelect) {
      this.orgServiceId = sel.serviceId;
    } else {
      this.orgServiceId = '';
    }
    this.orgMirrorWarning = null;
  }

  private applyOrgFromSubServiceId(subServiceId: number | null | undefined): void {
    if (!subServiceId) {
      if (this.orgAssignmentDepth === 'cellule' && this.loadedManagedServiceIds.length) {
        this.applyOrgFromManagedServiceId(this.loadedManagedServiceIds[0]);
        return;
      }
      if (this.showOrgAssignmentBlock) {
        this.ensureOrgPickerDefaults();
      }
      return;
    }
    const sub = this.subServices.find((s) => s.id === subServiceId);
    const primeId = sub?.primeServiceId?.trim();
    if (!primeId) {
      this.orgMirrorWarning =
        'Affectation Planning sans lien Organisation RH — choisissez la structure ci-dessous.';
      return;
    }
    const sel = findOperationalSelectionByServiceId(
      this.operationalDepartments,
      this.unassignedPoles,
      primeId,
    );
    if (!sel) {
      this.orgMirrorWarning = 'Structure Organisation RH introuvable pour cet employé.';
      return;
    }
    this.applyOrgSelection(sel);
    this.form.subServiceId = subServiceId;
    this.orgMirrorWarning = null;
  }

  private applyOrgFromManagedServiceId(planningServiceId: number): void {
    const svc = this.planningServices.find((s) => s.id === planningServiceId);
    const cellulePrimeId = svc?.primeCelluleId?.trim();
    if (!cellulePrimeId) return;
    const sel = findOperationalSelectionByCelluleId(
      this.operationalDepartments,
      this.unassignedPoles,
      cellulePrimeId,
    );
    if (!sel) return;
    this.orgOperationalDeptId = sel.operationalDeptId;
    this.orgPoleId = sel.poleId;
    this.orgCelluleId = sel.celluleId;
    this.orgServiceId = '';
    this.orgMirrorWarning = null;
  }

  private reconcileOrgPickerAfterLoad(): void {
    const guid = this.loadedUserGuid.trim();
    const roleName = this.selectedRoleName;
    if (guid && (needsPrimeStructureAssignment(roleName) || isPiloteRole(roleName))) {
      if (this.applyOrgFromPrimeOverview(guid, roleName)) {
        return;
      }
    }
    if (this.applyOrgFromTrainingPath()) {
      this.syncDefaultMentorsFromIncumbents();
      return;
    }
    if (this.form.subServiceId) {
      this.applyOrgFromSubServiceId(this.form.subServiceId);
    } else if (this.showOrgAssignmentBlock) {
      this.ensureOrgPickerDefaults();
    }
    this.syncDefaultMentorsFromIncumbents();
  }

  private applyOrgFromTrainingPath(): boolean {
    const serviceId = this.trainingPathOrg?.orgServiceId?.trim() ?? '';
    if (!serviceId || !this.showOrgAssignmentBlock) return false;

    const sel = findOperationalSelectionByServiceId(
      this.operationalDepartments,
      this.unassignedPoles,
      serviceId,
    ) ?? {
      operationalDeptId: this.trainingPathOrg?.orgDepartmentId?.trim() ?? '',
      poleId: this.trainingPathOrg?.orgPoleId?.trim() ?? '',
      celluleId: this.trainingPathOrg?.orgCelluleId?.trim() ?? '',
      serviceId,
    };
    if (!sel.serviceId) return false;

    this.applyOrgSelection(sel);
    this.syncSubServiceFromOrg();
    if (this.supportsMultiOrg && this.orgAssignmentDepth === 'service' && this.orgServiceId.trim()) {
      this.applyMultiOrgLeafSelection();
    } else if (this.orgServiceId.trim()) {
      this.selectedOrgNodeIds = [this.orgServiceId.trim()];
      this.primaryOrgNodeId = this.orgServiceId.trim();
    }
    this.orgMirrorWarning = null;
    return true;
  }

  private applyOrgFromPrimeOverview(guid: string, roleName: string): boolean {
    const overview = this.orgOverview;
    if (!overview || !guid.trim()) return false;

    if (supportsMultiOrgSelection(roleName) || isPiloteRole(roleName)) {
      const hydrated = hydrateMultiOrgSelectionFromOverview(overview, guid, roleName);
      if (hydrated && hydrated.selectedOrgNodeIds.length > 0) {
        this.selectedOrgNodeIds = hydrated.selectedOrgNodeIds;
        this.primaryOrgNodeId = hydrated.primaryOrgNodeId;
        this.syncCascadeFromPrimaryOrgNode();
        this.orgMirrorWarning = null;
        return true;
      }
    }

    if (isChefDeProjetRole(roleName)) {
      const mgr = overview.managerEtage?.find((a) => a.userId === guid);
      if (!mgr?.etageId) return false;
      const sel = findOperationalSelectionByPoleId(
        this.operationalDepartments,
        this.unassignedPoles,
        mgr.etageId,
      );
      if (!sel) return false;
      this.orgOperationalDeptId = sel.operationalDeptId;
      this.orgPoleId = sel.poleId;
      this.orgCelluleId = '';
      this.orgServiceId = '';
      this.orgMirrorWarning = null;
      return true;
    }

    if (isSuperviseurRole(roleName)) {
      const sup = overview.supervisorService?.find((a) => a.userId === guid);
      if (!sup) return false;
      const celluleId = (sup.celluleId ?? sup.serviceId ?? '').trim();
      if (!celluleId) return false;
      const sel = findOperationalSelectionByCelluleId(
        this.operationalDepartments,
        this.unassignedPoles,
        celluleId,
      );
      if (!sel) return false;
      this.orgOperationalDeptId = sel.operationalDeptId;
      this.orgPoleId = sel.poleId;
      this.orgCelluleId = sel.celluleId;
      this.orgServiceId = '';
      this.orgMirrorWarning = null;
      return true;
    }

    if (isReferentTechniqueRole(roleName) || isPiloteRole(roleName)) {
      const coach = overview.coachSousService?.find((a) => a.userId === guid);
      const emp = overview.employees?.find((e) => e.id === guid);
      const svcId = (
        coach?.serviceId ??
        coach?.sousServiceId ??
        emp?.serviceId ??
        ''
      ).trim();
      if (!svcId) return false;
      const sel = findOperationalSelectionByServiceId(
        this.operationalDepartments,
        this.unassignedPoles,
        svcId,
      );
      if (!sel) return false;
      this.applyOrgSelection(sel);
      this.form.subServiceId = resolveSubServiceIdByPrimeServiceId(this.subServices, svcId);
      this.orgMirrorWarning = null;
      return true;
    }

    return false;
  }

  private loadDirectoryEmployeeContext(guid: string): void {
    if (!guid.trim()) return;
    this.http.get<{
      businessDepartmentId?: string;
      businessDepartmentKind?: string;
      idTechnicien?: number | null;
      htelCode?: string | null;
    }>(
      `/api/directory/employees/${encodeURIComponent(guid)}`,
    ).subscribe({
      next: (emp) => {
        if (emp.idTechnicien != null) {
          this.htelIdTechnicien = emp.idTechnicien;
          this.htelCode = emp.htelCode ?? '';
          this.htelPreviewHint = '';
        }
        const kind = String(emp.businessDepartmentKind ?? '').toLowerCase();
        if (kind === 'support' && emp.businessDepartmentId) {
          this.orgMode = 'support';
          this.supportDepartmentId = emp.businessDepartmentId;
          this.cdr.detectChanges();
        } else if (kind === 'operational' && emp.businessDepartmentId && isSupportManagerRole(this.selectedRoleName)) {
          this.orgMode = 'operational';
          this.operationalBusinessDepartmentId = emp.businessDepartmentId;
          this.cdr.detectChanges();
        } else {
          this.cdr.detectChanges();
        }
      },
    });
  }

  private syncEducationPickersFromProfile(): void {
    const known = this.educationLevelOptions.find((o) => o.label === this.hrProfile.niveauScolaire);
    if (known) {
      this.niveauScolaireCode = known.value;
      this.niveauScolaireAutre = '';
    } else if (this.hrProfile.niveauScolaire.trim()) {
      this.niveauScolaireCode = 'AUTRE';
      this.niveauScolaireAutre = this.hrProfile.niveauScolaire;
    }
  }

  private syncNationalityPickersFromProfile(): void {
    const synced = syncNationalityCodeFromLabel(this.hrProfile.nationalite, this.hrProfile.sexe);
    this.nationaliteCode = synced.code;
    this.nationaliteAutre = synced.autre;
    if (synced.code !== 'AUTRE') {
      this.hrProfile.nationalite = nationalityLabelForCode(synced.code);
    }
  }

  loadUser(id: number): void {
    this.loading = true;
    this.userService.getUserById(id).subscribe({
      next: (user) => {
        this.form = {
          roleId: user.roleId,
          subServiceId: user.subServiceId ?? null,
          firstName: user.firstName,
          lastName: user.lastName,
          email: user.email,
          hireDate: user.hireDate
            ? this.toDateInputValue(new Date(user.hireDate))
            : this.toDateInputValue(new Date()),
          isActive: user.isActive,
          level: user.level ?? 1,
          chefDeProjetId: user.chefDeProjetId ?? '',
          superviseurId: user.superviseurId ?? '',
          referentTechniqueId: user.referentTechniqueId ?? '',
          niveauExpertiseMetier: user.niveauExpertiseMetier ?? null,
        };
        this.applyHrProfileFromUser(user.hrProfile);
        this.syncNationalityPickersFromProfile();
        this.loadedManagedServiceIds = user.managedServices?.map(s => s.id) ?? [];
        this.loadedManagedSubServiceIds = user.managedSubServices?.map(s => s.id) ?? [];
        this.loadedUserGuid = resolveUserGuid(user);
        this.htelIdTechnicien = user.idTechnicien ?? null;
        this.htelCode = user.htelCode ?? '';
        if (user.customFields) {
          for (const [key, value] of Object.entries(user.customFields)) {
            this.customFieldValues[key] = value ?? '';
          }
        }
        this.loadDirectoryEmployeeContext(this.loadedUserGuid);
        this.syncEducationPickersFromProfile();
        this.syncAncienPickersFromProfile();
        this.loadContractForUser(id);
        void this.loadFormationDocs();
        if (this.passageProductionPathId) {
          void this.formationTraining
            .getPathChecklist(this.passageProductionPathId)
            .then((rows) => {
              this.formationDocs = rows;
              this.cdr.detectChanges();
            })
            .catch(() => undefined);
        }
        void this.loadTrainingPathOrgThenReconcile();
        this.startFormDraft();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = `Erreur: ${err.status}`;
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  onRoleChange(roleId: number): void {
    this.form.roleId = Number(roleId);
    this.clearOrgFilters();
    if (!isSupportManagerRole(this.selectedRoleName)) {
      this.operationalBusinessDepartmentId = '';
    }
    if (this.orgAssignmentDepth === 'none') {
      this.clearOrgAssignment();
    } else {
      const cleared = clearMultiOrgSelection();
      this.selectedOrgNodeIds = cleared.selectedOrgNodeIds;
      this.primaryOrgNodeId = cleared.primaryOrgNodeId;
      const depth = this.orgAssignmentDepth;
      this.orgServiceId = '';
      this.form.subServiceId = null;
      this.orgMirrorWarning = null;
      if (depth === 'pole') {
        this.orgCelluleId = '';
      } else if (depth === 'cellule') {
        this.orgServiceId = '';
      }
      this.ensureOrgPickerDefaults();
    }
    this.cdr.detectChanges();
  }

  setLevel(level: 1 | 2 | 3): void {
    this.form.level = level;
    this.cdr.detectChanges();
  }

  setExpertiseLevel(level: 1 | 2 | 3): void {
    this.form.niveauExpertiseMetier = level;
    this.cdr.detectChanges();
  }

  goToOrganisationRh(): void {
    void this.navActions.openOrganisationRh();
  }

  checkEmail(): void {
    if (!this.form.email.trim()) return;
    this.userService.checkEmailUnique(this.form.email, this.userId ?? undefined).subscribe({
      next: (res) => {
        this.emailError = res.isUnique ? null : 'Ce mail interne est déjà utilisé.';
        this.cdr.detectChanges();
      }
    });
  }

  checkPersonalEmail(): void {
    const value = this.hrProfile.emailPersonnel.trim();
    if (!value) {
      this.personalEmailError = null;
      return;
    }
    const basicEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    this.personalEmailError = basicEmail.test(value) ? null : 'Format d\'email personnel invalide.';
    this.cdr.detectChanges();
  }

  private validateOrgAssignment(): string | null {
    if (this.isSupportMode) return null;
    const depth = this.orgAssignmentDepth;
    if (!orgAssignmentIsRequired(depth)) return null;

    if (this.supportsMultiOrg) {
      const multiErr = validateMultiOrgSelection(
        {
          selectedOrgNodeIds: this.selectedOrgNodeIds,
          primaryOrgNodeId: this.primaryOrgNodeId,
        },
        true,
      );
      if (multiErr) return multiErr;

      // Ancre technique (1er nœud) → champs cascade legacy pour mentors / Planning.
      if (!this.orgPoleId.trim() && orgAssignmentRequiresPole(depth)) {
        return 'Périmètre invalide (pôle introuvable).';
      }
      if (orgAssignmentRequiresCellule(depth) && !this.orgCelluleId.trim()) {
        return 'Périmètre invalide (cellule introuvable).';
      }
      if (orgAssignmentRequiresService(depth) && !this.orgServiceId.trim()) {
        return 'Périmètre invalide (service introuvable).';
      }
      if (this.orgMirrorWarning) return this.orgMirrorWarning;
      if (orgAssignmentRequiresService(depth) && !this.form.subServiceId) {
        return 'Un des services choisis n’est pas encore disponible dans Planning.';
      }
      if (isSuperviseurRole(this.selectedRoleName)) {
        for (const celluleId of this.selectedOrgNodeIds) {
          const svcId = resolvePlanningServiceIdByPrimeCelluleId(this.planningServices, celluleId);
          if (!svcId) {
            return 'Une des cellules choisies n’est pas encore synchronisée dans Planning.';
          }
        }
      }
      return null;
    }

    if (
      orgAssignmentRequiresOperationalDept(depth) &&
      !this.orgOperationalDeptId.trim() &&
      !isUnassignedPole(this.unassignedPoles, this.orgPoleId)
    ) {
      return 'Sélectionnez un département de production (Organisation RH).';
    }
    if (orgAssignmentRequiresPole(depth) && !this.orgPoleId.trim()) {
      return 'Sélectionnez un pôle (Organisation RH).';
    }
    if (orgAssignmentRequiresCellule(depth) && !this.orgCelluleId.trim()) {
      return 'Sélectionnez une cellule (Organisation RH).';
    }
    if (orgAssignmentRequiresService(depth) && !this.orgServiceId.trim()) {
      return 'Sélectionnez un service (Organisation RH).';
    }
    if (this.orgMirrorWarning) return this.orgMirrorWarning;
    if (orgAssignmentRequiresService(depth) && !this.form.subServiceId) {
      return 'Le service choisi n’est pas encore disponible dans Planning.';
    }
    if (isSuperviseurRole(this.selectedRoleName)) {
      const svcId = resolvePlanningServiceIdByPrimeCelluleId(this.planningServices, this.orgCelluleId);
      if (!svcId) {
        return 'La cellule choisie n’est pas encore synchronisée dans Planning.';
      }
    }
    return null;
  }

  private validateOperationalManagerAssignment(): string | null {
    if (!this.showOperationalManagerBlock) return null;
    if (!this.operationalBusinessDepartmentId.trim()) {
      return 'Sélectionnez un département de production.';
    }
    if (this.operationalBusinessDepartments.length === 0) {
      return 'Aucun département de production disponible — créez-en un dans Organisation RH.';
    }
    return null;
  }

  private validateSupportAssignment(): string | null {
    if (!this.isSupportMode) return null;
    if (!this.supportDepartmentId.trim()) {
      return 'Sélectionnez un département Support.';
    }
    if (this.supportDepartments.length === 0) {
      return 'Aucun département Support disponible — créez-en un dans Organisation support.';
    }
    return null;
  }

  private buildUserMutationDto(): Pick<
    CreateUserDto,
    | 'roleId'
    | 'subServiceId'
    | 'managedSubServiceIds'
    | 'managedServiceIds'
    | 'firstName'
    | 'lastName'
    | 'email'
    | 'level'
    | 'chefDeProjetId'
    | 'superviseurId'
    | 'referentTechniqueId'
    | 'hrProfile'
    | 'niveauExpertiseMetier'
  > {
    const roleName = this.selectedRoleName;
    let subServiceId = this.form.subServiceId ?? undefined;
    const managedServiceIds: number[] = [];
    const managedSubServiceIds: number[] = [];
    if (this.isSupportMode) {
      subServiceId = undefined;
    } else if (isChefDeProjetRole(roleName) || isSuperviseurRole(roleName)) {
      subServiceId = undefined;
    }
    return {
      roleId: this.resolvedRoleId(),
      subServiceId,
      managedSubServiceIds,
      managedServiceIds,
      firstName: this.form.firstName,
      lastName: this.form.lastName,
      email: this.form.email,
      level: this.form.level,
      chefDeProjetId: this.toOptionalGuid(this.form.chefDeProjetId),
      superviseurId: this.toOptionalGuid(this.form.superviseurId),
      referentTechniqueId: this.toOptionalGuid(this.form.referentTechniqueId),
      hrProfile: this.buildHrProfilePayload(),
      niveauExpertiseMetier: this.form.niveauExpertiseMetier,
    };
  }

  private buildCustomFieldsPayload(): Record<string, string | null> {
    const payload: Record<string, string | null> = {};
    for (const field of this.customEmployeeFields) {
      const raw = (this.customFieldValues[field.fieldKey] ?? '').trim();
      payload[field.fieldKey] = raw.length ? raw : null;
    }
    return payload;
  }

  private buildCreateUserDto(): CreateUserDto {
    return {
      ...this.buildUserMutationDto(),
      idTechnicien: this.preferredIdTechnicien,
      hireDate: this.toISOString(this.form.hireDate),
      customFields: this.buildCustomFieldsPayload(),
    };
  }

  private formatRevokedLog(result: unknown): string[] {
    if (!result || typeof result !== 'object') return [];
    const typed = result as StructuralRoleAssignmentResult;
    const messages: string[] = [];
    const revoked = typed.revoked;
    if (Array.isArray(revoked)) {
      for (const v of revoked) {
        const where = v.nodeLabel ?? v.departmentCode ?? v.nodeId;
        messages.push(`Ancien rôle retiré : ${v.role}${where ? ` (${where})` : ''}`);
      }
    }
    const revokedOnNode = typed.revokedOnNode;
    if (Array.isArray(revokedOnNode)) {
      for (const v of revokedOnNode) {
        messages.push(`Titulaire remplacé sur ${v.nodeId} (unicité par nœud)`);
      }
    }
    return messages;
  }

  private showRevokedToasts(result: unknown): void {
    for (const msg of this.formatRevokedLog(result)) {
      this.toastService.show(msg, 'info', 6000);
    }
  }

  private applyPrimeStructureAssignment(
    employeeGuid: string,
    roleName: string,
    strict = false,
  ): Observable<StructuralRoleAssignmentResult | void> {
    if (!needsPrimeStructureAssignment(roleName)) {
      return of(undefined);
    }
    const kind = directoryAssignmentKindForRole(roleName);
    if (kind && supportsMultiOrgSelection(roleName)) {
      const nodeIds = [...this.selectedOrgNodeIds];
      const primaryNodeId = this.primaryOrgNodeId.trim();
      if (nodeIds.length === 0 || !primaryNodeId) {
        return throwError(() => new Error('Sélection multi-périmètres incomplète.'));
      }
      return defer(() =>
        this.orgApi.reconcileEmployeeStructuralAssignments(
          kind,
          employeeGuid,
          nodeIds,
          primaryNodeId,
          'Formulaire employé — synchronisation multi-périmètres',
        ),
      ).pipe(
        map((result) => {
          this.selectedOrgNodeIds = [...result.nodeIds];
          this.primaryOrgNodeId = result.primaryNodeId;
          this.syncCascadeFromPrimaryOrgNode();
          if (result.revokedOtherKinds?.length) {
            for (const msg of result.revokedOtherKinds.map(
              (r) => `Rôle retiré : ${r.role} (${r.nodeId})`,
            )) {
              this.toastService.show(msg, 'info', 6000);
            }
          }
          if (result.revokedOnNode?.length) {
            for (const v of result.revokedOnNode) {
              this.toastService.show(
                `Titulaire remplacé sur ${v.nodeId} (unicité par nœud)`,
                'info',
                6000,
              );
            }
          }
          return {
            revoked: result.revokedOtherKinds ?? [],
            revokedOnNode: result.revokedOnNode ?? [],
            addedEmployeeId: result.employeeId,
          } satisfies StructuralRoleAssignmentResult;
        }),
        catchError((err) => (strict ? throwError(() => err) : of(undefined))),
      );
    }
    const depth = orgRoleAssignmentDepth(roleName);
    const call = (): Observable<StructuralRoleAssignmentResult> => {
      if (depth === 'pole') {
        return this.orgApi.setStructureManager(this.orgPoleId, employeeGuid);
      }
      if (depth === 'cellule') {
        return this.orgApi.setStructureSupervisor(this.orgCelluleId, employeeGuid);
      }
      if (isReferentTechniqueRole(roleName)) {
        return this.orgApi.setStructureCoach(this.orgServiceId, employeeGuid);
      }
      return of({ revoked: [] });
    };
    return defer(() => call()).pipe(
      retry({ count: 10, delay: 800 }),
      catchError((err) => (strict ? throwError(() => err) : of(undefined))),
    );
  }

  private syncPiloteDirectoryAssignment(employeeGuid: string): Observable<void> {
    const serviceId = this.orgServiceId.trim();
    if (!serviceId) {
      return throwError(() => new Error('Service Pilote manquant.'));
    }
    return this.orgApi.addStructurePilot(serviceId, employeeGuid).pipe(
      retry({ count: 5, delay: 800 }),
      map((result) => {
        this.showRevokedToasts(result);
        return undefined;
      }),
    );
  }

  private buildEnsureEmployeeDto(employeeGuid: string, roleName: string) {
    const primeServiceId =
      (this.supportsMultiOrg && isReferentTechniqueRole(roleName)
        ? this.primaryOrgNodeId.trim()
        : this.orgServiceId.trim()) || null;
    return {
      employeeId: employeeGuid,
      firstName: this.form.firstName.trim(),
      lastName: this.form.lastName.trim(),
      email: this.form.email.trim(),
      role: roleName,
      primeServiceId,
    };
  }

  private ensureEmployeeInDirectory(employeeGuid: string, roleName: string): Observable<void> {
    return this.orgApi.ensureEmployeeFromPlanning(this.buildEnsureEmployeeDto(employeeGuid, roleName)).pipe(
      map(() => undefined),
      catchError((ensureErr) =>
        this.orgApi.waitForEmployee(employeeGuid, 3000).pipe(
          map(() => undefined),
          catchError(() => throwError(() => ensureErr)),
        ),
      ),
    );
  }

  private syncPrimeStructureAssignment(employeeGuid: string, roleName: string): Observable<void> {
    if (isPiloteRole(roleName)) {
      return this.ensureEmployeeInDirectory(employeeGuid, roleName).pipe(
        switchMap(() => this.syncPiloteDirectoryAssignment(employeeGuid)),
      );
    }
    if (!needsPrimeStructureAssignment(roleName)) {
      return of(undefined);
    }
    // Réconciliation atomique : pas de retry en boucle (évite états partiels / 409 répétés).
    return this.ensureEmployeeInDirectory(employeeGuid, roleName).pipe(
      switchMap(() => this.applyPrimeStructureAssignment(employeeGuid, roleName, true)),
      map((result) => {
        if (result && 'revoked' in result) {
          this.showRevokedToasts(result);
        }
        return undefined;
      }),
    );
  }

  private syncOperationalManagerAssignment(employeeGuid: string, roleName: string): Observable<void> {
    const deptId = this.operationalBusinessDepartmentId.trim();
    if (!deptId) {
      return throwError(() => new Error('Département de production manquant.'));
    }
    return this.ensureEmployeeInDirectory(employeeGuid, roleName).pipe(
      switchMap(() =>
        defer(() =>
          this.http.post<StructuralRoleAssignmentResult>(
            `/api/directory/business-departments/${deptId}/manager`,
            { employeeId: employeeGuid },
          ),
        ).pipe(
          map((result) => {
            this.showRevokedToasts(result);
            return undefined;
          }),
        ),
      ),
      retry({ count: 5, delay: 800 }),
    );
  }

  private resolveDirectorySync$(employeeGuid: string, roleName: string): Observable<void> {
    if (this.isSupportMode) {
      return this.syncSupportDirectoryAssignment(employeeGuid, roleName);
    }
    if (this.showOperationalManagerBlock) {
      return this.syncOperationalManagerAssignment(employeeGuid, roleName);
    }
    return this.syncPrimeStructureAssignment(employeeGuid, roleName);
  }

  private syncSupportDirectoryAssignment(employeeGuid: string, roleName: string): Observable<void> {
    const deptId = this.supportDepartmentId.trim();
    if (!deptId) {
      return throwError(() => new Error('Département Support manquant.'));
    }
    const hireDateISO = this.toISOString(this.form.hireDate);
    const apply = (): Observable<void> => {
      if (isSupportManagerRole(roleName)) {
        return defer(() =>
          this.http.post<StructuralRoleAssignmentResult>(
            `/api/directory/business-departments/${deptId}/manager`,
            { employeeId: employeeGuid },
          ),
        ).pipe(
          map((result) => {
            this.showRevokedToasts(result);
            return undefined;
          }),
        );
      }
      return defer(() =>
        this.http.put(`/api/directory/employees/${employeeGuid}`, {
          firstName: this.form.firstName.trim(),
          lastName: this.form.lastName.trim(),
          email: this.form.email.trim(),
          role: roleName,
          serviceId: null,
          parentId: null,
          isActive: true,
          hireDate: hireDateISO,
          businessDepartmentId: deptId,
        }),
      ).pipe(map(() => undefined));
    };
    return this.ensureEmployeeInDirectory(employeeGuid, roleName).pipe(
      switchMap(() => apply()),
      retry({ count: 5, delay: 800 }),
    );
  }

  private async confirmCrossRoleAssignment(employeeId: string): Promise<boolean> {
    const overview = this.orgOverview;
    if (!overview || !employeeId.trim()) return true;
    const targetRole = this.selectedRoleName || '';
    const existing = findConflictingStructuralRole(overview, employeeId, targetRole);
    if (!existing) return true;
    const name = employeeDisplayName(overview.employees, employeeId);
    return this.confirmService.confirm({
      title: 'Remplacer le rôle actuel',
      message: buildCrossRoleOverwriteMessage(name, existing),
      confirmLabel: 'Remplacer et continuer',
      cancelLabel: 'Annuler',
      variant: 'warning',
    });
  }

  private async confirmStructureAssignmentIfNeeded(roleName: string): Promise<boolean> {
    if (this.isSupportMode || this.showOperationalManagerBlock) {
      return true;
    }
    const needsStructure = needsPrimeStructureAssignment(roleName) || isPiloteRole(roleName);
    if (!needsStructure) {
      return true;
    }
    const overview = this.orgOverview;
    if (!overview) {
      this.error = 'Structure organisationnelle non chargée — réessayez dans quelques secondes.';
      return false;
    }

    if (this.isEditMode && this.loadedUserGuid.trim()) {
      return this.confirmCrossRoleAssignment(this.loadedUserGuid);
    }
    return true;
  }

  submit(): void {
    void this.submitAsync();
  }

  private async submitAsync(): Promise<void> {
    const identityErr = this.validateWizardStepIdentity();
    if (identityErr) {
      this.error = identityErr;
      this.currentWizardStep = 'identity';
      return;
    }
    const positionErr = this.validateWizardStepPosition();
    if (positionErr) {
      this.error = positionErr;
      this.currentWizardStep = 'position';
      return;
    }
    const pathwayErr = this.validateWizardStepPathway();
    if (pathwayErr) {
      this.error = pathwayErr;
      this.currentWizardStep = 'pathway';
      return;
    }
    if (!this.isEditMode && this.preferredIdTechnicien &&
        (this.htelLoading || this.htelLoadError || !this.htelAvailableIds.has(this.preferredIdTechnicien)
          || !this.htelIdentityConfirmed || !htelIdentityMatches(this.htelSourceName, this.form.firstName, this.form.lastName))) {
      this.error = 'Vérifiez le collaborateur HTEL et confirmez la séparation Nom / Prénom avant de créer la fiche.';
      this.currentWizardStep = 'identity';
      return;
    }
    if (!this.form.roleId || !this.form.firstName.trim() ||
        !this.form.lastName.trim() || !this.form.email.trim() || !this.form.hireDate) {
      this.error = 'Tous les champs obligatoires doivent être remplis.';
      return;
    }
    if (this.emailError) return;
    if (this.personalEmailError) return;
    const supportError = this.validateSupportAssignment();
    if (supportError) {
      this.error = supportError;
      return;
    }
    const operationalManagerError = this.validateOperationalManagerAssignment();
    if (operationalManagerError) {
      this.error = operationalManagerError;
      return;
    }
    const orgError = this.validateOrgAssignment();
    if (orgError) {
      this.error = orgError;
      return;
    }
    const roleName = this.selectedRoleName;
    if (!(await this.confirmStructureAssignmentIfNeeded(roleName))) {
      return;
    }
    this.submitting = true;
    this.error = null;
    const hireDateISO = this.toISOString(this.form.hireDate);
    if (this.isEditMode && this.userId) {
      const dto: UpdateUserDto = {
        ...this.buildUserMutationDto(),
        hireDate: hireDateISO,
        isActive: this.form.isActive,
        customFields: this.buildCustomFieldsPayload(),
      };
      const sync$ = this.resolveDirectorySync$(this.loadedUserGuid.trim(), roleName);
      this.userService.updateUser(this.userId, dto).pipe(
        switchMap(() => {
          const guid = this.loadedUserGuid.trim();
          if (!guid) {
            return throwError(
              () => new Error('Identifiant employé manquant — rechargez la fiche puis réessayez.'),
            );
          }
          return sync$;
        }),
        switchMap(() => this.saveContract$(this.userId!)),
      ).subscribe({
        next: () => {
          this.draftBinder?.clear();
          void this.router.navigate(['/users', this.userId]);
        },
        error: (err) => {
          this.error = formatHttpErrorMessage(err, 'Échec de la mise à jour employé ou contrat.');
          this.submitting = false;
          this.cdr.detectChanges();
        }
      });
    } else {
      const dto = this.buildCreateUserDto();
      this.userService.createUser(dto).pipe(
        switchMap((user) => {
          const guid = resolveUserGuid(user);
          if (!guid) {
            return throwError(
              () => new Error('Réponse serveur invalide : identifiant employé (guid) manquant.'),
            );
          }
          return this.resolveDirectorySync$(guid, roleName).pipe(map(() => user));
        }),
        switchMap((user) => of(user)),
        switchMap((user) => {
          const guid = resolveUserGuid(user);
          if (!guid || !this.selectedReferralId.trim()) {
            return of({ user, referralResult: null as Referral | null });
          }
          return this.completeReferralOnboarding$(guid).pipe(
            map((referralResult) => ({ user, referralResult })),
            catchError((err) =>
              this.compensateFailedCreate$(
                user.id,
                err,
                'Échec de la finalisation parrainage. Aucun employé n’a été conservé.',
              ),
            ),
          );
        }),
        switchMap(({ user, referralResult }) => {
          if (!this.hrProfile.enFormation) {
            return of({ user, referralResult });
          }
          const guid = resolveUserGuid(user);
          if (!guid) return of({ user, referralResult });
          const name = `${this.form.firstName} ${this.form.lastName}`.trim();
          return defer(() =>
            this.formationTraining.createInitialPath({
              employeeId: guid,
              employeeName: name || this.form.email,
              dateDebut: this.hrProfile.dateDebutFormation || this.form.hireDate,
              dateFinPrevue: this.hrProfile.dateFinFormationPrevue,
            }),
          ).pipe(
            map(() => ({ user, referralResult })),
            catchError((err) =>
              this.compensateFailedCreate$(
                user.id,
                err,
                'Échec de l’enregistrement du parcours formation. Aucun employé n’a été conservé.',
              ),
            ),
          );
        }),
        switchMap(({ user, referralResult }) =>
          this.saveContract$(user.id).pipe(
            map(() => ({ user, referralResult })),
            catchError((err) =>
              this.compensateFailedCreate$(
                user.id,
                err,
                'Échec de l’enregistrement du contrat. Aucun employé n’a été conservé.',
              ),
            ),
          ),
        ),
      ).subscribe({
        next: ({ user, referralResult }) => {
          this.createdUserId = user.id;
          let message = this.hrProfile.enFormation
            ? 'Employé créé — le contrat pourra être défini après la formation.'
            : 'Employé et contrat enregistrés avec succès.';
          if (referralResult) {
            const statusLabel =
              referralResult.status === 'IN_TRAINING'
                ? 'En cours de formation'
                : referralResult.status === 'APPROVED'
                  ? 'Validé'
                  : this.referralStatusLabels[referralResult.status] ?? referralResult.status;
            message = `Employé créé et dossier parrainage passé en ${statusLabel}.`;
          }
          const generated = (user as { generatedPassword?: string | null }).generatedPassword?.trim();
          this.createdCredentials = generated
            ? { email: user.email, password: generated }
            : null;
          this.showCreatedPassword = false;
          this.createSuccessMessage = message;
          this.showCreateSuccess = true;
          this.draftBinder?.clear();
          this.submitting = false;
          this.toastService.success(this.createSuccessMessage);
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.error = formatHttpErrorMessage(err);
          this.submitting = false;
          this.cdr.detectChanges();
        }
      });
    }
  }

  goBack(): void {
    this.isEditMode
      ? this.router.navigate(['/users', this.userId])
      : this.router.navigate(['/users']);
  }

  wizardStepIndex(): number {
    return this.wizardSteps.findIndex((s) => s.id === this.currentWizardStep);
  }

  isWizardStepDone(stepId: WizardStepId): boolean {
    return this.wizardStepIndex() > this.wizardSteps.findIndex((s) => s.id === stepId);
  }

  canNavigateToWizardStep(stepId: WizardStepId): boolean {
    const target = this.wizardSteps.findIndex((s) => s.id === stepId);
    const current = this.wizardStepIndex();
    if (target <= current) return true;
    if (target === current + 1) {
      return this.validateCurrentWizardStep() === null;
    }
    return false;
  }

  goToWizardStep(stepId: WizardStepId): void {
    if (!this.canNavigateToWizardStep(stepId)) return;
    this.currentWizardStep = stepId;
    this.error = null;
    if (stepId === 'pathway') {
      this.preparePathwayStep();
    }
    this.cdr.detectChanges();
  }

  nextWizardStep(): void {
    const err = this.validateCurrentWizardStep();
    if (err) {
      this.error = err;
      this.cdr.detectChanges();
      return;
    }
    this.error = null;
    const idx = this.wizardStepIndex();
    if (idx < this.wizardSteps.length - 1) {
      const nextStep = this.wizardSteps[idx + 1].id;
      this.currentWizardStep = nextStep;
      if (nextStep === 'pathway') {
        this.preparePathwayStep();
      }
      this.cdr.detectChanges();
    }
  }

  prevWizardStep(): void {
    const idx = this.wizardStepIndex();
    if (idx > 0) {
      this.currentWizardStep = this.wizardSteps[idx - 1].id;
      this.error = null;
      this.cdr.detectChanges();
    }
  }

  validateCurrentWizardStep(): string | null {
    switch (this.currentWizardStep) {
      case 'identity':
        return this.validateWizardStepIdentity();
      case 'position':
        return this.validateWizardStepPosition();
      case 'pathway':
        return this.validateWizardStepPathway();
      default:
        return null;
    }
  }

  private validateWizardStepIdentity(): string | null {
    if (!this.form.firstName.trim() || !this.form.lastName.trim()) {
      return 'Le nom et le prénom sont obligatoires.';
    }
    if (!this.form.email.trim()) {
      return 'Le mail interne est obligatoire.';
    }
    if (this.emailError) {
      return this.emailError;
    }
    if (this.personalEmailError) {
      return this.personalEmailError;
    }
    if (!this.form.hireDate) {
      return 'La date d\'embauche est obligatoire.';
    }
    if (this.showSituationEnfantsChoice && !this.situationAvecEnfants) {
      return 'Indiquez si l\'employé a des enfants pour cette situation familiale.';
    }
    if (this.showNombreEnfants && (this.hrProfile.nombreEnfants === null || this.hrProfile.nombreEnfants < 1)) {
      return 'Le nombre d\'enfants est obligatoire (minimum 1).';
    }
    if (this.showNationaliteAutre && !this.nationaliteAutre.trim()) {
      return 'Précisez la nationalité lorsque « Autre » est sélectionné.';
    }
    if (this.showRequiresAutoentrepreneur && !this.hrProfile.numeroCarteAutoentrepreneur.trim()) {
      return 'Le numéro de carte autoentrepreneur est obligatoire pour la nationalité « Autre ».';
    }
    return null;
  }

  private validateWizardStepPosition(): string | null {
    if (!this.form.roleId) {
      return 'Sélectionnez un rôle.';
    }
    const supportError = this.validateSupportAssignment();
    if (supportError) return supportError;
    const operationalManagerError = this.validateOperationalManagerAssignment();
    if (operationalManagerError) return operationalManagerError;
    const orgError = this.validateOrgAssignment();
    if (orgError) return orgError;
    return this.validateResponsables();
  }

  private validateWizardStepPathway(): string | null {
    if (this.hrProfile.enFormation) {
      if (!this.hrProfile.dateDebutFormation.trim()) {
        return 'La date de début de formation est obligatoire.';
      }
      if (!this.hrProfile.dateFinFormationPrevue.trim()) {
        return 'La date de fin de formation prévue est obligatoire.';
      }
      if (this.hrProfile.dateFinFormationPrevue < this.hrProfile.dateDebutFormation) {
        return 'La date de fin de formation doit être postérieure ou égale à la date de début.';
      }
      if (
        this.hrProfile.dateEntree.trim() &&
        this.hrProfile.dateDebutFormation < this.hrProfile.dateEntree
      ) {
        return 'La date de début de formation doit être postérieure ou égale à la date d\'entrée.';
      }
      if (
        this.form.hireDate.trim() &&
        this.hrProfile.dateFinFormationPrevue < this.form.hireDate
      ) {
        return 'La date de fin de formation doit être postérieure ou égale à la date d\'embauche.';
      }
      if (this.isEditMode || !this.canShowContractFields) return null;
    }
    if (!this.canShowContractFields) return null;
    if (!this.contractDraft.startDate.trim()) {
      return 'La date de début du contrat est obligatoire.';
    }
    if (this.contractDraft.type !== 'CDI' && !this.contractDraft.endDate.trim()) {
      return 'La date de fin du contrat est obligatoire pour ce type.';
    }
    if (
      this.contractDraft.type !== 'CDI' &&
      this.contractDraft.endDate < this.contractDraft.startDate
    ) {
      return 'La date de fin du contrat doit être postérieure à la date de début.';
    }
    return null;
  }

  private preparePathwayStep(): void {
    if (this.hrProfile.enFormation) {
      if (!this.hrProfile.dateDebutFormation.trim()) {
        this.hrProfile.dateDebutFormation = this.form.hireDate;
      }
    } else if (!this.contractDraft.startDate.trim()) {
      this.contractDraft.startDate = this.form.hireDate;
    }
  }

  private buildCreateContractDto(userId: number): CreateContractDto {
    const dto: CreateContractDto = {
      userId,
      type: this.contractDraft.type,
      startDate: this.toISOString(this.contractDraft.startDate),
      alertThresholdDays: this.contractDraft.alertThresholdDays,
    };
    if (this.contractDraft.type !== 'CDI' && this.contractDraft.endDate.trim()) {
      dto.endDate = this.toISOString(this.contractDraft.endDate);
    }
    if (this.contractDraft.probationDays != null && this.contractDraft.probationDays >= 0) {
      dto.probationDays = this.contractDraft.probationDays;
    }
    if (this.contractDraft.notes.trim()) {
      dto.notes = this.contractDraft.notes.trim();
    }
    dto.status = this.contractDraft.status;
    return dto;
  }

  get wizardRecapName(): string {
    return `${this.form.firstName} ${this.form.lastName}`.trim() || '—';
  }

  get wizardRecapRole(): string {
    return this.selectedRoleName || '—';
  }

  get wizardRecapPathway(): string {
    if (this.hrProfile.enFormation) {
      return `Formation du ${this.hrProfile.dateDebutFormation || '—'} au ${this.hrProfile.dateFinFormationPrevue || '—'}`;
    }
    const end = this.contractDraft.type !== 'CDI' && this.contractDraft.endDate
      ? ` → ${this.contractDraft.endDate}`
      : '';
    return `${this.contractDraft.type} à partir du ${this.contractDraft.startDate || '—'}${end}`;
  }

  get submitButtonLabel(): string {
    if (this.submitting) return 'Enregistrement...';
    if (this.isEditMode) return 'Enregistrer les modifications';
    if (this.selectedReferralId.trim()) return 'Enregistrer et valider le dossier parrainage';
    return 'Créer l\'employé';
  }

  goToNewContract(): void {
    if (!this.createdUserId) return;
    void this.router.navigate(['/contracts/new'], { queryParams: { userId: this.createdUserId } });
  }

  viewCreatedUser(): void {
    if (!this.createdUserId) return;
    void this.router.navigate(['/users', this.createdUserId]);
  }

  async copyCreatedPassword(): Promise<void> {
    if (!this.createdCredentials?.password) return;
    try {
      await copyTextToClipboard(this.createdCredentials.password);
      this.toastService.success('Mot de passe copié.');
    } catch (err) {
      console.error('copyCreatedPassword', err);
      this.toastService.error('Impossible de copier le mot de passe.');
    } finally {
      this.cdr.detectChanges();
    }
  }

  async downloadCreatedCredentialsExcel(): Promise<void> {
    if (!this.createdCredentials || this.downloadingCredentialsExcel) return;
    this.downloadingCredentialsExcel = true;
    this.cdr.detectChanges();
    try {
      const { downloadCredentialsExcel } = await import('../../../../core/lib/credentials-excel.util');
      await downloadCredentialsExcel(
        [
          {
            email: this.createdCredentials.email,
            password: this.createdCredentials.password,
            firstName: this.form.firstName,
            lastName: this.form.lastName,
          },
        ],
        { fileNamePrefix: 'identifiants-mykyntus' },
      );
      this.toastService.success('Excel des identifiants téléchargé.');
    } catch (err) {
      console.error('downloadCreatedCredentialsExcel', err);
      this.toastService.error('Échec du téléchargement Excel.');
    } finally {
      this.downloadingCredentialsExcel = false;
      this.cdr.detectChanges();
    }
  }
}
