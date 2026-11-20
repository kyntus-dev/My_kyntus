import { describe, expect, it } from 'vitest';
import type { PresenceAgentDto, PresenceJourDto } from '../../../core/models/presence.models';
import {
  FILTRES_VIDES,
  encadrementDans,
  familleCode,
  filtrerAgents,
  joursDeLaPeriode,
  libellesUniques,
  lignesRetards,
  optionsActivites,
  optionsOrganisation,
  optionsPerimetre,
  periodePaie,
  structureDeLActivite,
} from './presence.util';

const jour = (p: Partial<PresenceJourDto>): PresenceJourDto => ({
  jour: '2026-11-02', code: 'P', auto: true, codeAuto: 'P', statut: 'Retard', planning: '09:00', arrivee: '09:12:00', depart: '18:00:00',
  pointages: [], retardEntreeMinutes: 12, retardPauseMinutes: 0, retardAutorise: false, retardPauseAutorise: false,
  motifAutorisation: null, retardRetenuMinutes: 12, retardAutoriseMinutes: 0, manquePointage: false, commentaire: null,
  modifiePar: null, modifieLe: null, ...p,
});

const agent = (p: Partial<PresenceAgentDto>): PresenceAgentDto => ({
  userId: 1, nom: 'ALPHA Agent', type: 'Salarié', pole: 'Pôle 1', poleId: 'p1', cellule: 'Cellule A', celluleId: 'c1',
  service: null, dateEntree: null, dateSortie: null, jours: [jour({})],
  totaux: { presents: 1, formation: 0, absences: {}, retardRetenuMinutes: 12, retardAutoriseMinutes: 0, joursEnRetard: 1, manquesPointage: 0, absencesNonQualifiees: 0 },
  ...p,
});

describe('presence.util', () => {
  it('période de paie du 24 au 23', () => {
    expect(periodePaie(new Date(2026, 10, 4))).toMatchObject({ du: '2026-10-24', au: '2026-11-23' });
    expect(periodePaie(new Date(2026, 10, 25))).toMatchObject({ du: '2026-11-24', au: '2026-12-23' });
    expect(periodePaie(new Date(2026, 0, 10), -1)).toMatchObject({ du: '2025-11-24', au: '2025-12-23' });
    expect(joursDeLaPeriode('2026-10-24', '2026-11-23')).toHaveLength(31);
  });

  it('couleurs par famille de code', () => {
    expect(familleCode('P')).toBe('present');
    expect(familleCode('ABS N')).toBe('absence-nj');
    expect(familleCode('CP')).toBe('conge');
    expect(familleCode('AM')).toBe('absence');
    expect(familleCode(null)).toBe('vide');
  });

  it('filtre activité, cellule, type et « à traiter »', () => {
    const a = agent({});
    const b = agent({ userId: 2, nom: 'BETA', cellule: 'Cellule B', type: 'Sous-traitant',
      totaux: { ...a.totaux, retardRetenuMinutes: 0, joursEnRetard: 0 } });
    const activite = (p: string | null) => (p === 'p1' ? 'BYTEL' : null);
    expect(filtrerAgents([a, b], { ...FILTRES_VIDES, cellule: 'Cellule B' }, activite).map((x) => x.userId)).toEqual([2]);
    expect(filtrerAgents([a, b], { ...FILTRES_VIDES, type: 'Sous-traitant' }, activite)).toHaveLength(1);
    expect(filtrerAgents([a, b], { ...FILTRES_VIDES, aTraiter: true }, activite).map((x) => x.userId)).toEqual([1]);
    expect(filtrerAgents([a, b], { ...FILTRES_VIDES, activite: 'BYTEL' }, activite)).toHaveLength(2);
  });

  it('liste des retards avec statut autorisé / non autorisé', () => {
    const a = agent({
      jours: [
        jour({}),
        jour({ jour: '2026-11-03', retardAutorise: true, retardRetenuMinutes: 0, retardAutoriseMinutes: 12, motifAutorisation: 'Prévenu la veille' }),
        jour({ jour: '2026-11-04', code: 'ABS N', retardEntreeMinutes: 0 }),
      ],
    });
    const l = lignesRetards([a], () => 'BYTEL');
    expect(l.map((x) => [x.jour, x.statut])).toEqual([['2026-11-03', 'Autorisé'], ['2026-11-02', 'Non autorisé']]);
    expect(l[0].motif).toBe('Prévenu la veille');
  });
});

