#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 8 (APRÈS VALIDATION) : périmètres des responsables.

Source : donnees/responsables_cible.csv (fichier « Périmètre » validé) — chaque responsable reçoit
EXACTEMENT le périmètre du fichier (activité, pôle ou cellule(s), plusieurs possibles) :
  colonnes activite / pole / cellules :
    activité seule                  → toute l'activité
    activité + pôle (+ « * »)       → tout le pôle
    activité + pôle + cellules « | »→ ces cellules
  action = Superviseur        → affectations sur les cellules du périmètre
           ChefDeProjet       → affectations sur les pôles du périmètre (activité ou pôle)
           ReferentTechnique  → affectations sur les services des cellules du périmètre
           RETIRER_SUPERVISEUR→ fin des affectations superviseur (historique conservé)
           INCHANGE           → aucune action (ligne documentaire)

Garde-fous :
  * Simulation par défaut ; --appliquer exécute réellement (compte Admin).
  * Compte retrouvé par e-mail, sinon par nom complet ; homonymes ou compte inactif → ignoré.
  * Toute cellule / activité introuvable bloque l'exécution (lancer d'abord 03 + 04).
  * Passe par l'API officielle (PUT /api/directory/org/scope-assignments/…) : historique,
    événements Planning / Prime / Congé ; le rôle du compte suit son affectation.
  * Journal complet des affectations AVANT modification ; retour arrière :
      python3 08_appliquer_responsables.py --retour-arriere rapport/journal_responsables_XXXX.json [--appliquer]
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path

from kyntus_api import KyntusApi
from reorg_commun import (
    HERE, KIND, export_dir, find_department, harmonize_pole_label, is_true, load_activity_mapping, norm, norm_pole,
    read_csv,
)

REASON = "Réorganisation 2026 — périmètres responsables"
ROLLBACK_REASON = "Retour arrière réorganisation 2026 — périmètres responsables"
SCOPED_KINDS = ("Superviseur", "ChefDeProjet", "ReferentTechnique")
ACTIONS = (*SCOPED_KINDS, "RETIRER_SUPERVISEUR", "INCHANGE")


def name_key(value: str) -> frozenset[str]:
    return frozenset(t for t in norm(value).split(" ") if t)


class Context:
    def __init__(self, export: Path, mapping_path: Path):
        self.employees = read_csv(export / "employees.csv")
        self.depts = [d for d in read_csv(export / "business_departments.csv") if d["Kind"] in ("0", "Operational")]
        self.poles = read_csv(export / "org_poles.csv")
        self.cellules = read_csv(export / "org_cellules.csv")
        self.services = read_csv(export / "org_services.csv")
        self.mapping = load_activity_mapping(mapping_path)
        self.by_email = defaultdict(list)
        self.by_name = defaultdict(list)
        for e in self.employees:
            if e.get("Email"):
                self.by_email[e["Email"].strip().lower()].append(e)
            self.by_name[name_key(f"{e['FirstName']} {e['LastName']}")].append(e)
        self.assignments: dict[str, dict[str, list[str]]] = defaultdict(lambda: defaultdict(list))
        for a in read_csv(export / "org_assignments_actives.csv"):
            self.assignments[a["EmployeeId"]][KIND.get(a["Kind"], a["Kind"])].append(a["NodeId"])
        self.pole_dept = {p["Id"]: p["BusinessDepartmentId"] for p in self.poles}
        self.cell_pole = {c["Id"]: c["PoleId"] for c in self.cellules}
        self.dept_label = {d["Id"]: f"{d['Code']} — {d['Name']}" for d in self.depts}

    def find_employee(self, name: str, email: str) -> tuple[dict | None, str]:
        candidates = self.by_email.get(email.strip().lower(), []) if email.strip() else []
        how = "e-mail"
        if not candidates:
            candidates = self.by_name.get(name_key(name), [])
            how = "nom"
        if not candidates:
            return None, "compte introuvable (vérifier l'orthographe ou créer le compte)"
        active = [c for c in candidates if is_true(c.get("IsActive"))]
        if len(active) > 1:
            return None, f"{len(active)} comptes actifs portent ce {how} : à traiter dans l'interface"
        if not active:
            return None, "compte inactif : le réactiver d'abord depuis l'interface"
        return active[0], ""

    def resolve_scope(self, row: dict) -> tuple[list[str], str, str]:
        """Retourne (scopeIds, libellé, erreur) au niveau écrit dans le fichier."""
        action = row["action"]
        dept = find_department(row["activite"], self.depts, self.mapping)
        if not dept:
            return [], "", f"activité « {row['activite']} » introuvable"
        if not (row.get("pole") or "").strip():
            return [dept["Id"]], f"{dept['Name']} (activité entière)", ""
        pole = next((p for p in self.poles
                     if p["BusinessDepartmentId"] == dept["Id"] and norm_pole(p["Name"]) == norm_pole(row["pole"])), None)
        if not pole:
            return [], "", f"pôle « {harmonize_pole_label(row['pole'])} » introuvable dans {dept['Name']} (lancer 03 + 04)"
        wanted = [w.strip() for w in (row.get("cellules") or "").split("|") if w.strip()]
        if not wanted or wanted == ["*"]:
            return [pole["Id"]], f"{dept['Name']} / {pole['Name']} (pôle entier)", ""
        if action == "ChefDeProjet":
            return [], "", "un chef de projet est rattaché à une activité ou à un pôle, pas à des cellules"
        pole_cells = [c for c in self.cellules if c["PoleId"] == pole["Id"]]
        cells = []
        for w in wanted:
            c = next((c for c in pole_cells if norm(c["Name"]) == norm(w)), None)
            if not c:
                return [], "", f"cellule « {w} » introuvable dans le pôle « {pole['Name']} » (lancer 03 + 04)"
            cells.append(c)
        return [c["Id"] for c in cells], f"{dept['Name']} / {pole['Name']} / " + " | ".join(c["Name"] for c in cells), ""

    def expected_nodes(self, kind: str, scope_ids: list[str]) -> set[str]:
        """Nœuds d'affectation attendus pour un périmètre mixte (activités, pôles, cellules)."""
        dept_ids = {d["Id"] for d in self.depts}
        pole_ids = {p["Id"] for p in self.poles}
        poles: set[str] = set()
        cells: set[str] = set()
        for sid in scope_ids:
            if sid in dept_ids:
                poles |= {p["Id"] for p in self.poles if p["BusinessDepartmentId"] == sid}
            elif sid in pole_ids:
                poles.add(sid)
            else:
                cells.add(sid)
        if kind == "ChefDeProjet":
            return poles
        cells |= {c["Id"] for c in self.cellules if c["PoleId"] in poles}
        if kind == "Superviseur":
            return cells
        return {s["Id"] for s in self.services if s["CelluleId"] in cells}


