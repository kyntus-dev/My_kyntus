#!/usr/bin/env python3
"""
Réécrit une copie de docker-compose.yml pour l'environnement de test « kyntus-test ».

- noms de conteneurs : kyntus_X -> kyntustest_X, avec l'ancien nom gardé comme alias réseau
  (les adresses internes « kyntus_gateway », « kyntus_auth_backend »… restent valables, mais
  seulement à l'intérieur du réseau de test) ;
- ports hôte décalés de +20000 (8200 -> 28200, 8500 -> 28500…) : aucun conflit avec la production ;
- image Postgres renommée : la construction de test n'écrase jamais l'image de production ;
- adresses publiques 10.10.10.25:8200/8201/8500 remplacées par les ports de test ;
- service Mailpit ajouté : il capture TOUS les e-mails (interface web sur le port 28025).

Usage : reecrire_compose.py <docker-compose.yml de la copie> <adresse IP du serveur>
"""
import re
import sys

OFFSET = 20000
path, host = sys.argv[1], sys.argv[2]
lines = open(path, encoding="utf-8").read().split("\n")
out = []
service = None
container = None
for line in lines:
    m = re.match(r"^  ([a-z0-9_-]+):\s*$", line)
    if m:
        service, container = m.group(1), None
    elif re.match(r"^[a-z]", line):
        service, container = None, None

    m = re.match(r"^(\s+)container_name:\s*(kyntus_[a-z0-9_]+)\s*$", line)
    if m:
        container = m.group(2)
        line = f"{m.group(1)}container_name: {container.replace('kyntus_', 'kyntustest_', 1)}"

    m = re.match(r'^(\s+)image:\s*projets_mykyntus--postgres:(\S+)\s*$', line)
    if m:
        line = f"{m.group(1)}image: kyntus-test-postgres:{m.group(2)}"

    m = re.match(r'^(\s+)- "?(\d+):(\d+)"?\s*$', line)
    if m and service:
        line = f'{m.group(1)}- "{int(m.group(2)) + OFFSET}:{m.group(3)}"'

    m = re.match(r"^(\s+)- microservices-network\s*$", line)
    if m and service and container:
        ind = m.group(1)
        out.append(f"{ind}microservices-network:")
        out.append(f'{ind}  aliases: ["{container}"]')
        continue

    if line.startswith("volumes:") and service is None and not any(l.startswith("  mailpit:") for l in out):
        out += [
            "  # E-mails de test : tout est capturé ici, rien ne part vers les vraies boîtes.",
            "  mailpit:",
            "    image: axllent/mailpit:v1.21",
            "    container_name: kyntustest_mailpit",
            "    restart: unless-stopped",
            "    ports:",
            f'      - "{8025 + OFFSET}:8025"',
            "    networks:",
            "      - microservices-network",
            "",
        ]
    out.append(line)

text = "\n".join(out)
for port in ("8200", "8201", "8500"):
    text = text.replace(f"{host}:{port}", f"{host}:{int(port) + OFFSET}")
open(path, "w", encoding="utf-8").write(text)
print("docker-compose.yml de test réécrit.")
