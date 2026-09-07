import { DocumentationHeaders } from './documentation-headers';

/**
 * Plus de repli démo (atlas-tech-demo / Yasmine).
 * Les en-têtes Documentation doivent venir de la session JWT / profil chargé.
 */
export const DocumentationGatewayDefaultHeaders: Partial<
  Record<(typeof DocumentationHeaders)[keyof typeof DocumentationHeaders], string>
> = {};
