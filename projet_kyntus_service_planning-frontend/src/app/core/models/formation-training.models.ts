export type AnimatorKind = 'Internal' | 'External';
export type TrainingSessionStatus = 'Draft' | 'Scheduled' | 'InProgress' | 'Completed' | 'Cancelled';
export type TrainingAttendance = 'Pending' | 'Present' | 'Absent';
export type TrainingProgramMode = 'Single' | 'Multiple';
export type TrainingQuizStatus = 'Draft' | 'Published' | 'Graded' | 'Validated' | 'Rejected';
export type TrainingQuizQuestionType = 'Qcm' | 'FreeText';
export type InitialTrainingStatus =
  | 'EnCours'
  | 'QuizASaisir'
  | 'AttenteValidationFormateur'
  | 'AttenteValidationRh'
  | 'EnProduction'
  | 'Rejete';

export interface TrainingSessionDto {
  id: string;
  title: string;
  description: string;
  type: string;
  animatorKind: AnimatorKind;
  animatorUserId?: string | null;
  externalAnimatorName?: string | null;
  externalAnimatorOrganization?: string | null;
  externalAnimatorEmail?: string | null;
  externalAnimatorPhone?: string | null;
  plannedStart: string;
  plannedEnd: string;
  capacity: number;
  status: TrainingSessionStatus;
  assignmentCount: number;
  programId?: string | null;
  sequenceNumber?: number;
  hasReport?: boolean;
  quizId?: string | null;
  quizStatus?: TrainingQuizStatus | string | null;
  catalogItemId?: string | null;
  learningGateMode?: string | null;
  canMarkAttendance?: boolean | null;
  canUploadReport?: boolean | null;
  attendanceBlockedReason?: string | null;
  reportBlockedReason?: string | null;
}

export interface TrainingProgramDto {
  id: string;
  title: string;
  description: string;
  mode: TrainingProgramMode | number;
  sessionCount: number;
  animatorKind: AnimatorKind | number;
  animatorUserId?: string | null;
  externalAnimatorName?: string | null;
  capacity: number;
  sessions: TrainingSessionDto[];
}

export interface ProgramBeneficiaryProgressDto {
  employeeId: string;
  employeeName: string;
  attendedInPerson: boolean;
  contentCompleted: boolean;
  quizPassed: boolean;
  hasContentTrack: boolean;
  hasQuizTrack: boolean;
  isComplete: boolean;
}

export interface TrainingProgramDetailDto {
  id: string;
  title: string;
  description: string;
  mode: TrainingProgramMode | number;
  sessionCount: number;
  animatorKind: AnimatorKind | number;
  animatorUserId?: string | null;
  externalAnimatorName?: string | null;
  externalAnimatorOrganization?: string | null;
  externalAnimatorEmail?: string | null;
  externalAnimatorPhone?: string | null;
  capacity: number;
  catalogItemId?: string | null;
  quizTemplateId?: string | null;
  learningGateMode?: string | null;
  sessions: TrainingSessionDto[];
  beneficiaries: ProgramBeneficiaryProgressDto[];
}

export interface TrainingAssignmentDto {
  id: string;
  sessionId: string;
  employeeId: string;
  employeeName: string;
  status: string;
  attendance: TrainingAttendance;
}

/** Session continue où l'employé est bénéficiaire (Mes formations). */
export interface MyAssignedTrainingSessionDto {
  sessionId: string;
  assignmentId: string;
  title: string;
  plannedStart: string;
  plannedEnd: string;
  status: TrainingSessionStatus;
  attendance: TrainingAttendance;
  quizId?: string | null;
  quizStatus?: string | null;
  canTakeQuiz?: boolean;
  attemptId?: string | null;
  attemptGraded?: boolean;
  finalScore?: number | null;
  passed?: boolean | null;
  catalogItemId?: string | null;
  catalogProgressPercent?: number;
  requiredLessonsDone?: number;
  requiredLessonsTotal?: number;
  quizBlockedReason?: string | null;
  allowMultipleAttempts?: boolean;
}

