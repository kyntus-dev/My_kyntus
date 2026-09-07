import { Injectable, inject } from '@angular/core';
import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest,
} from '@angular/common/http';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { KyntusSessionService } from '../session/kyntus-session.service';
import { KyntusAuthRefreshService } from '../session/kyntus-auth-refresh.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  private readonly session = inject(KyntusSessionService);
  private readonly authRefresh = inject(KyntusAuthRefreshService);

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const authReq = this.withAuthHeader(req);
    return next.handle(authReq).pipe(
      catchError((error: unknown) => {
        if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
          return throwError(() => error);
        }
        // Inbox push / IAM catalogue : un 401 métier ne doit pas détruire la session login.
        if (this.isOptionalAuthFailure(req.url)) {
          return throwError(() => error);
        }
        if (!this.shouldAttemptRefresh(req)) {
          this.authRefresh.redirectToLogin();
          return throwError(() => error);
        }

        return from(this.authRefresh.refreshAccessToken()).pipe(
          switchMap((token) => {
            if (!token) {
              this.authRefresh.redirectToLogin();
              return throwError(() => error);
            }
            return next.handle(this.withAuthHeader(req, token));
          }),
          catchError(() => {
            this.authRefresh.redirectToLogin();
            return throwError(() => error);
          }),
        );
      }),
    );
  }

  private withAuthHeader(req: HttpRequest<any>, tokenOverride?: string): HttpRequest<any> {
    const token = tokenOverride ?? this.session.getToken();
    if (!token) return req;

    const isAuthRoute =
      req.url.includes('/auth/login') ||
      req.url.includes('/auth/register') ||
      req.url.includes('/auth/refresh') ||
      req.url.includes('/Auth/login') ||
      req.url.includes('/Auth/register') ||
      req.url.includes('/Auth/refresh');

    if (isAuthRoute) return req;

    return req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`,
      },
    });
  }

  /** Endpoints optionnels : 401 ≠ session expirée (claims / route absente). */
  private isOptionalAuthFailure(url: string): boolean {
    const u = url.toLowerCase();
    return u.includes('/api/iam') || u.includes('/api/auth/push');
  }

  private shouldAttemptRefresh(req: HttpRequest<any>): boolean {
    if (req.headers.has('X-Skip-Auth-Refresh')) return false;

    const isAuthRoute =
      req.url.includes('/auth/login') ||
      req.url.includes('/auth/register') ||
      req.url.includes('/auth/refresh') ||
      req.url.includes('/Auth/login') ||
      req.url.includes('/Auth/register') ||
      req.url.includes('/Auth/refresh');
    if (isAuthRoute) return false;

    return !!this.session.getRefreshToken();
  }
}