def build_plan(ctx: Context, rows: list[dict]) -> list[dict]:
    plan = []
    for row in rows:
        action = (row.get("action") or "").strip()
        entry = {"nom": row["nom"], "action": action, "commentaire": row.get("commentaire", "")}
        if action not in ACTIONS:
            entry.update(statut="BLOQUANT", motif=f"action inconnue « {action} »")
            plan.append(entry)
            continue
        if action == "INCHANGE":
            entry.update(statut="INCHANGE", motif=row.get("commentaire", ""))
            plan.append(entry)
            continue
        emp, err = ctx.find_employee(row["nom"], row.get("email", ""))
        if not emp:
            entry.update(statut="IGNORE", motif=err)
            plan.append(entry)
            continue
        before = {k: list(v) for k, v in ctx.assignments.get(emp["Id"], {}).items()}
        entry.update(employee_id=emp["Id"], email=emp["Email"], role_actuel=emp["Role"], avant=before)
        if action == "RETIRER_SUPERVISEUR":
            nodes = before.get("Superviseur", [])
            entry.update(statut="A_APPLIQUER" if nodes else "DEJA_EN_PLACE",
                         cible=f"retrait de {len(nodes)} cellule(s) superviseur", noeuds_a_retirer=nodes, motif="")
            plan.append(entry)
            continue
        scope_ids, label, err = ctx.resolve_scope(row)
        if err:
            entry.update(statut="BLOQUANT", motif=err)
            plan.append(entry)
            continue
        expected = ctx.expected_nodes(action, scope_ids)
        held = set(before.get(action, []))
        others = [k for k in before if k != action and before[k]]
        entry.update(scope_ids=scope_ids, cible=label, kind=action)
        if held == expected and not others:
            entry.update(statut="DEJA_EN_PLACE", motif="")
        else:
            note = f"retire aussi : {', '.join(others)}" if others else ""
            entry.update(statut="A_APPLIQUER", motif=note)
        plan.append(entry)
    return plan


def print_plan(plan: list[dict]) -> None:
    print(f"{len(plan)} ligne(s) : " + ", ".join(f"{k} × {v}" for k, v in sorted(Counter(e['statut'] for e in plan).items())))
    for status in ("A_APPLIQUER", "DEJA_EN_PLACE", "INCHANGE", "IGNORE", "BLOQUANT"):
        rows = [e for e in plan if e["statut"] == status]
        if not rows:
            continue
        print(f"\n{status} :")
        for e in rows:
            what = e.get("cible") or ""
            role = f" [{e['role_actuel']}]" if e.get("role_actuel") else ""
            extra = f" — {e['motif']}" if e.get("motif") else ""
            print(f"  - {e['nom']:<26}{role:<22} {e['action']:<20} {what}{extra}")


