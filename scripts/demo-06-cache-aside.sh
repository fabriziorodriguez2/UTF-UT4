#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DEMO 5 — Cache-Aside (Redis)
#
# TaskManager valida en ProjectManager que el proyecto esté abierto antes de
# crear cada tarea. Con Cache-Aside, la primera vez es un MISS (llamada HTTP real
# + se guarda en Redis con TTL) y las siguientes son HIT, aunque las atiendan
# réplicas distintas: el caché es compartido.
# ─────────────────────────────────────────────────────────────────────────────
cd "$(dirname "$0")/.." || exit 1
source scripts/_common.sh

echo "== Escalando TaskManager a 3 réplicas =="
docker compose up -d --scale taskmanager=3
echo "   esperando a que las réplicas estén listas..."
sleep 8

wait_for_gateway
bootstrap_user
PROJID=$(create_project "Proyecto cache")
KEY="projects:$PROJID:is-open"
echo "   proyecto: $PROJID"

echo; echo "== 6 tareas en el mismo proyecto (cada una valida IsOpen) =="
for i in $(seq 1 6); do
  curl -s -D - -o /dev/null -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
    -d "{\"projectId\":\"$PROJID\",\"title\":\"Tarea $i\"}" "$GW/tasks" \
    | awk -v i="$i" 'tolower($1)=="x-served-by:"{printf "   tarea %s → réplica %s\n", i, $2}' | tr -d '\r'
done
sleep 1

echo; echo "== Logs de TaskManager para la clave $KEY =="
LOGS=$(docker compose logs taskmanager 2>/dev/null | grep "$KEY" || true)
echo "$LOGS" | sed 's/^/   /'
MISS=$(echo "$LOGS" | grep -c "Cache MISS" || true)
HIT=$(echo "$LOGS" | grep -c "Cache HIT" || true)
echo "   MISS: $MISS   HIT: $HIT"

echo; echo "== Entrada en Redis (TTL restante en segundos) =="
echo "   TTL: $(docker compose exec -T redis redis-cli TTL "mgp:$KEY" | tr -d '\r')"

if [ "$MISS" = "1" ] && [ "$HIT" -ge 5 ]; then
  echo "✓ Demo 5 OK: una sola llamada a ProjectManager; el resto salió del caché compartido."
else
  echo "✗ Demo 5 FALLÓ."; exit 1
fi
