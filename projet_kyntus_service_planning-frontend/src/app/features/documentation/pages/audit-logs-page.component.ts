import { Component, OnInit, inject } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  standalone: true,
  selector: 'app-audit-logs-page',
  template: '',
})
export class AuditLogsPageComponent implements OnInit {
  private readonly router = inject(Router);

  ngOnInit(): void {
    void this.router.navigate(['/audit'], {
      queryParams: { section: 'journal', module: 'documentation' },
      replaceUrl: true,
    });
  }
}
