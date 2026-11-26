import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ArrowLeft, BookOpen, Plus } from 'lucide';
import { FormationTrainingService } from '../../../core/services/formation-training.service';
import type {
  LearningQuizResultExportRowDto,
  LearningQuizStatsDto,
  TrainingCatalogItemDto,
  TrainingLessonDto,
  TrainingModuleDto,
  TrainingQuizTemplateDto,
  TrainingQuizTemplateListItemDto,
  TrainingResourceDto,
} from '../../../core/models/formation-training.models';
import { KyntusPageHeaderComponent } from '../../../shared/components/ui/kyntus-page-header.component';
import { LucideIconComponent } from '../../../shared/lucide-icon.component';
import { UserService, type RoleOption } from '../../users/services/user.service';
import { downloadLearningResultsExcel } from '../../../core/lib/learning-results-excel.util';
import { FormationQuizQuestionsEditorComponent } from '../shared/formation-quiz-questions-editor.component';
import {
  KyntusAudiencePickerComponent,
  type AudiencePickerSelection,
} from '../shared/kyntus-audience-picker.component';
import {
  buildQuizQuestionPayload,
  emptyQuizDraftQuestion,
  type QuizDraftQuestion,
} from '../shared/formation-quiz-draft.types';
import { FormationCatalogOutlineComponent, modulesToDraft } from './formation-catalog-outline.component';
import { FormationResourceViewerComponent } from './formation-resource-viewer.component';
import { BodyPortalDirective } from '../../../shared/directives/body-portal.directive';
import type { DraftModule } from './formation-catalog-draft.types';
import { groupResourcesByPart } from '../../../core/lib/formation-parts.util';
import { KyntusConfirmService } from '../../../shared/components/kyntus-confirm/kyntus-confirm.service';
import {
  buildCatalogStructureRequest,
  countDraftLessons,
  uploadCatalogPendingFiles,
} from './formation-catalog-structure.util';
import { PlatformAccessService } from '../../../core/platform-access/platform-access.service';

type MainTab = 'contents' | 'templates' | 'stats';
type WizardStep = 1 | 2 | 3;

type ContentFormDraft = {
  title: string;
  description: string;
  category: string;
  defaultGateMode: string;
  selfServiceEnabled: boolean;
  dueMode: string;
  dueDate: string;
  dueInDays: number | null;
  defaultQuizTemplateId: string;
};

type ContentDraftSnapshot = {
  selectedId: string | null;
  draft: ContentFormDraft;
  selectedRoles: string[];
  selectedStructureKeys: string[];
  selectedUserIds: string[];
  wizardStep?: WizardStep;
};

const CONTENT_DRAFT_STASH_KEY = 'fcat-content-draft-v1';

@Component({
  selector: 'app-formation-catalog-admin',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    KyntusPageHeaderComponent,
    LucideIconComponent,
    FormationQuizQuestionsEditorComponent,
    KyntusAudiencePickerComponent,
    FormationCatalogOutlineComponent,
    FormationResourceViewerComponent,
    BodyPortalDirective,
  ],
  templateUrl: './formation-catalog-admin.component.html',
  styleUrls: ['./formation-catalog-admin.component.css'],
})
export class FormationCatalogAdminComponent implements OnInit {
  /** v72 — Droits « Accès par rôle / par compte » : une action n'apparaît que si le serveur l'accepte. */
  readonly droits = inject(PlatformAccessService);
  /** v72 — Éditer, publier, archiver, dupliquer : « Formation continue › Modifier ». */
  get peutModifierFormation(): boolean {
    return this.droits.autorise('formation.planification', 'Act');
  }
  readonly icons = { add: Plus, book: BookOpen, back: ArrowLeft };
  private readonly api = inject(FormationTrainingService);
  private readonly usersApi = inject(UserService);
  private readonly confirmService = inject(KyntusConfirmService);

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly items = signal<TrainingCatalogItemDto[]>([]);
  readonly selected = signal<TrainingCatalogItemDto | null>(null);
  readonly stats = signal<LearningQuizStatsDto | null>(null);
  readonly exportRows = signal<LearningQuizResultExportRowDto[]>([]);
  readonly tab = signal<MainTab>('contents');
  readonly contentEditing = signal(false);
  readonly wizardStep = signal<WizardStep>(1);
  readonly structureDirty = signal(false);
  /** Brouillon fiche figé le temps de créer un quiz modèle. */
  readonly contentDraftStash = signal<ContentDraftSnapshot | null>(null);
  readonly previewOpen = signal(false);
  readonly previewLesson = signal<TrainingLessonDto | null>(null);
  readonly previewPartIndex = signal(0);
  readonly previewResource = signal<TrainingResourceDto | null>(null);

