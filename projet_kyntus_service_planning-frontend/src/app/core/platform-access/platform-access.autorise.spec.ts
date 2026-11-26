// @vitest-environment jsdom
import '@angular/compiler';
import { describe, expect, it } from 'vitest';
import { Injector } from '@angular/core';
import { of } from 'rxjs';
import { PlatformAccessService } from './platform-access.service';
import { PlatformIamApiService } from './platform-iam-api.service';
import { KyntusSessionService } from '../session/kyntus-session.service';

/**
 * v72 — Un bouton d'action n'apparaît que si le serveur acceptera l'action :
 * « si tu as le droit c'est bon, si tu ne l'as pas, rien n'est proposé ».
 */
async function droits(permissions: string[], retraits: string[] = [], role = 'Superviseur', adminRestreint = false) {
  const injector = Injector.create({
    providers: [
      { provide: PlatformAccessService },
      {
        provide: PlatformIamApiService,
        useValue: { getEffective: () => of({ permissions, retraits, superAdmin: false, adminRestreint }) },
      },
      { provide: KyntusSessionService, useValue: { getRole: () => role, isSuperAdmin: () => false } },
    ],
  });
  const service = injector.get(PlatformAccessService);
  await service.ensureLoaded(true);
  return service;
}

describe('PlatformAccessService.autorise — actions selon « Accès par rôle / par compte »', () => {
  it('propose « Générer » quand le droit Modifier est accordé', async () => {
    const s = await droits(['planning.generate.View:Cellule', 'planning.generate.Act:Cellule']);
    expect(s.autorise('planning.generate', 'Act')).toBe(true);
  });

  it('ne propose pas « Générer » quand seul Consulter reste (Modifier retiré)', async () => {
    const s = await droits(['planning.generate.View:Cellule'], ['planning.generate.Act']);
    expect(s.autorise('planning.generate', 'Act')).toBe(false);
    // La page reste consultable.
    expect(s.autorise('planning.generate', 'View')).toBe(true);
  });

  it('ne propose rien quand la rubrique entière est retirée', async () => {
    const s = await droits(['planning.generate.View:Cellule', 'planning.generate.Act:Cellule'], ['planning.generate.View']);
    expect(s.autorise('planning.generate', 'Act')).toBe(false);
    expect(s.autorise('planning.generate', 'View')).toBe(false);
  });

  it('accepte l’une ou l’autre des rubriques citées, comme le serveur', async () => {
    const s = await droits(['planning.self.View:Self']);
    expect(s.autorise(['planning.self', 'planning.change-requests'], 'Act')).toBe(true);
    expect(s.autorise('planning.change-requests', 'Act')).toBe(false);
  });

  it('un verbe que la rubrique ne propose pas suit Consulter (ex. « Mes congés »)', async () => {
    const s = await droits(['conges.self.View:Self'], [], 'Ingénieur');
    expect(s.autorise('conges.self', 'Act')).toBe(true);
  });

  it('Valider couvre Modifier quand la rubrique propose Valider', async () => {
    const s = await droits(['planning.validation.View:Cellule', 'planning.validation.Validate:Cellule']);
    expect(s.autorise('planning.validation', 'Act')).toBe(true);
    expect(s.autorise('planning.validation', 'Validate')).toBe(true);
  });

  it('Paramétrer exige le droit Paramétrer', async () => {
    const avec = await droits(['planning.shift-config.View:Cellule', 'planning.shift-config.Configure:Cellule']);
    expect(avec.autorise('planning.shift-config', 'Configure')).toBe(true);
    const sans = await droits(['planning.shift-config.View:Cellule'], ['planning.shift-config.Configure']);
    expect(sans.autorise('planning.shift-config', 'Configure')).toBe(false);
  });

  it('le joker Admin reste soumis aux retraits du Super Admin', async () => {
    const s = await droits(['*:Global'], ['planning.generate.Act'], 'Admin');
    expect(s.autorise('planning.generate', 'Act')).toBe(false);
    expect(s.autorise('planning.validation', 'Validate')).toBe(true);
  });
});

/**
 * v79 — « Configuration des horaires » et « Périmètre superviseur » : chacun ne voit que son
 * périmètre. Un droit limité à la cellule ne doit pas passer pour « toute l'entreprise ».
 */
describe('PlatformAccessService — périmètre global', () => {
  it('un droit Configurer limité à la cellule n’ouvre pas toute l’organisation', async () => {
    const s = await droits(['planning.shift-config.View:Cellule', 'planning.shift-config.Configure:Cellule'], [], 'Référent technique');
    expect(s.can('planning.shift-config', 'Configure', 'Global')).toBe(true); // ancien test, trompeur
    expect(s.accordeGlobalement('planning.shift-config', 'Configure')).toBe(false);
    expect(s.voitTousLesPerimetres('Référent technique')).toBe(false);
  });

  it('un droit accordé explicitement au niveau « Toute l’entreprise » est reconnu', async () => {
    const s = await droits(['planning.shift-config.Configure:Global'], [], 'Chef de projet');
    expect(s.accordeGlobalement('planning.shift-config', 'Configure')).toBe(true);
  });

  it('la rubrique « Voir les périmètres des autres encadrants » ouvre tout, à n’importe quel périmètre', async () => {
    const s = await droits(['perimetre.global.View:Cellule'], [], 'Superviseur');
    expect(s.voitTousLesPerimetres('Superviseur')).toBe(true);
  });

  it('Admin et RH voient toujours tout', async () => {
    const s = await droits([], [], 'RH');
    expect(s.voitTousLesPerimetres('RH')).toBe(true);
    expect(s.voitTousLesPerimetres('admin')).toBe(true);
  });
});
