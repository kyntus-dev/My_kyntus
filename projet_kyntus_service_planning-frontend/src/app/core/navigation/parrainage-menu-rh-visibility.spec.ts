import { describe, expect, it } from 'vitest';
import { buildParrainageMenuItemsForRole } from './parrainage-menu.config';
import { mapJwtRoleToParrainageRole } from '../session/kyntus-role-ui.config';
import { filterItemsByHat } from './workspace-hat.util';

describe('Parrainage menu RH visibility', () => {
  it('maps JWT RH to Parrainage RH (not stale PILOTE)', () => {
    expect(mapJwtRoleToParrainageRole('RH')).toBe('RH');
    expect(mapJwtRoleToParrainageRole('')).toBe('PILOTE');
  });

  it('keeps RH items visible under team hat', () => {
    const items = buildParrainageMenuItemsForRole('RH');
    expect(items.length).toBeGreaterThan(0);
    const visible = filterItemsByHat(items, 'team', true);
    expect(visible.some((i) => i.parrainageView === 'rh-dashboard')).toBe(true);
  });

  it('hides RH items under self hat (dual-hat)', () => {
    const items = buildParrainageMenuItemsForRole('RH');
    const visible = filterItemsByHat(items, 'self', true);
    expect(visible.every((i) => i.parrainageView !== 'rh-dashboard')).toBe(true);
  });

  it('wrong PILOTE role under team hat yields empty group (regression)', () => {
    const items = buildParrainageMenuItemsForRole('PILOTE');
    const visible = filterItemsByHat(items, 'team', true);
    expect(visible.length).toBe(0);
  });
});
