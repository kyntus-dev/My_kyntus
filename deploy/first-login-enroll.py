#!/usr/bin/env python3
"""Activation explicite des comptes Auth, prévisualisation par défaut (bibliothèque standard)."""
import argparse
import getpass
import json
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', required=True, help='Origine HTTPS de la gateway, sans /api')
    parser.add_argument('--ids-file', required=True, help='Fichier UTF-8 : un ID Auth entier par ligne')
    parser.add_argument('--apply', action='store_true', help='Appliquer aux seuls comptes sélectionnés')
    parser.add_argument('--renew-expired', action='store_true', help='Renouveler aussi les activations expirées')
    args = parser.parse_args()
    url = urllib.parse.urlparse(args.base_url)
    if url.scheme != 'https' or not url.hostname or url.username or url.password or url.query or url.fragment:
        parser.error('Utiliser une origine HTTPS valide sans identifiants ni paramètres.')
    try:
        ids = list(dict.fromkeys(int(line.strip()) for line in Path(args.ids_file).read_text(encoding='utf-8').splitlines() if line.strip()))
    except (ValueError, OSError):
        parser.error('Le fichier doit contenir un ID Auth entier positif par ligne.')
    if not 1 <= len(ids) <= 500 or any(i <= 0 for i in ids):
        parser.error('Sélectionner entre 1 et 500 IDs Auth positifs.')
    token = getpass.getpass('Jeton accessToken Admin ou RH (saisie masquée) : ').strip()
    body = json.dumps({'userIds': ids, 'dryRun': not args.apply, 'renewExpired': args.renew_expired}).encode()
    request = urllib.request.Request(args.base_url.rstrip('/') + '/api/Auth/first-login/enroll', data=body,
        headers={'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'}, method='POST')
    # Never forward the bearer to a redirect target.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):
            return None
    opener = urllib.request.build_opener(NoRedirect(), urllib.request.HTTPSHandler(context=ssl.create_default_context()))
    try:
        with opener.open(request, timeout=60) as response:
            result = json.load(response)
    except urllib.error.HTTPError as error:
        print(f'Activation refusée (HTTP {error.code}). Vérifier le rôle, le jeton, la configuration et la gateway.', file=sys.stderr)
        return 1
    except (urllib.error.URLError, TimeoutError):
        print('Serveur indisponible. Vérifier TLS et la connexion. En cas de délai dépassé, relancer une prévisualisation.', file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
