#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 9 : renommer le libellé d'une activité (département de production).

Libellés du fichier (décisions du 29/10) : OP-001 « bouygues » → « Bytel Kyntus »,
OP-002 « orange » → « Orange ». Seul le libellé change :
identifiant, code, pôles, affectations et historique sont conservés ; l'événement de mise à jour
propage le nouveau nom aux autres modules.

Usage :
  python3 09_renommer_activite.py                       # simulation (lecture via l'API)
  python3 09_renommer_activite.py --appliquer
  python3 09_renommer_activite.py --code OP-001 --nom "bouygues" --appliquer   # retour arrière (une activité)
  python3 09_renommer_activite.py --code OP-002 --nom "orange" --appliquer
"""
from __future__ import annotations

import argparse
import sys

from kyntus_api import KyntusApi

# Libellés exacts du fichier Contrat pour les activités existantes (Bytel TISSEO a été créé à ce nom).
FILE_LABELS = (("OP-001", "Bytel Kyntus"), ("OP-002", "Orange"))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--code", help="une seule activité (sinon : libellés du fichier)")
    ap.add_argument("--nom")
    ap.add_argument("--appliquer", action="store_true")
    args = ap.parse_args()
    renames = [(args.code, args.nom)] if args.code and args.nom else list(FILE_LABELS)

    api = KyntusApi()
    api.login_if_needed()
    depts = api.request("GET", "/api/directory/business-departments") or []
    status = 0
    for code, name in renames:
        status = max(status, rename(api, depts, code, name, args.appliquer))
    return status


def rename(api: KyntusApi, depts: list, code: str, name: str, appliquer: bool) -> int:
    dept = next((d for d in depts if (d.get("code") or "").upper() == code.upper()), None)
    if not dept:
        print(f"Activité {code} introuvable.")
        return 2
    new_name = name.strip()
    print(f"{dept['code']} : « {dept['name']} » → « {new_name} » "
          f"(type {dept['kind']}, {'actif' if dept['isActive'] else 'inactif'}, {len(dept.get('poleIds') or [])} pôle(s) inchangé(s))")
    if dept["name"] == new_name:
        print("  déjà à jour")
        return 0
    if not appliquer:
        print("  simulation uniquement (relancer avec --appliquer)")
        return 0
    # Type et statut renvoyés à l'identique : seul le libellé change.
    res = api.request("PATCH", f"/api/directory/business-departments/{dept['id']}",
                      {"name": new_name, "kind": dept["kind"], "isActive": dept["isActive"]})
    print(f"  ✔ {res['code']} — {res['name']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
