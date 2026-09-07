/**
 * Couche HTTP bas niveau du module parrainage.
 * Même origine que l'app : le nginx du conteneur proxy `/api/` vers l'API gateway,
 * qui route `/api/parrainage/*` vers `parrainage-backend`.
 * Identité = JWT Bearer uniquement (pas d'en-têtes X-Parrainage-* spoofables).
 */
export const PARRAINAGE_API_BASE =
  (import.meta as unknown as { env?: { NG_APP_PARRAINAGE_API_BASE?: string } }).env?.NG_APP_PARRAINAGE_API_BASE ?? '';

function authHeaders(): Record<string, string> {
  const token = localStorage.getItem('token') || localStorage.getItem('accessToken');
  const h: Record<string, string> = {};
  if (token) h['Authorization'] = `Bearer ${token}`;
  return h;
}

function buildHeaders(json: boolean): Record<string, string> {
  return {
    ...(json ? { 'Content-Type': 'application/json' } : {}),
    ...authHeaders(),
  };
}

async function parseError(res: Response): Promise<never> {
  const text = await res.text().catch(() => '');
  throw new Error(text || `HTTP ${res.status}`);
}

export async function parrainageApiGet<T>(path: string): Promise<T> {
  const res = await fetch(`${PARRAINAGE_API_BASE}${path}`, {
    credentials: 'include',
    headers: buildHeaders(false),
  });
  if (!res.ok) return parseError(res);
  return res.json() as Promise<T>;
}

export async function parrainageApiSend<T>(
  method: 'POST' | 'PUT' | 'PATCH' | 'DELETE',
  path: string,
  body?: unknown,
): Promise<T> {
  const res = await fetch(`${PARRAINAGE_API_BASE}${path}`, {
    method,
    credentials: 'include',
    headers: buildHeaders(body !== undefined),
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  });
  if (!res.ok) return parseError(res);
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}
