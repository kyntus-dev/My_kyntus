import { HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { DocumentationHeaders } from '../models/documentation-headers';
import { DocumentationIdentityService } from '../services/documentation-identity.service';
import { KyntusSessionService } from '../session/kyntus-session.service';

const DocumentationApiPrefix = '/api/documentation';
const GenerateDocumentAiApiPrefix = '/api/generate-document-ai';

function shouldApplyDocumentationGatewayDefaults(url: string): boolean {
  return url.includes(GenerateDocumentAiApiPrefix) || url.includes(DocumentationApiPrefix);
}

/**
 * Pose les en-têtes Documentation depuis le profil/session authentifiée uniquement
 * (pas de tenant / utilisateur démo de repli).
 */
@Injectable()
export class DocumentationGatewayHeadersInterceptor implements HttpInterceptor {
  private readonly identity = inject(DocumentationIdentityService);
  private readonly session = inject(KyntusSessionService);

  intercept(req: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    if (!shouldApplyDocumentationGatewayDefaults(req.url)) {
      return next.handle(req);
    }

    if (!this.session.isAuthenticated()) {
      return next.handle(req);
    }

    let headers = req.headers;
    const profileHeaders = this.identity.getHeaderMap();
    for (const [key, value] of Object.entries(profileHeaders)) {
      if (value && !headers.has(key)) {
        headers = headers.set(key, value);
      }
    }

    return next.handle(req.clone({ headers }));
  }
}
