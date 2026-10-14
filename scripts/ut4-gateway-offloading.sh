#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# UT4 — Patrón GATEWAY OFFLOADING (seguridad)
#
# Demuestra que responsabilidades transversales se resuelven UNA sola vez en el
# gateway, y no en cada servicio:
#
#   A) Rate limiting: al superar el límite, el gateway responde 429 ANTES de
#      reenviar. Prueba empírica: las respuestas 429 NO llevan el header
#      "X-Served-By" (que solo agregan los servicios) → nunca llegaron a un
#      servicio: el gateway los frenó y protegió al backend.
#
#   B) Correlation-Id: el gateway lo genera si no viene, y respeta el que manda
#      el cliente. Los servicios no tienen que crearlo.
#
# El límite del gateway es PermitLimit por WindowSeconds (default 50 / 10s). El
# burst se dispara EN PARALELO y supera ese límite, para que el efecto se vea en
# una sola ventana sin depender de la velocidad de la máquina.
# ─────────────────────────────────────────────────────────────────────────────
cd "$(dirname "$0")/.." || exit 1
source scripts/_common.sh

WINDOW="${RATE_WINDOW:-10}"   # debe coincidir con RateLimit:WindowSeconds del gateway
BURST="${BURST:-100}"         # debe superar RateLimit:PermitLimit (default 50)

wait_for_gateway
bootstrap_user
PROJID=$(create_project "Offloading")

status_of() { printf '%s\n' "$1" | awk 'toupper($1) ~ /^HTTP/ {print $2; exit}'; }
has_served_by() { printf '%s\n' "$1" | grep -iq '^x-served-by:'; }
header_value() { printf '%s\n' "$1" | awk -v k="$2" 'tolower($1)==tolower(k)":"{print $2; exit}' | tr -d '\r'; }

echo "============================================================"
echo " A) RATE LIMITING centralizado en el gateway"
echo "============================================================"
echo "Esperando a que la ventana de rate limit se reinicie ($WINDOW s)..."
sleep "$((WINDOW + 1))"

echo "Disparando $BURST requests EN PARALELO a GET /tasks..."
tmp=$(mktemp -d)
for i in $(seq 1 "$BURST"); do
  (
    resp=$(curl -s -D - -o /dev/null "${AUTH[@]}" "$GW/tasks?projectId=$PROJID")
    code=$(status_of "$resp")
    if has_served_by "$resp"; then served="con-X-Served-By"; else served="sin-X-Served-By"; fi
    echo "$code $served" > "$tmp/$i"
  ) &
done
wait

tally_file=$(mktemp)
cat "$tmp"/* > "$tally_file"
rm -rf "$tmp"

echo
echo "Resultado (código  /  ¿lo atendió un servicio?):"
sort "$tally_file" | uniq -c

n200=$(grep -c '^200 ' "$tally_file" || true)
n429=$(grep -c '^429 ' "$tally_file" || true)
bad=$(grep -c '^429 con-X-Served-By' "$tally_file" || true)
rm -f "$tally_file"

echo
echo "  → $n200 pasaron (200, atendidas por un servicio)"
echo "  → $n429 frenadas por el gateway (429)"
echo "  → $bad respuestas 429 llegaron a un servicio (debe ser 0)"

ok_rl=1
if [ "$n200" -gt 0 ] && [ "$n429" -gt 0 ] && [ "$bad" -eq 0 ]; then
  echo "  ✓ El gateway absorbió el exceso; el backend solo vio las $n200 permitidas."
else
  echo "  ✗ No se cumplió lo esperado (se esperaban 200s y 429s, y ningún 429 con X-Served-By)."
  ok_rl=0
fi

echo
echo "============================================================"
echo " B) CORRELATION-ID centralizado en el gateway"
echo "============================================================"
echo "Esperando a que la ventana de rate limit se reinicie ($WINDOW s)..."
sleep "$((WINDOW + 1))"

resp=$(curl -s -D - -o /dev/null "${AUTH[@]}" "$GW/tasks?projectId=$PROJID")
cid_gen=$(header_value "$resp" "X-Correlation-Id")
echo "Sin enviar nada  → el gateway asignó: ${cid_gen:-(ninguno)}"

resp=$(curl -s -D - -o /dev/null -H 'X-Correlation-Id: demo-abc-123' "${AUTH[@]}" "$GW/tasks?projectId=$PROJID")
cid_echo=$(header_value "$resp" "X-Correlation-Id")
echo "Enviando demo-abc-123 → el gateway respetó: ${cid_echo:-(ninguno)}"

ok_cid=1
if [ -n "$cid_gen" ] && [ "$cid_echo" = "demo-abc-123" ]; then
  echo "  ✓ El gateway genera el correlation-id y respeta el del cliente."
else
  echo "  ✗ No se obtuvo el correlation-id esperado."
  ok_cid=0
fi

echo
if [ "$ok_rl" -eq 1 ] && [ "$ok_cid" -eq 1 ]; then
  echo "✓ GATEWAY OFFLOADING OK: rate limiting y correlation-id resueltos en el gateway."
else
  echo "✗ La prueba de Gateway Offloading no pasó del todo."
  exit 1
fi
