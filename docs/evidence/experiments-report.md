# Experimentos CP3 — execução automatizada

Gerado por `make experiments` (`scripts/run-experiments.sh`) contra `compose.release.yaml`.
Sem tokens, senhas ou cabeçalhos `Authorization`: as contas usam senhas aleatórias mantidas em memória.

| Item | Valor |
|---|---|
| Execução | `a438f3fe` |
| `IMAGE_TAG` | `9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` |
| `GET /version` | `9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` |
| Período dos relatórios | `2026-10-28` a `2026-11-04` |
| Imagem `api` | `ghcr.io/gh-johnny/fiap-expensehub-api:9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` |
| Imagem `worker` | `ghcr.io/gh-johnny/fiap-expensehub-worker:9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` |

## E01 — Fluxo normal

Employee `employee-a-a438f3fe` com um reembolso por estado: Draft 10,00 · Submitted 20,00 · Approved 30,00 ·
Rejected 40,00 · Paid 50,00. Relatório `Mine` id `be9e8dd8-a348-4b92-add9-d032080142ac`.

| Verificação | Esperado | Obtido | Resultado |
|---|---|---|---|
| POST /api/reports (Mine) | `202` | `202` | PASS |
| Status logo após o POST | `Pending` | `Pending` | PASS |
| Status final | `Completed` | `Completed` | PASS |
| Total (itens/valor) | `5/150.00` | `5/150.00` | PASS |
| Por estado | `Draft=1/10.00 Submitted=1/20.00 Approved=1/30.00 Rejected=1/40.00 Paid=1/50.00` | `Draft=1/10.00 Submitted=1/20.00 Approved=1/30.00 Rejected=1/40.00 Paid=1/50.00` | PASS |
| GET /excel | `200` | `200` | PASS |
| Content-Type | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | PASS |
| Arquivo .xlsx (assinatura ZIP) | `PK` | `PK` | PASS |

Planilha salva em `artifacts/experiments/E01-report-be9e8dd8-a348-4b92-add9-d032080142ac.xlsx`; envelope em `artifacts/experiments/E01-report.json`.

Logs da API e do worker para `be9e8dd8-a348-4b92-add9-d032080142ac`:

```text
Report be9e8dd8-a348-4b92-add9-d032080142ac registered with scope Mine
Report be9e8dd8-a348-4b92-add9-d032080142ac published
HTTP GET /api/reports/be9e8dd8-a348-4b92-add9-d032080142ac be9e8dd8-a348-4b92-add9-d032080142ac b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIBFC0BM:00000001 200 48.8904
HTTP GET /api/reports/be9e8dd8-a348-4b92-add9-d032080142ac be9e8dd8-a348-4b92-add9-d032080142ac b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIBFC0BN:00000001 200 12.0149
HTTP GET /api/reports/be9e8dd8-a348-4b92-add9-d032080142ac be9e8dd8-a348-4b92-add9-d032080142ac b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIBFC0BP:00000001 200 3.4791
HTTP GET /api/reports/be9e8dd8-a348-4b92-add9-d032080142ac be9e8dd8-a348-4b92-add9-d032080142ac b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIBFC0BQ:00000001 200 23.4309
HTTP GET /api/reports/be9e8dd8-a348-4b92-add9-d032080142ac/excel be9e8dd8-a348-4b92-add9-d032080142ac b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIBFC0BR:00000001 200 1150.3722
```

```text
Report be9e8dd8-a348-4b92-add9-d032080142ac received (redelivered: False)
=> ReportId:be9e8dd8-a348-4b92-add9-d032080142ac
Report be9e8dd8-a348-4b92-add9-d032080142ac started attempt 1
=> ReportId:be9e8dd8-a348-4b92-add9-d032080142ac
Report be9e8dd8-a348-4b92-add9-d032080142ac completed on attempt 1
Report be9e8dd8-a348-4b92-add9-d032080142ac acknowledged as Completed
```

## E02 — Worker parado

Worker parado com `docker compose stop worker`, relatório `8a09d266-8f1b-4ae2-871d-53d13b573a69` solicitado e worker religado.

| Verificação | Esperado | Obtido | Resultado |
|---|---|---|---|
| POST com o worker parado | `202` | `202` | PASS |
| Mensagens Ready com o worker parado | `1` | `1` | PASS |
| Status com o worker parado | `Pending` | `Pending` | PASS |
| Mesmo id depois de religar | `Completed` | `Completed` | PASS |
| Mensagens Ready depois do consumo | `0` | `0` | PASS |

Logs do worker para `8a09d266-8f1b-4ae2-871d-53d13b573a69` (`received` → `started` → `completed` → `acknowledged`):

```text
Report 8a09d266-8f1b-4ae2-871d-53d13b573a69 received (redelivered: False)
=> ReportId:8a09d266-8f1b-4ae2-871d-53d13b573a69
Report 8a09d266-8f1b-4ae2-871d-53d13b573a69 started attempt 1
=> ReportId:8a09d266-8f1b-4ae2-871d-53d13b573a69
Report 8a09d266-8f1b-4ae2-871d-53d13b573a69 completed on attempt 1
Report 8a09d266-8f1b-4ae2-871d-53d13b573a69 acknowledged as Completed
```

## E03 — Duplicata após reinício

Worker reiniciado e `{"reportId":"be9e8dd8-a348-4b92-add9-d032080142ac"}` republicado (delivery mode 2) na fila `expensehub.reports.requested` pela API de
gerenciamento, o mesmo efeito de *Publish message* no painel.

