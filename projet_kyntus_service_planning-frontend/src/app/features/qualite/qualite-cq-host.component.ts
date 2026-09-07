import { Component, ElementRef, OnDestroy, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Subscription } from 'rxjs';
import { KyntusSessionService } from '../../core/session/kyntus-session.service';
import { redirectToAuthLogin } from '../../core/session/kyntus-auth-refresh.service';
import { KyntusPageHeaderComponent } from '../../shared/components/ui/kyntus-page-header.component';

@Component({
  selector: 'app-qualite-cq-host',
  standalone: true,
  imports: [CommonModule, KyntusPageHeaderComponent],
  templateUrl: './qualite-cq-host.component.html',
  styleUrls: ['./qualite-cq-host.component.css'],
})
export class QualiteCqHostComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(KyntusSessionService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly subs = new Subscription();

  iframeSrc: SafeResourceUrl = this.sanitizer.bypassSecurityTrustResourceUrl('/qualite-app/?embed=1');
  title = 'Contrôle qualité';

  @ViewChild('frame') frame?: ElementRef<HTMLIFrameElement>;

  constructor() {
    this.subs.add(
      this.route.queryParamMap.subscribe((q) => {
        const view = q.get('view') || 'evaluations';
        if (view === 'audit') {
          void this.router.navigate(['/audit'], { queryParams: { section: 'journal', module: 'qualite' } });
          return;
        }
        this.title = this.titleFor(view);
        const url = `/qualite-app/?embed=1&view=${encodeURIComponent(view)}`;
        this.iframeSrc = this.sanitizer.bypassSecurityTrustResourceUrl(url);
        queueMicrotask(() => this.pushToken());
      }),
    );
  }

  ngOnInit(): void {
    window.addEventListener('message', this.onMessage);
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
    window.removeEventListener('message', this.onMessage);
  }

  onFrameLoad(): void {
    this.pushToken();
  }

  onMessage = (ev: MessageEvent): void => {
    const type = (ev.data && ev.data.type) || '';
    if (type === 'KYNTUS_CQ_READY') {
      this.pushToken();
      return;
    }
    if (type === 'KYNTUS_CQ_SESSION_EXPIRED') {
      redirectToAuthLogin();
    }
  };

  private pushToken(): void {
    const token = this.session.getToken();
    const win = this.frame?.nativeElement?.contentWindow;
    if (!token || !win) return;
    win.postMessage({ type: 'KYNTUS_CQ_TOKEN', token }, window.location.origin);
  }

  private titleFor(view: string): string {
    switch (view) {
      case 'dashboard':
      case 'stats':
      case 'overview':
        return 'Tableau de bord';
      case 'new':
        return 'Nouvelle évaluation';
      case 'grids':
        return 'Grilles d’évaluation';
      case 'coaching':
        return 'Coaching qualité';
      case 'picking':
        return 'Appels à évaluer';
      case 'notifications':
        return 'Notifications CQ';
      case 'health':
        return 'Santé';
      case 'audit':
        return 'Audit log';
      case 'settings':
        return 'Paramètres CQ';
      case 'mine':
        return 'Mes évaluations';
      case 'coachings-me':
        return 'Mes coachings';
      default:
        return 'Évaluations qualité';
    }
  }
}