  /** Arbre local (modules / leçons / parties) — enregistré uniquement au clic final. */
  draftModules: DraftModule[] = [];
  outlineSeed: TrainingModuleDto[] | null = null;

  readonly templates = signal<TrainingQuizTemplateListItemDto[]>([]);
  readonly selectedTemplate = signal<TrainingQuizTemplateDto | null>(null);
  readonly templateEditing = signal(false);
  includeArchived = false;
  includeArchivedTemplates = false;

  roleOptions: RoleOption[] = [];

  selectedRoles: string[] = [];
  selectedStructureKeys: string[] = [];
  selectedUserIds: string[] = [];

  filterSessionId = '';
  filterCatalogItemId = '';

  draft: ContentFormDraft = {
    title: '',
    description: '',
    category: '',
    defaultGateMode: 'Content',
    selfServiceEnabled: true,
    dueMode: 'None',
    dueDate: '',
    dueInDays: null,
    defaultQuizTemplateId: '',
  };

  templateDraft = {
    title: '',
    description: '',
    category: '',
    passThreshold: 70,
    allowMultipleAttempts: false,
  };
  templateQuestions: QuizDraftQuestion[] = [];

  ngOnInit(): void {
    void this.droits.ensureLoaded();
    this.hydrateContentDraftStash();
    void this.reload();
    void this.loadAudienceSources();
  }

  hasPendingQuizReturn(): boolean {
    return !!this.contentDraftStash();
  }

  setTab(tab: MainTab): void {
    this.error.set(null);
    this.message.set(null);
    if (tab === 'contents' && this.contentDraftStash()) {
      void this.restoreContentDraftAfterQuiz(null);
      return;
    }
    this.tab.set(tab);
    if (tab === 'contents') {
      this.contentEditing.set(false);
      void this.reload();
    } else if (tab === 'templates') {
      this.templateEditing.set(false);
      void this.reloadTemplates();
      if (!this.items().length) void this.reload();
    } else {
      void this.loadStats();
    }
  }

