#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 11 : contrôle final de conformité au fichier (LECTURE SEULE).

Compare l'export (étape 01) au fichier Contrat / Pôle / Cellule / Collaborateur :
  1. Chaque collaborateur du fichier : activité, pôle et cellule affichés dans l'application
     doivent être exactement ceux du fichier (libellés compris).
  2. Structure des activités du fichier : aucun pôle / cellule hors fichier (hors nœuds du
     fichier Périmètre).
  3. Planning : chaque cellule du fichier a son reflet Planning sous le même nom.

Écrit rapport/controle_conformite.csv ; code de sortie 0 = conforme, 1 = écarts.
"""
from __future__ import annotations

import argparse
import csv
import sys
from collections import defaultdict
from pathlib import Path

from reorg_commun import (
    HERE, KIND, export_dir, find_department, harmonize_pole_label, is_true, load_activity_mapping, norm, norm_pole,
    read_csv,
)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export")
    ap.add_argument("--cible", default=str(HERE / "donnees" / "organisation_cible.csv"))
    ap.add_argument("--complement", default=str(HERE / "donnees" / "organisation_complement.csv"))
    ap.add_argument("--deplacements", default=str(HERE / "donnees" / "deplacements_pilotes.csv"))
    ap.add_argument("--responsables", default=str(HERE / "donnees" / "responsables_cible.csv"))
    ap.add_argument("--correspondances", default=str(HERE / "donnees" / "correspondance_activites.csv"))
    args = ap.parse_args()

    export = export_dir(args.export)
    mapping = load_activity_mapping(Path(args.correspondances))
    depts_all = read_csv(export / "business_departments.csv")
    depts = [d for d in depts_all if d["Kind"] in ("0", "Operational")]
    dept_by_id = {d["Id"]: d for d in depts_all}
    poles = {p["Id"]: p for p in read_csv(export / "org_poles.csv")}
    cells = {c["Id"]: c for c in read_csv(export / "org_cellules.csv")}
    services = {s["Id"]: s for s in read_csv(export / "org_services.csv")}
    employees = {e["Id"]: e for e in read_csv(export / "employees.csv")}
    pilot_service: dict[str, list[str]] = defaultdict(list)
    for a in read_csv(export / "org_assignments_actives.csv"):
        if KIND.get(a["Kind"], a["Kind"]) == "Pilote":
            pilot_service[a["EmployeeId"]].append(a["NodeId"])

    rows_out: list[list[str]] = []
    ecarts = 0

    # 1. Collaborateurs du fichier
    expected = {r["employee_id"]: r for r in read_csv(Path(args.deplacements), ";")}
    for emp_id, r in sorted(expected.items(), key=lambda x: int(x[1]["ligne_excel"])):
        want = (r["activite_cible"], harmonize_pole_label(r["pole_cible"]), r["cellule_cible"])
        emp = employees.get(emp_id)
        if not emp:
            got, status = ("", "", ""), "COMPTE INTROUVABLE"
        else:
            svc_ids = pilot_service.get(emp_id, [])
            svc = services.get(svc_ids[0]) if len(svc_ids) == 1 else None
            cell = cells.get(svc["CelluleId"]) if svc else None
            pole = poles.get(cell["PoleId"]) if cell else None
            dept = dept_by_id.get(pole["BusinessDepartmentId"]) if pole else None
            got = (dept["Name"] if dept else "", pole["Name"] if pole else "", cell["Name"] if cell else "")
            if not is_true(emp.get("IsActive")):
                status = "COMPTE INACTIF"
            elif len(svc_ids) != 1:
                status = f"{len(svc_ids)} AFFECTATION(S) PILOTE"
            elif got == want:
                status = "OK"
            elif tuple(norm(x) for x in got) == tuple(norm(x) for x in want):
                status = "LIBELLÉ DIFFÉRENT"
            else:
                status = "MAUVAISE AFFECTATION"
        if status != "OK":
            ecarts += 1
        rows_out.append(["collaborateur", r["ligne_excel"], r["collaborateur_fichier"], " / ".join(want), " / ".join(got), status])

    # 2. Structure hors fichier dans les activités du fichier
    targets = read_csv(Path(args.cible), ";") + read_csv(Path(args.complement), ";")
    keep_cells: set[tuple[str, str, str]] = set()
    scope: set[str] = set()
    for t in targets:
        d = find_department(t["activite"], depts, mapping)
        if d:
            scope.add(d["Id"])
            keep_cells.add((d["Id"], norm_pole(t["pole"]), norm(t["cellule"])))
    keep_poles_extra: set[tuple[str, str]] = set()
    for r in read_csv(Path(args.responsables), ";"):
        if (r.get("action") or "").strip() != "ReferentTechnique":
            continue
        d = find_department(r["activite"], depts, mapping)
        if not d:
            continue
        wanted = [w.strip() for w in (r.get("cellules") or "").split("|") if w.strip()]
        if wanted == ["*"]:
            keep_poles_extra.add((d["Id"], norm_pole(r["pole"])))
        for w in wanted:
            keep_cells.add((d["Id"], norm_pole(r["pole"]), norm(w)))
    for c in cells.values():
        p = poles.get(c["PoleId"])
        if not p or p["BusinessDepartmentId"] not in scope:
            continue
        key = (p["BusinessDepartmentId"], norm_pole(p["Name"]), norm(c["Name"]))
        if key not in keep_cells and key[:2] not in keep_poles_extra:
            ecarts += 1
            dept = dept_by_id[p["BusinessDepartmentId"]]["Name"]
            rows_out.append(["structure", "", "", "", f"{dept} / {p['Name']} / {c['Name']}", "HORS FICHIER"])

    # 3. Reflet Planning des cellules du fichier
    mirrors = {r["PrimeServiceId"]: r["Name"] for r in read_csv(export / "planning_sub_services.csv") if r.get("PrimeServiceId")}
    if mirrors:
        for s in services.values():
            c = cells.get(s["CelluleId"])
            p = poles.get(c["PoleId"]) if c else None
            if not p or p["BusinessDepartmentId"] not in scope:
                continue
            name = mirrors.get(s["Id"])
            if name is None:
                status = "ABSENT DE PLANNING"
            elif name != s["Name"]:
                status = "NOM DIFFÉRENT DANS PLANNING"
            else:
                continue
            ecarts += 1
            rows_out.append(["planning", "", "", s["Name"], name or "", status])

    out = export / "rapport" / "controle_conformite.csv"
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["controle", "ligne_fichier", "collaborateur", "attendu (fichier)", "constaté (application)", "statut"])
        w.writerows(rows_out)

    ok = sum(1 for r in rows_out if r[5] == "OK")
    print(f"Collaborateurs du fichier conformes : {ok} / {len(expected)}")
    for r in rows_out:
        if r[5] != "OK":
            print(f"  ✘ [{r[0]}] {r[2] or ''} attendu « {r[3]} » — constaté « {r[4]} » : {r[5]}")
    print(f"\n{'✔ CONFORME AU FICHIER' if ecarts == 0 else f'✘ {ecarts} écart(s)'} — détail : {out}")
    return 0 if ecarts == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
