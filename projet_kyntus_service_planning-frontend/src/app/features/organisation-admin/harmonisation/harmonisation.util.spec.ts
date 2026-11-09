import { describe, expect, it } from 'vitest';
import {
  ARCHIVER,
  estDifferee,
  type EtatOrganisation,
  cle,
  estProtege,
  planifier,
  proposerDecisions,
  trouverEmploye,
} from './harmonisation.util';
import type { RefCollaborateur, RefContrat, RefResponsable } from './referentiel-organisation';
import { REFERENTIEL_COLLABORATEURS, REFERENTIEL_CONTRATS } from './referentiel-organisation';

const ref: RefContrat[] = [
  { nom: 'Bytel Kyntus', activite: 'Bytel', poles: [
    { nom: 'Pôle client', cellules: ['Gestion des RDV', 'Satcli'] },
    { nom: 'Pôle technique', cellules: ['Support technicien'] },
  ] },
  { nom: 'Bytel TISSEO', activite: 'Bytel', poles: [{ nom: 'Pôle technique', cellules: ['BTB'] }] },
];

function etat(): EtatOrganisation {
  return {
    contrats: [
      { id: 'd1', code: 'BYT', nom: 'BYTEL', poles: [
        { id: 'p1', nom: 'Pole Client', cellules: [
          { id: 'c1', nom: 'gestion des rdv', services: [{ id: 's1', nom: 'RDV' }] },
          { id: 'c2', nom: 'Satisfaction client', services: [{ id: 's2', nom: 'N1' }, { id: 's3', nom: 'N2' }] },
          { id: 'c3', nom: 'Cible Prio', services: [{ id: 's4', nom: 'Cible Prio' }] },
        ] },
        { id: 'p2', nom: 'Pôle technique', cellules: [{ id: 'c4', nom: 'Support technicien', services: [{ id: 's5', nom: 'Support technicien' }] }] },
        { id: 'p3', nom: 'Ancien pôle', cellules: [] },
      ] },
      { id: 'd2', code: 'OP-003', nom: 'IT', poles: [{ id: 'p9', nom: 'Réseau/Dev/Support', cellules: [] }] },
      { id: 'd3', code: 'FAC', nom: 'Facturation', poles: [] },
    ],
    polesSansContrat: [],
    employes: [
      { id: 'e1', prenom: 'Nour EL Houda', nom: 'ABDELOUAHAB', role: 'Pilote', poleId: 'p1', celluleId: 'c3', serviceId: 's4' },
      { id: 'e2', prenom: 'Ikram', nom: 'BEN MILOUD', role: 'Pilote', poleId: 'p2', celluleId: 'c4', serviceId: 's5' },
      { id: 'e3', prenom: 'Salah Eddine', nom: 'Zemmouri', role: 'Chef de projet', poleId: null, celluleId: null, serviceId: null },
      { id: 'e4', prenom: 'Iman', nom: 'KHALDI', role: 'Superviseur', poleId: null, celluleId: null, serviceId: null },
    ],
    affectations: [{ type: 'ChefDeProjet', employeId: 'e3', noeudId: 'p1' }],
  };
}

describe('harmonisation : correspondances', () => {
  it('écriture : accents, majuscules, « & », « pôle » en tête', () => {
    expect(cle('Pôle Client')).toBe(cle('pole client'));
    expect(cle('Prestations réseaux & CV')).toBe('prestations reseaux et cv');
    expect(cle('Sécurisation & BO ')).toBe(cle('securisation et bo'));
  });

  it('Facturation et IT ne sont jamais touchés', () => {
    expect(estProtege('IT')).toBe(true);
    expect(estProtege('Facturation')).toBe(true);
    expect(estProtege('Facturation clients')).toBe(true);
    expect(estProtege('Bytel Kyntus')).toBe(false);
    expect(estProtege('Itinérance')).toBe(false);
  });

  it('collaborateur trouvé quel que soit l’ordre prénom / nom', () => {
    const es = etat().employes;
    expect(trouverEmploye('ABDELOUAHAB Nour EL Houda', es)).toMatchObject({ id: 'e1' });
    expect(trouverEmploye('Salah Eddine ZEMMOURI', es)).toMatchObject({ id: 'e3' });
    expect(trouverEmploye('Inconnu Total', es)).toBeNull();
  });

  it('décisions proposées : même nom gardé, alias de contrat, le reste archivé', () => {
    const d = proposerDecisions(etat(), ref);
    expect(d.contrats).toEqual({ d1: 'Bytel Kyntus' });
    expect(d.poles).toMatchObject({ p1: 'Bytel Kyntus › Pôle client', p2: 'Bytel Kyntus › Pôle technique', p3: ARCHIVER });
    // « Satisfaction client » est proposée vers « Satcli » (ancien nom connu), Cible Prio vers Sécurisation (absente de ce mini-référentiel).
    expect(d.cellules).toMatchObject({ c1: 'Bytel Kyntus › Pôle client › Gestion des RDV', c2: 'Bytel Kyntus › Pôle client › Satcli', c3: ARCHIVER, c4: 'Bytel Kyntus › Pôle technique › Support technicien' });
    expect(d.poles['p9']).toBeUndefined();
  });
});

