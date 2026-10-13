#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DEMO 1 — Componentes e interfaces (API REST end-to-end a través del gateway)
#
# Recorre los 3 componentes de negocio pasando SIEMPRE por el ApiGateway:
#   registrar usuario → crear proyecto → crear tarea → asignar → listar.
# Cada paso ejercita una interfaz distinta y las dependencias entre componentes.
# ─────────────────────────────────────────────────────────────────────────────
cd "$(dirname "$0")/.." || exit 1
source scripts/_common.sh

wait_for_gateway

echo "== 1. Registrar usuario (POST /auth/register → UserManager) =="
bootstrap_user
echo "   userId = $OWNER"
echo "   token  = ${TOKEN:0:24}..."

echo; echo "== 2. Ver usuarios (GET /users → UserManager) =="
curl -s "${AUTH[@]}" "$GW/users" | jq .

echo; echo "== 3. Crear proyecto (POST /projects → ProjectManager valida dueño en UserManager) =="
PROJ=$(curl -s -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "{\"ownerId\":\"$OWNER\",\"name\":\"TFU UT3\"}" "$GW/projects")
echo "$PROJ" | jq .
PROJID=$(echo "$PROJ" | jq -r '.id')

echo; echo "== 4. Crear tarea (POST /tasks → TaskManager valida proyecto abierto en ProjectManager) =="
TASK=$(curl -s -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "{\"projectId\":\"$PROJID\",\"title\":\"Escribir el informe\"}" "$GW/tasks")
echo "$TASK" | jq .
TASKID=$(echo "$TASK" | jq -r '.id')

echo; echo "== 5. Asignar tarea (POST /tasks/$TASKID/assign → valida usuario en UserManager) =="
curl -s -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
  -d "{\"userId\":\"$OWNER\"}" "$GW/tasks/$TASKID/assign" | jq .

echo; echo "== 6. Listar tareas del proyecto (GET /tasks?projectId=) =="
curl -s "${AUTH[@]}" "$GW/tasks?projectId=$PROJID" | jq .

echo; echo "✓ Demo 1 OK: los 3 componentes colaboraron a través de sus interfaces."
