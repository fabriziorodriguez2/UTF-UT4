#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DEMO 3 — ACID (atomicidad con transacciones)
#
# Ejemplo del enunciado: "crear una tarea y asignarla" debe ser atómico.
# Se invoca crear+asignar con un usuario INEXISTENTE: la validación falla, la
# transacción hace rollback y la tarea NO queda persistida (todo o nada).
# ─────────────────────────────────────────────────────────────────────────────
cd "$(dirname "$0")/.." || exit 1
source scripts/_common.sh

wait_for_gateway
bootstrap_user
PROJID=$(create_project "ACID")

BEFORE=$(curl -s "${AUTH[@]}" "$GW/tasks?projectId=$PROJID" | jq 'length')
echo "== Tareas del proyecto ANTES: $BEFORE =="

echo; echo "== crear+asignar con userId inexistente (debe fallar con 409) =="
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "${AUTH[@]}" \
  -H 'Content-Type: application/json' \
  -d '{"projectId":"'"$PROJID"'","title":"Tarea que no debe quedar","userId":"00000000-0000-0000-0000-000000000000"}' \
  "$GW/tasks/create-and-assign")
echo "   HTTP $CODE"

AFTER=$(curl -s "${AUTH[@]}" "$GW/tasks?projectId=$PROJID" | jq 'length')
echo; echo "== Tareas del proyecto DESPUÉS: $AFTER =="

if [ "$CODE" = "409" ] && [ "$BEFORE" = "$AFTER" ]; then
  echo "✓ Demo 3 OK: la operación falló y no quedó nada a medio crear (rollback)."
else
  echo "✗ Demo 3 FALLÓ: se esperaba 409 y que la cantidad no cambiara."
  exit 1
fi
