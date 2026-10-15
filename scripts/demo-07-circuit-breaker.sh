#!/usr/bin/env bash
# DEMO 7 - Circuit Breaker. Requiere la configuracion predeterminada y una
# replica de TaskManager. Restaura UserManager incluso si una comprobacion falla.
cd "$(dirname "$0")/.." || exit 1
source scripts/_common.sh

GW="${GW%/}"
TOKEN=''
RESTORE_USERS=0

fail() { echo "ERROR: $*" >&2; exit 1; }

cleanup() {
  local status=$?
  trap - EXIT
  if [ "$RESTORE_USERS" = 1 ]; then
    echo 'Restaurando UserManager...'
    if ! docker compose start usermanager; then
      echo 'ERROR: ejecuta docker compose start usermanager para restaurarlo.' >&2
      status=1
    fi
  fi
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# Conserva cuerpo, estado y tiempo sin ocultar errores HTTP (incluidos 503).
api() {
  local method=$1 path=$2 raw metadata
  local args=(-sS --max-time 10 -X "$method")
  if [ -n "$TOKEN" ]; then args+=(-H "Authorization: Bearer $TOKEN"); fi
  if [ "$#" -ge 3 ]; then args+=(-H 'Content-Type: application/json' -d "$3"); fi
  raw=$(curl "${args[@]}" -w '\n%{http_code} %{time_total}' "$GW$path") || fail "No se pudo consultar $method $path."
  BODY=${raw%$'\n'*}
  metadata=${raw##*$'\n'}
  read -r STATUS ELAPSED <<< "$metadata"
  [ "$STATUS" != 429 ] || fail 'El gateway limito las solicitudes. Espera o aumenta RATELIMIT_PERMIT y repeti la demo.'
}

ok() {
  api "$@"
  [[ "$STATUS" == 2?? ]] || fail "$1 $2 devolvio HTTP $STATUS: $BODY"
}

assert_json() {
  printf '%s' "$BODY" | jq -e "$@" >/dev/null || fail 'La respuesta no cumple la comprobacion esperada.'
}

attempts() {
  local logs
  logs=$(docker compose logs --no-color taskmanager) || fail 'No se pudieron leer los logs.'
  printf '%s\n' "$logs" | awk '/Dependency HTTP attempt UserManager/ { count++ } END { print count+0 }'
}

command -v docker >/dev/null 2>&1 || fail 'Falta Docker.'
docker info --format '{{.ServerVersion}}' >/dev/null || fail 'Abri Docker Desktop antes de ejecutar la demo.'
TASKS=$(docker compose ps --status running -q taskmanager)
COUNT=$(printf '%s\n' "$TASKS" | awk 'NF { count++ } END { print count+0 }')
[ "$COUNT" = 1 ] || fail 'Se requiere una replica: docker compose up -d --build --scale taskmanager=1'
USERS=$(docker compose ps --status running -q usermanager)
[ -n "$USERS" ] || fail 'UserManager debe estar levantado antes de empezar.'
wait_for_gateway

echo '== 1. Crear datos y comprobar una asignacion normal =='
ok POST '/auth/register' "$(jq -nc --arg email "circuit+$RANDOM$RANDOM@example.com" \
  '{name:"Ana Circuit",email:$email,password:"DemoCircuit123!"}')"
TOKEN=$(printf '%s' "$BODY" | jq -er '.token | select(type == "string" and length > 0)')
OWNER=$(jwt_sub "$TOKEN")
[ -n "$OWNER" ] && [ "$OWNER" != null ] || fail 'El JWT no contiene el usuario.'
ok POST '/projects' "$(jq -nc --arg id "$OWNER" '{ownerId:$id,name:"Demo Circuit Breaker"}')"
PROJECT=$(printf '%s' "$BODY" | jq -er '.id')
ok POST '/projects' "$(jq -nc --arg id "$OWNER" '{ownerId:$id,name:"Circuito independiente"}')"
OTHER_PROJECT=$(printf '%s' "$BODY" | jq -er '.id')
ok POST '/tasks/create-and-assign' "$(jq -nc --arg project "$PROJECT" --arg user "$OWNER" \
  '{projectId:$project,title:"Informe",userId:$user}')"
assert_json '.assigneeName == "Ana Circuit"'
TASK=$(printf '%s' "$BODY" | jq -er '.id')
ASSIGNMENT="/tasks/$TASK/assign"
ASSIGN_BODY=$(jq -nc --arg id "$OWNER" '{userId:$id}')
# Sacar los exitos de la ventana de muestreo de 10 segundos.
sleep 11

echo '== 2. Detener UserManager y provocar tres fallos reales =='
RESTORE_USERS=1
docker compose stop usermanager
for i in 1 2 3; do
  api POST "$ASSIGNMENT" "$ASSIGN_BODY"
  [ "$STATUS" = 503 ] || fail "Se esperaba HTTP 503; se recibio $STATUS."
  assert_json '.code == "dependency_unavailable"'
  echo "Intento $i: HTTP 503 dependency_unavailable"
done
sleep 0.3
BEFORE=$(attempts)

echo '== 3. Circuito abierto: rechaza sin enviar otra llamada HTTP =='
api POST "$ASSIGNMENT" "$ASSIGN_BODY"
[ "$STATUS" = 503 ] || fail "Se esperaba HTTP 503; se recibio $STATUS."
assert_json '.code == "circuit_open"'
echo "HTTP 503 circuit_open en ${ELAPSED}s."
sleep 0.3
[ "$(attempts)" = "$BEFORE" ] || fail 'Se envio una llamada HTTP con el circuito abierto.'
echo 'OK: sin nuevos intentos HTTP en los logs.'

echo '== 4. El tablero sigue disponible y conserva la asignacion =='
ok GET "/tasks?projectId=$PROJECT"
assert_json --arg user "$OWNER" --arg task "$TASK" \
  'length == 1 and .[0].id == $task and .[0].assignee == $user and .[0].assigneeName == "Ana Circuit"'
# Este proyecto no tiene entrada en cache: prueba otra dependencia HTTP.
ok POST '/tasks' "$(jq -nc --arg id "$OTHER_PROJECT" '{projectId:$id,title:"Otra dependencia disponible"}')"

echo '== 5. Restaurar UserManager y esperar la prueba de recuperacion =='
docker compose start usermanager
READY=0
for _ in $(seq 1 20); do
  sleep 2
  api GET "/users/$OWNER"
  if [ "$STATUS" = 200 ]; then READY=1; break; fi
done
[ "$READY" = 1 ] || fail 'UserManager no se recupero.'
sleep 16
ok POST "$ASSIGNMENT" "$ASSIGN_BODY"
assert_json '.assigneeName == "Ana Circuit"'
ok POST "$ASSIGNMENT" "$ASSIGN_BODY"
docker compose logs --no-color taskmanager | awk '/Circuit UserManager:/'
echo 'DEMO OK: Closed -> Open -> HalfOpen -> Closed; tablero disponible y asignacion conservada.'