  async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.items.set(await this.api.listCatalog(this.includeArchived));
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Chargement impossible');
    } finally {
      this.loading.set(false);
    }
  }

  async reloadTemplates(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.templates.set(await this.api.listQuizTemplates(this.includeArchivedTemplates));
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Chargement modèles impossible');
    } finally {
      this.loading.set(false);
    }
  }

  async openEdit(id: string, preferStep?: WizardStep): Promise<void> {
    this.busy.set(true);
    try {
      await this.ensurePublishedTemplatesLoaded();
      const item = await this.api.getCatalogItem(id);
      this.selected.set(item);
      this.draft = {
        title: item.title,
        description: item.description,
        category: item.category,
        defaultGateMode: String(item.defaultGateMode ?? 'Content'),
        selfServiceEnabled: !!item.selfServiceEnabled,
        dueMode: this.dueModeToString(item.dueMode),
        dueDate: item.dueDate ? item.dueDate.slice(0, 10) : '',
        dueInDays: item.dueInDays ?? null,
        defaultQuizTemplateId: item.defaultQuizTemplateId ?? '',
      };
      this.selectedRoles = [...(item.audience?.roles ?? [])];
      this.selectedStructureKeys = [...(item.audience?.structureKeys ?? [])];
      this.selectedUserIds = [...(item.audience?.userIds ?? [])];
      this.outlineSeed = item.modules ?? [];
      this.draftModules = modulesToDraft(item.modules);
      this.structureDirty.set(false);
      this.wizardStep.set(preferStep ?? 1);
      this.contentEditing.set(true);
      this.tab.set('contents');
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Chargement détail impossible');
    } finally {
      this.busy.set(false);
    }
  }

  onDraftModulesChange(next: DraftModule[]): void {
    this.draftModules = next;
    this.structureDirty.set(true);
  }

  goToStep(step: WizardStep): void {
    this.error.set(null);
    if (step === 2 && !this.draft.title.trim()) {
      this.error.set('Indiquez un titre avant de passer au contenu.');
      return;
    }
    this.wizardStep.set(step);
  }

  lessonCount(): number {
    return countDraftLessons(this.draftModules);
  }

  async backToContentsList(): Promise<void> {
    const hasDraft =
      this.structureDirty() || !!this.draft.title.trim() || this.draftModules.length > 0;
    if (hasDraft) {
      const ok = await this.confirmService.confirm({
        title: 'Quitter le formulaire',
        message: 'Les modifications non enregistrées seront perdues. Continuer ?',
        confirmLabel: 'Quitter',
        cancelLabel: 'Rester',
        variant: 'danger',
      });
      if (!ok) return;
    }
    this.contentEditing.set(false);
    this.selected.set(null);
    this.draftModules = [];
    this.outlineSeed = null;
    void this.reload();
  }

  async saveItem(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      if (!this.draft.title.trim()) {
        this.error.set('Le titre est obligatoire.');
        this.wizardStep.set(1);
        return;
      }

      const body: Record<string, unknown> = {
        title: this.draft.title.trim(),
        description: this.draft.description.trim(),
        category: this.draft.category.trim(),
        defaultGateMode: this.toGateMode(this.draft.defaultGateMode),
        audienceMatchMode: 0,
        selfServiceEnabled: this.draft.selfServiceEnabled,
        dueMode: this.toDueMode(this.draft.dueMode),
        dueDate:
          this.draft.dueMode === 'Absolute' && this.draft.dueDate
            ? new Date(this.draft.dueDate).toISOString()
            : null,
        dueInDays: this.draft.dueMode === 'RelativeDays' ? this.draft.dueInDays : null,
        defaultQuizTemplateId: this.draft.defaultQuizTemplateId || null,
      };

      let saved: TrainingCatalogItemDto;
      if (!this.selected()?.id) {
        this.error.set('La création de contenu se fait depuis Planifier une formation continue.');
        return;
      }
      saved = await this.api.updateCatalogItem(this.selected()!.id, body);

      await this.api.upsertCatalogAudience(saved.id, {
        matchMode: 0,
        roles: [...this.selectedRoles],
        structureKeys: [...this.selectedStructureKeys],
        userIds: [...this.selectedUserIds],
      });

      const structureBody = buildCatalogStructureRequest(this.draftModules);
      const structureResult = await this.api.replaceCatalogStructure(saved.id, structureBody);
      await uploadCatalogPendingFiles(this.draftModules, structureResult, (lessonId, file, title, type, sortOrder) =>
        this.api.uploadCatalogResource(lessonId, file, title, type, sortOrder),
      );

      this.message.set('Formation catalogue enregistrée.');
      this.structureDirty.set(false);
      await this.openEdit(saved.id, 3);
      await this.reload();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Enregistrement impossible');
    } finally {
      this.busy.set(false);
    }
  }

  async publish(): Promise<void> {
    const id = this.selected()?.id;
    if (!id) return;
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      // Persister le brouillon avant publication (évite un contenu publié sans libre accès / audience à jour).
      const enabledLibreAcces = this.draft.selfServiceEnabled;
      if (!enabledLibreAcces) {
        this.draft.selfServiceEnabled = true;
      }
      await this.persistDraftAndAudience(id);
      await this.api.publishCatalogItem(id);
      this.message.set(
        enabledLibreAcces
          ? 'Formation publiée. Elle apparaît dans Mes formations pour les personnes du périmètre (bloc Libre accès).'
          : 'Formation publiée. Le libre accès a été activé automatiquement pour qu’elle apparaisse dans Mes formations aux personnes du périmètre.',
      );
      await this.openEdit(id);
      await this.reload();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Publication impossible');
    } finally {
      this.busy.set(false);
    }
  }

  /** Enregistre métadonnées + audience sans basculer l’UI (utilisé avant publish). */
  private async persistDraftAndAudience(id: string): Promise<void> {
    const body: Record<string, unknown> = {
      title: this.draft.title.trim(),
      description: this.draft.description.trim(),
      category: this.draft.category.trim(),
      defaultGateMode: this.toGateMode(this.draft.defaultGateMode),
      audienceMatchMode: 0, // MatchAny
      selfServiceEnabled: this.draft.selfServiceEnabled,
      dueMode: this.toDueMode(this.draft.dueMode),
      dueDate:
        this.draft.dueMode === 'Absolute' && this.draft.dueDate
          ? new Date(this.draft.dueDate).toISOString()
          : null,
      dueInDays: this.draft.dueMode === 'RelativeDays' ? this.draft.dueInDays : null,
      defaultQuizTemplateId: this.draft.defaultQuizTemplateId || null,
    };
    await this.api.updateCatalogItem(id, body);
    await this.api.upsertCatalogAudience(id, {
      matchMode: 0,
      roles: [...this.selectedRoles],
      structureKeys: [...this.selectedStructureKeys],
      userIds: [...this.selectedUserIds],
    });
  }

  async archive(): Promise<void> {
    const id = this.selected()?.id;
    if (!id) return;
    this.busy.set(true);
    try {
      await this.api.archiveCatalogItem(id);
      this.message.set('Formation archivée.');
      this.contentEditing.set(false);
      await this.reload();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Archivage impossible');
    } finally {
      this.busy.set(false);
    }
  }

  openCreateTemplate(): void {
    this.selectedTemplate.set(null);
    this.templateDraft = {
      title: '',
      description: '',
      category: '',
      passThreshold: 70,
      allowMultipleAttempts: false,
    };
    this.templateQuestions = [emptyQuizDraftQuestion()];
    this.templateEditing.set(true);
    this.tab.set('templates');
  }

  /** Depuis la fiche formation → créer un quiz sans perdre le brouillon. */
  createQuizFromContentForm(): void {
    this.stashContentDraftForQuizCreate();
    this.openCreateTemplate();
    this.message.set(null);
    this.error.set(null);
  }

  async openEditTemplate(id: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const tpl = await this.api.getQuizTemplate(id);
      this.selectedTemplate.set(tpl);
      this.templateDraft = {
        title: tpl.title,
        description: tpl.description,
        category: tpl.category,
        passThreshold: Number(tpl.passThreshold) || 70,
        allowMultipleAttempts: !!tpl.allowMultipleAttempts,
      };
      this.templateQuestions = (tpl.questions ?? [])
        .slice()
        .sort((a, b) => a.sortOrder - b.sortOrder)
        .map((q) => {
          const indexes =
            q.correctOptionIndexes?.length
              ? [...q.correctOptionIndexes]
              : [q.correctOptionIndex ?? 0];
          return {
            id: q.id,
            type: q.type === 1 || q.type === 'FreeText' ? ('FreeText' as const) : ('Qcm' as const),
            prompt: q.prompt,
            options: q.options?.length ? [...q.options] : ['', ''],
            correctOptionIndex: indexes[0] ?? 0,
            correctOptionIndexes: indexes,
            allowMultiple: !!q.allowMultiple,
            points: q.points || 1,
            imageUrl: q.imageUrl || '',
            explanation: q.explanation || '',
            mediaKind:
              q.mediaKind === 'video' || q.mediaKind === 'image'
                ? q.mediaKind
                : null,
          };
        });
      if (!this.templateQuestions.length) this.templateQuestions = [emptyQuizDraftQuestion()];
      this.templateEditing.set(true);
      this.tab.set('templates');
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Chargement modèle impossible');
    } finally {
      this.busy.set(false);
    }
  }

  backToTemplatesList(): void {
    if (this.contentDraftStash()) {
      void this.restoreContentDraftAfterQuiz(null);
      return;
    }
    this.templateEditing.set(false);
    this.selectedTemplate.set(null);
    void this.reloadTemplates();
  }

  async saveTemplate(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      const questions = buildQuizQuestionPayload(this.templateQuestions);
      if (!this.templateDraft.title.trim()) throw new Error('Le titre est obligatoire.');
      if (!questions.length) throw new Error('Ajoutez au moins une question valide.');
      const body = {
        title: this.templateDraft.title.trim(),
        description: this.templateDraft.description.trim(),
        category: this.templateDraft.category.trim(),
        passThreshold: Math.min(100, Math.max(1, Number(this.templateDraft.passThreshold) || 70)),
        allowMultipleAttempts: this.templateDraft.allowMultipleAttempts,
        catalogItemId: null,
        questions,
      };
      let saved: TrainingQuizTemplateDto;
      if (this.selectedTemplate()?.id) {
        saved = await this.api.updateQuizTemplate(this.selectedTemplate()!.id, body);
      } else {
        saved = await this.api.createQuizTemplate(body);
      }

      const returning = !!this.contentDraftStash();
      if (returning) {
        try {
          await this.api.publishQuizTemplate(saved.id);
        } catch (pubErr) {
          this.error.set(
            pubErr instanceof Error
              ? pubErr.message
              : 'Modèle enregistré mais publication impossible — publiez-le pour le sélectionner.',
          );
        }
        await this.reloadTemplates();
        await this.restoreContentDraftAfterQuiz(saved.id);
        return;
      }

      this.message.set('Modèle de quiz enregistré.');
      await this.openEditTemplate(saved.id);
      await this.reloadTemplates();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Enregistrement modèle impossible');
    } finally {
      this.busy.set(false);
    }
  }

  async publishTemplate(id?: string): Promise<void> {
    const tplId = id ?? this.selectedTemplate()?.id;
    if (!tplId) return;
    this.busy.set(true);
    try {
      await this.api.publishQuizTemplate(tplId);
      this.message.set('Modèle publié.');
      if (this.templateEditing()) await this.openEditTemplate(tplId);
      await this.reloadTemplates();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Publication modèle impossible');
    } finally {
      this.busy.set(false);
    }
  }

  async archiveTemplate(id?: string): Promise<void> {
    const tplId = id ?? this.selectedTemplate()?.id;
    if (!tplId) return;
    this.busy.set(true);
    try {
      await this.api.archiveQuizTemplate(tplId);
      this.message.set('Modèle archivé.');
      this.templateEditing.set(false);
      await this.reloadTemplates();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Archivage modèle impossible');
    } finally {
      this.busy.set(false);
    }
  }

  async duplicateTemplate(id: string): Promise<void> {
    this.busy.set(true);
    try {
      const copy = await this.api.duplicateQuizTemplate(id);
      this.message.set('Modèle dupliqué.');
      await this.reloadTemplates();
      await this.openEditTemplate(copy.id);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Duplication impossible');
    } finally {
      this.busy.set(false);
    }
  }

  publishedTemplates(): TrainingQuizTemplateListItemDto[] {
    const published = this.templates().filter((t) => t.status === 'Published' || t.status === 1);
    const selectedId = this.draft.defaultQuizTemplateId;
    if (!selectedId || published.some((t) => t.id === selectedId)) return published;
    const orphan = this.templates().find((t) => t.id === selectedId);
    return orphan ? [orphan, ...published] : published;
  }

  private stashContentDraftForQuizCreate(): void {
    const snapshot: ContentDraftSnapshot = {
      selectedId: this.selected()?.id ?? null,
      draft: { ...this.draft },
      selectedRoles: [...this.selectedRoles],
      selectedStructureKeys: [...this.selectedStructureKeys],
      selectedUserIds: [...this.selectedUserIds],
      wizardStep: this.wizardStep(),
    };
    this.contentDraftStash.set(snapshot);
    try {
      sessionStorage.setItem(CONTENT_DRAFT_STASH_KEY, JSON.stringify(snapshot));
    } catch {
      /* quota / private mode */
    }
  }

  private clearContentDraftStash(): void {
    this.contentDraftStash.set(null);
    try {
      sessionStorage.removeItem(CONTENT_DRAFT_STASH_KEY);
    } catch {
      /* ignore */
    }
  }

  private hydrateContentDraftStash(): void {
    try {
      const raw = sessionStorage.getItem(CONTENT_DRAFT_STASH_KEY);
      if (!raw) return;
      const parsed = JSON.parse(raw) as ContentDraftSnapshot;
      if (parsed?.draft) this.contentDraftStash.set(parsed);
    } catch {
      this.clearContentDraftStash();
    }
  }

  private async restoreContentDraftAfterQuiz(templateId: string | null): Promise<void> {
    const stash = this.contentDraftStash();
    if (!stash) {
      this.templateEditing.set(false);
      this.tab.set('contents');
      this.contentEditing.set(false);
      return;
    }

    this.busy.set(true);
    try {
      await this.ensurePublishedTemplatesLoaded();
      this.draft = { ...stash.draft };
      if (templateId) this.draft.defaultQuizTemplateId = templateId;
      this.selectedRoles = [...stash.selectedRoles];
      this.selectedStructureKeys = [...stash.selectedStructureKeys];
      this.selectedUserIds = [...stash.selectedUserIds];
      this.wizardStep.set(stash.wizardStep ?? 3);

      if (stash.selectedId) {
        try {
          this.selected.set(await this.api.getCatalogItem(stash.selectedId));
        } catch {
          this.selected.set(null);
        }
      } else {
        this.selected.set(null);
      }

      // Sans contenu existant : pas de formulaire de création ici.
      if (!this.selected()) {
        this.contentEditing.set(false);
        this.tab.set('contents');
        this.clearContentDraftStash();
        this.error.set('La création de contenu se fait depuis Planifier une formation continue.');
        return;
      }

      this.templateEditing.set(false);
      this.selectedTemplate.set(null);
      this.contentEditing.set(true);
      this.tab.set('contents');
      this.clearContentDraftStash();
      if (templateId) {
        this.message.set('Modèle créé et publié — pré-sélectionné sur la fiche formation.');
      } else {
        this.message.set('Retour à la fiche formation (brouillon conservé).');
      }
    } finally {
      this.busy.set(false);
    }
  }

  onAudienceChange(sel: AudiencePickerSelection): void {
    this.selectedRoles = [...sel.roles];
    this.selectedStructureKeys = [...sel.structureKeys];
    this.selectedUserIds = [...sel.userIds];
  }

  async onOutlineChanged(): Promise<void> {
    // Plus de sync immédiate : le brouillon local est la source de vérité.
  }

  openPreview(): void {
    const item = this.selected();
    if (!item && !this.draftModules.length) {
      this.error.set('Enregistrez d’abord la formation pour prévisualiser les médias serveur.');
      return;
    }
    // Prévisualisation basée sur l’item chargé (après enregistrement) ou structure locale texte seule.
    if (item) {
      this.previewOpen.set(true);
      const firstLesson =
        this.sortedPreviewModules(item)
          .flatMap((m) => this.sortedPreviewLessons(m))[0] ?? null;
      this.selectPreviewLesson(firstLesson);
      return;
    }
    this.error.set('Enregistrez la formation pour ouvrir la prévisualisation complète.');
  }

  closePreview(): void {
    this.previewOpen.set(false);
    this.previewLesson.set(null);
    this.previewResource.set(null);
    this.previewPartIndex.set(0);
  }

  sortedPreviewModules(item: TrainingCatalogItemDto): TrainingModuleDto[] {
    return [...(item.modules ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
  }

  sortedPreviewLessons(m: TrainingModuleDto): TrainingLessonDto[] {
    return [...(m.lessons ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
  }

  previewParts(lesson: TrainingLessonDto) {
    return groupResourcesByPart(lesson.resources);
  }

  selectPreviewLesson(lesson: TrainingLessonDto | null): void {
    this.previewLesson.set(lesson);
    this.previewPartIndex.set(0);
    const parts = lesson ? groupResourcesByPart(lesson.resources) : [];
    this.previewResource.set(parts[0]?.resources[0] ?? null);
  }

  selectPreviewPart(index: number): void {
    this.previewPartIndex.set(index);
    const lesson = this.previewLesson();
    if (!lesson) return;
    const parts = groupResourcesByPart(lesson.resources);
    this.previewResource.set(parts[index]?.resources[0] ?? null);
  }

  selectPreviewResource(resource: TrainingResourceDto): void {
    this.previewResource.set(resource);
  }

  async loadStats(): Promise<void> {
    this.tab.set('stats');
    if (!this.items().length) {
      try {
        this.items.set(await this.api.listCatalog(true));
      } catch {
        /* ignore */
      }
    }
    await this.refreshResults();
  }

  async refreshResults(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.stats.set(await this.api.getLearningStats(this.filterCatalogItemId || undefined));
      this.exportRows.set(
        await this.api.exportLearningResults(
          this.filterSessionId || undefined,
          this.filterCatalogItemId || undefined,
        ),
      );
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Stats impossibles');
    } finally {
      this.busy.set(false);
    }
  }

  filterBySession(sessionId: string): void {
    this.filterSessionId = sessionId;
    void this.refreshResults();
  }

  clearResultFilters(): void {
    this.filterSessionId = '';
    this.filterCatalogItemId = '';
    void this.refreshResults();
  }

  exportCsv(): void {
    const rows = this.exportRows();
    const header = ['Collaborateur', 'Email', 'Rôle', 'Structure', 'Session', 'Score', 'Réussi', 'Tentative', 'Date'];
    const lines = [
      header.join(';'),
      ...rows.map((r) =>
        [
          r.employeeName,
          r.email,
          r.role,
          r.structureKey,
          r.sessionTitle,
          r.score ?? '',
          r.passed == null ? '' : r.passed ? 'Oui' : 'Non',
          r.attemptNumber,
          r.submittedAt,
        ]
          .map((v) => `"${String(v).replace(/"/g, '""')}"`)
          .join(';'),
      ),
    ];
    const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `resultats_formation_${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  async exportXlsx(): Promise<void> {
    try {
      await downloadLearningResultsExcel(this.exportRows());
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Export Excel impossible');
    }
  }

  statusLabel(status: string | number): string {
    if (status === 0 || status === 'Draft') return 'Brouillon';
    if (status === 1 || status === 'Published') return 'Publié';
    if (status === 2 || status === 'Archived') return 'Archivé';
    return String(status);
  }

  private async ensurePublishedTemplatesLoaded(): Promise<void> {
    if (this.templates().length) return;
    try {
      this.templates.set(await this.api.listQuizTemplates(false));
    } catch {
      /* select optionnel */
    }
  }

  private async loadAudienceSources(): Promise<void> {
    try {
      const roles = await firstValueFrom(
        this.usersApi.getRoles().pipe(catchError(() => of([] as RoleOption[]))),
      );
      this.roleOptions = (roles ?? [])
        .map((r) => ({
          id: Number((r as RoleOption & { Id?: number }).id ?? (r as { Id?: number }).Id ?? 0),
          name: String((r as RoleOption & { Name?: string }).name ?? (r as { Name?: string }).Name ?? '').trim(),
        }))
        .filter((r) => !!r.name);
    } catch {
      this.roleOptions = [];
    }
  }

  private toGateMode(v: string): number {
    if (v === 'Attendance' || v === '0') return 0;
    if (v === 'Both' || v === '2') return 2;
    return 1;
  }

  private toDueMode(v: string): number {
    if (v === 'Absolute' || v === '1') return 1;
    if (v === 'RelativeDays' || v === '2') return 2;
    return 0;
  }

  private dueModeToString(v: string | number | null | undefined): string {
    if (v === 1 || v === 'Absolute') return 'Absolute';
    if (v === 2 || v === 'RelativeDays') return 'RelativeDays';
    return 'None';
  }
}
