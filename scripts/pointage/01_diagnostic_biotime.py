#!/usr/bin/env python3
"""
Pointage — ÉTAPE 1 (LECTURE SEULE) : diagnostic de l'API ZKBio Time avant intégration à My-Kyntus.

À lancer depuis le serveur My-Kyntus (10.10.10.25), qui est sur le même réseau que ZKBio Time.
Le script ne modifie RIEN, ni dans ZKBio Time ni dans My-Kyntus. Il vérifie :

  1. que l'API répond et quel mode d'authentification elle accepte ;
  2. les pointeuses connues (une par étage) et leur dernière activité ;
  3. les pointages des derniers jours (volume, états entrée / sortie / pause, par pointeuse) ;
  4. la correspondance entre l'ID pointeuse (emp_code) et les collaborateurs My-Kyntus
     (par ID technicien HTEL, sinon par nom) → rapport CSV à relire.

Le certificat HTTPS de ZKBio Time est auto-signé (« Non sécurisé » dans le navigateur) : au lieu de
désactiver le contrôle, on épingle son empreinte SHA-256.

  # 1) relever l'empreinte du certificat, puis la comparer à celle affichée par le navigateur
  python3 01_diagnostic_biotime.py --empreinte
  # 2) diagnostic complet (compte ZKBio Time dédié, en lecture seule)
  python3 01_diagnostic_biotime.py --empreinte-attendue AB:CD:...

Variables d'environnement : BIOTIME_URL (défaut https://10.10.10.7), BIOTIME_USER, BIOTIME_PASSWORD,
KYNTUS_GATEWAY / KYNTUS_EMAIL / KYNTUS_PASSWORD (pour la correspondance avec My-Kyntus).
"""
from __future__ import annotations

import argparse
import csv
import getpass
import hashlib
import json
import os
import re
import socket
import ssl
import sys
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "reorg-2026"))

# États de pointage ZKTeco (punch_state).
ETATS = {
    "0": "Entrée",
    "1": "Sortie",
    "2": "Début de pause",
    "3": "Fin de pause",
    "4": "Entrée heures sup.",
    "5": "Sortie heures sup.",
    "255": "Non défini",
}


def norm(value: str | None) -> str:
    value = unicodedata.normalize("NFKD", (value or "").replace("\xa0", " "))
    value = "".join(ch for ch in value if not unicodedata.combining(ch))
    value = re.sub(r"[^A-Za-z0-9 ]+", " ", value)
    return re.sub(r"\s+", " ", value).strip().upper()


def name_key(*parts: str | None) -> frozenset[str]:
    return frozenset(t for p in parts for t in norm(p).split(" ") if t)


# ── TLS : épinglage de l'empreinte du certificat auto-signé ─────────────────────

def empreinte_serveur(url: str) -> str:
    u = urllib.parse.urlparse(url)
    port = u.port or (443 if u.scheme == "https" else 80)
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    with socket.create_connection((u.hostname, port), timeout=10) as sock:
        with ctx.wrap_socket(sock, server_hostname=u.hostname) as tls:
            der = tls.getpeercert(binary_form=True)
    digest = hashlib.sha256(der).hexdigest().upper()
    return ":".join(digest[i:i + 2] for i in range(0, len(digest), 2))


