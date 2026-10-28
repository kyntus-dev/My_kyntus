#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 7 (APRÈS VALIDATION) : affectation des pilotes du fichier à leur
nouvelle cellule (service technique miroir de la cellule).

Source : donnees/deplacements_pilotes.csv (issu du classeur de décisions validé :
correspondances exactes + correspondances confirmées + comptes « LIER »).

Garde-fous :
  * Simulation par défaut : aucune écriture. --appliquer exécute réellement.
  * Seuls les comptes ACTIFS au rôle « Pilote » sont déplacés : un autre rôle n'est jamais
    modifié (l'affectation pilote changerait le rôle), un compte inactif est signalé
    (Q9 : réactiver d'abord depuis l'interface, puis refaire l'export 01).
  * Passe par l'API officielle (POST /api/directory/org/assignments/Pilote/{service}) :
    historique d'affectation conservé, événements vers Planning / Prime / Congé…
  * Règle d'ancienneté ignorée (Q6) avec le motif « Réorganisation 2026 » : compte Admin requis.
  * Contrôle préalable : chaque cellule cible doit déjà exister dans Planning (sous-service
    miroir), sinon arrêt avant toute écriture.
  * Journal ligne à ligne (rapport/journal_pilotes_<horodatage>.json) ; retour arrière :
      python3 07_deplacer_pilotes.py --retour-arriere rapport/journal_pilotes_XXXX.json [--appliquer]

Usage :
  python3 07_deplacer_pilotes.py                      # simulation (lecture seule)
  KYNTUS_EMAIL=admin@... python3 07_deplacer_pilotes.py --appliquer
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
    HERE, KIND, export_dir, find_department, harmonize_pole_label, is_true, load_activity_mapping, norm, norm_pole, read_csv,
)

REASON = "Réorganisation 2026"
ROLLBACK_REASON = "Retour arrière réorganisation 2026"


class Structure:
    def __init__(self, export: Path, mapping_path: Path):
        self.depts = [d for d in read_csv(export / "business_departments.csv") if d["Kind"] in ("0", "Operational")]
        self.poles = read_csv(export / "org_poles.csv")
        self.cellules = read_csv(export / "org_cellules.csv")
        self.services = read_csv(export / "org_services.csv")
        self.mapping = load_activity_mapping(mapping_path)
        self.service_label = {}
        cell_by_id = {c["Id"]: c for c in self.cellules}
        pole_by_id = {p["Id"]: p for p in self.poles}
        dept_by_id = {d["Id"]: d for d in read_csv(export / "business_departments.csv")}
        for s in self.services:
            c = cell_by_id.get(s["CelluleId"], {})
            p = pole_by_id.get(c.get("PoleId", ""), {})
            d = dept_by_id.get(p.get("BusinessDepartmentId", ""), {})
            self.service_label[s["Id"]] = " / ".join(
                x for x in (d.get("Name"), p.get("Name"), c.get("Name"), s["Name"]) if x)

    def target_service(self, activite: str, pole: str, cellule: str) -> tuple[str | None, str]:
        dept = find_department(activite, self.depts, self.mapping)
        if not dept:
            return None, f"activité « {activite} » absente (lancer l'étape 04)"
        p = next((x for x in self.poles
                  if x["BusinessDepartmentId"] == dept["Id"] and norm_pole(x["Name"]) == norm_pole(pole)), None)
        if not p:
            return None, f"pôle « {pole} » absent de {dept['Code']} (lancer l'étape 04)"
        c = next((x for x in self.cellules if x["PoleId"] == p["Id"] and norm(x["Name"]) == norm(cellule)), None)
        if not c:
            return None, f"cellule « {cellule} » absente du pôle « {p['Name']} » (lancer l'étape 04)"
        svc = [s for s in self.services if s["CelluleId"] == c["Id"]]
        same_name = [s for s in svc if norm(s["Name"]) == norm(cellule)]
        if len(same_name) == 1:
            return same_name[0]["Id"], ""
        if len(svc) == 1:
            return svc[0]["Id"], ""
        if not svc:
            return None, f"cellule « {cellule} » sans service technique (lancer l'étape 04)"
        return None, f"cellule « {cellule} » : plusieurs services, cible ambiguë"


def planning_mirrors(export: Path) -> set[str] | None:
    rows = read_csv(export / "planning_sub_services.csv")
    if not (export / "planning_sub_services.csv").exists():
        return None
    return {r["PrimeServiceId"] for r in rows if r.get("PrimeServiceId")}


def build_plan(export: Path, moves_path: Path, mapping_path: Path):
    structure = Structure(export, mapping_path)
    employees = {e["Id"]: e for e in read_csv(export / "employees.csv")}
    pilot_service: dict[str, list[str]] = defaultdict(list)
    for a in read_csv(export / "org_assignments_actives.csv"):
        if KIND.get(a["Kind"], a["Kind"]) == "Pilote":
            pilot_service[a["EmployeeId"]].append(a["NodeId"])

    plan, blocking = [], []
    for row in read_csv(moves_path, ";"):
        emp_id = row["employee_id"].strip()
        emp = employees.get(emp_id)
        entry = {
            "ligne_excel": row["ligne_excel"], "collaborateur": row["collaborateur_fichier"],
            "employee_id": emp_id, "origine": row.get("origine", ""),
            "cible": f"{row['activite_cible']} / {harmonize_pole_label(row['pole_cible'])} / {row['cellule_cible']}",
        }
        if not emp:
            entry.update(statut="IGNORE", motif="compte introuvable dans l'export")
            plan.append(entry)
            continue
        entry["nom_app"] = f"{emp['FirstName']} {emp['LastName']}".strip()
        entry["email"] = emp["Email"]
        if norm(emp["Role"]) != "PILOTE":
            entry.update(statut="IGNORE", motif=f"rôle « {emp['Role']} » : non modifié (le fichier ne concerne que les pilotes)")
            plan.append(entry)
            continue
        if not is_true(emp["IsActive"]):
            entry.update(statut="IGNORE", motif="compte inactif : le réactiver depuis l'interface (Q9) puis refaire l'export 01")
            plan.append(entry)
            continue
        target, error = structure.target_service(row["activite_cible"], row["pole_cible"], row["cellule_cible"])
        current = pilot_service.get(emp_id, [])
        previous = current[0] if len(current) == 1 else (emp.get("ServiceId") or None)
        entry.update(service_precedent=previous,
                     libelle_precedent=structure.service_label.get(previous or "", "aucune affectation pilote"),
                     service_cible=target)
        if not target:
            entry.update(statut="BLOQUANT", motif=error)
            blocking.append(entry)
        elif target in current and len(current) == 1:
            entry.update(statut="DEJA_EN_PLACE", motif="")
        else:
            entry.update(statut="A_DEPLACER", motif="" if len(current) <= 1 else f"{len(current)} affectations pilote actives")
        plan.append(entry)
    return plan, blocking, structure


def print_plan(plan: list[dict]) -> None:
    counts = Counter(e["statut"] for e in plan)
    print(f"{len(plan)} ligne(s) : " + ", ".join(f"{k} × {v}" for k, v in sorted(counts.items())))
    by_target = Counter(e["cible"] for e in plan if e["statut"] == "A_DEPLACER")
    if by_target:
        print("\nDéplacements prévus par cellule cible :")
        for target, n in sorted(by_target.items()):
            print(f"  {n:>4}  → {target}")
    for status in ("BLOQUANT", "IGNORE"):
        rows = [e for e in plan if e["statut"] == status]
        if rows:
            print(f"\n{status} :")
            for e in rows:
                print(f"  - l.{e['ligne_excel']:>3} {e['collaborateur']:<32} {e['motif']}")


def apply_moves(api: KyntusApi, todo: list[dict], journal_path: Path, pause: float) -> int:
    journal, failures = [], 0
    try:
        for i, e in enumerate(todo, 1):
            record = {k: e.get(k) for k in ("employee_id", "collaborateur", "nom_app", "email",
                                             "service_precedent", "libelle_precedent", "service_cible", "cible")}
            try:
                api.request("POST", f"/api/directory/org/assignments/Pilote/{e['service_cible']}",
                            {"employeeId": e["employee_id"], "reason": REASON, "forceTenureOverride": True})
                record["resultat"] = "OK"
                print(f"  ✔ {i:>3}/{len(todo)} {e['collaborateur']:<32} → {e['cible']}")
            except RuntimeError as err:
                failures += 1
                record["resultat"] = "ECHEC"
                record["erreur"] = str(err)
                print(f"  ✘ {i:>3}/{len(todo)} {e['collaborateur']:<32} {err}")
            journal.append(record)
            time.sleep(pause)  # laisser l'outbox projeter vers Planning / Prime / Congé
    finally:
        journal_path.write_text(json.dumps(journal, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"\nJournal (retour arrière) : {journal_path}")
    return failures


def rollback(api: KyntusApi | None, journal_file: Path, pause: float) -> int:
    entries = [e for e in json.loads(journal_file.read_text(encoding="utf-8")) if e.get("resultat") == "OK"]
    print(f"{len(entries)} déplacement(s) à annuler ({journal_file.name}) {'— EXÉCUTION' if api else '(simulation)'}")
    failures = 0
    for e in reversed(entries):
        previous = e.get("service_precedent")
        action = f"retour vers {e.get('libelle_precedent')}" if previous else "retrait de l'affectation pilote ajoutée"
        if not api:
            print(f"  - {e['collaborateur']:<32} {action}")
            continue
        try:
            if previous:
                api.request("POST", f"/api/directory/org/assignments/Pilote/{previous}",
                            {"employeeId": e["employee_id"], "reason": ROLLBACK_REASON, "forceTenureOverride": True})
            else:
                api.request("DELETE", f"/api/directory/org/assignments/Pilote/{e['service_cible']}/employees/{e['employee_id']}")
            print(f"  ↺ {e['collaborateur']:<32} {action}")
        except RuntimeError as err:
            failures += 1
            print(f"  ✘ {e['collaborateur']:<32} {err}")
        time.sleep(pause)
    return 1 if failures else 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--export")
    ap.add_argument("--deplacements", default=str(HERE / "donnees" / "deplacements_pilotes.csv"))
    ap.add_argument("--correspondances", default=str(HERE / "donnees" / "correspondance_activites.csv"))
    ap.add_argument("--appliquer", action="store_true", help="exécuter réellement (sinon simulation)")
    ap.add_argument("--retour-arriere", metavar="JOURNAL", help="annuler les déplacements d'un journal")
    ap.add_argument("--limite", type=int, default=0, help="ne traiter que les N premiers (essai sur un lot)")
    ap.add_argument("--pause", type=float, default=0.3)
    args = ap.parse_args()

    if args.retour_arriere:
        api = None
        if args.appliquer:
            api = KyntusApi()
            api.login_if_needed()
        return rollback(api, Path(args.retour_arriere), args.pause)

    export = export_dir(args.export)
    print(f"Export utilisé : {export}\n")
    plan, blocking, _ = build_plan(export, Path(args.deplacements), Path(args.correspondances))
    print_plan(plan)

    report = export / "rapport" / "plan_deplacements_pilotes.csv"
    report.parent.mkdir(parents=True, exist_ok=True)
    cols = ["ligne_excel", "collaborateur", "nom_app", "email", "employee_id", "origine", "statut", "motif",
            "libelle_precedent", "cible", "service_precedent", "service_cible"]
    with report.open("w", encoding="utf-8-sig") as f:
        f.write(";".join(cols) + "\n")
        for e in plan:
            f.write(";".join(str(e.get(c) or "").replace(";", ",") for c in cols) + "\n")
    print(f"\nDétail : {report}")

    todo = [e for e in plan if e["statut"] == "A_DEPLACER"]
    if args.limite:
        todo = todo[: args.limite]
    if blocking:
        print(f"\n✘ {len(blocking)} ligne(s) BLOQUANTE(S) : aucune écriture. Corrigez la structure (étape 04) puis refaites l'export 01.")
        return 2

    mirrors = planning_mirrors(export)
    targets = {e["service_cible"] for e in todo}
    if mirrors is None:
        print("\n⚠ planning_sub_services.csv absent de l'export : contrôle Planning impossible.")
        if args.appliquer:
            print("  Refaites l'export 01 (version à jour) avant d'appliquer.")
            return 2
    else:
        missing = sorted(t for t in targets if t not in mirrors)
        if missing:
            print(f"\n✘ {len(missing)} cellule(s) cible(s) pas encore projetée(s) dans Planning : {', '.join(missing)}")
            print("  Attendez quelques secondes (outbox), refaites l'export 01 puis relancez. Aucune écriture.")
            return 2
        print(f"\n✔ Les {len(targets)} cellule(s) cible(s) existent dans Planning.")

    if not args.appliquer:
        print(f"\nSimulation uniquement : {len(todo)} déplacement(s) prêts. Relancez avec --appliquer après validation.")
        return 0
    if not todo:
        print("Rien à déplacer.")
        return 0

    api = KyntusApi()
    api.login_if_needed()
    journal = export / "rapport" / f"journal_pilotes_{datetime.now():%Y%m%d_%H%M%S}.json"
    failures = apply_moves(api, todo, journal, args.pause)
    print(f"\n{len(todo) - failures} déplacement(s) OK, {failures} échec(s).")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
