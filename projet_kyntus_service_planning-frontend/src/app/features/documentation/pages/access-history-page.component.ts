import { Component, OnInit, inject } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  standalone: true,
  selector: 'app-access-history-page',
  template: '',
})
export class AccessHistoryPageComponent implements OnInit {
  private readonly router = inject(Router);

  ngOnInit(): void {
    void this.router.navigate(['/audit'], {
      queryParams: { section: 'access-history', module: 'documentation' },
      replaceUrl: true,
    });
  }
}
