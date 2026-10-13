#!/usr/bin/env bash
# Materialized View: materializar al escribir y leer con UserManager detenido.
cd "$(dirname "$0")/.." || exit 1
source scripts/_common.sh

wait_for_gateway
bootstrap_user
PROJID=$(create_project "Materialized View")

echo "== 1. Crear y asignar: se guarda el nombre junto a la tarea =="
TASK=$(curl --fail -sS -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "{\"projectId\":\"$PROJID\",\"title\":\"Informe\"}" "$GW/tasks")
TASKID=$(printf '%s' "$TASK" | jq -er '.id')
ASSIGNED=$(curl --fail -sS -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "{\"userId\":\"$OWNER\"}" "$GW/tasks/$TASKID/assign")
printf '%s' "$ASSIGNED" | jq .
printf '%s' "$ASSIGNED" | jq -e --arg owner "$OWNER" \
  '.assignee == $owner and .assigneeName == "Euge"' >/dev/null

echo "== 2. Crear y asignar en una transacción: también materializa el nombre =="
COMBINED=$(curl --fail -sS -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "{\"projectId\":\"$PROJID\",\"title\":\"Pruebas\",\"userId\":\"$OWNER\"}" \
  "$GW/tasks/create-and-assign")
printf '%s' "$COMBINED" | jq .
printf '%s' "$COMBINED" | jq -e '.assigneeName == "Euge"' >/dev/null

curl --fail -sS -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "{\"projectId\":\"$PROJID\",\"title\":\"Sin responsable\"}" "$GW/tasks" >/dev/null

echo "== 3. Detener UserManager y leer el tablero con el mismo JWT =="
# Restaurar el servicio incluso si falla alguna comprobación de la demo.
trap 'docker compose start usermanager >/dev/null' EXIT
docker compose stop usermanager
BOARD=$(curl --fail -sS "${AUTH[@]}" "$GW/tasks?projectId=$PROJID")
printf '%s' "$BOARD" | jq .
printf '%s' "$BOARD" | jq -e --arg owner "$OWNER" '
  length == 3 and
  ([.[] | select(.assignee == $owner and .assigneeName == "Euge")] | length) == 2 and
  ([.[] | select(.title == "Sin responsable" and .assigneeName == null)] | length) == 1
' >/dev/null

echo "Demo 5 OK: tablero con nombres desde tasks-db, sin depender de UserManager."
echo "UserManager se vuelve a iniciar al salir del script."
