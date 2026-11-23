import { describe, expect, it } from 'vitest';
import { StatutDemande, TypeConge, TypeCongeExceptionnel } from '../../../core/models/conge.models';
import {
  apercuModification,
  controlerModification,
  estModifiable,
  finExceptionnelle,
  joursOuvrables,
  type DemandeModifiable,
} from './conge-modification.util';

const annuel = (statut = StatutDemande.Validee): DemandeModifiable => ({
  typeConge: TypeConge.Annuel,
  typeExceptionnel: null,
  statut,
  dateDebut: '2026-12-21T00:00:00',
  dateFin: '2027-01-04T00:00:00',
});

describe('conge-modification.util (v64.2)', () => {
  it('compte les jours ouvrables comme le serveur (samedi et dimanche exclus)', () => {
    expect(joursOuvrables('2026-12-21', '2027-01-04')).toBe(11);
    expect(joursOuvrables('2026-12-26', '2026-12-27')).toBe(0);
    expect(joursOuvrables('2026-12-24', '2026-12-21')).toBe(0);
  });

  it('les demandes validées et en attente sont modifiables, pas les refusées ni les annulées', () => {
    expect(estModifiable(annuel(StatutDemande.Validee))).toBe(true);
    expect(estModifiable(annuel(StatutDemande.EnAttente))).toBe(true);
    expect(estModifiable(annuel(StatutDemande.EnAttenteRh))).toBe(true);
    expect(estModifiable(annuel(StatutDemande.Refusee))).toBe(false);
    expect(estModifiable(annuel(StatutDemande.Annulee))).toBe(false);
  });

  it('congé exceptionnel : la fin suit la durée fixe', () => {
    expect(finExceptionnelle('2026-12-28', TypeCongeExceptionnel.Mariage)).toBe('2026-12-31');
    const d: DemandeModifiable = { ...annuel(), typeConge: TypeConge.Exceptionnel, typeExceptionnel: TypeCongeExceptionnel.DecesParent };
    expect(apercuModification(d, '2026-12-31', '')).toEqual({ debut: '2026-12-31', fin: '2027-01-01', jours: 2 });
  });

  it('refuse un début déplacé dans le passé, des dates identiques ou une période sans jour ouvrable', () => {
    const d = annuel();
    expect(controlerModification(d, '2026-11-02', '2026-11-06', '2026-11-23')).toContain('passé');
    expect(controlerModification(d, '2026-12-21', '2027-01-04', '2026-11-23')).toContain('identiques');
    expect(controlerModification(d, '2026-12-26', '2026-12-27', '2026-11-23')).toContain('ouvrable');
    expect(controlerModification(d, '2026-12-22', '2026-12-21', '2026-11-23')).toContain('postérieure');
  });

  it('accepte de raccourcir un congé validé déjà commencé (début inchangé)', () => {
    const d = { ...annuel(), dateDebut: '2026-11-16', dateFin: '2026-11-27' };
    expect(controlerModification(d, '2026-11-16', '2026-11-20', '2026-11-23')).toBeNull();
    expect(apercuModification(d, '2026-11-16', '2026-11-20').jours).toBe(5);
  });
});