class BioTime:
    def __init__(self, url: str, empreinte: str | None):
        self.base = url.rstrip("/")
        self.auth_header = ""
        self.ctx = None
        if self.base.startswith("https"):
            if not empreinte:
                raise SystemExit("HTTPS : indiquez --empreinte-attendue (relevée avec --empreinte).")
            reelle = empreinte_serveur(self.base)
            if reelle.replace(":", "") != empreinte.upper().replace(":", ""):
                raise SystemExit(f"STOP : empreinte du certificat inattendue.\n  attendue : {empreinte}\n  reçue    : {reelle}")
            # Certificat auto-signé : vérifié par empreinte ci-dessus, pas par une autorité.
            self.ctx = ssl.create_default_context()
            self.ctx.check_hostname = False
            self.ctx.verify_mode = ssl.CERT_NONE

    def _call(self, method: str, path: str, body: dict | None = None, auth: bool = True):
        data = json.dumps(body).encode("utf-8") if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        req.add_header("Accept", "application/json")
        if auth and self.auth_header:
            req.add_header("Authorization", self.auth_header)
        try:
            with urllib.request.urlopen(req, timeout=60, context=self.ctx) as r:
                raw = r.read().decode("utf-8", "replace")
                return r.status, (json.loads(raw) if raw.strip().startswith(("{", "[")) else raw)
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode("utf-8", "replace")[:300]

    def login(self, user: str, password: str) -> str:
        """Essaie le jeton classique (Token) puis JWT ; retourne le mode retenu."""
        for path, prefix in (("/api-token-auth/", "Token"), ("/jwt-api-token-auth/", "JWT")):
            status, resp = self._call("POST", path, {"username": user, "password": password}, auth=False)
            token = resp.get("token") if isinstance(resp, dict) else None
            if status < 300 and token:
                self.auth_header = f"{prefix} {token}"
                status, _ = self._call("GET", "/iclock/api/terminals/?page_size=1")
                if status < 300:
                    return f"{prefix} ({path})"
        return None

    def pages(self, path: str, params: dict, limite: int = 200_000):
        """Parcourt toutes les pages ({count, next, data})."""
        params = {**params, "page_size": params.get("page_size", 200), "page": 1}
        lus = 0
        while True:
            status, resp = self._call("GET", f"{path}?{urllib.parse.urlencode(params)}")
            if status >= 300 or not isinstance(resp, dict):
                raise RuntimeError(f"GET {path} → HTTP {status} : {resp}")
            rows = resp.get("data") or resp.get("results") or []
            for row in rows:
                yield row
                lus += 1
                if lus >= limite:
                    return
            if not resp.get("next") or not rows:
                return
            params["page"] += 1