describe('filtres v46 : formation, contrats, superviseurs et référents', () => {
  it('filtre formation et affiche l’encadrement seulement sur demande (chef de projet)', async () => {
    const { filtrerAgents, FILTRES_VIDES, TYPES_CONTRAT } = await import('./presence.util');
    const base = { nom: '', type: 'Salarié', pole: null, poleId: null, cellule: null, celluleId: null, service: null, dateEntree: null, dateSortie: null,
      jours: [], totaux: { presents: 0, formation: 0, absences: {}, retardRetenuMinutes: 0, retardAutoriseMinutes: 0, joursEnRetard: 0, manquesPointage: 0, absencesNonQualifiees: 0 } };
    const agents = [
      { ...base, userId: 1, nom: 'Pilote' },
      { ...base, userId: 2, nom: 'Stagiaire', enFormation: true },
      { ...base, userId: 3, nom: 'Sup', role: 'Superviseur' },
      { ...base, userId: 4, nom: 'Ref', role: 'Référent technique' },
    ] as never[];
    const act = () => null;
    const ids = (f: object, cp = true) => filtrerAgents(agents, { ...FILTRES_VIDES, ...f }, act, cp).map((a: { userId: number }) => a.userId);
    expect(TYPES_CONTRAT).toEqual(['Salarié', 'Sous-traitant']);
    expect(ids({})).toEqual([1, 2]);
    expect(ids({}, false)).toEqual([1, 2, 3, 4]);
    expect(ids({ formation: 'oui' })).toEqual([2]);
    expect(ids({ formation: 'non' })).toEqual([1]);
    // v46.1 : chacun son tableau.
    expect(ids({ superviseurs: '*' })).toEqual([3]);
    expect(ids({ referents: '4', formation: 'oui' })).toEqual([4]);
    expect(ids({ referents: '*' })).toEqual([4]);
    expect(ids({ superviseurs: '99' })).toEqual([]);
  });

  it('v48 : le filtre formation distingue contractuel et non contractuel', async () => {
    const { filtrerAgents, FILTRES_VIDES } = await import('./presence.util');
    const base = { nom: '', type: 'Salarié', pole: null, poleId: null, cellule: null, celluleId: null, service: null, dateEntree: null, dateSortie: null,
      jours: [], totaux: { presents: 0, formation: 0, absences: {}, retardRetenuMinutes: 0, retardAutoriseMinutes: 0, joursEnRetard: 0, manquesPointage: 0, absencesNonQualifiees: 0 } };
    const agents = [
      { ...base, userId: 1, nom: 'Pilote' },
      { ...base, userId: 2, nom: 'Contractuel', enFormation: true, typeFormation: 'contractuel' },
      { ...base, userId: 3, nom: 'Non contractuel', enFormation: true, typeFormation: 'non-contractuel' },
      { ...base, userId: 4, nom: 'Sans type', enFormation: true },
    ] as never[];
    const ids = (f: object) =>
      filtrerAgents(agents, { ...FILTRES_VIDES, ...f }, () => null).map((a: { userId: number }) => a.userId);

    expect(ids({ formation: 'oui' })).toEqual([2, 3, 4]);
    expect(ids({ formation: 'contractuel' })).toEqual([2]);
    expect(ids({ formation: 'non-contractuel' })).toEqual([3]);
    expect(ids({ formation: 'non' })).toEqual([1]);
    expect(ids({})).toEqual([1, 2, 3, 4]);
  });
});

