#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 4 (APRÈS VALIDATION) : création des activités / pôles / cellules
/ services manquants, via l'API (événements outbox → Planning, Prime, Congé… restent synchronisés).

  * Mode simulation par défaut : n'appelle aucune API d'écriture.
  * --appliquer : exécute les créations ADDITIVES de rapport/plan_structure.json.
    Aucune suppression, aucun renommage, aucune modification d'affectation ni de collaborateur.
    Noms de pôles harmonisés par l'étape 03 (« Pôle technique », « Pôle BTB »).
  * Chaque création est journalisée (rapport/journal_structure_<horodatage>.json)
    pour le retour arrière (06_retour_arriere.py).

Usage :
  python3 04_appliquer_structure.py                 # simulation
  KYNTUS_EMAIL=... python3 04_appliquer_structure.py --appliquer
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from datetime import datetime

from kyntus_api import KyntusApi
from reorg_commun import export_dir


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export")
    ap.add_argument("--appliquer", action="store_true", help="exécuter réellement (sinon simulation)")
    args = ap.parse_args()

    export = export_dir(args.export)
    plan = json.loads((export / "rapport" / "plan_structure.json").read_text(encoding="utf-8"))
    actions = plan["actions"]
    if not actions:
        print("Aucune action : la structure cible existe déjà.")
        return 0

    print(f"{len(actions)} action(s) {'À EXÉCUTER' if args.appliquer else '(simulation)'} :")
    for a in actions:
        print(f"  - {a['action']:<18} {a['name']}")
    if not args.appliquer:
        print("\nSimulation uniquement. Relancez avec --appliquer après validation.")
        return 0

    api = KyntusApi()
    api.login_if_needed()
    refs: dict[str, str] = {}
    journal: list[dict] = []
    journal_path = export / "rapport" / f"journal_structure_{datetime.now():%Y%m%d_%H%M%S}.json"

    def resolve(value: str) -> str:
        return refs.get(value, value)

    try:
        for a in actions:
            kind = a["action"]
            if kind == "create_department":
                res = api.request("POST", "/api/directory/business-departments",
                                  {"name": a["name"], "kind": "Operational"})
                new_id = res["id"]
                journal.append({"type": "department", "id": new_id, "name": a["name"]})
            elif kind == "create_pole":
                res = api.request("POST", "/api/directory/org/structure/poles",
                                  {"name": a["name"], "businessDepartmentId": resolve(a["department"])})
                new_id = res["id"]
                journal.append({"type": "pole", "id": new_id, "name": a["name"]})
            elif kind == "create_cellule":
                pole_id = resolve(a["pole"])
                res = api.request("POST", f"/api/directory/org/structure/poles/{pole_id}/cellules", {"name": a["name"]})
                new_id = res["id"]
                journal.append({"type": "cellule", "id": new_id, "parent": pole_id, "name": a["name"]})
            elif kind == "create_service":
                cell_id = resolve(a["cellule"])
                res = api.request("POST", f"/api/directory/org/structure/cellules/{cell_id}/services", {"name": a["name"]})
                new_id = res["id"]
                journal.append({"type": "service", "id": new_id, "parent": cell_id, "name": a["name"]})
            else:
                raise RuntimeError(f"Action inconnue : {kind}")
            refs[a["ref"]] = new_id
            journal[-1]["ref"] = a["ref"]
            print(f"  ✔ {kind:<18} {a['name']}  → {new_id}")
            time.sleep(0.2)  # laisser l'outbox projeter vers Planning / Prime
    finally:
        journal_path.write_text(json.dumps(journal, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nJournal (retour arrière) : {journal_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
