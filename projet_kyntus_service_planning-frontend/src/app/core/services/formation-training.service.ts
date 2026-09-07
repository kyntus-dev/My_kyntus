import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  CatalogPlayerDto,
  FormationAttendanceMetricsStub,
  FormationDashboardStatsDto,
  FormationInitialDashboardStatsDto,
  FormationDocumentChecklistItemDto,
  FormationDocumentDefinitionDto,
  FormateurValidateOrgRequest,
  InitialTrainingPathDto,
  LearningQuizResultExportRowDto,
  LearningQuizStatsDto,
  MyQuizAttemptHistoryItemDto,
  MySelfServiceCatalogItemDto,
  MyAssignedTrainingSessionDto,
  PromoteSessionQuizRequest,
  TrainingAssignmentDto,
  TrainingAttendance,
  TrainingCatalogAudienceDto,
  TrainingCatalogItemDto,
  TrainingLessonDto,
  TrainingModuleDto,
  TrainingProgramDto,
  TrainingProgramDetailDto,
  ProgramBeneficiaryProgressDto,
  TrainingQuizAttemptDto,
  TrainingQuizDto,
  TrainingQuizForEmployeeDto,
  TrainingQuizTemplateDto,
  TrainingQuizTemplateListItemDto,
  TrainingResourceDto,
  TrainingSessionDto,
  TrainingSessionReportDto,
  UpsertTrainingQuizTemplateRequest,
  ReplaceCatalogStructureRequest,
  ReplaceCatalogStructureResponse,
} from '../models/formation-training.models';

const PREFIX = '/api/formations';

@Injectable({ providedIn: 'root' })
export class FormationTrainingService {
  private readonly http = inject(HttpClient);

  listSessions(): Promise<TrainingSessionDto[]> {
    return firstValueFrom(this.http.get<TrainingSessionDto[]>(`${PREFIX}/sessions`));
  }

  listMyAnimatedSessions(animatorUserId: string): Promise<TrainingSessionDto[]> {
    return firstValueFrom(
      this.http.get<TrainingSessionDto[]>(`${PREFIX}/sessions/my-animated`, {
        params: { animatorUserId },
      }),
    );
  }

  listMyAssignedSessions(): Promise<MyAssignedTrainingSessionDto[]> {
    return firstValueFrom(
      this.http.get<MyAssignedTrainingSessionDto[]>(`${PREFIX}/sessions/my-assigned`),
    );
  }

  listMyInitialPaths(): Promise<InitialTrainingPathDto[]> {
    return firstValueFrom(
      this.http.get<InitialTrainingPathDto[]>(`${PREFIX}/initial-paths/me`),
    );
  }

  listSessionAssignments(sessionId: string, animatorUserId: string): Promise<TrainingAssignmentDto[]> {
    return firstValueFrom(
      this.http.get<TrainingAssignmentDto[]>(`${PREFIX}/sessions/${sessionId}/assignments`, {
        params: { animatorUserId },
      }),
    ).catch((err) => {
      throw new Error(err?.error?.error || err?.message || 'Échec du chargement des bénéficiaires');
    });
  }

  markAttendance(
    sessionId: string,
    assignmentId: string,
    attendance: Extract<TrainingAttendance, 'Present' | 'Absent'>,
    animatorUserId: string,
  ): Promise<TrainingAssignmentDto> {
    return firstValueFrom(
      this.http.patch<TrainingAssignmentDto>(
        `${PREFIX}/sessions/${sessionId}/assignments/${assignmentId}/attendance`,
        { attendance, animatorUserId },
      ),
    ).catch((err) => {
      throw new Error(err?.error?.error || err?.message || 'Échec du pointage');
    });
  }

  createSession(body: Record<string, unknown>): Promise<TrainingSessionDto> {
    return firstValueFrom(this.http.post<TrainingSessionDto>(`${PREFIX}/sessions`, body)).catch((err) => {
      throw new Error(extractError(err, 'Échec de la création'));
    });
  }