export interface TrainingSessionReportDto {
  id: string;
  sessionId: string;
  fileName: string;
  contentType: string;
  uploadedAt: string;
}

export interface TrainingQuizQuestionDto {
  id: string;
  sortOrder: number;
  type: TrainingQuizQuestionType | number;
  prompt: string;
  options?: string[] | null;
  correctOptionIndex?: number | null;
  correctOptionIndexes?: number[] | null;
  allowMultiple?: boolean;
  points: number;
  imageUrl?: string | null;
  explanation?: string | null;
  mediaKind?: string | null;
}

export interface TrainingQuizDto {
  id: string;
  sessionId: string;
  title: string;
  status: TrainingQuizStatus | number;
  questions: TrainingQuizQuestionDto[];
  rejectedReason?: string | null;
  /** Seuil de réussite en % (score ≥ plafond → Valide). */
  passThreshold?: number;
  allowMultipleAttempts?: boolean;
}

export interface TrainingQuizForEmployeeDto {
  id: string;
  sessionId: string;
  title: string;
  status: TrainingQuizStatus | number;
  questions: {
    id: string;
    sortOrder: number;
    type: TrainingQuizQuestionType | number;
    prompt: string;
    options?: string[] | null;
    points: number;
    allowMultiple?: boolean;
    imageUrl?: string | null;
    mediaKind?: string | null;
  }[];
  allowMultipleAttempts?: boolean;
  passThreshold?: number;
  catalogItemId?: string | null;
  enrollmentId?: string | null;
}

export interface TrainingQuizAttemptAnswerDetailDto {
  questionId: string;
  sortOrder: number;
  type: TrainingQuizQuestionType | number;
  prompt: string;
  options?: string[] | null;
  selectedOptionIndex?: number | null;
  selectedOptionIndexes?: number[] | null;
  freeText?: string | null;
  correctOptionIndex?: number | null;
  correctOptionIndexes?: number[] | null;
  allowMultiple: boolean;
  isCorrect?: boolean | null;
  points: number;
  imageUrl?: string | null;
  explanation?: string | null;
  mediaKind?: string | null;
}

export interface TrainingQuizAttemptDto {
  id: string;
  quizId: string;
  assignmentId: string;
  employeeId: string;
  employeeName: string;
  autoScore?: number | null;
  manualScore?: number | null;
  finalScore?: number | null;
  passed?: boolean | null;
  isGraded: boolean;
  submittedAt: string;
  animatorComment?: string | null;
  answers?: TrainingQuizAttemptAnswerDetailDto[] | null;
  attemptNumber?: number;
}

export type CatalogItemStatus = 'Draft' | 'Published' | 'Archived' | number;
export type LearningGateMode = 'Attendance' | 'Content' | 'Both' | number;
export type CatalogAudienceMatchMode = 'MatchAny' | 'MatchAll' | number;
export type TrainingResourceType = 'Pdf' | 'Video' | 'Link' | 'Text' | 'Image' | number;
export type CatalogDueMode = 'None' | 'Absolute' | 'RelativeDays' | number;

export interface TrainingCatalogAudienceDto {
  matchMode: CatalogAudienceMatchMode;
  roles: string[];
  structureKeys: string[];
  userIds: string[];
  estimatedBeneficiaryCount?: number;
}

export interface TrainingResourceDto {
  id: string;
  lessonId: string;
  type: TrainingResourceType;
  title: string;
  url?: string | null;
  contentType?: string | null;
  fileName?: string | null;
  textContent?: string | null;
  sortOrder: number;
  durationMinutes?: number | null;
  downloadPath?: string | null;
}

export interface TrainingLessonDto {
  id: string;
  moduleId: string;
  title: string;
  description: string;
  sortOrder: number;
  isRequired: boolean;
  resources: TrainingResourceDto[];
  isCompleted?: boolean;
  progressPercent?: number;
}

