import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { HtelLiaisonsPageComponent } from './htel-liaisons-page.component';
import { HtelLiaisonsReportDto } from '../../services/htel-api.service';
import { KyntusConfirmService } from '../../../../shared/components/kyntus-confirm/kyntus-confirm.service';

describe('Suivi RH des collaborateurs HTEL', () => {
  let fixture: ComponentFixture<HtelLiaisonsPageComponent>;
  let http: HttpTestingController;
  const confirm = vi.fn(async () => true);
  const report = (): HtelLiaisonsReportDto => ({
    checkedAtUtc: '2026-09-15T10:00:00Z', missingFromHtel: [], linked: [], ambiguous: [], unlinkedEmployees: [],
    orphansHtel: [{ idTechnicien: 4551, technicien: 'AARAB Fatima Zahra', actif: 1, code: 'SUPPORT' }],
  });
  beforeEach(async () => {
    confirm.mockReset().mockResolvedValue(true);
    await TestBed.configureTestingModule({
      imports: [HtelLiaisonsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: KyntusConfirmService, useValue: { confirm } }],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(HtelLiaisonsPageComponent);
    fixture.detectChanges();
  });
  afterEach(() => { fixture.destroy(); http.verify(); });

  it('affiche le nouveau et transmet uniquement son identifiant au formulaire', () => {
    http.expectOne('/api/directory/htel/liaisons').flush(report());
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('AARAB Fatima Zahra');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.componentInstance.createEmployeeFromOrphan(report().orphansHtel[0]);
    expect(navigate).toHaveBeenCalledWith(['/users/create'], { queryParams: { idTechnicien: 4551 } });
    http.expectNone('/api/directory/employees');
  });

  it('bloque les actions sur une comparaison devenue indisponible', () => {
    http.expectOne('/api/directory/htel/liaisons').flush(report());
    fixture.componentInstance.reload();
    http.expectOne('/api/directory/htel/liaisons').flush({ error: 'HTEL indisponible' }, { status: 502, statusText: 'Bad Gateway' });
    expect(fixture.componentInstance.canAct).toBe(false);
    fixture.componentInstance.sync();
    http.expectNone('/api/directory/htel/techniciens/sync');
  });

  it('ne lance pas de création pour un technicien inactif', () => {
    http.expectOne('/api/directory/htel/liaisons').flush(report());
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.componentInstance.createEmployeeFromOrphan({ ...report().orphansHtel[0], actif: 0 });
    expect(navigate).not.toHaveBeenCalled();
  });

  it('ne lie pas une correspondance avant confirmation RH', async () => {
    http.expectOne('/api/directory/htel/liaisons').flush(report());
    confirm.mockResolvedValue(false);
    await fixture.componentInstance.linkCandidate('00000000-0000-0000-0000-000000000001', 4551);
    expect(confirm).toHaveBeenCalled();
    http.expectNone('/api/directory/htel/liaisons/link');
  });
});
