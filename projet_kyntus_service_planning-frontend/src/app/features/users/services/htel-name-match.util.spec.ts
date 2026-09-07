import { describe, expect, it } from 'vitest';
import {
  findUniqueHtelMatch,
  normalizeHtelName,
  scoreEmployeeToHtel,
  HTEL_MATCH_ALERT_THRESHOLD,
  HTEL_MATCH_AUTO_LINK_THRESHOLD,
  type HtelNameMatchTechnicien,
} from './htel-name-match.util';

function tech(id: number, name: string): HtelNameMatchTechnicien {
  return { idTechnicien: id, technicien: name };
}

describe('HTEL name fuzzy match', () => {
  it('normalizes accents, case, punctuation and spaces', () => {
    expect(normalizeHtelName('  Garçon-Jean  ')).toBe('garcon jean');
    expect(normalizeHtelName('Dupont   Jean')).toBe('dupont jean');
  });

  it.each([
    ['Jean', 'Dupont', 'Dupont Jean'],
    ['Jean', 'Dupont', 'Jean Dupont'],
    ['Jean', 'Dupont', '  Dupont   Jean  '],
    ['Jean', 'Dupont', 'Dupontt Jean'],
    ['Jean', 'Dupont', 'Dupond Jean'],
    ['Jean', 'Dupont', 'Dupont Jeaan'],
    ['Fatima Zahra', 'Aarab', 'AARAB Fatima Zahra'],
    ['Jean-Pierre', 'Dupont', 'Dupont Jean Pierre'],
    ['Marie', 'Martin', 'Martin Mariee'],
  ])('scores %s %s vs %s as auto-link', (first, last, technicien) => {
    const score = scoreEmployeeToHtel(first, last, technicien);
    expect(score).toBeGreaterThanOrEqual(HTEL_MATCH_AUTO_LINK_THRESHOLD);
  });

  it('rejects distant names', () => {
    expect(scoreEmployeeToHtel('Jean', 'Dupont', 'Bernard Paul')).toBeLessThan(
      HTEL_MATCH_ALERT_THRESHOLD,
    );
  });

  it('findUniqueHtelMatch returns unique near match', () => {
    const list = [tech(1, 'Dupontt Jean'), tech(2, 'Bernard Paul')];
    expect(findUniqueHtelMatch('Jean', 'Dupont', list)?.idTechnicien).toBe(1);
  });

  it('findUniqueHtelMatch returns null when two close matches', () => {
    const list = [tech(1, 'Dupont Jean'), tech(2, 'Jean Dupont')];
    expect(findUniqueHtelMatch('Jean', 'Dupont', list)).toBeNull();
  });
});