describe('structureDeLActivite (v48.1)', () => {
  const activites = [
    {
      name: 'Formation & Qualité',
      isActive: true,
      poles: [
        { name: 'Formation', cellules: [{ name: 'Formateurs' }, { name: 'Agents en formation' }] },
        { name: 'Contrôle qualité', cellules: [{ name: 'Contrôleurs de qualité' }] },
      ],
    },
    { name: 'Bytel Kyntus', isActive: true, poles: [{ name: 'Pôle client', cellules: [{ name: 'Support VIP' }] }] },
  ];

  it('propose les pôles et cellules de l’activité même sans agent présent (triés, v58)', () => {
    expect(structureDeLActivite(activites, 'Formation & Qualité', '')).toEqual({
      poles: ['Contrôle qualité', 'Formation'],
      cellules: ['Agents en formation', 'Contrôleurs de qualité', 'Formateurs'],
    });
  });

  it('limite les cellules au pôle choisi', () => {
    expect(structureDeLActivite(activites, 'Formation & Qualité', 'Formation').cellules)
      .toEqual(['Agents en formation', 'Formateurs']);
  });

  it('ne propose rien sans activité choisie ou pour une activité inconnue', () => {
    expect(structureDeLActivite(activites, '', '')).toEqual({ poles: [], cellules: [] });
    expect(structureDeLActivite(activites, 'Inconnue', '')).toEqual({ poles: [], cellules: [] });
  });
});

describe('listes déroulantes Présence et Paie (v58)', () => {
  const activites = [
    {
      name: 'Bytel Kyntus',
      isActive: true,
      poles: [
        { name: 'Pôle technique', cellules: [{ name: 'Support technicien' }, { name: 'Prestations réseaux & CV' }] },
        { name: 'Pôle BTB', cellules: [{ name: 'BTB' }] },
      ],
    },
  ];

  it('retire les doublons d’orthographe, d’espaces et d’accents', () => {
    expect(libellesUniques(['Support technicien', 'support  technicien ', 'SUPPORT TECHNICIEN', 'BTB']))
      .toEqual(['BTB', 'Support technicien']);
    expect(libellesUniques(['Pôle technique', 'Pole technique'])).toEqual(['Pôle technique']);
    expect(libellesUniques([null, undefined, '  '])).toEqual([]);
  });

  it('l’organisation fait foi : un pôle qui n’existe plus n’est pas proposé', () => {
    const options = optionsOrganisation(activites, 'Bytel Kyntus', '', {
      poles: ['Pôle technique', 'Ancien pôle supprimé'],
      cellules: ['Support technicien', 'Cellule fantôme'],
    });

    expect(options.poles).toEqual(['Pôle BTB', 'Pôle technique']);
    expect(options.cellules).toEqual(['BTB', 'Prestations réseaux & CV', 'Support technicien']);
  });

  it('respecte la hiérarchie : seules les cellules du pôle choisi', () => {
    const options = optionsOrganisation(activites, 'Bytel Kyntus', 'Pôle BTB', {
      poles: [],
      cellules: ['Support technicien'],
    });

    expect(options.cellules).toEqual(['BTB']);
  });

  it('se rabat sur les agents quand l’organisation ne déclare rien', () => {
    const options = optionsOrganisation(activites, 'Activité inconnue', '', {
      poles: ['Pôle orphelin', 'Pôle orphelin'],
      cellules: ['Cellule orpheline'],
    });

    expect(options.poles).toEqual(['Pôle orphelin']);
    expect(options.cellules).toEqual(['Cellule orpheline']);
  });

  it('les activités proposées viennent de l’organisation puis des agents, sans doublon', () => {
    expect(optionsActivites(activites, ['bytel kyntus', 'Formation & Qualité']))
      .toEqual(['Bytel Kyntus', 'Formation & Qualité']);
  });
});

