#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 10 (APRÈS 07 ET 08) : suppression des pôles / cellules / services
absents du fichier, dans les activités du fichier (Bytel Kyntus, Bytel TISSEO, Orange).

Décisions du 29/10 :
  * Seule la structure du fichier est conservée, plus les nœuds du fichier « Périmètre » :
    compléments validés (SFR/ATS, Pôle Sécurisation, Prestations réseaux & Malfaçons) et
    cellules confiées aux référents (Pôle client : Support Client, Satisfaction client,
    Gestion des retards ; Pôle escalade et contrôle : toutes ses cellules).
    Facturation et IT ne sont pas touchés.
  * Pilotes hors fichier encore affectés à un ancien nœud : comptes à désactiver (dans
    l'interface) AVANT la suppression ; leurs affectations sont ensuite clôturées.
  * Planning : les nœuds supprimés sont masqués et leurs plannings conservés (historique).

Garde-fous :
  * Simulation par défaut ; --appliquer demande en plus de taper SUPPRIMER.
  * Aucune suppression tant qu'un collaborateur ACTIF dépend encore d'un ancien nœud :
      - pilote du fichier non déplacé            → relancer l'étape 07 ;
      - pilote hors fichier encore actif         → le désactiver (liste rapport/a_desactiver.csv) ;
      - responsable sans autre périmètre         → le traiter (étape 08 ou interface).
  * Affectations clôturées (historique conservé) avant toute suppression. Les comptes
    Admin / RH / Audit ne sont jamais rétrogradés : leur affectation sur un ancien nœud est
    clôturée directement en base, avec une ligne d'historique, sans changer leur rôle.
  * Journal complet (rapport/journal_suppression_*.json). Retour arrière : restauration des
    sauvegardes de l'étape 01 (PROCEDURE_SERVEUR.md § Retour arrière, cas 5).
"""
from __future__ import annotations

import argparse
import csv
import json
import subprocess
import sys
import time
from collections import defaultdict
from datetime import datetime
from pathlib import Path

from kyntus_api import KyntusApi
from reorg_commun import (
    HERE, KIND, export_dir, find_department, harmonize_pole_label, is_true, load_activity_mapping, norm, norm_pole,
    read_csv,
)

REASON = "Réorganisation 2026 — suppression des anciens nœuds"
PROTECTED_ROLES = {"ADMIN", "RH", "AUDIT"}


def referent_targets(rows: list[dict], poles: list[dict], cellules: list[dict], depts: list[dict], mapping: dict) -> list[dict]:
    """Cellules confiées aux référents techniques dans le fichier Périmètre (« * » = tout le pôle)."""
    out = []
    for r in rows:
        if (r.get("action") or "").strip() != "ReferentTechnique":
            continue
        wanted = [w.strip() for w in (r.get("cellules") or "").split("|") if w.strip()]
        if wanted == ["*"]:
            dept = find_department(r["activite"], depts, mapping)
            pole = dept and next((p for p in poles if p["BusinessDepartmentId"] == dept["Id"]
                                  and norm_pole(p["Name"]) == norm_pole(r["pole"])), None)
            wanted = [c["Name"] for c in cellules if pole and c["PoleId"] == pole["Id"] and c["Name"].strip() not in ("", "-")]
        out += [{"activite": r["activite"], "pole": r["pole"], "cellule": w} for w in wanted]
    return out


def build_plan(export: Path, targets: list[dict], mapping: dict, moves_ids: set[str],
               responsables: list[dict] | None = None) -> dict:
    depts = [d for d in read_csv(export / "business_departments.csv") if d["Kind"] in ("0", "Operational")]
    poles = read_csv(export / "org_poles.csv")
    cellules = read_csv(export / "org_cellules.csv")
    services = read_csv(export / "org_services.csv")
    employees = {e["Id"]: e for e in read_csv(export / "employees.csv")}
    assignments = read_csv(export / "org_assignments_actives.csv")
    targets = targets + referent_targets(responsables or [], poles, cellules, depts, mapping)

    blocking: list[str] = []
    scope_depts: dict[str, dict] = {}
    keep_poles: set[str] = set()
    keep_cells: set[str] = set()
    for row in targets:
        dept = find_department(row["activite"], depts, mapping)
        if not dept:
            blocking.append(f"activité « {row['activite']} » introuvable")
            continue
        scope_depts[dept["Id"]] = dept
        pole = next((p for p in poles if p["BusinessDepartmentId"] == dept["Id"]
                     and norm_pole(p["Name"]) == norm_pole(row["pole"])), None)
        if not pole:
            blocking.append(f"pôle « {harmonize_pole_label(row['pole'])} » absent de {dept['Name']} : lancer 03 + 04")
            continue
        keep_poles.add(pole["Id"])
        cell = next((c for c in cellules if c["PoleId"] == pole["Id"] and norm(c["Name"]) == norm(row["cellule"])), None)
        if not cell:
            blocking.append(f"cellule « {row['cellule']} » absente du pôle « {pole['Name']} » : lancer 03 + 04")
            continue
        keep_cells.add(cell["Id"])

    del_poles = [p for p in poles if p["BusinessDepartmentId"] in scope_depts and p["Id"] not in keep_poles]
    del_pole_ids = {p["Id"] for p in del_poles}
    del_cells = [c for c in cellules if (c["PoleId"] in del_pole_ids)
                 or (c["PoleId"] in keep_poles and c["Id"] not in keep_cells)]
    del_cell_ids = {c["Id"] for c in del_cells}
    del_services = [s for s in services if s["CelluleId"] in del_cell_ids]
    del_ids = del_pole_ids | del_cell_ids | {s["Id"] for s in del_services}

    by_employee: dict[str, list[dict]] = defaultdict(list)
    for a in assignments:
        by_employee[a["EmployeeId"]].append(a)

    closes, sql_closes, to_deactivate = [], [], []
    for emp_id, rows in by_employee.items():
        on_deleted = [a for a in rows if a["NodeId"] in del_ids]
        if not on_deleted:
            continue
        emp = employees.get(emp_id, {})
        name = f"{emp.get('FirstName', '')} {emp.get('LastName', '')}".strip() or emp_id
        active = is_true(emp.get("IsActive"))
        kinds = {KIND.get(a["Kind"], a["Kind"]) for a in on_deleted}
        remaining = [a for a in rows if a["NodeId"] not in del_ids]
        entry_base = {"employee_id": emp_id, "nom": name, "email": emp.get("Email", ""), "role": emp.get("Role", "")}
        if not active:
            closes += [{**entry_base, "kind": KIND.get(a["Kind"], a["Kind"]), "node": a["NodeId"], "assignment_id": a["Id"]}
                       for a in on_deleted]
            continue
        if "Pilote" in kinds:
            if emp_id in moves_ids:
                blocking.append(f"{name} : pilote du fichier pas encore déplacé (relancer l'étape 07)")
            else:
                to_deactivate.append(entry_base)
            continue
        if norm(emp.get("Role")) in PROTECTED_ROLES:
            sql_closes += [{**entry_base, "kind": KIND.get(a["Kind"], a["Kind"]), "node": a["NodeId"], "assignment_id": a["Id"],
                            "level": a["NodeLevel"], "kind_code": a["Kind"]} for a in on_deleted]
            continue
        if not remaining:
            blocking.append(f"{name} ({emp.get('Role')}) : responsable uniquement sur d'anciens nœuds "
                            f"(traiter avec l'étape 08 ou dans l'interface)")
            continue
        closes += [{**entry_base, "kind": KIND.get(a["Kind"], a["Kind"]), "node": a["NodeId"], "assignment_id": a["Id"]}
                   for a in on_deleted]

    dangling = []
    for e in employees.values():
        if not is_true(e.get("IsActive")):
            continue
        refs = [e.get(c) for c in ("PoleId", "CelluleId", "ServiceId") if e.get(c) in del_ids]
        if refs and e["Id"] not in by_employee:
            dangling.append(f"{e['FirstName']} {e['LastName']}")
    for name in dangling:
        blocking.append(f"{name} : collaborateur actif rattaché à un ancien nœud sans affectation (réaffecter dans l'interface)")
    for e in to_deactivate:
        blocking.append(f"{e['nom']} : pilote hors fichier encore actif (à désactiver, cf. a_desactiver.csv)")

    return {
        "scope": [f"{d['Code']} — {d['Name']}" for d in scope_depts.values()],
        "scope_ids": list(scope_depts),
        "del_poles": del_poles, "del_cells": del_cells, "del_services": del_services,
        "closes": closes, "sql_closes": sql_closes, "to_deactivate": to_deactivate,
        "blocking": sorted(set(blocking)),
        "pole_name": {p["Id"]: p["Name"] for p in poles},
    }


def print_plan(plan: dict) -> None:
    print("Activités concernées : " + ", ".join(plan["scope"]))
    pole_name = plan["pole_name"]
    print(f"\nÀ SUPPRIMER : {len(plan['del_poles'])} pôle(s), {len(plan['del_cells'])} cellule(s), {len(plan['del_services'])} service(s)")
    for p in sorted(plan["del_poles"], key=lambda x: x["Name"]):
        print(f"  - pôle    {p['Name']}")
    for c in sorted(plan["del_cells"], key=lambda x: (pole_name.get(x["PoleId"], ""), x["Name"])):
        print(f"  - cellule {pole_name.get(c['PoleId'], '?')} / {c['Name']}")
    print(f"\nAffectations clôturées (historique conservé) : {len(plan['closes'])} via l'API, "
          f"{len(plan['sql_closes'])} en base pour un compte Admin/RH/Audit (rôle inchangé)")
    for c in plan["sql_closes"]:
        print(f"  - {c['nom']} [{c['role']}] {c['kind']} sur {c['node']}")
    if plan["to_deactivate"]:
        print(f"\nPilotes hors fichier encore actifs : {len(plan['to_deactivate'])} (à désactiver dans l'interface)")
    if plan["blocking"]:
        print(f"\nBLOQUANT ({len(plan['blocking'])}) :")
        for b in plan["blocking"]:
            print(f"  - {b}")


def psql(sql: str) -> None:
    subprocess.run(["docker", "exec", "-i", "kyntus_db", "psql", "-U", "postgres", "-d", "employee_directory_db",
                    "-v", "ON_ERROR_STOP=1", "-q"], input=sql.encode("utf-8"), check=True)


def sql_close(c: dict) -> None:
    reason = REASON.replace("'", "''")
    psql(f"""
BEGIN;
INSERT INTO org_assignment_history ("Id","Kind","NodeId","NodeLevel","PreviousEmployeeId","NewEmployeeId","ChangedBy","ChangeReason","ChangedAt")
SELECT gen_random_uuid(), "Kind", "NodeId", "NodeLevel", "EmployeeId", NULL, NULL, '{reason}', now()
FROM org_assignments WHERE "Id" = '{c['assignment_id']}' AND "EffectiveTo" IS NULL;
UPDATE org_assignments SET "EffectiveTo" = now(), "SupersededBy" = gen_random_uuid(), "ChangeReason" = '{reason}'
WHERE "Id" = '{c['assignment_id']}' AND "EffectiveTo" IS NULL;
COMMIT;
""")


def apply(api: KyntusApi, plan: dict, journal_path: Path, pause: float) -> int:
    journal = {"date": datetime.now().isoformat(timespec="seconds"), "closes": [], "sql_closes": [], "deleted": []}
    failures = 0
    try:
        for c in plan["closes"]:
            path = (f"/api/directory/org/assignments/Pilote/{c['node']}/employees/{c['employee_id']}" if c["kind"] == "Pilote"
                    else f"/api/directory/org/assignments/{c['kind']}/{c['node']}/employees/{c['employee_id']}")
            try:
                api.request("DELETE", path)
                journal["closes"].append(c)
            except RuntimeError as err:
                if "404" not in str(err):
                    failures += 1
                    print(f"  ✘ clôture {c['nom']} {c['kind']} : {err}")
            time.sleep(pause)
        for c in plan["sql_closes"]:
            sql_close(c)
            journal["sql_closes"].append(c)
            print(f"  ✔ clôture (rôle {c['role']} conservé) : {c['nom']} {c['kind']}")
        if failures:
            print("✘ Des clôtures ont échoué : aucune suppression effectuée. Corrigez puis relancez.")
            return failures

        links = {d["id"]: set(d.get("poleIds") or []) for d in (api.request("GET", "/api/directory/business-departments") or [])}
        for s in plan["del_services"]:
            api.request("DELETE", f"/api/directory/org/structure/cellules/{s['CelluleId']}/services/{s['Id']}")
            journal["deleted"].append({"type": "service", **s})
            time.sleep(pause)
        for c in plan["del_cells"]:
            api.request("DELETE", f"/api/directory/org/structure/poles/{c['PoleId']}/cellules/{c['Id']}")
            journal["deleted"].append({"type": "cellule", **c})
            print(f"  ✔ cellule supprimée : {plan['pole_name'].get(c['PoleId'], '?')} / {c['Name']}")
            time.sleep(pause)
        for p in plan["del_poles"]:
            dept = p["BusinessDepartmentId"]
            if p["Id"] in links.get(dept, set()):
                api.request("DELETE", f"/api/directory/business-departments/{dept}/poles/{p['Id']}")
            api.request("DELETE", f"/api/directory/org/structure/poles/{p['Id']}")
            journal["deleted"].append({"type": "pole", **p})
            print(f"  ✔ pôle supprimé : {p['Name']}")
            time.sleep(pause)
    finally:
        journal_path.write_text(json.dumps(journal, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nJournal : {journal_path}")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--export")
    ap.add_argument("--cible", default=str(HERE / "donnees" / "organisation_cible.csv"))
    ap.add_argument("--complement", default=str(HERE / "donnees" / "organisation_complement.csv"))
    ap.add_argument("--deplacements", default=str(HERE / "donnees" / "deplacements_pilotes.csv"))
    ap.add_argument("--responsables", default=str(HERE / "donnees" / "responsables_cible.csv"))
    ap.add_argument("--correspondances", default=str(HERE / "donnees" / "correspondance_activites.csv"))
    ap.add_argument("--appliquer", action="store_true")
    ap.add_argument("--pause", type=float, default=0.3)
    args = ap.parse_args()

    export = export_dir(args.export)
    print(f"Export utilisé : {export}\n")
    targets = read_csv(Path(args.cible), ";") + read_csv(Path(args.complement), ";")
    moves_ids = {r["employee_id"] for r in read_csv(Path(args.deplacements), ";")}
    plan = build_plan(export, targets, load_activity_mapping(Path(args.correspondances)), moves_ids,
                      read_csv(Path(args.responsables), ";"))
    print_plan(plan)

    out = export / "rapport"
    out.mkdir(parents=True, exist_ok=True)
    with (out / "a_desactiver.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["nom", "email", "role", "employee_id"])
        w.writerows([[e["nom"], e["email"], e["role"], e["employee_id"]] for e in plan["to_deactivate"]])
    print(f"\nListe des comptes à désactiver : {out / 'a_desactiver.csv'}")

    if plan["blocking"]:
        print("\n✘ Suppression impossible tant que des lignes sont BLOQUANTES (rien n'a été modifié).")
        return 2
    if not args.appliquer:
        print("\nSimulation uniquement. Relancez avec --appliquer après validation.")
        return 0
    if not (plan["del_poles"] or plan["del_cells"] or plan["del_services"]):
        print("Rien à supprimer.")
        return 0
    if input("\nSuppression définitive dans l'Organisation RH. Tapez SUPPRIMER pour confirmer : ").strip() != "SUPPRIMER":
        print("Annulé.")
        return 1
    api = KyntusApi()
    api.login_if_needed()
    return apply(api, plan, out / f"journal_suppression_{datetime.now():%Y%m%d_%H%M%S}.json", args.pause)


if __name__ == "__main__":
    sys.exit(main())
