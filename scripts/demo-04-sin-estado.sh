#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# DEMO 4 — Servicios sin estado (JWT)
#
# El token se emite una vez y CUALQUIER réplica lo valida localmente con el
# secreto compartido, sin sesión en memoria. Se escala ProjectManager, se
# repite la misma request con el mismo token y todas las réplicas la aceptan.
# ─────────────────────────────────────────────────────────────────────────────
cd "$(dirname "$0")/.." || exit 1
source scripts/_common.sh

echo "== Escalando ProjectManager a 3 réplicas =="
docker compose up -d --scale projectmanager=3
echo "   esperando a que las réplicas estén listas..."
sleep 8

wait_for_gateway
bootstrap_user   # emite UN token
echo "   token emitido una sola vez: ${TOKEN:0:24}..."

echo; echo "== 12 requests con el MISMO token (se esperan todos 200) =="
codes=""
for _ in $(seq 1 12); do
  codes="$codes $(curl -s -o /dev/null -w '%{http_code}' "${AUTH[@]}" "$GW/projects?userId=$OWNER")"
done
echo "   códigos:$codes"

echo; echo "== réplicas de ProjectManager que atendieron (X-Served-By) =="
for _ in $(seq 1 12); do
  curl -s -D - -o /dev/null "${AUTH[@]}" "$GW/projects?userId=$OWNER" \
    | awk 'tolower($1)=="x-served-by:"{print $2}' | tr -d '\r'
done | sort | uniq -c | sort -rn

echo; echo "== sin token → 401 en cualquier réplica =="
NO=$(curl -s -o /dev/null -w '%{http_code}' "$GW/projects?userId=$OWNER")
echo "   HTTP $NO"

if [[ "$codes" == *" 401"* || "$codes" == *"000"* ]] || [ "$NO" != "401" ]; then
  echo "✗ Demo 4 FALLÓ."; exit 1
else
  echo "✓ Demo 4 OK: el mismo token vale en todas las réplicas; sin token, 401."
fi
