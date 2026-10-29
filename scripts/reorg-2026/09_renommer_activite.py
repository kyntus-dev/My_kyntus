#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 9 : renommer le libellé d'une activité (département de production).

Décision du 29/10 : OP-001 « bouygues » → « Bytel Kyntus ». Seul le libellé change :
identifiant, code, pôles, affectations et historique sont conservés ; l'événement de mise à jour
propage le nouveau nom aux autres modules.

Usage :
  python3 09_renommer_activite.py                       # simulation (lecture via l'API)
  python3 09_renommer_activite.py --appliquer
  python3 09_renommer_activite.py --code OP-001 --nom "bouygues" --appliquer   # retour arrière
"""
from __future__ import annotations

import argparse
import sys

from kyntus_api import KyntusApi


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--code", default="OP-001")
    ap.add_argument("--nom", default="Bytel Kyntus")
    ap.add_argument("--appliquer", action="store_true")
    args = ap.parse_args()

    api = KyntusApi()
    api.login_if_needed()
    depts = api.request("GET", "/api/directory/business-departments") or []
    dept = next((d for d in depts if (d.get("code") or "").upper() == args.code.upper()), None)
    if not dept:
        print(f"Activité {args.code} introuvable.")
        return 2
    new_name = args.nom.strip()
    print(f"{dept['code']} : « {dept['name']} » → « {new_name} » "
          f"(type {dept['kind']}, {'actif' if dept['isActive'] else 'inactif'}, {len(dept.get('poleIds') or [])} pôle(s) inchangé(s))")
    if dept["name"] == new_name:
        print("Déjà à jour.")
        return 0
    if not args.appliquer:
        print("Simulation uniquement. Relancez avec --appliquer.")
        return 0
    # Type et statut renvoyés à l'identique : seul le libellé change.
    res = api.request("PATCH", f"/api/directory/business-departments/{dept['id']}",
                      {"name": new_name, "kind": dept["kind"], "isActive": dept["isActive"]})
    print(f"✔ {res['code']} — {res['name']} ({len(res.get('poleIds') or [])} pôle(s))")
    return 0


if __name__ == "__main__":
    sys.exit(main())