def apply_plan(api: KyntusApi, todo: list[dict], journal_path: Path, pause: float) -> int:
    journal, failures = [], 0
    try:
        for e in todo:
            record = {k: e.get(k) for k in ("nom", "employee_id", "email", "action", "kind", "scope_ids", "cible", "avant")}
            try:
                if e["action"] == "RETIRER_SUPERVISEUR":
                    for node in e["noeuds_a_retirer"]:
                        api.request("DELETE", f"/api/directory/org/assignments/Superviseur/{node}/employees/{e['employee_id']}")
                    record["apres"] = []
                else:
                    res = api.request("PUT", f"/api/directory/org/scope-assignments/{e['kind']}/employees/{e['employee_id']}",
                                      {"scopeIds": e["scope_ids"], "primaryScopeId": e["scope_ids"][0], "reason": REASON})
                    record["apres"] = ((res or {}).get("assignments") or {}).get("nodeIds") or []
                record["resultat"] = "OK"
                print(f"  ✔ {e['nom']:<26} {e['action']:<20} {e.get('cible') or ''}")
            except RuntimeError as err:
                failures += 1
                record.update(resultat="ECHEC", erreur=str(err))
                print(f"  ✘ {e['nom']:<26} {err}")
            journal.append(record)
            time.sleep(pause)
    finally:
        journal_path.write_text(json.dumps(journal, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nJournal (retour arrière) : {journal_path}")
    return failures


def rollback(api: KyntusApi | None, journal_file: Path, pause: float) -> int:
    entries = [e for e in json.loads(journal_file.read_text(encoding="utf-8")) if e.get("resultat") == "OK"]
    print(f"{len(entries)} responsable(s) à remettre dans leur état d'avant {'— EXÉCUTION' if api else '(simulation)'}")
    failures = 0
    for e in reversed(entries):
        emp, before = e["employee_id"], e.get("avant") or {}
        steps: list[tuple[str, str, dict | None]] = []
        kind = e.get("kind") or "Superviseur"
        if not before.get(kind):
            steps += [("DELETE", f"/api/directory/org/assignments/{kind}/{n}/employees/{emp}", None) for n in e.get("apres") or []]
        for k, nodes in before.items():
            if not nodes:
                continue
            if k == "Pilote":
                steps.append(("POST", f"/api/directory/org/assignments/Pilote/{nodes[0]}",
                              {"employeeId": emp, "reason": ROLLBACK_REASON, "forceTenureOverride": True}))
            elif k in SCOPED_KINDS:
                steps.append(("PUT", f"/api/directory/org/assignments/{k}/employees/{emp}",
                              {"nodeIds": nodes, "primaryNodeId": nodes[0], "reason": ROLLBACK_REASON}))
        print(f"  ↺ {e['nom']:<26} {len(steps)} opération(s)")
        if not api:
            continue
        for method, path, body in steps:
            try:
                api.request(method, path, body)
            except RuntimeError as err:
                failures += 1
                print(f"    ✘ {method} {path} : {err}")
        time.sleep(pause)
    return 1 if failures else 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--export")
    ap.add_argument("--responsables", default=str(HERE / "donnees" / "responsables_cible.csv"))
    ap.add_argument("--correspondances", default=str(HERE / "donnees" / "correspondance_activites.csv"))
    ap.add_argument("--appliquer", action="store_true", help="exécuter réellement (sinon simulation)")
    ap.add_argument("--retour-arriere", metavar="JOURNAL")
    ap.add_argument("--pause", type=float, default=0.5)
    args = ap.parse_args()

    if args.retour_arriere:
        api = None
        if args.appliquer:
            api = KyntusApi()
            api.login_if_needed()
        return rollback(api, Path(args.retour_arriere), args.pause)

    export = export_dir(args.export)
    print(f"Export utilisé : {export}\n")
    ctx = Context(export, Path(args.correspondances))
    plan = build_plan(ctx, read_csv(Path(args.responsables), ";"))
    print_plan(plan)

    report = export / "rapport" / "plan_responsables.json"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(plan, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nDétail : {report}")

    if any(e["statut"] == "BLOQUANT" for e in plan):
        print("\n✘ Lignes BLOQUANTES : aucune écriture. Créez la structure (03 + 04) puis refaites l'export 01.")
        return 2
    todo = [e for e in plan if e["statut"] == "A_APPLIQUER"]
    if not args.appliquer:
        print(f"\nSimulation uniquement : {len(todo)} responsable(s) à mettre à jour. Relancez avec --appliquer après validation.")
        return 0
    if not todo:
        print("Rien à appliquer.")
        return 0
    api = KyntusApi()
    api.login_if_needed()
    journal = export / "rapport" / f"journal_responsables_{datetime.now():%Y%m%d_%H%M%S}.json"
    failures = apply_plan(api, todo, journal, args.pause)
    print(f"\n{len(todo) - failures} responsable(s) mis à jour, {failures} échec(s).")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
