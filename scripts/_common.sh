#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Utilidades compartidas por los scripts de demo.
#
#   GW  → URL pública del gateway (por defecto http://localhost:8080).
#         Si el 8080 está ocupado, correr: export GW=http://localhost:8090
#         y levantar con: GATEWAY_PORT=8090 docker compose up --build
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

GW="${GW:-http://localhost:8080}"

# Verifica dependencias del host.
for _bin in curl jq; do
  command -v "$_bin" >/dev/null 2>&1 || { echo "ERROR: falta '$_bin' en el sistema."; exit 1; }
done

# Espera hasta que el gateway responda (o falla tras ~60s).
wait_for_gateway() {
  for _ in $(seq 1 60); do
    [ "$(curl -s -o /dev/null -w '%{http_code}' "$GW/health" 2>/dev/null || echo 000)" = "200" ] && return 0
    sleep 1
  done
  echo "ERROR: el gateway no responde en $GW (¿corriste 'docker compose up'?)."; exit 1
}

# Decodifica el claim 'sub' (userId) desde el JWT, sin depender del orden de /users.
jwt_sub() {
  local payload; payload=$(printf '%s' "$1" | cut -d. -f2 | tr '_-' '/+')
  case $(( ${#payload} % 4 )) in 2) payload="$payload==";; 3) payload="$payload=";; esac
  printf '%s' "$payload" | base64 -d 2>/dev/null | jq -r '.sub'
}

# Registra un usuario nuevo y deja TOKEN y OWNER (su id) en el entorno.
bootstrap_user() {
  local email="euge+$RANDOM$RANDOM@ucu.edu.uy" reg
  reg=$(curl -s -X POST "$GW/auth/register" -H 'Content-Type: application/json' \
    -d "{\"name\":\"Euge\",\"email\":\"$email\",\"password\":\"secret123\"}")
  TOKEN=$(printf '%s' "$reg" | jq -r '.token // empty')
  [ -n "$TOKEN" ] || { echo "ERROR: no se pudo registrar el usuario: $reg"; exit 1; }
  OWNER=$(jwt_sub "$TOKEN")
  AUTH=(-H "Authorization: Bearer $TOKEN")
}

# Crea un proyecto y devuelve su id por stdout.
create_project() {
  curl -s -X POST "${AUTH[@]}" -H 'Content-Type: application/json' \
    -d "{\"ownerId\":\"$OWNER\",\"name\":\"${1:-Proyecto demo}\"}" "$GW/projects" | jq -r '.id'
}
