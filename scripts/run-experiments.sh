#!/usr/bin/env bash
# CP3-I06 — runs experiments E01–E05 (plus the broker-outage diagnosis) against the PUBLISHED images
# (compose.release.yaml) and writes artifacts/experiments/report.md.
#
# Run it with `make experiments`: the target starts the release stack and executes this script inside a
# docker:cli container attached to the stack network, so the host needs only git, docker and make.
# Accounts get random passwords kept in memory; tokens, passwords and Authorization headers are never written.
set -euo pipefail

COMPOSE=(docker compose -f compose.release.yaml)
API="${EXPERIMENTS_API_URL:-http://api:8080}"
MQ="${EXPERIMENTS_MQ_URL:-http://rabbitmq:15672}"
QUEUE="expensehub.reports.requested"
OUT="artifacts/experiments"
REPORT="$OUT/report.md"
RUN="$(head -c 4 /dev/urandom | od -An -tx1 | tr -d ' \n')"
TODAY="$(date -u +%F)"
FROM="$(date -u -d "@$(($(date +%s) - 7 * 86400))" +%F)"
FAILURES=0

set -a
# shellcheck disable=SC1091
. ./.env
set +a

mkdir -p "$OUT"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# ---------------------------------------------------------------------------------------------------------- helpers

log() { printf '\n▶ %s\n' "$*" >&2; }
md() { printf '%s\n' "$*" >> "$REPORT"; }

# check <description> <expected> <actual>: records a PASS/FAIL row in the current table.
check() {
  local result="PASS"
  if [[ "$2" != "$3" ]]; then result="FAIL"; FAILURES=$((FAILURES + 1)); fi
  md "| $1 | \`$2\` | \`$3\` | $result |"
  printf '  %-4s %s (expected %s, got %s)\n' "$result" "$1" "$2" "$3" >&2
}
table() { md ""; md "| Verificação | Esperado | Obtido | Resultado |"; md "|---|---|---|---|"; }

