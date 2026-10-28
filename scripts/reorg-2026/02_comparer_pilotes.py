#!/usr/bin/env python3
"""
Réorganisation 2026 — ÉTAPE 2 : comparaison des PILOTES (lecture seule).

Compare la liste des agents du fichier (pilotes uniquement) avec les collaborateurs
enregistrés dans Employee Directory, puis produit des CSV de décision.

  * N'écrit RIEN en base. Ne désactive, ne supprime, ne modifie aucun compte.
  * Seuls les collaborateurs au rôle Pilote / Employee sont candidats à la liste
    « présents dans l'application mais absents du fichier » : le fichier ne contient
    que des pilotes et ne doit jamais servir à traiter les autres rôles.

Usage :
  python3 02_comparer_pilotes.py [--export DOSSIER_EXPORT] [--cible donnees/organisation_cible.csv]
Sorties (dans DOSSIER_EXPORT/rapport/) :
  1_pilotes_app_absents_du_fichier.csv   ← à compléter : colonne "decision" = CONSERVER / DESACTIVER
  2_agents_fichier_introuvables_app.csv
  3_correspondances_a_verifier.csv       ← rapprochements approximatifs à confirmer
  4_correspondances_exactes.csv          ← avec cellule actuelle vs cellule cible
  5_fichier_vs_autres_roles.csv          ← noms du fichier portés par un non-pilote (aucune action)
  6_lignes_fichier_non_nominatives.csv
  synthese.md
"""
from __future__ import annotations

import argparse
import csv
import difflib
import re
import sys
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
PILOT_ROLES = {"pilote", "employee"}
FUZZY_THRESHOLD = 0.86


def norm_text(value: str | None) -> str:
    value = (value or "").replace("\xa0", " ")
    value = unicodedata.normalize("NFKD", value)
    value = "".join(ch for ch in value if not unicodedata.combining(ch))
    value = re.sub(r"[^A-Za-z0-9 ]+", " ", value.replace("-", " ").replace("'", " "))
    return re.sub(r"\s+", " ", value).strip().upper()


def name_key(value: str | None) -> str:
    """Clé insensible à l'ordre NOM/Prénom, à la casse, aux accents et aux espaces."""
    return " ".join(sorted(norm_text(value).split()))


def read_csv(path: Path, delimiter: str = ",") -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f, delimiter=delimiter))


def write_csv(path: Path, header: list[str], rows: list[list]) -> None:
    with path.open("w", newline="", encoding="utf-8-sig") as f:  # BOM : ouverture directe dans Excel
        w = csv.writer(f, delimiter=";")
        w.writerow(header)
        w.writerows(rows)


