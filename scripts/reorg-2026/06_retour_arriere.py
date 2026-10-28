#!/usr/bin/env python3
"""
Réorganisation 2026 — retour arrière applicatif (sans restauration de base).

  journal_perimetres_*.json : restaure exactement les nœuds détenus avant l'étape 5
                              (PUT /api/directory/org/assignments/{kind}/employees/{id}).
  journal_structure_*.json  : supprime, dans l'ordre inverse, les nœuds créés à l'étape 4
                              (services → cellules → pôles → activités). ATTENTION : l'API de
                              suppression ne vérifie pas l'usage d'un nœud ; ce script le fait
                              lui-même (overview Directory) et IGNORE tout nœud encore utilisé
                              par un collaborateur ou une affectation active.

Usage :
  python3 06_retour_arriere.py --journal CHEMIN/journal_xxx.json            # simulation
  python3 06_retour_arriere.py --journal CHEMIN/journal_xxx.json --appliquer

Retour arrière complet (dernier recours) : restauration des sauvegardes pg_dump de l'étape 1,
voir PROCEDURE_SERVEUR.md § Retour arrière.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from kyntus_api import KyntusApi


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--journal", required=True)
    ap.add_argument("--appliquer", action="store_true")
    args = ap.parse_args()

    path = Path(args.journal)
    entries = json.loads(path.read_text(encoding="utf-8"))
    is_scope = path.name.startswith("journal_perimetres")
    print(f"{len(entries)} entrée(s) à annuler {'(simulation)' if not args.appliquer else ''}")

    api = None
    if args.appliquer:
        api = KyntusApi()
        api.login_if_needed()

    if is_scope:
        for e in entries:
            nodes = e["nodesBefore"]
            label = f"{e['kind']} {e['employeeId']} → {len(nodes)} nœud(s) d'origine"
            if not nodes:
                print(f"  ! {label} : aucun nœud avant → à retirer manuellement dans Organisation RH")
                continue
            primary = e["anchorBefore"] if e["anchorBefore"] in nodes else nodes[0]
            print(f"  - {label}")
            if api:
                api.request("PUT", f"/api/directory/org/assignments/{e['kind']}/employees/{e['employeeId']}",
                            {"nodeIds": nodes, "primaryNodeId": primary, "reason": "Retour arrière réorganisation 2026"})
        return 0

    used: set[str] = set()
    if api:
        ov = api.request("GET", "/api/directory/org/overview") or {}
        for emp in ov.get("employees", []):
            for key in ("poleId", "celluleId", "serviceId", "businessDepartmentId"):
                if emp.get(key):
                    used.add(str(emp[key]))
        for a in ov.get("managerEtage", []):
            used.add(a.get("etageId", ""))
        for a in ov.get("supervisorService", []):
            used.add(a.get("celluleId") or a.get("serviceId") or "")
        for a in ov.get("coachSousService", []):
            used.add(a.get("serviceId") or a.get("sousServiceId") or "")

    order = {"service": 0, "cellule": 1, "pole": 2, "department": 3}
    for e in sorted(reversed(entries), key=lambda x: order[x["type"]]):
        print(f"  - suppression {e['type']:<10} {e['name']} ({e['id']})")
        if not api:
            continue
        if e["id"] in used:
            print("    ⚠ ignoré : nœud encore utilisé (collaborateur ou responsable rattaché)")
            continue
        try:
            if e["type"] == "service":
                api.request("DELETE", f"/api/directory/org/structure/cellules/{e['parent']}/services/{e['id']}")
            elif e["type"] == "cellule":
                api.request("DELETE", f"/api/directory/org/structure/poles/{e['parent']}/cellules/{e['id']}")
            elif e["type"] == "pole":
                api.request("DELETE", f"/api/directory/org/structure/poles/{e['id']}")
            else:
                api.request("DELETE", f"/api/directory/business-departments/{e['id']}")
        except RuntimeError as ex:
            print(f"    ⚠ non supprimé : {ex}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