# http <method> <path> <token|-> [json-body]: sets STATUS and writes the body to $TMP/body.
http() {
  local args=(-sS -o "$TMP/body" -w '%{http_code}' -X "$1" "$API$2")
  [[ "$3" != "-" ]] && args+=(-H "Authorization: Bearer $3")
  [[ $# -ge 4 ]] && args+=(-H 'Content-Type: application/json' -d "$4")
  STATUS="$(curl "${args[@]}")"
}
body() { jq -r "$1" "$TMP/body"; }
# Money with two decimals (jq prints 10.0 for 10.00), so results compare as text.
# shellcheck disable=SC2016 # jq syntax, not shell expansion
MONEY='def money: (. * 100 | round) as $c | "\($c / 100 | floor).\($c % 100 | tostring | if length < 2 then "0" + . else . end)";'

password() { printf '%sAa1!' "$(head -c 18 /dev/urandom | base64 | tr -d '/+=')"; }

login() {
  http POST "/login?useCookies=false" - "$(jq -nc --arg e "$1" --arg p "$2" '{email:$e,password:$p}')"
  [[ "$STATUS" == 200 ]] || { echo "login failed for $1 ($STATUS)" >&2; exit 1; }
  body .accessToken
}

# account <name> <role>: registers name-RUN@experiments.local, grants the role as Admin and prints a fresh token.
account() {
  local email="$1-$RUN@experiments.local" pass id
  pass="$(password)"
  http POST /register - "$(jq -nc --arg e "$email" --arg p "$pass" '{email:$e,password:$p}')"
  http GET /api/admin/users "$ADMIN"
  id="$(jq -r --arg e "$email" '.[] | select(.email == $e) | .id' "$TMP/body")"
  http PUT "/api/admin/users/$id/roles" "$ADMIN" "$(jq -nc --arg r "$2" '{roles:[$r]}')"
  [[ "$STATUS" == 200 || "$STATUS" == 204 ]] || { echo "role $2 not granted ($STATUS)" >&2; exit 1; }
  login "$email" "$pass"
}

expense() { # expense <token> <amount> → id
  http POST /api/expenses "$1" "$(jq -nc --arg d "Experiment $RUN expense" --argjson a "$2" --arg t "$TODAY" \
    '{description:$d,amount:$a,expenseDate:$t}')"
  body .id
}

request_report() { # request_report <token> <scope> → sets STATUS, body has the envelope
  http POST /api/reports "$1" "$(jq -nc --arg f "$FROM" --arg t "$TODAY" --arg s "$2" '{fromDate:$f,toDate:$t,scope:$s}')"
}

wait_status() { # wait_status <token> <id> <status> <seconds> → last observed status
  local deadline=$((SECONDS + $4)) status=""
  while ((SECONDS < deadline)); do
    http GET "/api/reports/$2" "$1"
    status="$(body .status)"
    [[ "$status" == "$3" ]] && break
    sleep 1
  done
  printf '%s' "$status"
}

queue_ready() { # messages Ready in the report queue (management statistics refresh every ~5 s)
  curl -sS -u "$RABBITMQ_USER:$RABBITMQ_PASSWORD" "$MQ/api/queues/%2F/$QUEUE" | jq -r '.messages_ready // 0'
}

wait_ready() { # wait_ready <count> <seconds> → last observed Ready
  local deadline=$((SECONDS + $2)) ready=""
  while ((SECONDS < deadline)); do
    ready="$(queue_ready)"
    [[ "$ready" == "$1" ]] && break
    sleep 1
  done
  printf '%s' "$ready"
}

republish() { # publishes {"reportId":"<id>"} persistently, exactly as the panel's "Publish message"
  curl -sS -u "$RABBITMQ_USER:$RABBITMQ_PASSWORD" -H 'Content-Type: application/json' \
    -X POST "$MQ/api/exchanges/%2F/amq.default/publish" \
    -d "$(jq -nc --arg q "$QUEUE" --arg p "{\"reportId\":\"$1\"}" \
      '{properties:{delivery_mode:2},routing_key:$q,payload:$p,payload_encoding:"string"}')" | jq -r .routed
}

logs_for() { # logs_for <service> <id>: log lines that carry the id, as a fenced block
  md ""
  md "\`\`\`text"
  "${COMPOSE[@]}" logs --no-log-prefix "$1" 2>&1 | grep -F "$2" | sed -E 's/^[[:space:]]+//' >> "$REPORT" || true
  md "\`\`\`"
}

wait_api() { # the API answers /health again (after a broker or container restart)
  local deadline=$((SECONDS + 60))
  until curl -fsS -o /dev/null "$API/health" 2>/dev/null; do
    ((SECONDS < deadline)) || { echo "API did not come back" >&2; exit 1; }
    sleep 1
  done
}

# ------------------------------------------------------------------------------------------------------------ setup

log "Setup (run $RUN)"
ADMIN="$(login "$SEED_ADMIN_EMAIL" "$SEED_ADMIN_PASSWORD")"
http GET /version -
VERSION="$(body .commit)"

{
  echo "# Experimentos CP3 — execução automatizada"
  echo
  echo "Gerado por \`make experiments\` (\`scripts/run-experiments.sh\`) contra \`compose.release.yaml\`."
  echo "Sem tokens, senhas ou cabeçalhos \`Authorization\`: as contas usam senhas aleatórias mantidas em memória."
  echo
  echo "| Item | Valor |"
  echo "|---|---|"
  echo "| Execução | \`$RUN\` |"
  echo "| \`IMAGE_TAG\` | \`${IMAGE_TAG:-}\` |"
  echo "| \`GET /version\` | \`$VERSION\` |"
  echo "| Período dos relatórios | \`$FROM\` a \`$TODAY\` |"
} > "$REPORT"
for service in api worker; do
  # Repository and tag in separate code spans: "…-api:<40 hex>" trips Gitleaks' generic-api-key rule.
  image="$(docker inspect --format '{{.Config.Image}}' "$("${COMPOSE[@]}" ps -q "$service")")"
  md "| Imagem \`$service\` | \`${image%:*}\` · tag \`${image##*:}\` |"
done

EMPLOYEE="$(account employee-a Employee)"
EMPLOYEE_B="$(account employee-b Employee)"
APPROVER="$(account approver Approver)"
FINANCE="$(account finance Finance)"
AUDITOR="$(account auditor Auditor)"

# ------------------------------------------------------------------------------------------------------------- E01

log "E01 — normal flow"
# One reimbursement per state with known amounts: expected 5 items, R$ 150.00, 1 per state.
expense "$EMPLOYEE" 10.00 > /dev/null # stays Draft
SUBMITTED="$(expense "$EMPLOYEE" 20.00)"; http POST "/api/expenses/$SUBMITTED/submit" "$EMPLOYEE"
APPROVED="$(expense "$EMPLOYEE" 30.00)"; http POST "/api/expenses/$APPROVED/submit" "$EMPLOYEE"
http POST "/api/expenses/$APPROVED/approve" "$APPROVER"
REJECTED="$(expense "$EMPLOYEE" 40.00)"; http POST "/api/expenses/$REJECTED/submit" "$EMPLOYEE"
http POST "/api/expenses/$REJECTED/reject" "$APPROVER" '{"reason":"Receipt is missing"}'
PAID="$(expense "$EMPLOYEE" 50.00)"; http POST "/api/expenses/$PAID/submit" "$EMPLOYEE"
http POST "/api/expenses/$PAID/approve" "$APPROVER"; http POST "/api/expenses/$PAID/pay" "$FINANCE"

request_report "$EMPLOYEE" Mine
E01_STATUS="$STATUS"; E01="$(body .id)"; E01_INITIAL="$(body .status)"
E01_FINAL="$(wait_status "$EMPLOYEE" "$E01" Completed 60)"
cp "$TMP/body" "$OUT/E01-report.json"
BY_STATUS="$(jq -r "$MONEY"'[.result.byStatus[] | "\(.status)=\(.count)/\(.amount | money)"] | join(" ")' "$TMP/body")"
TOTAL="$(jq -r "$MONEY"'"\(.result.totalCount)/\(.result.totalAmount | money)"' "$TMP/body")"
E01_COMPLETED_AT="$(body .completedAt)"; E01_GENERATED_AT="$(body .result.generatedAt)"
curl -sS -o "$OUT/E01-report-$E01.xlsx" -w '%{http_code} %{content_type}\n' -H "Authorization: Bearer $EMPLOYEE" \
  "$API/api/reports/$E01/excel" > "$TMP/excel"
read -r EXCEL_STATUS EXCEL_TYPE < "$TMP/excel"
EXCEL_MAGIC="$(head -c 2 "$OUT/E01-report-$E01.xlsx")"

md ""; md "## E01 — Fluxo normal"; md ""
md "Employee \`employee-a-$RUN\` com um reembolso por estado: Draft 10,00 · Submitted 20,00 · Approved 30,00 ·"
md "Rejected 40,00 · Paid 50,00. Relatório \`Mine\` id \`$E01\`."
table
check "POST /api/reports (Mine)" 202 "$E01_STATUS"
check "Status logo após o POST" Pending "$E01_INITIAL"
check "Status final" Completed "$E01_FINAL"
check "Total (itens/valor)" "5/150.00" "$TOTAL"
check "Por estado" "Draft=1/10.00 Submitted=1/20.00 Approved=1/30.00 Rejected=1/40.00 Paid=1/50.00" "$BY_STATUS"
check "GET /excel" 200 "$EXCEL_STATUS"
check "Content-Type" "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" "$EXCEL_TYPE"
check "Arquivo .xlsx (assinatura ZIP)" PK "$EXCEL_MAGIC"
md ""; md "Planilha salva em \`$OUT/E01-report-$E01.xlsx\`; envelope em \`$OUT/E01-report.json\`."
md ""; md "Logs da API e do worker para \`$E01\`:"
logs_for api "$E01"
logs_for worker "$E01"

# ------------------------------------------------------------------------------------------------------------- E02

log "E02 — stopped worker"
"${COMPOSE[@]}" stop worker >&2
READY_BEFORE="$(queue_ready)"
request_report "$EMPLOYEE" Mine
E02_STATUS="$STATUS"; E02="$(body .id)"
READY_STOPPED="$(wait_ready $((READY_BEFORE + 1)) 20)"
http GET "/api/reports/$E02" "$EMPLOYEE"; E02_WHILE_STOPPED="$(body .status)"
"${COMPOSE[@]}" up -d --wait worker >&2
E02_FINAL="$(wait_status "$EMPLOYEE" "$E02" Completed 60)"
READY_AFTER="$(wait_ready "$READY_BEFORE" 20)"

md ""; md "## E02 — Worker parado"; md ""
md "Worker parado com \`docker compose stop worker\`, relatório \`$E02\` solicitado e worker religado."
table
check "POST com o worker parado" 202 "$E02_STATUS"
check "Mensagens Ready com o worker parado" "$((READY_BEFORE + 1))" "$READY_STOPPED"
check "Status com o worker parado" Pending "$E02_WHILE_STOPPED"
check "Mesmo id depois de religar" Completed "$E02_FINAL"
check "Mensagens Ready depois do consumo" "$READY_BEFORE" "$READY_AFTER"
md ""; md "Logs do worker para \`$E02\` (\`received\` → \`started\` → \`completed\` → \`acknowledged\`):"
logs_for worker "$E02"

# ------------------------------------------------------------------------------------------------------------- E03

log "E03 — duplicate after restart"
"${COMPOSE[@]}" restart worker >&2
sleep 5
ROUTED="$(republish "$E01")"
sleep 5
http GET "/api/reports/$E01" "$EMPLOYEE"
md ""; md "## E03 — Duplicata após reinício"; md ""
md "Worker reiniciado e \`{\"reportId\":\"$E01\"}\` republicado (delivery mode 2) na fila \`$QUEUE\` pela API de"
md "gerenciamento, o mesmo efeito de *Publish message* no painel."
table
check "Mensagem roteada" true "$ROUTED"
check "Status" Completed "$(body .status)"
check "completedAt inalterado" "$E01_COMPLETED_AT" "$(body .completedAt)"
check "result.generatedAt inalterado" "$E01_GENERATED_AT" "$(body .result.generatedAt)"
DUPLICATE_LOGS="$("${COMPOSE[@]}" logs --no-log-prefix worker 2>&1 | grep -F "$E01" | grep -c 'duplicate delivery ignored' || true)"
check "Logs de duplicata para o id" "1" "$DUPLICATE_LOGS"
logs_for worker "$E01"

# ------------------------------------------------------------------------------------------------------------- E04

log "E04 — isolation"
request_report "$EMPLOYEE" Global; E04_EMPLOYEE_GLOBAL="$STATUS"
http GET "/api/reports/$E01" "$EMPLOYEE_B"; E04_B_READ="$STATUS"
http GET "/api/reports/$E01/excel" "$EMPLOYEE_B"; E04_B_EXCEL="$STATUS"
request_report "$AUDITOR" Global; E04_AUDITOR_GLOBAL="$STATUS"; E04_AUDITOR_ID="$(body .id)"
http GET "/api/reports/$E01" "$AUDITOR"; E04_AUDITOR_READ="$STATUS"
http GET "/api/reports/$E01/excel" "$AUDITOR"; E04_AUDITOR_EXCEL="$STATUS"
http GET "/api/reports/$E01" -; E04_ANONYMOUS="$STATUS"

md ""; md "## E04 — Isolamento"; md ""
md "Employee A é dono de \`$E01\`; Employee B e Auditor são outras contas. Relatório \`Global\` do Auditor: \`$E04_AUDITOR_ID\`."
table
check "Anônimo lendo o relatório" 401 "$E04_ANONYMOUS"
check "Employee + Global" 403 "$E04_EMPLOYEE_GLOBAL"
check "Employee B lendo o relatório de A" 404 "$E04_B_READ"
check "Employee B baixando o Excel de A" 404 "$E04_B_EXCEL"
check "Auditor + Global" 202 "$E04_AUDITOR_GLOBAL"
check "Auditor lendo o relatório de A" 404 "$E04_AUDITOR_READ"
check "Auditor baixando o Excel de A" 404 "$E04_AUDITOR_EXCEL"

# ------------------------------------------------------------------------------------------------------------- E05

log "E05 — persistence and version"
"${COMPOSE[@]}" up -d --force-recreate --wait api worker >&2
wait_api
http GET "/api/reports/$E01" "$EMPLOYEE"
E05_STATUS="$STATUS"
http GET /version -
E05_VERSION="$(body .commit)"
md ""; md "## E05 — Persistência e versão"; md ""
md "API e worker recriados com \`docker compose up -d --force-recreate api worker\`, **sem** remover volumes. O token"
md "do Employee A foi obtido antes da recriação."
table
http GET "/api/reports/$E01" "$EMPLOYEE"
check "Token antigo lendo $E01" 200 "$E05_STATUS"
check "Status" Completed "$(body .status)"
check "Total (itens/valor)" "$TOTAL" "$(jq -r "$MONEY"'"\(.result.totalCount)/\(.result.totalAmount | money)"' "$TMP/body")"
check "result.generatedAt" "$E01_GENERATED_AT" "$(body .result.generatedAt)"
check "GET /version = IMAGE_TAG" "${IMAGE_TAG:-}" "$E05_VERSION"
for service in api worker; do
  check "Label revision ($service)" "${IMAGE_TAG:-}" "$(docker inspect --format \
    '{{ index .Config.Labels "org.opencontainers.image.revision" }}' "$("${COMPOSE[@]}" ps -q "$service")")"
done

# ------------------------------------------------------------------------------------------------- broker outage

log "Diagnosis — broker unavailable and the no-outbox window"
"${COMPOSE[@]}" stop rabbitmq >&2
START=$SECONDS
request_report "$EMPLOYEE" Mine
D_STATUS="$STATUS"; D_ELAPSED=$((SECONDS - START))
D_ID="$(body .reportId // .extensions.reportId // empty)"
D_CODE="$(body .code)"
"${COMPOSE[@]}" up -d --wait rabbitmq >&2
wait_api
sleep 10
http GET "/api/reports/$D_ID" "$EMPLOYEE"; D_AFTER_BROKER="$(body .status)"
"${COMPOSE[@]}" restart worker >&2
sleep 10
http GET "/api/reports/$D_ID" "$EMPLOYEE"; D_AFTER_RESTART="$(body .status)"
D_ROUTED="$(republish "$D_ID")"
D_FINAL="$(wait_status "$EMPLOYEE" "$D_ID" Completed 60)"

md ""; md "## Diagnóstico — broker indisponível e janela sem outbox"; md ""
md "RabbitMQ parado, POST, RabbitMQ religado, worker reiniciado e, por fim, republicação manual do payload."
md "Solicitação: \`$D_ID\`. Tempo até a resposta: ${D_ELAPSED} s."
table
check "POST com o broker parado" 503 "$D_STATUS"
check "ProblemDetails code" report.publish_unconfirmed "$D_CODE"
check "Broker de volta: status" Pending "$D_AFTER_BROKER"
check "Worker reiniciado: status" Pending "$D_AFTER_RESTART"
check "Republicação manual roteada" true "$D_ROUTED"
check "Depois da republicação" Completed "$D_FINAL"
logs_for api "$D_ID"
logs_for worker "$D_ID"

# ---------------------------------------------------------------------------------------------------------- summary

md ""
md "---"
md ""
if ((FAILURES == 0)); then md "**Resultado:** todas as verificações passaram."; else md "**Resultado:** $FAILURES verificação(ões) falharam."; fi
[[ -n "${HOST_UID:-}" ]] && chown -R "$HOST_UID" "$OUT"
log "Report written to $REPORT ($FAILURES failure(s))"
exit $((FAILURES > 0))
