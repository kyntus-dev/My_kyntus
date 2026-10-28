#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 3 : plan de la nouvelle organisation (lecture seule).

Compare la structure cible (fichier : Contrat → Pôle → Cellule) avec la structure
Employee Directory (Département de production → Pôle → Cellule → Service) et produit :
  rapport/plan_organisation.md    : différences lisibles + impact sur les responsables
  rapport/plan_structure.json     : actions ADDITIVES proposées (créations uniquement)
  rapport/perimetres_responsables.csv : périmètre actuel vs cible de chaque responsable
                                        (colonne "valider" à compléter : OUI / NON)

Correspondance des colonnes :
  Contrat  (fichier) → Département de production / « activité »  (business_departments, Kind=Operational)
  Pôle     (fichier) → Pôle                                       (org_poles)
  Cellule  (fichier) → Cellule                                    (org_cellules)
  —                  → Service : niveau technique conservé, 1 service miroir par cellule (même nom)

Si une activité du fichier correspond à un département existant sous un autre nom
(ex. « Bytel Kyntus » ↔ « bouygues »), renseignez donnees/correspondance_activites.csv.
Rien n'est supprimé : les nœuds absents du fichier sont seulement listés.
"""
from __future__ import annotations

import argparse
import csv
import json
import re
import sys
import unicodedata
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
KIND = {"0": "ChefDeProjet", "1": "Superviseur", "2": "ReferentTechnique", "3": "Pilote"}


def norm(value: str | None) -> str:
    value = (value or "").replace("\xa0", " ")
    value = unicodedata.normalize("NFKD", value)
    value = "".join(ch for ch in value if not unicodedata.combining(ch))
    value = re.sub(r"[^A-Za-z0-9&/ ]+", " ", value)
    return re.sub(r"\s+", " ", value).strip().upper()


def norm_pole(value: str | None) -> str:
    return re.sub(r"^POLE ", "", norm(value))


def read_csv(path: Path, delimiter: str = ",") -> list[dict[str, str]]:
    if not path.exists():
        return []
    with path.open(newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f, delimiter=delimiter))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export")
    ap.add_argument("--cible", default=str(HERE / "donnees" / "organisation_cible.csv"))
    ap.add_argument("--correspondances", default=str(HERE / "donnees" / "correspondance_activites.csv"))
    args = ap.parse_args()

    export = Path(args.export) if args.export else Path((HERE / ".dernier_export").read_text().strip())
    out = export / "rapport"
    out.mkdir(parents=True, exist_ok=True)

    target = read_csv(Path(args.cible), ";")
    depts = [d for d in read_csv(export / "business_departments.csv") if d["Kind"] in ("0", "Operational")]
    poles = read_csv(export / "org_poles.csv")
    cellules = read_csv(export / "org_cellules.csv")
    services = read_csv(export / "org_services.csv")
    employees = {e["Id"]: e for e in read_csv(export / "employees.csv")}
    assignments = read_csv(export / "org_assignments_actives.csv")
    mapping = {
        norm(r["activite_fichier"]): r["departement_existant"].strip()
        for r in read_csv(Path(args.correspondances), ";")
        if r.get("departement_existant", "").strip()
    }

    # Arbre cible
    tree: dict[str, dict[str, dict[str, int]]] = defaultdict(lambda: defaultdict(lambda: defaultdict(int)))
    labels: dict[str, str] = {}
    for r in target:
        a, p, c = r["activite"], r["pole"], r["cellule"]
        labels.setdefault(norm(a), a)
        labels.setdefault("P|" + norm(a) + "|" + norm_pole(p), p)
        labels.setdefault("C|" + norm(a) + "|" + norm_pole(p) + "|" + norm(c), c)
        if r["type_ligne"] == "agent":
            tree[norm(a)][norm_pole(p)][norm(c)] += 1
        else:
            tree[norm(a)][norm_pole(p)].setdefault(norm(c), 0)

    def find_dept(activity_key: str) -> dict | None:
        wanted = mapping.get(activity_key)
        for d in depts:
            if wanted and (d["Code"] == wanted or norm(d["Name"]) == norm(wanted) or d["Id"] == wanted):
                return d
        for d in depts:
            if norm(d["Name"]) == activity_key:
                return d
        return None

    actions: list[dict] = []
    lines: list[str] = ["# Plan de réorganisation — activités, pôles, cellules", ""]
    lines += ["Correspondance des colonnes : **Contrat → Département de production (activité)**, "
              "**Pôle → Pôle**, **Cellule → Cellule**, Service = niveau technique (1 service miroir par cellule).", ""]
    matched_nodes: set[str] = set()
    target_scope: dict[str, list[str]] = {}

    for a_key, poles_t in tree.items():
        dept = find_dept(a_key)
        a_label = labels[a_key]
        if dept:
            matched_nodes.add(dept["Id"])
            lines.append(f"## Activité « {a_label} » → existante : {dept['Code']} — {dept['Name']}")
        else:
            lines.append(f"## Activité « {a_label} » → **À CRÉER** (département de production)")
            actions.append({"action": "create_department", "name": a_label, "ref": f"A:{a_key}"})
        dept_ref = dept["Id"] if dept else f"A:{a_key}"
        target_scope[a_label] = []

        for p_key, cells_t in poles_t.items():
            p_label = labels["P|" + a_key + "|" + p_key]
            pole = None
            if dept:
                pole = next((p for p in poles if p["BusinessDepartmentId"] == dept["Id"] and norm_pole(p["Name"]) == p_key), None)
            other_dept_pole = None
            if not pole:
                other_dept_pole = next((p for p in poles if norm_pole(p["Name"]) == p_key), None)
            if pole:
                matched_nodes.add(pole["Id"])
                lines.append(f"- Pôle « {p_label} » : existant")
            else:
                hint = ""
                if other_dept_pole:
                    hint = f" (un pôle homonyme existe ailleurs : id {other_dept_pole['Id']} — rattachement possible au lieu d'une création)"
                lines.append(f"- Pôle « {p_label} » : **À CRÉER**{hint}")
                actions.append({"action": "create_pole", "name": p_label, "department": dept_ref,
                                "ref": f"P:{a_key}|{p_key}", "homonym_pole_id": (other_dept_pole or {}).get("Id")})
            pole_ref = pole["Id"] if pole else f"P:{a_key}|{p_key}"

            for c_key, n_agents in cells_t.items():
                c_label = labels["C|" + a_key + "|" + p_key + "|" + c_key]
                cell = None
                if pole:
                    cell = next((c for c in cellules if c["PoleId"] == pole["Id"] and norm(c["Name"]) == c_key), None)
                if cell:
                    matched_nodes.add(cell["Id"])
                    svc = [s for s in services if s["CelluleId"] == cell["Id"]]
                    for s in svc:
                        matched_nodes.add(s["Id"])
                    svc_note = f"{len(svc)} service(s)" if svc else "**aucun service → service miroir À CRÉER**"
                    lines.append(f"  - Cellule « {c_label} » : existante · {svc_note} · {n_agents} agent(s) dans le fichier")
                    if not svc:
                        actions.append({"action": "create_service", "name": c_label, "cellule": cell["Id"],
                                        "ref": f"S:{a_key}|{p_key}|{c_key}"})
                else:
                    lines.append(f"  - Cellule « {c_label} » : **À CRÉER** (+ service miroir) · {n_agents} agent(s)")
                    actions.append({"action": "create_cellule", "name": c_label, "pole": pole_ref,
                                    "ref": f"C:{a_key}|{p_key}|{c_key}"})
                    actions.append({"action": "create_service", "name": c_label,
                                    "cellule": f"C:{a_key}|{p_key}|{c_key}", "ref": f"S:{a_key}|{p_key}|{c_key}"})
                target_scope[a_label].append(c_label)
        lines.append("")

    # Nœuds existants hors fichier (conservés)
    staff_by_node: dict[str, int] = defaultdict(int)
    for e in employees.values():
        if e["IsActive"].lower() in ("t", "true"):
            for col in ("PoleId", "CelluleId", "ServiceId"):
                if e.get(col):
                    staff_by_node[e[col]] += 1
    lines += ["## Existant absent du fichier — CONSERVÉ (aucune suppression, aucune désactivation)", ""]
    lines.append("| Niveau | Nom | Rattachement | Collaborateurs actifs rattachés |")
    lines.append("|---|---|---|---|")
    dept_name = {d["Id"]: f"{d['Code']} — {d['Name']}" for d in depts}
    pole_name = {p["Id"]: p["Name"] for p in poles}
    cell_name = {c["Id"]: c["Name"] for c in cellules}
    for d in depts:
        if d["Id"] not in matched_nodes:
            lines.append(f"| Activité | {d['Code']} — {d['Name']} | — | — |")
    for p in poles:
        if p["Id"] not in matched_nodes:
            lines.append(f"| Pôle | {p['Name']} | {dept_name.get(p['BusinessDepartmentId'], 'non rattaché')} | {staff_by_node[p['Id']]} |")
    for c in cellules:
        if c["Id"] not in matched_nodes:
            lines.append(f"| Cellule | {c['Name']} | {pole_name.get(c['PoleId'], '?')} | {staff_by_node[c['Id']]} |")
    for s in services:
        if s["Id"] not in matched_nodes:
            lines.append(f"| Service | {s['Name']} | {cell_name.get(s['CelluleId'], '?')} | {staff_by_node[s['Id']]} |")
    lines.append("")

    # Responsables : périmètre actuel vs règle cible
    pole_dept = {p["Id"]: p["BusinessDepartmentId"] for p in poles}
    cell_pole = {c["Id"]: c["PoleId"] for c in cellules}
    svc_cell = {s["Id"]: s["CelluleId"] for s in services}
    resp_rows = []
    by_emp_kind: dict[tuple[str, str], list[str]] = defaultdict(list)
    for a in assignments:
        k = KIND.get(a["Kind"], a["Kind"])
        if k in ("ChefDeProjet", "Superviseur", "ReferentTechnique"):
            by_emp_kind[(a["EmployeeId"], k)].append(a["NodeId"])
    for (emp_id, kind), nodes in sorted(by_emp_kind.items(), key=lambda x: (x[0][1], x[0][0])):
        e = employees.get(emp_id, {})
        name = f"{e.get('FirstName', '')} {e.get('LastName', '')}".strip() or emp_id
        groups: dict[str, set[str]] = defaultdict(set)
        for n in nodes:
            if kind == "ChefDeProjet":
                groups[pole_dept.get(n) or "?"].add(n)
            elif kind == "Superviseur":
                groups[pole_dept.get(cell_pole.get(n, ""), "") or "?"].add(n)
            else:
                groups[svc_cell.get(n) or "?"].add(n)
        for scope, held in groups.items():
            if kind == "ChefDeProjet":
                total = sum(1 for p in poles if p["BusinessDepartmentId"] == scope)
                scope_label = dept_name.get(scope, "(pôle sans activité)")
            elif kind == "Superviseur":
                total = sum(1 for c in cellules if pole_dept.get(c["PoleId"]) == scope)
                scope_label = dept_name.get(scope, "(cellule sans activité)")
            else:
                total = sum(1 for s in services if s["CelluleId"] == scope)
                scope_label = cell_name.get(scope, "(service orphelin)")
            status = "complet" if total and len(held) >= total else f"PARTIEL {len(held)}/{total}"
            resp_rows.append([emp_id, name, e.get("Email", ""), e.get("Role", ""), kind,
                              "Activite" if kind != "ReferentTechnique" else "Cellule",
                              scope, scope_label, len(held), total, status, ""])
    with (out / "perimetres_responsables.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["employee_id", "nom", "email", "role", "kind", "niveau_perimetre", "perimetre_id",
                    "perimetre", "noeuds_detenus", "noeuds_total", "couverture_actuelle", "valider"])
        w.writerows(resp_rows)

    partial = [r for r in resp_rows if r[10].startswith("PARTIEL")]
    lines += ["## Responsables — application des nouvelles règles", "",
              "- Superviseur : toutes les cellules de son activité · Chef de projet : tous les pôles de son activité · "
              "Référent technique : tous les services de sa cellule.",
              f"- {len(resp_rows)} périmètre(s) de responsables trouvés, dont **{len(partial)} partiel(s)** qui seraient étendus.",
              "- Détail et validation ligne à ligne : `perimetres_responsables.csv` (colonne `valider`).", ""]
    for r in partial:
        lines.append(f"  - {r[1]} ({r[4]}) — {r[7]} : {r[10]}")
    lines.append("")

    counts = defaultdict(int)
    for a in actions:
        counts[a["action"]] += 1
    lines.insert(2, "**Actions proposées (créations uniquement)** : " +
                 ", ".join(f"{k} × {v}" for k, v in counts.items()) + "\n" if actions else "**Aucune création nécessaire.**\n")

    (out / "plan_structure.json").write_text(json.dumps({"actions": actions}, ensure_ascii=False, indent=2), encoding="utf-8")
    (out / "plan_organisation.md").write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print(f"\nPlan : {out / 'plan_organisation.md'}\nActions : {out / 'plan_structure.json'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