export interface TrainingModuleDto {
  id: string;
  catalogItemId: string;
  title: string;
  description: string;
  sortOrder: number;
  lessons: TrainingLessonDto[];
}

export interface StructureResourceRequest {
  clientKey: string;
  id?: string | null;
  type: number | string;
  title: string;
  url?: string | null;
  textContent?: string | null;
  sortOrder: number;
}

export interface StructureLessonRequest {
  clientKey: string;
  id?: string | null;
  title: string;
  description: string;
  sortOrder: number;
  isRequired: boolean;
  resources: StructureResourceRequest[];
}

export interface StructureModuleRequest {
  clientKey: string;
  id?: string | null;
  title: string;
  description: string;
  sortOrder: number;
  lessons: StructureLessonRequest[];
}

export interface ReplaceCatalogStructureRequest {
  modules: StructureModuleRequest[];
}

export interface StructureResourceResultDto {
  clientKey: string;
  id: string;
}

export interface StructureLessonResultDto {
  clientKey: string;
  id: string;
  resources: StructureResourceResultDto[];
}

export interface StructureModuleResultDto {
  clientKey: string;
  id: string;
  lessons: StructureLessonResultDto[];
}

export interface ReplaceCatalogStructureResponse {
  catalogItemId: string;
  modules: StructureModuleResultDto[];
}

export interface TrainingCatalogItemDto {
  id: string;
  title: string;
  description: string;
  category: string;
  status: CatalogItemStatus;
  isActive: boolean;
  defaultGateMode: LearningGateMode;
  audienceMatchMode: CatalogAudienceMatchMode;
  createdAt: string;
  updatedAt: string;
  publishedAt?: string | null;
  archivedAt?: string | null;
  moduleCount: number;
  lessonCount: number;
  resourceCount: number;
  audience?: TrainingCatalogAudienceDto | null;
  modules?: TrainingModuleDto[] | null;
  selfServiceEnabled?: boolean;
  dueMode?: CatalogDueMode;
  dueDate?: string | null;
  dueInDays?: number | null;
  defaultQuizTemplateId?: string | null;
}

export interface TrainingQuizTemplateListItemDto {
  id: string;
  title: string;
  description: string;
  category: string;
  status: CatalogItemStatus;
  passThreshold: number;
  allowMultipleAttempts: boolean;
  catalogItemId?: string | null;
  questionCount: number;
  sessionUsageCount: number;
  createdAt: string;
  updatedAt: string;
  publishedAt?: string | null;
  archivedAt?: string | null;
}

export interface TrainingQuizTemplateQuestionDto {
  id: string;
  sortOrder: number;
  type: TrainingQuizQuestionType | number;
  prompt: string;
  options?: string[] | null;
  correctOptionIndex?: number | null;
  points: number;
  allowMultiple?: boolean;
  correctOptionIndexes?: number[] | null;
  imageUrl?: string | null;
  explanation?: string | null;
  mediaKind?: string | null;
}

export interface TrainingQuizTemplateDto {
  id: string;
  title: string;
  description: string;
  category: string;
  status: CatalogItemStatus;
  passThreshold: number;
  allowMultipleAttempts: boolean;
  catalogItemId?: string | null;
  createdByUserId: string;
  createdAt: string;
  updatedAt: string;
  publishedAt?: string | null;
  archivedAt?: string | null;
  sessionUsageCount: number;
  questions: TrainingQuizTemplateQuestionDto[];
}

export interface UpsertTrainingQuizTemplateRequest {
  title: string;
  description?: string;
  category?: string;
  passThreshold?: number;
  allowMultipleAttempts?: boolean;
  catalogItemId?: string | null;
  createdByUserId?: string;
  questions: Array<{
    id?: string | null;
    type: number | TrainingQuizQuestionType;
    prompt: string;
    options?: string[] | null;
    correctOptionIndex?: number | null;
    allowMultiple?: boolean;
    correctOptionIndexes?: number[] | null;
    points?: number;
    imageUrl?: string | null;
    explanation?: string | null;
  }>;
}

