import { describe, expect, it, beforeEach, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AppGuideService, MyAppGuides } from './app-guide.service';
import { APP_GUIDE_ROUTE, MICROSERVICES } from '../navigation/microservices.config';

const MINE: MyAppGuides = {
  role: 'Pilote',
  guides: [
    { id: 1, title: 'Guide Pilotes', kind: 'Video', mediaAssetId: 10, sortOrder: 1, viewed: false },
    { id: 2, title: 'Bienvenue', kind: 'Link', externalUrl: 'https://intranet/bienvenue', sortOrder: 0, viewed: true },
  ],
  viewedCount: 1,
  total: 2,
};

describe('AppGuideService (espace Aide)', () => {
  let service: AppGuideService;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AppGuideService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('compte les guides non consultés pour le badge du menu', () => {
    service.refresh().subscribe();
    http.expectOne('/api/planning/app-guides/mine').flush(MINE);

    expect(service.pendingCount()).toBe(1);
    expect(service.myGuides()?.role).toBe('Pilote');
  });

  it('met à jour la progression après « Marquer comme consulté »', () => {
    service.refresh().subscribe();
    http.expectOne('/api/planning/app-guides/mine').flush(MINE);

    service.markViewed(1).subscribe();
    const req = http.expectOne('/api/planning/app-guides/1/viewed');
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.pendingCount()).toBe(0);
    expect(service.myGuides()?.viewedCount).toBe(2);
  });

  it('ne propose l’accueil guidé qu’une fois par session', () => {
    service.refresh().subscribe();
    http.expectOne('/api/planning/app-guides/mine').flush(MINE);

    expect(service.shouldOfferOnboarding()).toBe(true);
    service.skipOnboardingForSession();
    expect(service.shouldOfferOnboarding()).toBe(false);
  });

  it('reste utilisable sans badge si l’API est indisponible', () => {
    let result: unknown = 'pending';
    service.refresh().subscribe((r) => (result = r));
    http.expectOne('/api/planning/app-guides/mine').flush('boom', { status: 503, statusText: 'Unavailable' });

    expect(result).toBeNull();
    expect(service.pendingCount()).toBe(0);
    expect(service.shouldOfferOnboarding()).toBe(false);
  });
});

describe('Menu « Aide »', () => {
  it('propose le guide de l’application et l’assistance à tous les rôles', () => {
    const aide = MICROSERVICES.find((g) => g.id === 'aide');

    expect(aide?.label).toBe('Aide');
    expect(aide?.children.map((c) => c.route)).toEqual([APP_GUIDE_ROUTE, '/assistance']);
    expect(aide?.children.every((c) => !c.roles?.length && c.hat === 'both')).toBe(true);
  });
});
