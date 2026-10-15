#!/usr/bin/env bash
# DEMO 8 - Retry. Requiere la configuracion predeterminada de Retry/CircuitBreaker
# y una replica de TaskManager. Restaura UserManager incluso si una comprobacion falla.
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
  local args=(-sS --max-time 20 -X "$method")
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

# Cuenta lineas de log de TaskManager que coinciden con un patron.
count_logs() {
  local logs
  logs=$(docker compose logs --no-color taskmanager) || fail 'No se pudieron leer los logs.'
  printf '%s\n' "$logs" | awk -v pattern="$1" '$0 ~ pattern { count++ } END { print count+0 }'
}

command -v docker >/dev/null 2>&1 || fail 'Falta Docker.'
docker info --format '{{.ServerVersion}}' >/dev/null || fail 'Abri Docker Desktop antes de ejecutar la demo.'
TASKS=$(docker compose ps --status running -q taskmanager)
COUNT=$(printf '%s\n' "$TASKS" | awk 'NF { count++ } END { print count+0 }')
[ "$COUNT" = 1 ] || fail 'Se requiere una replica: docker compose up -d --build --scale taskmanager=1'
USERS=$(docker compose ps --status running -q usermanager)
[ -n "$USERS" ] || fail 'UserManager debe estar levantado antes de empezar.'
wait_for_gateway

echo '== 1. Crear datos y comprobar una asignacion normal (sin reintentos) =='
ok POST '/auth/register' "$(jq -nc --arg email "retry+$RANDOM$RANDOM@example.com" \
  '{name:"Ana Retry",email:$email,password:"DemoRetry123!"}')"
TOKEN=$(printf '%s' "$BODY" | jq -er '.token | select(type == "string" and length > 0)')
OWNER=$(jwt_sub "$TOKEN")
[ -n "$OWNER" ] && [ "$OWNER" != null ] || fail 'El JWT no contiene el usuario.'
ok POST '/projects' "$(jq -nc --arg id "$OWNER" '{ownerId:$id,name:"Demo Retry"}')"
PROJECT=$(printf '%s' "$BODY" | jq -er '.id')
ok POST '/tasks/create-and-assign' "$(jq -nc --arg project "$PROJECT" --arg user "$OWNER" \
  '{projectId:$project,title:"Informe",userId:$user}')"
assert_json '.assigneeName == "Ana Retry"'
TASK=$(printf '%s' "$BODY" | jq -er '.id')
ASSIGNMENT="/tasks/$TASK/assign"
ASSIGN_BODY=$(jq -nc --arg id "$OWNER" '{userId:$id}')

echo '== 2. Detener UserManager y enviar UNA solicitud =='
RESTORE_USERS=1
docker compose stop usermanager
sleep 0.3
ATTEMPTS_BEFORE=$(count_logs 'Dependency HTTP attempt UserManager')
RETRIES_BEFORE=$(count_logs 'Retry UserManager:')
api POST "$ASSIGNMENT" "$ASSIGN_BODY"
[ "$STATUS" = 503 ] || fail "Se esperaba HTTP 503; se recibio $STATUS."
assert_json '.code == "dependency_unavailable"'
echo "HTTP 503 dependency_unavailable tras ${ELAPSED}s (incluye las esperas del backoff)."
sleep 0.3
ATTEMPTS=$(( $(count_logs 'Dependency HTTP attempt UserManager') - ATTEMPTS_BEFORE ))
RETRIES=$(( $(count_logs 'Retry UserManager:') - RETRIES_BEFORE ))

echo '== 3. Una solicitud = 1 intento inicial + 2 reintentos =='
[ "$ATTEMPTS" = 3 ] || fail "Se esperaban 3 intentos HTTP; hubo $ATTEMPTS."
[ "$RETRIES" = 2 ] || fail "Se esperaban 2 reintentos registrados; hubo $RETRIES."
echo "OK: $ATTEMPTS intentos HTTP y $RETRIES reintentos para una sola solicitud."
docker compose logs --no-color taskmanager | awk '/Retry UserManager:/' | tail -n 2

echo '== 4. Restaurar UserManager: la solicitud vuelve a funcionar =='
docker compose start usermanager
READY=0
for _ in $(seq 1 20); do
  sleep 2
  api GET "/users/$OWNER"
  if [ "$STATUS" = 200 ]; then READY=1; break; fi
done
[ "$READY" = 1 ] || fail 'UserManager no se recupero.'
RESTORE_USERS=0
ATTEMPTS_BEFORE=$(count_logs 'Dependency HTTP attempt UserManager')
ok POST "$ASSIGNMENT" "$ASSIGN_BODY"
assert_json '.assigneeName == "Ana Retry"'
sleep 0.3
ATTEMPTS=$(( $(count_logs 'Dependency HTTP attempt UserManager') - ATTEMPTS_BEFORE ))
echo "Asignacion correcta. Intentos HTTP para esta solicitud: $ATTEMPTS."
echo 'DEMO OK: el Retry reintenta fallos transitorios y se detiene al agotar los intentos.'