describe('harmonisation : plan', () => {
  const collaborateurs: RefCollaborateur[] = [
    { nom: 'ABDELOUAHAB Nour EL Houda', contrat: 'Bytel Kyntus', pole: 'Pôle client', cellule: 'Gestion des RDV' },
    { nom: 'BEN MILOUD Ikram', contrat: 'Bytel TISSEO', pole: 'Pôle technique', cellule: 'BTB' },
    { nom: 'Fantôme Absent', contrat: 'Bytel Kyntus', pole: 'Pôle client', cellule: 'Satcli' },
  ];
  const responsables: RefResponsable[] = [
    { type: 'ChefDeProjet', contrat: 'Bytel Kyntus', nom: 'Salah Eddine ZEMMOURI' },
    { type: 'Superviseur', contrat: 'Bytel Kyntus', nom: 'Iman KHALDI', pole: 'Pôle client' },
  ];

  it('renomme et crée ce qu’il faut, place les agents, archive seulement les nœuds vides', () => {
    const e = etat();
    const d = proposerDecisions(e, ref);
    d.cellules['c2'] = 'Bytel Kyntus › Pôle client › Satcli'; // choix de l'Admin : Satisfaction client → Satcli
    const plan = planifier(e, d, { ref, collaborateurs, responsables });
    const libelles = plan.etapes.map((x) => x.libelle);

    expect(libelles).toContain('Renommer le contrat « BYTEL » en « Bytel Kyntus »');
    expect(libelles).toContain('Créer le contrat « Bytel TISSEO »');
    expect(libelles).toContain('Renommer le pôle « Pole Client » en « Pôle client » (Bytel Kyntus)');
    expect(libelles).toContain('Renommer la cellule « gestion des rdv » en « Gestion des RDV » (Bytel Kyntus › Pôle client)');
    expect(libelles).toContain('Renommer la cellule « Satisfaction client » en « Satcli » (Bytel Kyntus › Pôle client)');
    expect(libelles).toContain('Renommer le service « RDV » en « Gestion des RDV »');
    // Satisfaction client a deux services : gardés, signalés.
    expect(plan.alertes.some((a) => a.includes('Satcli') && a.includes('2 services'))).toBe(true);
    // Agent de Cible Prio placé dans Gestion des RDV ; l'agent de TISSEO attend la création de BTB.
    expect(plan.etapes).toContainEqual(expect.objectContaining({ operation: { type: 'placerAgent', employeId: 'e1', serviceId: 's1' } }));
    expect(plan.introuvables).toEqual(['Fantôme Absent (Bytel Kyntus › Pôle client › Satcli)']);
    // Cible Prio garde un agent : pas archivée tant qu'il n'est pas déplacé ; l'ancien pôle vide l'est.
    expect(plan.alertes.some((a) => a.startsWith('Cellule « Cible Prio » non archivée'))).toBe(true);
    expect(libelles).toContain('Archiver le pôle « Ancien pôle »');
    // Responsables : chef de projet sur le pôle technique (déjà sur le pôle client), superviseur des cellules du pôle client.
    expect(libelles).toContain('ZEMMOURI Salah Eddine : chef de projet de Bytel Kyntus › Pôle technique');
    expect(libelles).not.toContain('ZEMMOURI Salah Eddine : chef de projet de Bytel Kyntus › Pôle client');
    expect(libelles).toContain('KHALDI Iman : superviseur de Bytel Kyntus › Pôle client › Gestion des RDV');
    // IT et Facturation : aucune opération.
    expect(plan.etapes.some((x) => JSON.stringify(x.operation).includes('p9') || JSON.stringify(x.operation).includes('d2'))).toBe(false);
  });

  it('une cellule rangée sous un autre pôle est déplacée (regroupée sous le bon pôle)', () => {
    const e = etat();
    e.contrats[0].poles[1].cellules.push({ id: 'c5', nom: 'Satcli', services: [{ id: 's6', nom: 'Satcli' }] });
    const d = proposerDecisions(e, ref);
    expect(d.cellules['c5']).toBe('Bytel Kyntus › Pôle client › Satcli');
    d.cellules['c2'] = ARCHIVER; // l'Admin archive « Satisfaction client » : « Satcli » du pôle technique est déplacée
    const plan = planifier(e, d, { ref, collaborateurs: [], responsables: [] });
    expect(plan.etapes).toContainEqual(expect.objectContaining({
      operation: { type: 'regrouperCellules', sourceIds: ['c5'], cibleId: null, nomCible: 'Satcli', poleId: 'p1' },
    }));
  });

  it('après application (état conforme), il ne reste rien à faire', () => {
    const conforme: EtatOrganisation = {
      contrats: [{ id: 'd1', code: 'B', nom: 'Bytel Kyntus', poles: [
        { id: 'p1', nom: 'Pôle client', cellules: [
          { id: 'c1', nom: 'Gestion des RDV', services: [{ id: 's1', nom: 'Gestion des RDV' }] },
          { id: 'c2', nom: 'Satcli', services: [{ id: 's2', nom: 'Satcli' }] }] },
        { id: 'p2', nom: 'Pôle technique', cellules: [{ id: 'c4', nom: 'Support technicien', services: [{ id: 's5', nom: 'Support technicien' }] }] }] },
      { id: 'd4', code: 'T', nom: 'Bytel TISSEO', poles: [{ id: 'p5', nom: 'Pôle technique', cellules: [{ id: 'c6', nom: 'BTB', services: [{ id: 's7', nom: 'BTB' }] }] }] }],
      polesSansContrat: [],
      employes: [{ id: 'e1', prenom: 'Nour EL Houda', nom: 'ABDELOUAHAB', role: 'Pilote', poleId: 'p1', celluleId: 'c1', serviceId: 's1' }],
      affectations: [],
    };
    const plan = planifier(conforme, proposerDecisions(conforme, ref), { ref, collaborateurs: collaborateurs.slice(0, 1), responsables: [] });
    expect(plan.etapes).toEqual([]);
  });

  it('référentiel réel : 5 contrats, 12 pôles, 29 cellules, pas de doublon de cellule dans un pôle', () => {
    expect(REFERENTIEL_CONTRATS.map((c) => c.nom)).toEqual(['Bytel Kyntus', 'Bytel TISSEO', 'Orange', 'SFR / ATS', 'Formation & Qualité']);
    expect(REFERENTIEL_CONTRATS.flatMap((c) => c.poles)).toHaveLength(12);
    expect(REFERENTIEL_CONTRATS.flatMap((c) => c.poles.flatMap((p) => p.cellules))).toHaveLength(29);
    for (const c of REFERENTIEL_CONTRATS) for (const p of c.poles) expect(new Set(p.cellules.map(cle)).size).toBe(p.cellules.length);
    // Chaque collaborateur du fichier vise une cellule du référentiel.
    const cellules = new Set(REFERENTIEL_CONTRATS.flatMap((c) => c.poles.flatMap((p) => p.cellules.map((x) => `${c.nom}|${p.nom}|${x}`))));
    expect(REFERENTIEL_COLLABORATEURS.filter((x) => !cellules.has(`${x.contrat}|${x.pole}|${x.cellule}`))).toEqual([]);
  });
});

