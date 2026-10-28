#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 5 (APRÈS VALIDATION) : application des règles de responsabilité.

Lit rapport/perimetres_responsables.csv : seules les lignes dont la colonne `valider` vaut OUI
sont traitées. Pour chaque (responsable, kind), le périmètre validé est appliqué via
PUT /api/directory/org/scope-assignments/{kind}/employees/{id} :
  Superviseur       → toutes les cellules des activités validées
  ChefDeProjet      → tous les pôles des activités validées
  ReferentTechnique → tous les services des cellules validées

  * Simulation par défaut ; --appliquer pour exécuter.
  * Le journal conserve les nœuds AVANT modification (issus de l'export) : 06_retour_arriere.py
    les restaure exactement (endpoint historique de synchronisation multi-nœuds).
  * Les affectations retirées sont clôturées (EffectiveTo), jamais supprimées : historique intact.
"""
from __future__ import annotations

import argparse
import csv
import json
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path

from kyntus_api import KyntusApi

HERE = Path(__file__).resolve().parent
KIND = {"0": "ChefDeProjet", "1": "Superviseur", "2": "ReferentTechnique", "3": "Pilote"}


def read_csv(path: Path, delimiter: str = ",") -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f, delimiter=delimiter))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export")
    ap.add_argument("--appliquer", action="store_true")
    args = ap.parse_args()

    export = Path(args.export) if args.export else Path((HERE / ".dernier_export").read_text().strip())
    rows = read_csv(export / "rapport" / "perimetres_responsables.csv", ";")
    validated = [r for r in rows if r.get("valider", "").strip().upper() == "OUI"]
    if not validated:
        print("Aucune ligne validée (colonne `valider` = OUI) : rien à faire.")
        return 0

    employees = {e["Id"]: e for e in read_csv(export / "employees.csv")}
    before: dict[tuple[str, str], list[str]] = defaultdict(list)
    for a in read_csv(export / "org_assignments_actives.csv"):
        before[(a["EmployeeId"], KIND.get(a["Kind"], a["Kind"]))].append(a["NodeId"])

    plan: dict[tuple[str, str], list[dict]] = defaultdict(list)
    for r in validated:
        if r["perimetre_id"] and not r["perimetre_id"].startswith("("):
            plan[(r["employee_id"], r["kind"])].append(r)

    print(f"{len(plan)} responsable(s) {'À MODIFIER' if args.appliquer else '(simulation)'} :")
    for (emp, kind), scopes in plan.items():
        primary = max(scopes, key=lambda s: int(s["noeuds_detenus"] or 0))
        print(f"  - {scopes[0]['nom']:<30} {kind:<18} → " + " | ".join(s["perimetre"] for s in scopes)
              + f"   (principal : {primary['perimetre']})")
    if not args.appliquer:
        print("\nSimulation uniquement. Relancez avec --appliquer après validation.")
        return 0

    api = KyntusApi()
    api.login_if_needed()
    journal = []
    journal_path = export / "rapport" / f"journal_perimetres_{datetime.now():%Y%m%d_%H%M%S}.json"
    anchor_col = {"ChefDeProjet": "PoleId", "Superviseur": "CelluleId", "ReferentTechnique": "ServiceId"}
    try:
        for (emp, kind), scopes in plan.items():
            primary = max(scopes, key=lambda s: int(s["noeuds_detenus"] or 0))
            res = api.request("PUT", f"/api/directory/org/scope-assignments/{kind}/employees/{emp}", {
                "scopeIds": [s["perimetre_id"] for s in scopes],
                "primaryScopeId": primary["perimetre_id"],
                "reason": "Réorganisation 2026 — périmètre validé",
            })
            journal.append({
                "employeeId": emp, "kind": kind,
                "nodesBefore": before.get((emp, kind), []),
                "anchorBefore": employees.get(emp, {}).get(anchor_col[kind], ""),
                "added": res["assignments"]["addedNodeIds"],
                "removed": res["assignments"]["removedNodeIds"],
            })
            print(f"  ✔ {scopes[0]['nom']:<30} +{len(res['assignments']['addedNodeIds'])} "
                  f"-{len(res['assignments']['removedNodeIds'])}")
    finally:
        journal_path.write_text(json.dumps(journal, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nJournal (retour arrière) : {journal_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
