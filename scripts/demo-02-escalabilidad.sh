#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DEMO 2 — Escalabilidad horizontal
#
# Levanta 3 réplicas de TaskManager y muestra cómo el gateway reparte las
# requests entre ellas. Cada réplica marca su respuesta con el header
# X-Served-By: <hostname>, así se ve qué instancia atendió cada pedido.
# ─────────────────────────────────────────────────────────────────────────────
cd "$(dirname "$0")/.." || exit 1
source scripts/_common.sh

echo "== Escalando TaskManager a 3 réplicas =="
docker compose up -d --scale taskmanager=3
echo "   esperando a que las réplicas estén listas..."
sleep 8
docker compose ps taskmanager --format "table {{.Name}}\t{{.Status}}"

wait_for_gateway
bootstrap_user
PROJID=$(create_project "Escalabilidad")

echo; echo "== 30 requests a GET /tasks: réplica que respondió cada una =="
for _ in $(seq 1 30); do
  curl -s -D - -o /dev/null "${AUTH[@]}" "$GW/tasks?projectId=$PROJID" \
    | awk 'tolower($1)=="x-served-by:"{print $2}' | tr -d '\r'
done | sort | uniq -c | sort -rn

echo
echo "Si aparecen 2 o 3 hostnames distintos, el gateway está balanceando entre réplicas. ✓"