describe('v61 : périmètre du RP, superviseurs et référents par contrat', () => {
  const activiteDuPole = (poleId: string | null) =>
    ({ 'p-bytel': 'Bytel Kyntus', 'p-sfr': 'SFR', 'p-sfr2': 'SFR' } as Record<string, string>)[poleId ?? ''] ?? null;
  const agents = [
    agent({ userId: 1, nom: 'AGENT Bytel', pole: 'Pôle Bytel', poleId: 'p-bytel', cellule: 'Bytel 1' }),
    agent({ userId: 2, nom: 'AGENT Sfr', pole: 'Pôle SFR', poleId: 'p-sfr', cellule: 'Sfr Ftth' }),
    // Superviseur sans placement, désigné par les agents Bytel.
    agent({ userId: 10, nom: 'SUP Bytel', role: 'Superviseur', pole: null, poleId: null, cellule: null,
      poleIdsEncadres: ['p-bytel'], polesEncadres: ['Pôle Bytel'], cellulesEncadrees: ['Bytel 1'] }),
    // Référent placé côté SFR.
    agent({ userId: 11, nom: 'RT Sfr', role: 'Référent technique', pole: 'Pôle SFR', poleId: 'p-sfr', cellule: 'Sfr Ftth',
      poleIdsEncadres: ['p-sfr'], polesEncadres: ['Pôle SFR'], cellulesEncadrees: ['Sfr Ftth'] }),
  ];

  it('un superviseur n’est proposé que pour le contrat, le pôle et la cellule de ses agents', () => {
    const sup = agents[2];
    expect(encadrementDans(sup, { activite: 'Bytel Kyntus', pole: '', cellule: '' }, activiteDuPole)).toBe(true);
    expect(encadrementDans(sup, { activite: 'SFR', pole: '', cellule: '' }, activiteDuPole)).toBe(false);
    expect(encadrementDans(sup, { activite: 'Bytel Kyntus', pole: 'pôle bytel', cellule: 'Bytel 1' }, activiteDuPole)).toBe(true);
    expect(encadrementDans(sup, { activite: 'Bytel Kyntus', pole: '', cellule: 'Autre' }, activiteDuPole)).toBe(false);
  });

  it('tableau des superviseurs filtré par contrat ; référents de l’autre contrat exclus', () => {
    const f = { ...FILTRES_VIDES, activite: 'Bytel Kyntus', superviseurs: '*' };
    expect(filtrerAgents(agents, f, activiteDuPole, true).map((a) => a.userId)).toEqual([10]);
    const g = { ...FILTRES_VIDES, activite: 'Bytel Kyntus', referents: '*' };
    expect(filtrerAgents(agents, g, activiteDuPole, true)).toEqual([]);
  });

  it('périmètre limité : contrats, pôles et cellules de ses agents seulement ; toute l’entreprise : organisation', () => {
    const structure = [
      { id: 'd1', name: 'Bytel Kyntus', isActive: true, poles: [{ id: 'x', name: 'Pôle Bytel', cellules: [{ id: 'c', name: 'Bytel 1' }] }] },
      { id: 'd2', name: 'Autre contrat', isActive: true, poles: [] },
    ] as never;
    const depuis = { activites: ['Bytel Kyntus', 'Bytel Kyntus'], poles: ['Pôle Bytel'], cellules: ['Bytel 1'] };
    expect(optionsPerimetre(false, structure, '', '', depuis).activites).toEqual(['Bytel Kyntus']);
    expect(optionsPerimetre(true, structure, '', '', depuis).activites).toEqual(['Autre contrat', 'Bytel Kyntus']);
  });
});

describe('v62 : listes superviseurs / référents pour les RH', () => {
  it('le choix ouvre leur tableau sans être chef de projet ; non placés masqués du tableau principal', async () => {
    const { filtrerAgents, FILTRES_VIDES, estEncadrementNonPlace } = await import('./presence.util');
    const agents = [
      agent({ userId: 1, nom: 'AGENT', role: 'Pilote' }),
      agent({ userId: 2, nom: 'SUP placé', role: 'Superviseur' }),
      agent({ userId: 3, nom: 'SUP organisation', role: 'Superviseur', pole: null, poleId: null, cellule: null, celluleId: null }),
    ];
    const act = () => null;
    expect(filtrerAgents(agents, { ...FILTRES_VIDES, superviseurs: '*' }, act, false).map((a) => a.userId)).toEqual([2, 3]);
    expect(filtrerAgents(agents, FILTRES_VIDES, act, false, true).map((a) => a.userId)).toEqual([1, 2]);
    expect(filtrerAgents(agents, FILTRES_VIDES, act, false).map((a) => a.userId)).toEqual([1, 2, 3]);
    expect(estEncadrementNonPlace(agents[2])).toBe(true);
    expect(estEncadrementNonPlace(agents[1])).toBe(false);
  });
});
