import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Ancien interceptor d'identité démo (X-Parrainage-*).
 * Conservé en no-op : l'identité Parrainage est portée uniquement par le JWT Bearer.
 */
export const parrainageIdentityInterceptor: HttpInterceptorFn = (req, next) => next(req);
