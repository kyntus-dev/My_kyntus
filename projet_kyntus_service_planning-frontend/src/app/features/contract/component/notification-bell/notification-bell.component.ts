import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { interval, Subscription } from 'rxjs';
import { ContractService, ContractNotification } from '../../services/contract.service';
import { NotificationService, PlanningNotification } from '../../../../core/services/notification.service'; // ← AJOUT
import { KyntusSessionService } from '../../../../core/session/kyntus-session.service';

export type UnifiedNotification =
  | { source: 'contract';    data: ContractNotification   }
  | { source: 'reclamation'; data: PlanningNotification   };

@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './notification-bell.component.html',
  styleUrls: ['./notification-bell.component.css'],
})
export class NotificationBellComponent implements OnInit, OnDestroy {

  // Contrats (HTTP)
  contractNotifications: ContractNotification[] = [];

  // Réclamations (SignalR)
  reclamationNotifications: PlanningNotification[] = [];

  unreadCount = 0;
  isOpen      = false;
  loading     = false;

  private pollSub?:    Subscription;
  private signalrSub?: Subscription;
  private readonly session = inject(KyntusSessionService);

  constructor(
    private contractService:     ContractService,
    private notificationService: NotificationService, // ← AJOUT
    private router:              Router,
    private cdr:                 ChangeDetectorRef
  ) {}

ngOnInit(): void {
  this.loadCount();
  this.pollSub = interval(300_000).subscribe(() => this.loadCount());

  this.signalrSub = this.notificationService.notifications$.subscribe(notifs => {
    this.reclamationNotifications = notifs.filter(n => n.type === 'reclamation');
    this.refreshUnreadCount();
    this.cdr.detectChanges();
  });
}
  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
    this.signalrSub?.unsubscribe();
  }

  // ── Badge total = contrats + réclamations non lues ──
  loadCount(): void {
    if (!this.session.isAuthenticated()) return;
    this.contractService.getNotificationsCount().subscribe({
      next: res => {
        this.refreshUnreadCount(res.count);
        this.cdr.detectChanges();
      },
      error: () => {}
    });
  }

  private refreshUnreadCount(contractCount?: number): void {
    const contracts   = contractCount ?? this.contractNotifications.length;
    const reclamations = this.reclamationNotifications.filter(n => !n.read).length;
    this.unreadCount  = contracts + reclamations;
  }

  loadNotifications(): void {
    if (!this.session.isAuthenticated()) return;
    this.loading = true;
    this.cdr.detectChanges();

    this.contractService.getNotifications().subscribe({
      next: data => {
        this.contractNotifications = data.slice(0, 8);
        this.loading               = false;
        this.refreshUnreadCount(data.length);
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  markReclamationRead(n: PlanningNotification): void {
    n.read = true;
    this.notificationService.markAllRead(); // ou cibler une seule
    this.refreshUnreadCount();
    this.cdr.detectChanges();
  }

  togglePanel(): void {
    this.isOpen = !this.isOpen;
    if (this.isOpen) this.loadNotifications();
  }

  closePanel(): void { this.isOpen = false; }

  onContractClick(n: ContractNotification): void {
    this.router.navigate(['/contracts', n.contractId]);
    this.closePanel();
  }

  onReclamationClick(n: PlanningNotification): void {
    this.markReclamationRead(n);
    this.router.navigate(['/reclamations-admin']);
    this.closePanel();
  }

  goToNotificationList(): void {
    this.router.navigate(['/contracts/notifications']);
    this.closePanel();
  }

  getIconClass(type: string): string {
    if (type.startsWith('AvantFin')) return 'ic-warn';
    if (type.startsWith('MiParcours')) return 'ic-info';
    return 'ic-default';
  }
}