| Verificação | Esperado | Obtido | Resultado |
|---|---|---|---|
| Mensagem roteada | `true` | `true` | PASS |
| Status | `Completed` | `Completed` | PASS |
| completedAt inalterado | `2026-11-04T20:45:52.986161Z` | `2026-11-04T20:45:52.986161Z` | PASS |
| result.generatedAt inalterado | `2026-11-04T20:45:52.986161Z` | `2026-11-04T20:45:52.986161Z` | PASS |
| Logs de duplicata para o id | `1` | `1` | PASS |

```text
Report be9e8dd8-a348-4b92-add9-d032080142ac received (redelivered: False)
=> ReportId:be9e8dd8-a348-4b92-add9-d032080142ac
Report be9e8dd8-a348-4b92-add9-d032080142ac is already Completed; duplicate delivery ignored
Report be9e8dd8-a348-4b92-add9-d032080142ac acknowledged as Duplicate
```

## E04 — Isolamento

Employee A é dono de `be9e8dd8-a348-4b92-add9-d032080142ac`; Employee B e Auditor são outras contas. Relatório `Global` do Auditor: `534091bd-6f06-4e13-8c6c-111861a7b80b`.

| Verificação | Esperado | Obtido | Resultado |
|---|---|---|---|
| Anônimo lendo o relatório | `401` | `401` | PASS |
| Employee + Global | `403` | `403` | PASS |
| Employee B lendo o relatório de A | `404` | `404` | PASS |
| Employee B baixando o Excel de A | `404` | `404` | PASS |
| Auditor + Global | `202` | `202` | PASS |
| Auditor lendo o relatório de A | `404` | `404` | PASS |
| Auditor baixando o Excel de A | `404` | `404` | PASS |

## E05 — Persistência e versão

API e worker recriados com `docker compose up -d --force-recreate api worker`, **sem** remover volumes. O token
do Employee A foi obtido antes da recriação.

| Verificação | Esperado | Obtido | Resultado |
|---|---|---|---|
| Token antigo lendo be9e8dd8-a348-4b92-add9-d032080142ac | `200` | `200` | PASS |
| Status | `Completed` | `Completed` | PASS |
| Total (itens/valor) | `5/150.00` | `5/150.00` | PASS |
| result.generatedAt | `2026-11-04T20:45:52.986161Z` | `2026-11-04T20:45:52.986161Z` | PASS |
| GET /version = IMAGE_TAG | `9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` | `9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` | PASS |
| Label revision (api) | `9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` | `9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` | PASS |
| Label revision (worker) | `9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` | `9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` | PASS |

## Diagnóstico — broker indisponível e janela sem outbox

RabbitMQ parado, POST, RabbitMQ religado, worker reiniciado e, por fim, republicação manual do payload.
Solicitação: `a6657984-d971-4901-ad74-0309b891c438`. Tempo até a resposta: 4 s.

| Verificação | Esperado | Obtido | Resultado |
|---|---|---|---|
| POST com o broker parado | `503` | `503` | PASS |
| ProblemDetails code | `report.publish_unconfirmed` | `report.publish_unconfirmed` | PASS |
| Broker de volta: status | `Pending` | `Pending` | PASS |
| Worker reiniciado: status | `Pending` | `Pending` | PASS |
| Republicação manual roteada | `true` | `true` | PASS |
| Depois da republicação | `Completed` | `Completed` | PASS |

```text
Report a6657984-d971-4901-ad74-0309b891c438 registered with scope Mine
Report a6657984-d971-4901-ad74-0309b891c438 persisted but its publication was not confirmed
HTTP GET /api/reports/a6657984-d971-4901-ad74-0309b891c438 a6657984-d971-4901-ad74-0309b891c438 b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIUF7PV9:00000001 200 4.2052
HTTP GET /api/reports/a6657984-d971-4901-ad74-0309b891c438 a6657984-d971-4901-ad74-0309b891c438 b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIUF7PVB:00000001 200 5.1631
HTTP GET /api/reports/a6657984-d971-4901-ad74-0309b891c438 a6657984-d971-4901-ad74-0309b891c438 b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIUF7PVC:00000001 200 6.1533
HTTP GET /api/reports/a6657984-d971-4901-ad74-0309b891c438 a6657984-d971-4901-ad74-0309b891c438 b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIUF7PVD:00000001 200 3.806
HTTP GET /api/reports/a6657984-d971-4901-ad74-0309b891c438 a6657984-d971-4901-ad74-0309b891c438 b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIUF7PVE:00000001 200 6.5568
HTTP GET /api/reports/a6657984-d971-4901-ad74-0309b891c438 a6657984-d971-4901-ad74-0309b891c438 b0c4cf3e-9aba-4290-ab45-3a7aa9f676d2 0HNPQMIUF7PVF:00000001 200 4.7406
```

```text
Report a6657984-d971-4901-ad74-0309b891c438 received (redelivered: False)
=> ReportId:a6657984-d971-4901-ad74-0309b891c438
Report a6657984-d971-4901-ad74-0309b891c438 started attempt 1
=> ReportId:a6657984-d971-4901-ad74-0309b891c438
Report a6657984-d971-4901-ad74-0309b891c438 completed on attempt 1
Report a6657984-d971-4901-ad74-0309b891c438 acknowledged as Completed
```

---

**Resultado:** todas as verificações passaram.
