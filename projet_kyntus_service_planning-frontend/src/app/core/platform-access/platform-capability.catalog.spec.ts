import { describe, expect, it } from 'vitest';
import { KyntusRoleNames } from '../org/kyntus-role-names';
import { PLATFORM_SCREENS, verbsForScreenRole } from './platform-capability.catalog';

describe('platform capability catalog', () => {
  it('lets Superviseur configure shift templates in their cellule', () => {
    const screen = PLATFORM_SCREENS.find((s) => s.id === 'planning.shift-config');
    expect(screen).toBeTruthy();
    expect(verbsForScreenRole(screen!, KyntusRoleNames.Admin)).toContain('Configure');
    expect(verbsForScreenRole(screen!, KyntusRoleNames.Superviseur)).toEqual(
      expect.arrayContaining(['View', 'Configure']),
    );
  });

  it('reserves publish validation to Admin/RH (double validation)', () => {
    const screen = PLATFORM_SCREENS.find((s) => s.id === 'planning.publish');
    expect(verbsForScreenRole(screen!, KyntusRoleNames.RH)).toContain('Validate');
    expect(verbsForScreenRole(screen!, KyntusRoleNames.Superviseur)).toEqual([]);
  });

  it('exposes inbox anomalies as an Admin/RH action screen', () => {
    const screen = PLATFORM_SCREENS.find((s) => s.id === 'admin.anomalies');
    expect(screen).toBeTruthy();
    expect(verbsForScreenRole(screen!, KyntusRoleNames.Admin)).toEqual(expect.arrayContaining(['View', 'Act']));
    expect(verbsForScreenRole(screen!, KyntusRoleNames.Audit)).toEqual([]);
  });

  it('lets Superviseur view planning validation without publish rights', () => {
    const screen = PLATFORM_SCREENS.find((s) => s.id === 'planning.validation');
    expect(verbsForScreenRole(screen!, KyntusRoleNames.Superviseur)).toEqual(['View']);
    expect(verbsForScreenRole(screen!, KyntusRoleNames.RH)).toEqual(
      expect.arrayContaining(['View', 'Validate']),
    );
  });

  it('lets Superviseur generate planning by default', () => {
    const screen = PLATFORM_SCREENS.find((s) => s.id === 'planning.generate');
    expect(verbsForScreenRole(screen!, KyntusRoleNames.Superviseur)).toContain('Act');
  });
});
