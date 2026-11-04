import { describe, expect, it } from 'vitest';
import { servicesGroupes } from './reaffectations.util';

describe('réaffectations', () => {
  const services = [
    { id: 1, service: 'Service A', cellule: 'Satisfaction client', pole: 'Pôle client' },
    { id: 2, service: 'Service B', cellule: 'Satisfaction client', pole: 'Pôle client' },
    { id: 3, service: 'Service C', cellule: 'Malfaçons', pole: 'Pôle réseau' },
  ];
  it('groupe par pôle et cellule, sans le service actuel', () => {
    expect(servicesGroupes(services, 1)).toEqual([
      { titre: 'Pôle client › Satisfaction client', services: [services[1]] },
      { titre: 'Pôle réseau › Malfaçons', services: [services[2]] },
    ]);
  });
  it('filtre sans accents', () => {
    expect(servicesGroupes(services, null, 'malfacons').map((g) => g.titre)).toEqual(['Pôle réseau › Malfaçons']);
  });
});
