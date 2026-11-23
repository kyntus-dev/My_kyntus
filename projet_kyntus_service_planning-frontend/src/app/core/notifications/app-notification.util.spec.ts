import { describe, expect, it } from 'vitest';
import {
  depuis,
  estNotificationApplication,
  libelleCategorie,
  lienInterne,
  severiteNotification,
  sourceDeCategorie,
} from './app-notification.util';

describe('v62 : notifications d’application', () => {
  it('reconnaît les notifications envoyées après une action', () => {
    expect(estNotificationApplication('NOTIF-CONGE-12')).toBe(true);
    expect(estNotificationApplication('S45')).toBe(false);
    expect(estNotificationApplication(null)).toBe(false);
  });

  it('range chaque catégorie dans la bonne rubrique de la cloche', () => {
    expect(sourceDeCategorie('conges')).toBe('conge');
    expect(sourceDeCategorie('documentation')).toBe('documentation');
    expect(sourceDeCategorie('presences')).toBe('planning');
    expect(sourceDeCategorie('affectation')).toBe('planning');
    expect(libelleCategorie('presences')).toBe('Présences');
    expect(libelleCategorie('affectation')).toBe('Affectation');
    expect(libelleCategorie('inconnue')).toBeNull();
  });

  it('ton et lien : refus en alerte, validation en succès, jamais de lien externe', () => {
    expect(severiteNotification('Demande de congé refusée')).toBe('warning');
    expect(severiteNotification('Absence enregistrée : Arrêt maladie')).toBe('warning');
    expect(severiteNotification('Congé validé')).toBe('success');
    expect(severiteNotification('Document disponible')).toBe('success');
    expect(lienInterne('/mes-conges')).toBe('/mes-conges');
    expect(lienInterne('https://exemple.com')).toBe('/notifications');
    expect(lienInterne('//exemple.com')).toBe('/notifications');
    expect(lienInterne(undefined)).toBe('/notifications');
  });

  it('date relative', () => {
    const maintenant = new Date(2026, 10, 20, 12, 0, 0);
    expect(depuis(new Date(2026, 10, 20, 11, 59, 30), maintenant)).toBe('à l’instant');
    expect(depuis(new Date(2026, 10, 20, 11, 55, 0), maintenant)).toBe('il y a 5 min');
    expect(depuis(new Date(2026, 10, 20, 9, 0, 0), maintenant)).toBe('il y a 3 h');
    expect(depuis(new Date(2026, 10, 19, 10, 0, 0), maintenant)).toBe('hier');
  });
});