def is_true(v: str | None) -> bool:
    return (v or "").strip().lower() in {"t", "true", "1", "yes", "oui"}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", help="dossier produit par 01_sauvegarde_et_export.sh")
    ap.add_argument("--cible", default=str(HERE / "donnees" / "organisation_cible.csv"))
    args = ap.parse_args()

    export = Path(args.export) if args.export else Path((HERE / ".dernier_export").read_text().strip())
    out = export / "rapport"
    out.mkdir(parents=True, exist_ok=True)

    target_rows = read_csv(Path(args.cible), delimiter=";")
    employees = read_csv(export / "employees.csv")
    depts = {r["Id"]: r for r in read_csv(export / "business_departments.csv")}
    poles = {r["Id"]: r for r in read_csv(export / "org_poles.csv")}
    cellules = {r["Id"]: r for r in read_csv(export / "org_cellules.csv")}
    services = {r["Id"]: r for r in read_csv(export / "org_services.csv")}

    def placement(e: dict[str, str]) -> tuple[str, str, str]:
        svc = services.get(e.get("ServiceId") or "")
        cell_id = (svc or {}).get("CelluleId") or e.get("CelluleId") or ""
        cell = cellules.get(cell_id)
        pole = poles.get((cell or {}).get("PoleId") or e.get("PoleId") or "")
        dept = depts.get((pole or {}).get("BusinessDepartmentId") or e.get("BusinessDepartmentId") or "")
        return ((dept or {}).get("Name", ""), (pole or {}).get("Name", ""), (cell or {}).get("Name", ""))

    agents = [r for r in target_rows if r["type_ligne"] == "agent"]
    non_nominatives = [r for r in target_rows if r["type_ligne"] != "agent"]

    # Index des employés : clé nom/prénom + nom HTEL
    by_key: dict[str, list[dict]] = defaultdict(list)
    for e in employees:
        keys = {name_key(f'{e["FirstName"]} {e["LastName"]}')}
        if e.get("HtelTechnicienName"):
            keys.add(name_key(e["HtelTechnicienName"]))
        for k in keys:
            if k:
                by_key[k].append(e)

    file_key_counts = Counter(name_key(a["collaborateur"]) for a in agents)

    matched_employee_ids: set[str] = set()
    exact_rows, fuzzy_rows, missing_rows, other_role_rows = [], [], [], []
    all_keys = list(by_key.keys())

    for a in agents:
        key = name_key(a["collaborateur"])
        cands = by_key.get(key, [])
        pilots = [e for e in cands if e["Role"].strip().lower() in PILOT_ROLES]
        others = [e for e in cands if e["Role"].strip().lower() not in PILOT_ROLES]
        target = (a["activite"], a["pole"], a["cellule"])

        if pilots:
            for e in pilots:
                matched_employee_ids.add(e["Id"])
                cur = placement(e)
                same_cell = norm_text(cur[2]) == norm_text(target[2])
                exact_rows.append([
                    a["ligne_excel"], a["collaborateur"], e["Id"], e["FirstName"], e["LastName"], e["Email"],
                    e["Role"], "oui" if is_true(e["IsActive"]) else "NON (inactif)",
                    *cur, *target, "identique" if same_cell else "CHANGEMENT",
                    "homonyme dans l'application" if len(pilots) > 1 else "",
                ])
            continue

        if others:
            for e in others:
                other_role_rows.append([
                    a["ligne_excel"], a["collaborateur"], e["Id"], e["FirstName"], e["LastName"], e["Email"],
                    e["Role"], "oui" if is_true(e["IsActive"]) else "non", *placement(e), *target,
                ])
            continue

        # Rapprochement approximatif (fautes de frappe, prénoms composés…)
        best = difflib.get_close_matches(key, all_keys, n=3, cutoff=FUZZY_THRESHOLD)
        best_pilots = [
            (k, e) for k in best for e in by_key[k] if e["Role"].strip().lower() in PILOT_ROLES
        ]
        if best_pilots:
            for k, e in best_pilots:
                score = difflib.SequenceMatcher(None, key, k).ratio()
                fuzzy_rows.append([
                    a["ligne_excel"], a["collaborateur"], f"{score:.2f}", e["Id"], e["FirstName"], e["LastName"],
                    e["Email"], e["Role"], "oui" if is_true(e["IsActive"]) else "non", *placement(e), *target,
                    "",  # confirmer : OUI / NON
                ])
                matched_employee_ids.add(e["Id"])  # ne pas le lister comme « absent » tant que non tranché
        else:
            missing_rows.append([a["ligne_excel"], a["collaborateur"], *target])

    absent_rows = []
    for e in employees:
        if e["Role"].strip().lower() not in PILOT_ROLES or not is_true(e["IsActive"]):
            continue
        if e["Id"] in matched_employee_ids:
            continue
        absent_rows.append([
            e["Id"], e["LastName"], e["FirstName"], e["Email"], e["Role"], *placement(e),
            e.get("IdTechnicien", ""), e.get("HtelTechnicienName", ""), e.get("HireDate", "")[:10],
            "",  # decision : CONSERVER / DESACTIVER
            "",  # commentaire
        ])
    absent_rows.sort(key=lambda r: (r[5], r[6], r[7], r[1], r[2]))

    write_csv(out / "1_pilotes_app_absents_du_fichier.csv",
              ["employee_id", "nom", "prenom", "email", "role", "activite_actuelle", "pole_actuel", "cellule_actuelle",
               "id_technicien_htel", "nom_htel", "date_embauche", "decision", "commentaire"], absent_rows)
    write_csv(out / "2_agents_fichier_introuvables_app.csv",
              ["ligne_excel", "collaborateur_fichier", "activite_cible", "pole_cible", "cellule_cible"], missing_rows)
    write_csv(out / "3_correspondances_a_verifier.csv",
              ["ligne_excel", "collaborateur_fichier", "score", "employee_id", "prenom_app", "nom_app", "email", "role",
               "actif", "activite_actuelle", "pole_actuel", "cellule_actuelle", "activite_cible", "pole_cible",
               "cellule_cible", "confirmer"], fuzzy_rows)
    write_csv(out / "4_correspondances_exactes.csv",
              ["ligne_excel", "collaborateur_fichier", "employee_id", "prenom_app", "nom_app", "email", "role", "actif",
               "activite_actuelle", "pole_actuel", "cellule_actuelle", "activite_cible", "pole_cible", "cellule_cible",
               "affectation", "alerte"], exact_rows)
    write_csv(out / "5_fichier_vs_autres_roles.csv",
              ["ligne_excel", "collaborateur_fichier", "employee_id", "prenom_app", "nom_app", "email", "role_app", "actif",
               "activite_actuelle", "pole_actuel", "cellule_actuelle", "activite_cible", "pole_cible", "cellule_cible"],
              other_role_rows)
    write_csv(out / "6_lignes_fichier_non_nominatives.csv",
              ["ligne_excel", "activite", "pole", "cellule", "libelle"],
              [[r["ligne_excel"], r["activite"], r["pole"], r["cellule"], r["collaborateur"]] for r in non_nominatives])

    pilots_app = [e for e in employees if e["Role"].strip().lower() in PILOT_ROLES and is_true(e["IsActive"])]
    moves = sum(1 for r in exact_rows if r[14] == "CHANGEMENT")
    dup_file = [k for k, n in file_key_counts.items() if n > 1]
    summary = f"""# Comparaison pilotes — fichier vs application (lecture seule)

| Indicateur | Valeur |
|---|---|
| Lignes du fichier | {len(target_rows)} |
| Agents nominatifs du fichier | {len(agents)} |
| Lignes non nominatives à vérifier (ex. « Gestion des RDV ») | {len(non_nominatives)} |
| Pilotes actifs dans l'application | {len(pilots_app)} |
| Correspondances exactes (pilotes) | {len(exact_rows)} |
| … dont changement de cellule | {moves} |
| Correspondances approximatives à confirmer | {len(fuzzy_rows)} |
| Agents du fichier introuvables dans l'application | {len(missing_rows)} |
| Noms du fichier portés par un autre rôle (aucune action) | {len(other_role_rows)} |
| **Pilotes de l'application absents du fichier** | **{len(absent_rows)}** |
| Doublons de noms dans le fichier | {len(dup_file)} |

Aucune donnée n'a été modifiée. Complétez la colonne `decision` (CONSERVER / DESACTIVER)
de `1_pilotes_app_absents_du_fichier.csv` et la colonne `confirmer` (OUI / NON) de
`3_correspondances_a_verifier.csv`, puis renvoyez-les pour validation.
"""
    (out / "synthese.md").write_text(summary, encoding="utf-8")
    print(summary)
    print(f"Rapports : {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
