import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { shareReplay, tap } from 'rxjs/operators';

const directoryBase = '/api/directory';
const orgOverviewUrl = `${directoryBase}/org/overview`;
const employeesUrl = `${directoryBase}/employees`;

/** TTL court : l’overview org change souvent (affectations, structure). */
const DEFAULT_TTL_MS = 30 * 1000;

interface TtlEntry<T> {
  at: number;
  stream$: Observable<T>;
}

/**
 * Facade annuaire : déduplique les GET org overview / employees via shareReplay + TTL mémoire.
 * Après mutation, appeler {@link invalidate} puis recharger — le cache navigateur est aussi
 * contourné via `_nc` + en-têtes no-cache.
 */
@Injectable({ providedIn: 'root' })
export class DirectoryFacadeService {
  private readonly http = inject(HttpClient);
  private readonly ttlMs = DEFAULT_TTL_MS;

  private overview?: TtlEntry<unknown>;
  private employees?: TtlEntry<unknown>;
  /** Incrémenté à chaque invalidation pour forcer un GET réseau (anti cache HTTP). */
  private bustToken = 0;

  getOrgOverview<T = unknown>(): Observable<T> {
    return this.getOrRefresh('overview', () =>
      this.http.get<T>(orgOverviewUrl, {
        params: this.noCacheParams(),
        headers: this.noCacheHeaders(),
      }),
    );
  }

  getEmployees<T = unknown>(): Observable<T> {
    return this.getOrRefresh('employees', () =>
      this.http.get<T>(employeesUrl, {
        params: this.noCacheParams(),
        headers: this.noCacheHeaders(),
      }),
    );
  }

  /** Invalide le cache local (après mutations org / employés). */
  invalidate(): void {
    this.overview = undefined;
    this.employees = undefined;
    this.bustToken = Date.now();
  }

  private noCacheParams(): HttpParams {
    return new HttpParams().set('_nc', String(this.bustToken || Date.now()));
  }

  private noCacheHeaders(): HttpHeaders {
    return new HttpHeaders({
      'Cache-Control': 'no-cache',
      Pragma: 'no-cache',
    });
  }

  private getOrRefresh<T>(
    kind: 'overview' | 'employees',
    factory: () => Observable<T>,
  ): Observable<T> {
    const existing = kind === 'overview' ? this.overview : this.employees;
    const now = Date.now();
    if (existing && now - existing.at < this.ttlMs) {
      return existing.stream$ as Observable<T>;
    }

    const stream$ = factory().pipe(
      tap({
        error: () => {
          if (kind === 'overview') this.overview = undefined;
          else this.employees = undefined;
        },
      }),
      shareReplay({ bufferSize: 1, refCount: true }),
    );

    const entry: TtlEntry<unknown> = { at: now, stream$ };
    if (kind === 'overview') this.overview = entry;
    else this.employees = entry;

    return stream$;
  }
}