export interface PromoteSessionQuizRequest {
  sessionId: string;
  actorUserId?: string;
  title?: string | null;
  description?: string | null;
  category?: string | null;
  catalogItemId?: string | null;
}

export type CatalogEnrollmentStatus = 'NotStarted' | 'InProgress' | 'Completed' | 'Overdue';

export interface MySelfServiceCatalogItemDto {
  catalogItemId: string;
  title: string;
  description: string;
  category: string;
  enrollmentId: string;
  status: CatalogEnrollmentStatus;
  dueAt?: string | null;
  progressPercent: number;
  requiredLessonsTotal: number;
  requiredLessonsDone: number;
  startedAt?: string | null;
  completedAt?: string | null;
}

export interface CatalogPlayerDto {
  catalogItemId: string;
  sessionId?: string | null;
  assignmentId?: string | null;
  enrollmentId: string;
  title: string;
  description: string;
  category: string;
  gateMode: LearningGateMode;
  progressPercent: number;
  requiredLessonsTotal: number;
  requiredLessonsDone: number;
  canTakeQuiz: boolean;
  quizBlockedReason?: string | null;
  modules: TrainingModuleDto[];
  dueAt?: string | null;
  enrollmentStatus?: CatalogEnrollmentStatus;
  defaultQuizTemplateId?: string | null;
}

export interface LearningQuizStatsDto {
  catalogCount: number;
  sessionWithCatalogCount: number;
  questionCount: number;
  attemptCount: number;
  avgScore: number;
  bestScore: number;
  passRate: number;
  bySession: LearningQuizStatsBySessionDto[];
}

export interface LearningQuizResultExportRowDto {
  employeeName: string;
  email: string;
  role: string;
  structureKey: string;
  sessionId?: string | null;
  sessionTitle: string;
  catalogItemId?: string | null;
  score?: number | null;
  passed?: boolean | null;
  attemptNumber: number;
  submittedAt: string;
}

export interface LearningQuizStatsBySessionDto {
  sessionId: string;
  catalogItemId?: string | null;
  title: string;
  category?: string | null;
  questionCount: number;
  attemptCount: number;
  avgScore: number;
  bestScore: number;
  passRate: number;
}

export interface MyQuizAttemptHistoryItemDto {
  attemptId: string;
  sessionId: string;
  sessionTitle: string;
  catalogItemId?: string | null;
  catalogTitle?: string | null;
  attemptNumber: number;
  score?: number | null;
  passed?: boolean | null;
  isGraded: boolean;
  submittedAt: string;
}

export interface FormationDashboardStatsDto {
  programCount: number;
  sessionCount: number;
  assignmentCount: number;
  presentCount: number;
  attendanceRate: number;
  quizCount: number;
  quizzesValidated: number;
  gradedAttempts: number;
  passedAttempts: number;
  quizSuccessRate: number;
  upcomingSessions: number;
  missingReports: number;
  quizzesPendingValidation: number;
}

export interface FormationInitialRiskItemDto {
  pathId: string;
  employeeId: string;
  employeeName: string;
  daysUntilEnd?: number | null;
  documentsReceivedCount: number;
  documentsTotalCount: number;
  missingDocumentTitles: string[];
}

export interface FormationInitialDashboardStatsDto {
  totalPaths: number;
  enCours: number;
  attenteValidationFormateur: number;
  attenteValidationRh: number;
  enProduction: number;
  rejete: number;
  pendingRh: number;
  avgQuizSuccessRate: number;
  pathsWithMissingDocs: number;
  endingWithin7Days: number;
  atRisk: FormationInitialRiskItemDto[];
}

