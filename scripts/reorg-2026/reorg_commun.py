"""Fonctions partagées par les étapes 03 et 07 (normalisation des noms, lecture des exports)."""
from __future__ import annotations

import csv
import re
import unicodedata
from pathlib import Path

HERE = Path(__file__).resolve().parent
KIND = {"0": "ChefDeProjet", "1": "Superviseur", "2": "ReferentTechnique", "3": "Pilote"}


def norm(value: str | None) -> str:
    """Clé de comparaison : sans accents, sans ponctuation, majuscules."""
    value = (value or "").replace("\xa0", " ")
    value = unicodedata.normalize("NFKD", value)
    value = "".join(ch for ch in value if not unicodedata.combining(ch))
    value = re.sub(r"[^A-Za-z0-9&/ ]+", " ", value)
    return re.sub(r"\s+", " ", value).strip().upper()


def norm_pole(value: str | None) -> str:
    """« pole technique », « Pôle technique » et « technique » désignent le même pôle."""
    return re.sub(r"^POLE ", "", norm(value))


def harmonize_pole_label(value: str) -> str:
    """Q7 : « pole technique » → « Pôle technique », « pole BTB » → « Pôle BTB »."""
    value = re.sub(r"\s+", " ", (value or "").strip())
    if norm(value).startswith("POLE "):
        return "Pôle " + value.split(" ", 1)[1]
    return value


def read_csv(path: Path, delimiter: str = ",") -> list[dict[str, str]]:
    if not path.exists():
        return []
    with path.open(newline="", encoding="utf-8-sig") as f:
        return list(csv.DictReader(f, delimiter=delimiter))


def is_true(value: str | None) -> bool:
    return (value or "").strip().lower() in ("t", "true", "1", "oui", "yes")


def export_dir(explicit: str | None) -> Path:
    if explicit:
        return Path(explicit)
    marker = HERE / ".dernier_export"
    if not marker.exists():
        raise SystemExit("Aucun export : lancez d'abord 01_sauvegarde_et_export.sh")
    return Path(marker.read_text().strip())


def load_activity_mapping(path: Path) -> dict[str, str]:
    """donnees/correspondance_activites.csv : activité du fichier → code / nom / id existant."""
    return {
        norm(r["activite_fichier"]): r["departement_existant"].strip()
        for r in read_csv(path, ";")
        if r.get("departement_existant", "").strip()
    }


def find_department(activity: str, depts: list[dict], mapping: dict[str, str]) -> dict | None:
    key = norm(activity)
    wanted = mapping.get(key)
    if wanted:
        for d in depts:
            if d["Code"] == wanted or d["Id"] == wanted or norm(d["Name"]) == norm(wanted):
                return d
    for d in depts:
        if norm(d["Name"]) == key:
            return d
    return None
