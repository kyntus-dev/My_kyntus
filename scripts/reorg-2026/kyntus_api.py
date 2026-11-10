"""Client HTTP minimal (stdlib) pour la passerelle Kyntus — utilisé par les étapes 04 à 06."""
from __future__ import annotations

import getpass
import json
import os
import urllib.error
import urllib.request


class KyntusApi:
    def __init__(self, base_url: str | None = None, token: str | None = None):
        self.base = (base_url or os.environ.get("KYNTUS_GATEWAY", "http://localhost:8500")).rstrip("/")
        self.token = token or os.environ.get("KYNTUS_TOKEN", "").strip()

    def login_if_needed(self) -> None:
        if self.token:
            return
        email = os.environ.get("KYNTUS_EMAIL") or input("Email Admin/RH : ").strip()
        password = os.environ.get("KYNTUS_PASSWORD") or getpass.getpass("Mot de passe : ")
        resp = self.request("POST", "/api/auth/login", {"email": email, "password": password}, auth=False)
        self.token = (resp or {}).get("accessToken", "")
        if not self.token:
            raise SystemExit("Connexion refusée : jeton absent.")

    def request(self, method: str, path: str, body: dict | None = None, auth: bool = True):
        data = json.dumps(body).encode("utf-8") if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        if auth and self.token:
            req.add_header("Authorization", f"Bearer {self.token}")
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                raw = r.read().decode("utf-8")
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as e:
            detail = e.read().decode("utf-8", "replace")
            raise RuntimeError(f"{method} {path} → HTTP {e.code} : {detail}") from None