export interface InitialTrainingQuizResultDto {
  id: string;
  title: string;
  score: number;
  passed: boolean;
  recordedBy?: string | null;
  recordedAt: string;
}

export interface InitialTrainingPathDto {
  id: string;
  employeeId: string;
  employeeName: string;
  dateDebut: string;
  dateFinPrevue: string;
  status: InitialTrainingStatus;
  hasQuizResult: boolean;
  formateurValidatedAt?: string | null;
  rhValidatedAt?: string | null;
  rejectedBy?: string | null;
  rejectReason?: string | null;
  quizResults?: InitialTrainingQuizResultDto[];
  quizSuccessRate?: number;
  documentsReceivedCount?: number;
  documentsTotalCount?: number;
  missingDocumentTitles?: string[];
  daysUntilEnd?: number | null;
  formateurValidatedBy?: string | null;
  rhValidatedBy?: string | null;
  orgDepartmentId?: string | null;
  orgPoleId?: string | null;
  orgCelluleId?: string | null;
  orgServiceId?: string | null;
  orgDepartmentName?: string | null;
  orgPoleName?: string | null;
  orgCelluleName?: string | null;
  orgServiceName?: string | null;
}

export interface FormateurValidateOrgRequest {
  orgDepartmentId: string;
  orgPoleId: string;
  orgCelluleId: string;
  orgServiceId: string;
  orgDepartmentName?: string;
  orgPoleName?: string;
  orgCelluleName?: string;
  orgServiceName?: string;
}

export function formatInitialTrainingOrgLabel(
  path: Pick<
    InitialTrainingPathDto,
    'orgDepartmentName' | 'orgPoleName' | 'orgCelluleName' | 'orgServiceName'
  >,
): string {
  return [path.orgDepartmentName, path.orgPoleName, path.orgCelluleName, path.orgServiceName]
    .map((part) => part?.trim() ?? '')
    .filter((part) => part.length > 0)
    .join(' · ');
}

export interface FormationDocumentDefinitionDto {
  id: string;
  title: string;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
}

export interface FormationDocumentChecklistItemDto {
  id: string;
  definitionId: string;
  title: string;
  sortOrder: number;
  isReceived: boolean;
  receivedAt?: string | null;
  receivedBy?: string | null;
  note?: string | null;
  pathId?: string | null;
}

/** Stub jusqu’à branchement de l’API absentéisme / retard externe. */
export interface FormationAttendanceMetricsStub {
  absenteeismRate: number | null;
  latenessRate: number | null;
}

export const INITIAL_TRAINING_STATUS_LABELS: Record<InitialTrainingStatus, string> = {
  EnCours: 'En cours',
  QuizASaisir: 'Quiz à saisir',
  AttenteValidationFormateur: 'Attente validation formateur',
  AttenteValidationRh: 'Attente validation RH',
  EnProduction: 'En production',
  Rejete: 'Rejeté',
};

export const TRAINING_SESSION_STATUS_LABELS: Record<TrainingSessionStatus, string> = {
  Draft: 'Brouillon',
  Scheduled: 'Planifiée',
  InProgress: 'En cours',
  Completed: 'Terminée',
  Cancelled: 'Annulée',
};

export const TRAINING_ATTENDANCE_LABELS: Record<TrainingAttendance, string> = {
  Pending: 'Non pointé',
  Present: 'Présent',
  Absent: 'Absent',
};

export const TRAINING_QUIZ_STATUS_LABELS: Record<TrainingQuizStatus, string> = {
  Draft: 'Brouillon',
  Published: 'Publié',
  Graded: 'Noté',
  Validated: 'Validé',
  Rejected: 'Rejeté',
};

/** Parcours initial encore en pipeline (hors production / rejet). */
export const INITIAL_TRAINING_ACTIVE_STATUSES: InitialTrainingStatus[] = [
  'EnCours',
  'QuizASaisir',
  'AttenteValidationFormateur',
  'AttenteValidationRh',
];