describe('harmonisation : noms proches', () => {
  it('un pôle « Sécurisation » de Bytel Kyntus devient « Sécurisation & BO », jamais un pôle de TISSEO', () => {
    const e: EtatOrganisation = {
      contrats: [{ id: 'd1', code: 'B', nom: 'BYTEL', poles: [{ id: 'p1', nom: 'Sécurisation', cellules: [
        { id: 'c1', nom: 'Prestations réseaux', services: [] }] },
        { id: 'p2', nom: 'Pole technique', cellules: [{ id: 'c2', nom: 'Prestations réseaux', services: [] }] }] }],
      polesSansContrat: [], employes: [], affectations: [],
    };
    const d = proposerDecisions(e, REFERENTIEL_CONTRATS);
    expect(d.poles['p1']).toBe('Bytel Kyntus › Sécurisation & BO');
    expect(d.cellules['c2']).toBe('Bytel Kyntus › Pôle technique › Prestations réseaux & CV');
    // Les deux « Prestations réseaux » vont vers la même cellule (elles seront regroupées).
    expect(d.cellules['c1']).toBe('Bytel Kyntus › Pôle technique › Prestations réseaux & CV');
  });
});

describe('harmonisation : organisation actuelle (capture de production)', () => {
  it('bouygues → Bytel Kyntus ; anciennes cellules proposées vers celles du fichier', () => {
    const cel = (id: string, nom: string) => ({ id, nom, services: [{ id: 's' + id, nom }] });
    const e: EtatOrganisation = {
      contrats: [
        { id: 'b', code: 'BOU', nom: 'bouygues', poles: [
          { id: 'bte', nom: 'BTE', cellules: [cel('c0', 'BTE')] },
          { id: 'pc', nom: 'Pôle client', cellules: [cel('c1', 'Cible Prio'), cel('c2', 'Expert'), cel('c3', 'Gestion des retards'), cel('c4', 'GRE'),
            cel('c5', 'MIGRATION'), cel('c6', 'Planification PDC + Lissage'), cel('c7', 'Préparation des RDV'), cel('c8', 'Rang 4 et plus & GRE et'),
            cel('c9', 'Satisfaction client'), cel('c10', 'Support Client'), cel('c11', 'Support VIP'), cel('c12', 'Ticket CO')] },
          { id: 'pe', nom: 'Pôle escalade et contrôle', cellules: [cel('c13', 'CR en retard / clotures'), cel('c14', 'Malfaçons'), cel('c15', 'Ticket qualité')] },
          { id: 'pt', nom: 'Pôle technicien', cellules: [cel('c16', 'Support Technicien RAC / SAV / PRESTA RESEAUX / Support VIP')] },
          { id: 'pq', nom: 'Pôle technique', cellules: [cel('c17', 'Prestations réseaux & CV'), cel('c18', 'Support technicien')] }] },
        { id: 't', code: 'TIS', nom: 'Bytel TISSEO', poles: [] },
        { id: 'x', code: 'TST', nom: 'TEST ACTIVITE', poles: [] },
        { id: 'i', code: 'OP-003', nom: 'IT', poles: [] },
      ],
      polesSansContrat: [], employes: [], affectations: [],
    };
    const d = proposerDecisions(e, REFERENTIEL_CONTRATS);
    expect(d.contrats).toEqual({ b: 'Bytel Kyntus', t: 'Bytel TISSEO', x: ARCHIVER });
    const K = 'Bytel Kyntus';
    expect(d.poles).toMatchObject({ bte: ARCHIVER, pc: `${K} › Pôle client`, pe: ARCHIVER, pt: ARCHIVER, pq: `${K} › Pôle technique` });
    expect(d.cellules).toMatchObject({
      c0: ARCHIVER, c2: ARCHIVER, c3: ARCHIVER, c11: ARCHIVER, c13: ARCHIVER,
      c1: `${K} › Sécurisation & BO › Sécurisation`, c4: `${K} › Sécurisation & BO › Sécurisation`,
      c5: `${K} › Sécurisation & BO › Sécurisation`, c8: `${K} › Sécurisation & BO › Sécurisation`,
      c6: `${K} › Pôle client › Planification`, c7: `${K} › Pôle client › Gestion des RDV`, c9: `${K} › Pôle client › Satcli`,
      c10: `${K} › Pôle client › Support client`, c12: `${K} › Sécurisation & BO › Tickets Co`,
      c14: `${K} › Sécurisation & BO › Malfaçons`, c15: `${K} › Pôle client › Tickets qualité`,
      c16: `${K} › Pôle technique › Support technicien`,
      c17: `${K} › Pôle technique › Prestations réseaux & CV`, c18: `${K} › Pôle technique › Support technicien`,
    });
    // Plan : le pôle Sécurisation & BO est créé, puis les 4 cellules regroupées sous « Sécurisation » au passage suivant.
    const plan = planifier(e, d, { collaborateurs: [], responsables: [] });
    expect(plan.etapes.map((x) => x.libelle)).toContain('Créer le pôle « Sécurisation & BO » dans Bytel Kyntus');
    expect(plan.etapes.map((x) => x.libelle)).toContain('Renommer le contrat « bouygues » en « Bytel Kyntus »');
  });
});