def main() -> int:
    ap = argparse.ArgumentParser(description="Diagnostic API ZKBio Time (lecture seule)")
    ap.add_argument("--url", default=os.environ.get("BIOTIME_URL", "https://10.10.10.7"))
    ap.add_argument("--empreinte", action="store_true", help="affiche l'empreinte SHA-256 du certificat et s'arrête")
    ap.add_argument("--empreinte-attendue", default=os.environ.get("BIOTIME_CERT_SHA256"))
    ap.add_argument("--jours", type=int, default=2, help="période de pointages analysée (défaut : 2 jours)")
    ap.add_argument("--sans-kyntus", action="store_true", help="ne pas comparer avec My-Kyntus")
    args = ap.parse_args()

    if args.empreinte:
        print(f"Empreinte SHA-256 du certificat de {args.url} :\n  {empreinte_serveur(args.url)}")
        print("Comparez-la avec celle du navigateur (cadenas › certificat › empreinte SHA-256).")
        return 0

    out = HERE / f"diagnostic_{datetime.now():%Y%m%d_%H%M}"
    out.mkdir(exist_ok=True)
    bt = BioTime(args.url, args.empreinte_attendue)
    user = os.environ.get("BIOTIME_USER") or input("Compte ZKBio Time (lecture seule) : ").strip()
    mode = None
    for essai in range(3):
        password = (os.environ.get("BIOTIME_PASSWORD") if essai == 0 else None) \
            or getpass.getpass(f"Mot de passe ZKBio Time du compte « {user} » : ")
        mode = bt.login(user, password)
        if mode:
            break
        print("  ✘ refusé par ZKBio Time (compte ou mot de passe).")
    if not mode:
        print("Authentification refusée 3 fois : vérifiez le compte dans ZKBio Time (Système › Utilisateur),\n"
              "ou que l'API est disponible (https://10.10.10.7/api/docs/).")
        return 2
    print(f"✔ API joignable — authentification : {mode}")

    # 2. Pointeuses
    terminaux = list(bt.pages("/iclock/api/terminals/", {"page_size": 50}))
    print(f"\n{len(terminaux)} pointeuse(s) :")
    for t in terminaux:
        print(f"  - {t.get('sn', '?'):<18} {str(t.get('alias') or t.get('terminal_name') or ''):<16} "
              f"{str(t.get('ip_address') or ''):<15} état={t.get('state')}  dernière activité={t.get('last_activity')}")

    # 3. Pointages récents
    fin = datetime.now()
    debut = fin - timedelta(days=args.jours)
    tx = list(bt.pages("/iclock/api/transactions/", {
        "start_time": debut.strftime("%Y-%m-%d %H:%M:%S"),
        "end_time": fin.strftime("%Y-%m-%d %H:%M:%S"),
    }))
    print(f"\n{len(tx)} pointage(s) du {debut:%d/%m %H:%M} au {fin:%d/%m %H:%M}")
    for etat, n in Counter(str(t.get("punch_state")) for t in tx).most_common():
        print(f"  - état {etat:<4} {ETATS.get(etat, 'inconnu'):<20} {n}")
    par_terminal = Counter(str(t.get("terminal_alias") or t.get("terminal_sn")) for t in tx)
    for term, n in par_terminal.most_common():
        print(f"  - {term:<20} {n}")
    retard = [t for t in tx if t.get("upload_time") and t.get("punch_time")]
    if retard:
        delais = sorted(
            (datetime.fromisoformat(str(t["upload_time"])[:19]) - datetime.fromisoformat(str(t["punch_time"])[:19])).total_seconds()
            for t in retard)
        print(f"  Délai pointeuse → ZKBio Time : médiane {delais[len(delais) // 2]:.0f} s, max {delais[-1]:.0f} s")
        tardifs = Counter(
            str(t.get("terminal_alias") or t.get("terminal_sn")) for t in retard
            if (datetime.fromisoformat(str(t["upload_time"])[:19]) - datetime.fromisoformat(str(t["punch_time"])[:19])).total_seconds() > 300)
        if tardifs:
            print("  Pointages arrivés avec plus de 5 min de retard (pointeuse momentanément hors ligne) : "
                  + ", ".join(f"{k} {v}" for k, v in tardifs.most_common()))

    if tx and all(str(t.get("punch_state")) == "255" for t in tx):
        print("  ⚠ Les pointeuses n'enregistrent pas l'état (entrée / sortie / pause) : il sera déduit de l'ordre\n"
              "    des pointages de la journée (1er = arrivée, dernier = départ, paires intermédiaires = pauses).")

    par_jour: dict[tuple[str, str], list[datetime]] = defaultdict(list)
    for t in tx:
        moment = datetime.fromisoformat(str(t["punch_time"])[:19])
        par_jour[(str(t.get("emp_code")), moment.strftime("%Y-%m-%d"))].append(moment)
    if par_jour:
        repartition = Counter(min(len(v), 6) for v in par_jour.values())
        print("  Pointages par personne et par jour : "
              + ", ".join(f"{'6+' if k == 6 else k} → {n}" for k, n in sorted(repartition.items())))
        doubles = 0
        for moments in par_jour.values():
            moments.sort()
            doubles += sum(1 for a, b in zip(moments, moments[1:]) if (b - a).total_seconds() < 120)
        print(f"  Doubles badgeages (< 2 min d'écart, à fusionner) : {doubles}")

    champs = sorted({k for t in tx[:50] for k in t.keys()})
    print(f"  Champs disponibles : {', '.join(champs)}")
    (out / "echantillon_pointages.json").write_text(json.dumps(tx[:5], ensure_ascii=False, indent=2, default=str), encoding="utf-8")

    # 4. Collaborateurs ZKBio Time et correspondance My-Kyntus
    employes = list(bt.pages("/personnel/api/employees/", {"page_size": 200}))
    print(f"\n{len(employes)} collaborateur(s) dans ZKBio Time")
    actifs = {str(t.get("emp_code")) for t in tx}

    rows = []
    if args.sans_kyntus:
        for e in employes:
            rows.append({"emp_code": e.get("emp_code"), "nom_biotime": f"{e.get('last_name') or ''} {e.get('first_name') or ''}".strip(),
                         "departement_biotime": (e.get("department") or {}).get("dept_name") if isinstance(e.get("department"), dict) else e.get("department"),
                         "kyntus_id": "", "kyntus_nom": "", "methode": "non comparé", "a_pointe": str(e.get("emp_code")) in actifs})
    else:
        from kyntus_api import KyntusApi  # noqa: E402  (scripts/reorg-2026)
        api = KyntusApi()
        email = os.environ.get("KYNTUS_EMAIL") or input("Email My-Kyntus (Admin / RH) : ").strip()
        for _ in range(3):
            mdp = getpass.getpass(f"Mot de passe My-Kyntus de « {email} » : ")
            try:
                resp = api.request("POST", "/api/auth/login", {"email": email, "password": mdp}, auth=False)
                api.token = (resp or {}).get("accessToken", "")
            except RuntimeError as err:
                print(f"  ✘ {err}")
                continue
            if api.token:
                break
        kyntus = []
        if api.token:
            kyntus = api.request("GET", "/api/directory/employees") or []
        else:
            print("  Connexion My-Kyntus impossible : rapport produit sans la correspondance (relancez plus tard).")
        par_idtech = {str(k.get("idTechnicien")): k for k in kyntus if k.get("idTechnicien") is not None}
        par_nom: dict[frozenset, list] = defaultdict(list)
        for k in kyntus:
            par_nom[name_key(k.get("firstName"), k.get("lastName"))].append(k)

        for e in employes:
            code = str(e.get("emp_code") or "").strip()
            nom_bt = f"{e.get('last_name') or ''} {e.get('first_name') or ''}".strip()
            dept = e.get("department")
            dept = dept.get("dept_name") if isinstance(dept, dict) else dept
            k, methode = None, ("introuvable" if kyntus else "non comparé")
            if code in par_idtech:
                k, methode = par_idtech[code], "ID technicien HTEL"
            else:
                cands = par_nom.get(name_key(e.get("first_name"), e.get("last_name")), [])
                if len(cands) == 1:
                    k, methode = cands[0], "nom"
                elif len(cands) > 1:
                    methode = "homonymes"
            rows.append({"emp_code": code, "nom_biotime": nom_bt, "departement_biotime": dept,
                         "kyntus_id": (k or {}).get("id", ""),
                         "kyntus_nom": f"{(k or {}).get('lastName', '')} {(k or {}).get('firstName', '')}".strip(),
                         "methode": methode, "a_pointe": code in actifs})

        if kyntus:
            stats = Counter(r["methode"] for r in rows)
            print("Correspondance ID pointeuse → My-Kyntus :")
            for m, n in stats.most_common():
                print(f"  - {m:<20} {n}")
            egal = sum(1 for r in rows if r["methode"] == "ID technicien HTEL")
            print(f"  → l'ID pointeuse {'EST' if egal > len(rows) * 0.8 else 'n’est PAS'} (majoritairement) l'ID technicien HTEL.")
            sans = [r for r in rows if r["a_pointe"] and r["methode"] in ("introuvable", "homonymes")]
            print(f"  Personnes qui pointent sans correspondance My-Kyntus : {len(sans)}")

    with (out / "correspondance_pointeuse_kyntus.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()) if rows else ["emp_code"], delimiter=";")
        w.writeheader()
        w.writerows(rows)

    print(f"\nRapport : {out}/  (correspondance_pointeuse_kyntus.csv, echantillon_pointages.json)")
    print("Aucune donnée n'a été modifiée.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
