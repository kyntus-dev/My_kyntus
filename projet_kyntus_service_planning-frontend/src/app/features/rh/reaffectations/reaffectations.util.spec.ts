import { describe, expect, it } from 'vitest';
import { choixRattachement, contratDe, contratsParPole, servicesFiltres, servicesGroupes } from './reaffectations.util';

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

describe('rattachement complet (contrat › pôle › cellule › service)', () => {
  const services = [
    { id: 1, service: 'N1', cellule: 'Satisfaction', pole: 'Pôle client', poleId: 'p-client' },
    { id: 2, service: 'FTTH', cellule: 'Malfaçons', pole: 'Pôle réseau', poleId: 'p-reseau' },
    { id: 3, service: 'Support', cellule: 'Support', pole: 'Pôle support', poleId: 'p-support' },
  ];
  const parPole = contratsParPole([
    { id: 'a', name: 'BYTEL', poleIds: ['p-client', 'p-reseau'] },
    { id: 'b', name: 'ANCIEN', isActive: false, poleIds: ['p-support'] },
  ]);
  it('retrouve le contrat d’un service par son pôle', () => {
    expect(contratDe(services[0], parPole)).toBe('BYTEL');
    expect(contratDe(services[2], parPole)).toBeNull();
  });
  it('propose les choix en cascade et filtre les services', () => {
    const f = { contrat: 'BYTEL', pole: '', cellule: '' };
    expect(choixRattachement(services, parPole, f)).toEqual({
      contrats: ['BYTEL', 'Sans contrat'], poles: ['Pôle client', 'Pôle réseau'], cellules: ['Malfaçons', 'Satisfaction'],
    });
    expect(servicesFiltres(services, parPole, { ...f, pole: 'Pôle réseau' }).map((s) => s.id)).toEqual([2]);
    expect(servicesFiltres(services, parPole, { contrat: 'Sans contrat', pole: '', cellule: '' }).map((s) => s.id)).toEqual([3]);
  });
});