describe('harmonisation : aucun nœud du fichier oublié', () => {
  it('organisation vide : chaque contrat, pôle et cellule du fichier figure dans l’aperçu', () => {
    const vide: EtatOrganisation = { contrats: [], polesSansContrat: [], employes: [], affectations: [] };
    const plan = planifier(vide, proposerDecisions(vide), { collaborateurs: [], responsables: [] });
    const ops = plan.etapes.map((e) => e.operation);
    expect(ops.filter((o) => o.type === 'creerContrat')).toHaveLength(5);
    expect(ops.filter((o) => o.type === 'creerPole')).toHaveLength(12);
    expect(ops.filter((o) => o.type === 'creerCellule')).toHaveLength(29);
    // Pôles et cellules attendent leur parent : exécutés dans la foulée, pas oubliés.
    expect(plan.etapes.filter((e) => e.phase === 'cellules').every((e) => estDifferee(e.operation))).toBe(true);
    expect(plan.etapes.map((e) => e.libelle)).toContain(
      'Créer la cellule « Sécurisation » et son service (Bytel Kyntus › Sécurisation & BO) — après création du pôle');
  });

  it('bouygues : la cellule Sécurisation apparaît (regroupement de Cible Prio, GRE, MIGRATION) avant même la création du pôle', () => {
    const cel = (id: string, nom: string) => ({ id, nom, services: [{ id: 's' + id, nom }] });
    const e: EtatOrganisation = {
      contrats: [{ id: 'b', code: 'BOU', nom: 'bouygues', poles: [
        { id: 'pc', nom: 'Pôle client', cellules: [cel('c1', 'Cible Prio'), cel('c4', 'GRE'), cel('c5', 'MIGRATION')] }] }],
      polesSansContrat: [], employes: [], affectations: [],
    };
    const plan = planifier(e, proposerDecisions(e), { collaborateurs: [], responsables: [] });
    const libelles = plan.etapes.map((x) => x.libelle);
    expect(libelles).toContain('Créer le pôle « Sécurisation & BO » dans Bytel Kyntus');
    expect(libelles).toContain('Regrouper « Cible Prio », « GRE », « MIGRATION » dans la cellule « Sécurisation » (Bytel Kyntus › Sécurisation & BO) — après création du pôle');
    expect(libelles).toContain('Créer la cellule « Support Sécu » et son service (Bytel Kyntus › Sécurisation & BO) — après création du pôle');
  });

  it('nom proche proposé pour un collaborateur introuvable, sans déplacement', () => {
    const e: EtatOrganisation = { contrats: [], polesSansContrat: [], affectations: [],
      employes: [{ id: 'x', prenom: 'Safae', nom: 'MALHAOUI', role: 'Pilote', poleId: null, celluleId: null, serviceId: null }] };
    const plan = planifier(e, proposerDecisions(e), {
      collaborateurs: [{ nom: 'MELHAOUI Safae', contrat: 'Bytel Kyntus', pole: 'Sécurisation & BO', cellule: 'Malfaçons' }], responsables: [] });
    expect(plan.introuvables[0]).toContain('nom proche dans l\'application : MALHAOUI Safae ?');
    expect(plan.etapes.some((x) => x.operation.type === 'placerAgent')).toBe(false);
  });
});