  createProgram(body: Record<string, unknown>): Promise<TrainingProgramDto> {
    return firstValueFrom(this.http.post<TrainingProgramDto>(`${PREFIX}/programs`, body)).catch((err) => {
      throw new Error(extractError(err, 'Échec de la création du programme'));
    });
  }

  getProgram(id: string): Promise<TrainingProgramDetailDto> {
    return firstValueFrom(this.http.get<TrainingProgramDetailDto>(`${PREFIX}/programs/${id}`)).catch((err) => {
      throw new Error(extractError(err, 'Échec du chargement du programme'));
    });
  }

  getProgramBeneficiaryProgress(id: string): Promise<ProgramBeneficiaryProgressDto[]> {
    return firstValueFrom(
      this.http.get<ProgramBeneficiaryProgressDto[]>(`${PREFIX}/programs/${id}/beneficiary-progress`),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec du chargement de l’avancement'));
    });
  }

  assignEmployeesToProgram(
    programId: string,
    employees: { employeeId: string; employeeName: string }[],
  ): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`${PREFIX}/programs/${programId}/assign`, { employees }),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec de l’affectation'));
    });
  }

  assignEmployees(sessionId: string, employees: { employeeId: string; employeeName: string }[]): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`${PREFIX}/sessions/${sessionId}/assign`, { employees }),
    );
  }

  patchSessionStatus(sessionId: string, status: string): Promise<TrainingSessionDto> {
    return firstValueFrom(
      this.http.patch<TrainingSessionDto>(`${PREFIX}/sessions/${sessionId}`, { status }),
    );
  }

  uploadSessionReport(sessionId: string, file: File, uploadedByUserId: string): Promise<TrainingSessionReportDto> {
    const form = new FormData();
    form.append('file', file);
    form.append('uploadedByUserId', uploadedByUserId);
    return firstValueFrom(
      this.http.post<TrainingSessionReportDto>(`${PREFIX}/sessions/${sessionId}/report`, form),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec de l’upload du compte rendu'));
    });
  }

  getQuiz(sessionId: string): Promise<TrainingQuizDto | null> {
    return firstValueFrom(this.http.get<TrainingQuizDto>(`${PREFIX}/sessions/${sessionId}/quiz`)).catch(() => null);
  }

  upsertQuiz(sessionId: string, body: Record<string, unknown>): Promise<TrainingQuizDto> {
    return firstValueFrom(
      this.http.put<TrainingQuizDto>(`${PREFIX}/sessions/${sessionId}/quiz`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec enregistrement quiz'));
    });
  }

  publishQuiz(sessionId: string, actorUserId: string): Promise<TrainingQuizDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizDto>(`${PREFIX}/sessions/${sessionId}/quiz/publish`, { actorUserId }),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec publication quiz'));
    });
  }

  getQuizForEmployee(sessionId: string, employeeId: string): Promise<TrainingQuizForEmployeeDto> {
    return firstValueFrom(
      this.http.get<TrainingQuizForEmployeeDto>(`${PREFIX}/sessions/${sessionId}/quiz/for-employee`, {
        params: { employeeId },
      }),
    ).catch((err) => {
      throw new Error(extractError(err, 'Quiz indisponible'));
    });
  }

  getCatalogQuizForEmployee(catalogItemId: string): Promise<TrainingQuizForEmployeeDto> {
    return firstValueFrom(
      this.http.get<TrainingQuizForEmployeeDto>(`${PREFIX}/catalog/${catalogItemId}/quiz/for-employee`),
    ).catch((err) => {
      throw new Error(extractError(err, 'Quiz indisponible'));
    });
  }

  submitQuizAttempt(
    sessionId: string,
    body: {
      assignmentId: string;
      employeeId: string;
      answers: {
        questionId: string;
        selectedOptionIndex?: number | null;
        selectedOptionIndexes?: number[] | null;
        freeText?: string | null;
      }[];
    },
  ): Promise<TrainingQuizAttemptDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizAttemptDto>(`${PREFIX}/sessions/${sessionId}/quiz/attempts`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec soumission quiz'));
    });
  }

  submitCatalogQuizAttempt(
    catalogItemId: string,
    body: {
      assignmentId: string;
      employeeId: string;
      answers: {
        questionId: string;
        selectedOptionIndex?: number | null;
        selectedOptionIndexes?: number[] | null;
        freeText?: string | null;
      }[];
    },
  ): Promise<TrainingQuizAttemptDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizAttemptDto>(`${PREFIX}/catalog/${catalogItemId}/quiz/attempts`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec soumission quiz'));
    });
  }

  listQuizAttempts(sessionId: string, animatorUserId: string): Promise<TrainingQuizAttemptDto[]> {
    return firstValueFrom(
      this.http.get<TrainingQuizAttemptDto[]>(`${PREFIX}/sessions/${sessionId}/quiz/attempts`, {
        params: { animatorUserId },
      }),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec chargement tentatives'));
    });
  }

  gradeQuizAttempt(
    sessionId: string,
    attemptId: string,
    body: { animatorUserId: string; manualScore?: number; passed: boolean; animatorComment?: string },
  ): Promise<TrainingQuizAttemptDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizAttemptDto>(
        `${PREFIX}/sessions/${sessionId}/quiz/attempts/${attemptId}/grade`,
        body,
      ),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec notation'));
    });
  }

  gradeFreeTextAnswer(
    sessionId: string,
    attemptId: string,
    body: { animatorUserId: string; questionId: string; isCorrect: boolean },
  ): Promise<TrainingQuizAttemptDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizAttemptDto>(
        `${PREFIX}/sessions/${sessionId}/quiz/attempts/${attemptId}/free-text-grade`,
        body,
      ),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec notation réponse libre'));
    });
  }

  validateQuiz(sessionId: string, actorUserId: string): Promise<TrainingQuizDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizDto>(`${PREFIX}/sessions/${sessionId}/quiz/validate`, { actorUserId }),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec validation'));
    });
  }

  rejectQuiz(sessionId: string, actorUserId: string, reason: string): Promise<TrainingQuizDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizDto>(`${PREFIX}/sessions/${sessionId}/quiz/reject`, {
        actorUserId,
        reason,
      }),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec rejet'));
    });
  }

  getDashboardStats(employeeIds?: string[]): Promise<FormationDashboardStatsDto> {
    const params: Record<string, string | string[]> = {};
    if (employeeIds?.length) {
      params['employeeIds'] = employeeIds;
    }
    return firstValueFrom(
      this.http.get<FormationDashboardStatsDto>(`${PREFIX}/dashboard/stats`, { params }),
    );
  }

  getInitialDashboardStats(employeeIds?: string[]): Promise<FormationInitialDashboardStatsDto> {
    const params: Record<string, string | string[]> = {};
    if (employeeIds?.length) {
      params['employeeIds'] = employeeIds;
    }
    return firstValueFrom(
      this.http.get<FormationInitialDashboardStatsDto>(`${PREFIX}/dashboard/stats-initial`, { params }),
    );
  }

  createInitialPath(body: {
    employeeId: string;
    employeeName: string;
    dateDebut: string;
    dateFinPrevue: string;
  }): Promise<InitialTrainingPathDto> {
    return firstValueFrom(this.http.post<InitialTrainingPathDto>(`${PREFIX}/initial-paths`, body));
  }

  listFormateurInitial(): Promise<InitialTrainingPathDto[]> {
    return firstValueFrom(this.http.get<InitialTrainingPathDto[]>(`${PREFIX}/initial-paths/formateur`));
  }

  listRhPendingInitial(): Promise<InitialTrainingPathDto[]> {
    return firstValueFrom(this.http.get<InitialTrainingPathDto[]>(`${PREFIX}/initial-paths/rh-pending`));
  }

  listInitialOverview(): Promise<InitialTrainingPathDto[]> {
    return firstValueFrom(this.http.get<InitialTrainingPathDto[]>(`${PREFIX}/initial-paths/overview`));
  }

  listInitialByEmployee(employeeId: string): Promise<InitialTrainingPathDto[]> {
    return firstValueFrom(
      this.http.get<InitialTrainingPathDto[]>(`${PREFIX}/initial-paths/by-employee/${employeeId}`),
    );
  }

  getInitialPath(pathId: string): Promise<InitialTrainingPathDto> {
    return firstValueFrom(
      this.http.get<InitialTrainingPathDto>(`${PREFIX}/initial-paths/${pathId}`),
    ).catch((err) => {
      throw new Error(extractError(err, 'Parcours introuvable'));
    });
  }

  recordQuiz(pathId: string, body: { quizScore: number; quizPassed: boolean; formateurComment?: string; recordedBy: string; title?: string }): Promise<InitialTrainingPathDto> {
    return firstValueFrom(this.http.post<InitialTrainingPathDto>(`${PREFIX}/initial-paths/${pathId}/quiz-result`, body));
  }

  addQuizResult(pathId: string, body: { title: string; score: number; recordedBy: string }): Promise<InitialTrainingPathDto> {
    return firstValueFrom(
      this.http.post<InitialTrainingPathDto>(`${PREFIX}/initial-paths/${pathId}/quiz-results`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec enregistrement des notes'));
    });
  }

  deleteQuizResult(pathId: string, resultId: string): Promise<InitialTrainingPathDto> {
    return firstValueFrom(
      this.http.delete<InitialTrainingPathDto>(`${PREFIX}/initial-paths/${pathId}/quiz-results/${resultId}`),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec suppression de la note'));
    });
  }

  formateurValidate(pathId: string, body: FormateurValidateOrgRequest): Promise<InitialTrainingPathDto> {
    return firstValueFrom(
      this.http.post<InitialTrainingPathDto>(`${PREFIX}/initial-paths/${pathId}/formateur-validate`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec validation formateur'));
    });
  }

  formateurReject(pathId: string, body: { rejectedBy: string; reason: string }): Promise<InitialTrainingPathDto> {
    return firstValueFrom(
      this.http.post<InitialTrainingPathDto>(`${PREFIX}/initial-paths/${pathId}/formateur-reject`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec du rejet'));
    });
  }

  extendInitial(pathId: string, dateFinPrevue: string): Promise<InitialTrainingPathDto> {
    return firstValueFrom(
      this.http.post<InitialTrainingPathDto>(`${PREFIX}/initial-paths/${pathId}/extend`, { dateFinPrevue }),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec de la prolongation'));
    });
  }

  rhValidate(pathId: string): Promise<InitialTrainingPathDto> {
    return firstValueFrom(
      this.http.post<InitialTrainingPathDto>(`${PREFIX}/initial-paths/${pathId}/rh-validate`, {}),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec validation RH'));
    });
  }

  rhReject(pathId: string, body: { rejectedBy: string; reason: string }): Promise<InitialTrainingPathDto> {
    return firstValueFrom(this.http.post<InitialTrainingPathDto>(`${PREFIX}/initial-paths/${pathId}/rh-reject`, body));
  }

  listDocumentDefinitions(): Promise<FormationDocumentDefinitionDto[]> {
    return firstValueFrom(this.http.get<FormationDocumentDefinitionDto[]>(`${PREFIX}/document-definitions`));
  }

  createDocumentDefinition(body: {
    title: string;
    sortOrder: number;
    isActive: boolean;
  }): Promise<FormationDocumentDefinitionDto> {
    return firstValueFrom(this.http.post<FormationDocumentDefinitionDto>(`${PREFIX}/document-definitions`, body));
  }

  updateDocumentDefinition(
    id: string,
    body: { title: string; sortOrder: number; isActive: boolean },
  ): Promise<FormationDocumentDefinitionDto> {
    return firstValueFrom(this.http.put<FormationDocumentDefinitionDto>(`${PREFIX}/document-definitions/${id}`, body));
  }

  deleteDocumentDefinition(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${PREFIX}/document-definitions/${id}`));
  }

  getPathChecklist(pathId: string): Promise<FormationDocumentChecklistItemDto[]> {
    return firstValueFrom(
      this.http.get<FormationDocumentChecklistItemDto[]>(`${PREFIX}/initial-paths/${pathId}/checklist`),
    );
  }

  getEmployeeChecklist(employeeId: string): Promise<FormationDocumentChecklistItemDto[]> {
    return firstValueFrom(
      this.http.get<FormationDocumentChecklistItemDto[]>(
        `${PREFIX}/initial-paths/by-employee/${employeeId}/checklist`,
      ),
    );
  }

  updateChecklistItem(
    pathId: string,
    itemId: string,
    body: { isReceived: boolean; receivedBy?: string; note?: string },
  ): Promise<FormationDocumentChecklistItemDto> {
    return firstValueFrom(
      this.http.patch<FormationDocumentChecklistItemDto>(
        `${PREFIX}/initial-paths/${pathId}/checklist/${itemId}`,
        body,
      ),
    );
  }

  /** Préparatif front — à remplacer par l’API externe absentéisme / retard. */
  getAttendanceMetricsStub(_employeeId: string): FormationAttendanceMetricsStub {
    return { absenteeismRate: null, latenessRate: null };
  }

  // ─── Catalogue e-learning ───────────────────────────────

  listCatalog(includeArchived = false, category?: string): Promise<TrainingCatalogItemDto[]> {
    const params: Record<string, string> = { includeArchived: String(includeArchived) };
    if (category) params['category'] = category;
    return firstValueFrom(this.http.get<TrainingCatalogItemDto[]>(`${PREFIX}/catalog`, { params }));
  }

  getCatalogItem(id: string): Promise<TrainingCatalogItemDto> {
    return firstValueFrom(this.http.get<TrainingCatalogItemDto>(`${PREFIX}/catalog/${id}`));
  }

  createCatalogItem(body: Record<string, unknown>): Promise<TrainingCatalogItemDto> {
    return firstValueFrom(this.http.post<TrainingCatalogItemDto>(`${PREFIX}/catalog`, body)).catch((err) => {
      throw new Error(extractError(err, 'Échec création catalogue'));
    });
  }

  updateCatalogItem(id: string, body: Record<string, unknown>): Promise<TrainingCatalogItemDto> {
    return firstValueFrom(this.http.put<TrainingCatalogItemDto>(`${PREFIX}/catalog/${id}`, body)).catch((err) => {
      throw new Error(extractError(err, 'Échec mise à jour catalogue'));
    });
  }

  publishCatalogItem(id: string): Promise<TrainingCatalogItemDto> {
    return firstValueFrom(this.http.post<TrainingCatalogItemDto>(`${PREFIX}/catalog/${id}/publish`, {})).catch((err) => {
      throw new Error(extractError(err, 'Échec publication'));
    });
  }

  archiveCatalogItem(id: string): Promise<TrainingCatalogItemDto> {
    return firstValueFrom(this.http.post<TrainingCatalogItemDto>(`${PREFIX}/catalog/${id}/archive`, {})).catch((err) => {
      throw new Error(extractError(err, 'Échec archivage'));
    });
  }

  deleteCatalogItem(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${PREFIX}/catalog/${id}`)).catch((err) => {
      throw new Error(extractError(err, 'Échec suppression'));
    });
  }

  upsertCatalogAudience(id: string, body: Record<string, unknown>): Promise<TrainingCatalogAudienceDto> {
    return firstValueFrom(
      this.http.put<TrainingCatalogAudienceDto>(`${PREFIX}/catalog/${id}/audience`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec audience'));
    });
  }

  createCatalogModule(catalogId: string, body: Record<string, unknown>): Promise<TrainingModuleDto> {
    return firstValueFrom(
      this.http.post<TrainingModuleDto>(`${PREFIX}/catalog/${catalogId}/modules`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec module'));
    });
  }

  updateCatalogModule(catalogId: string, moduleId: string, body: Record<string, unknown>): Promise<TrainingModuleDto> {
    return firstValueFrom(
      this.http.put<TrainingModuleDto>(`${PREFIX}/catalog/${catalogId}/modules/${moduleId}`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec module'));
    });
  }

  deleteCatalogModule(catalogId: string, moduleId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${PREFIX}/catalog/${catalogId}/modules/${moduleId}`));
  }

  createCatalogLesson(moduleId: string, body: Record<string, unknown>): Promise<TrainingLessonDto> {
    return firstValueFrom(
      this.http.post<TrainingLessonDto>(`${PREFIX}/catalog/modules/${moduleId}/lessons`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec leçon'));
    });
  }

  updateCatalogLesson(moduleId: string, lessonId: string, body: Record<string, unknown>): Promise<TrainingLessonDto> {
    return firstValueFrom(
      this.http.put<TrainingLessonDto>(`${PREFIX}/catalog/modules/${moduleId}/lessons/${lessonId}`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec leçon'));
    });
  }

  deleteCatalogLesson(moduleId: string, lessonId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${PREFIX}/catalog/modules/${moduleId}/lessons/${lessonId}`));
  }

  createCatalogResource(lessonId: string, body: Record<string, unknown>): Promise<TrainingResourceDto> {
    return firstValueFrom(
      this.http.post<TrainingResourceDto>(`${PREFIX}/catalog/lessons/${lessonId}/resources`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec ressource'));
    });
  }

  updateCatalogResource(
    lessonId: string,
    resourceId: string,
    body: Record<string, unknown>,
  ): Promise<TrainingResourceDto> {
    return firstValueFrom(
      this.http.put<TrainingResourceDto>(
        `${PREFIX}/catalog/lessons/${lessonId}/resources/${resourceId}`,
        body,
      ),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec ressource'));
    });
  }

  uploadCatalogResource(
    lessonId: string,
    file: File,
    title?: string,
    type?: string,
    sortOrder?: number,
  ): Promise<TrainingResourceDto> {
    const fd = new FormData();
    fd.append('file', file);
    if (title) fd.append('title', title);
    if (type) fd.append('type', type);
    if (sortOrder != null) fd.append('sortOrder', String(sortOrder));
    return firstValueFrom(
      this.http.post<TrainingResourceDto>(`${PREFIX}/catalog/lessons/${lessonId}/resources/upload`, fd),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec upload'));
    });
  }

  replaceCatalogStructure(
    catalogId: string,
    body: ReplaceCatalogStructureRequest,
  ): Promise<ReplaceCatalogStructureResponse> {
    return firstValueFrom(
      this.http.put<ReplaceCatalogStructureResponse>(`${PREFIX}/catalog/${catalogId}/structure`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec enregistrement structure'));
    });
  }

  issueResourceAccess(resourceId: string): Promise<{ url: string; expiresAt: string }> {
    return firstValueFrom(
      this.http.post<{ url: string; expiresAt: string }>(`${PREFIX}/catalog/resources/${resourceId}/access`, {}),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec accès média'));
    });
  }

  /** Fichier ressource authentifié (fallback si pas de jeton). */
  downloadCatalogResourceBlob(resourceId: string): Promise<Blob> {
    return firstValueFrom(
      this.http.get(`${PREFIX}/catalog/resources/file/${resourceId}`, { responseType: 'blob' }),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec téléchargement ressource'));
    });
  }

  deleteCatalogResource(lessonId: string, resourceId: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`${PREFIX}/catalog/lessons/${lessonId}/resources/${resourceId}`),
    );
  }

  linkSessionCatalog(
    sessionId: string,
    body: { catalogItemId?: string | null; learningGateMode?: string | null; assignAudience?: boolean },
  ): Promise<TrainingSessionDto> {
    return firstValueFrom(
      this.http.put<TrainingSessionDto>(`${PREFIX}/sessions/${sessionId}/catalog-link`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec liaison catalogue'));
    });
  }

  listMySelfServiceCatalog(): Promise<MySelfServiceCatalogItemDto[]> {
    return firstValueFrom(
      this.http.get<MySelfServiceCatalogItemDto[]>(`${PREFIX}/catalog/me/self-service`),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec e-learning libre accès'));
    });
  }

  getCatalogPlayer(sessionId: string, employeeId?: string): Promise<CatalogPlayerDto> {
    const params: Record<string, string> = {};
    if (employeeId) params['employeeId'] = employeeId;
    return firstValueFrom(
      this.http.get<CatalogPlayerDto>(`${PREFIX}/catalog/sessions/${sessionId}/player`, { params }),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec lecteur'));
    });
  }

  getCatalogPlayerByCatalog(catalogItemId: string): Promise<CatalogPlayerDto> {
    return firstValueFrom(
      this.http.get<CatalogPlayerDto>(`${PREFIX}/catalog/${catalogItemId}/player`),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec lecteur'));
    });
  }

  completeLesson(
    sessionId: string,
    lessonId: string,
    body: { employeeId?: string; lastResourceId?: string | null },
  ): Promise<TrainingLessonDto> {
    return firstValueFrom(
      this.http.post<TrainingLessonDto>(
        `${PREFIX}/catalog/sessions/${sessionId}/lessons/${lessonId}/complete`,
        body,
      ),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec progression'));
    });
  }

  completeLessonByCatalog(
    catalogItemId: string,
    lessonId: string,
    body: { employeeId?: string; lastResourceId?: string | null },
  ): Promise<TrainingLessonDto> {
    return firstValueFrom(
      this.http.post<TrainingLessonDto>(
        `${PREFIX}/catalog/${catalogItemId}/lessons/${lessonId}/complete`,
        body,
      ),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec progression'));
    });
  }

  getLearningStats(catalogItemId?: string): Promise<LearningQuizStatsDto> {
    const params: Record<string, string> = {};
    if (catalogItemId) params['catalogItemId'] = catalogItemId;
    return firstValueFrom(this.http.get<LearningQuizStatsDto>(`${PREFIX}/catalog/stats`, { params }));
  }

  exportLearningResults(sessionId?: string, catalogItemId?: string): Promise<LearningQuizResultExportRowDto[]> {
    const params: Record<string, string> = {};
    if (sessionId) params['sessionId'] = sessionId;
    if (catalogItemId) params['catalogItemId'] = catalogItemId;
    return firstValueFrom(
      this.http.get<LearningQuizResultExportRowDto[]>(`${PREFIX}/catalog/results/export`, { params }),
    );
  }

  uploadQuizQuestionImage(
    sessionId: string,
    questionId: string,
    file: File,
    animatorUserId: string,
  ): Promise<{ id: string; imageUrl?: string | null }> {
    const form = new FormData();
    form.append('file', file);
    form.append('animatorUserId', animatorUserId);
    return firstValueFrom(
      this.http.post<{ id: string; imageUrl?: string | null }>(
        `${PREFIX}/sessions/${sessionId}/quiz/questions/${questionId}/image`,
        form,
      ),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec upload média question'));
    });
  }

  uploadQuizTemplateQuestionMedia(
    templateId: string,
    questionId: string,
    file: File,
  ): Promise<{ id: string; imageUrl?: string | null; mediaKind?: string | null }> {
    const form = new FormData();
    form.append('file', file);
    return firstValueFrom(
      this.http.post<{ id: string; imageUrl?: string | null; mediaKind?: string | null }>(
        `${PREFIX}/quiz-templates/${templateId}/questions/${questionId}/media`,
        form,
      ),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec upload média question'));
    });
  }

  listMyQuizAttempts(sessionId: string, employeeId: string): Promise<TrainingQuizAttemptDto[]> {
    return firstValueFrom(
      this.http.get<TrainingQuizAttemptDto[]>(`${PREFIX}/sessions/${sessionId}/quiz/my-attempts`, {
        params: { employeeId },
      }),
    );
  }

  listMyCatalogQuizAttempts(catalogItemId: string): Promise<TrainingQuizAttemptDto[]> {
    return firstValueFrom(
      this.http.get<TrainingQuizAttemptDto[]>(`${PREFIX}/catalog/${catalogItemId}/quiz/my-attempts`),
    );
  }

  listMyQuizHistory(employeeId: string): Promise<MyQuizAttemptHistoryItemDto[]> {
    return firstValueFrom(
      this.http.get<MyQuizAttemptHistoryItemDto[]>(`${PREFIX}/employees/me/quiz-attempts`, {
        params: { employeeId },
      }),
    );
  }

  // ─── Bibliothèque quiz (modèles) ─────────────────────────

  listQuizTemplates(includeArchived = false): Promise<TrainingQuizTemplateListItemDto[]> {
    return firstValueFrom(
      this.http.get<TrainingQuizTemplateListItemDto[]>(`${PREFIX}/quiz-templates`, {
        params: { includeArchived: String(includeArchived) },
      }),
    );
  }

  getQuizTemplate(id: string): Promise<TrainingQuizTemplateDto> {
    return firstValueFrom(this.http.get<TrainingQuizTemplateDto>(`${PREFIX}/quiz-templates/${id}`));
  }

  createQuizTemplate(body: UpsertTrainingQuizTemplateRequest): Promise<TrainingQuizTemplateDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizTemplateDto>(`${PREFIX}/quiz-templates`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec création modèle quiz'));
    });
  }

  updateQuizTemplate(id: string, body: UpsertTrainingQuizTemplateRequest): Promise<TrainingQuizTemplateDto> {
    return firstValueFrom(
      this.http.put<TrainingQuizTemplateDto>(`${PREFIX}/quiz-templates/${id}`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec mise à jour modèle quiz'));
    });
  }

  publishQuizTemplate(id: string): Promise<TrainingQuizTemplateDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizTemplateDto>(`${PREFIX}/quiz-templates/${id}/publish`, {}),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec publication modèle'));
    });
  }

  archiveQuizTemplate(id: string): Promise<TrainingQuizTemplateDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizTemplateDto>(`${PREFIX}/quiz-templates/${id}/archive`, {}),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec archivage modèle'));
    });
  }

  duplicateQuizTemplate(id: string): Promise<TrainingQuizTemplateDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizTemplateDto>(`${PREFIX}/quiz-templates/${id}/duplicate`, {}),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec duplication modèle'));
    });
  }

  instantiateQuizTemplate(
    id: string,
    body: { sessionId: string; actorUserId?: string },
  ): Promise<TrainingQuizDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizDto>(`${PREFIX}/quiz-templates/${id}/instantiate`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec instantiation modèle'));
    });
  }

  promoteSessionQuiz(body: PromoteSessionQuizRequest): Promise<TrainingQuizTemplateDto> {
    return firstValueFrom(
      this.http.post<TrainingQuizTemplateDto>(`${PREFIX}/quiz-templates/promote`, body),
    ).catch((err) => {
      throw new Error(extractError(err, 'Échec promotion en modèle'));
    });
  }
}

function extractError(err: any, fallback: string): string {
  return (
    err?.error?.message ||
    err?.error?.error ||
    err?.error?.title ||
    (typeof err?.error === 'string' ? err.error : null) ||
    err?.message ||
    fallback
  );
}
