# Dossiê CP3 — Experimentos e diagnóstico

**CP3-I06** · Racass/checkpoint-csharpracass-expensehub#16 · André Nakamatsu Rocha (RM555004)

Este dossiê registra os cinco experimentos obrigatórios (E01–E05) e o diagnóstico de falhas do ExpenseHub, executados
**com as imagens publicadas** no GHCR, por meio de `compose.release.yaml`. Os resultados completos (ids, status,
respostas e logs filtrados por `reportId`) estão em [`experiments-report.md`](experiments-report.md), gerado por
`make experiments` e copiado para cá sem edição.

| Item | Valor |
|---|---|
| `IMAGE_TAG` usado nesta execução | `9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` |
| Imagem da API | `ghcr.io/gh-johnny/fiap-expensehub-api:9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` |
| Imagem do worker | `ghcr.io/gh-johnny/fiap-expensehub-worker:9ce0027786dde57f9eb5b59ca97785aa3f53d9ba` |
| Resultado | **38 de 38 verificações passaram** (`0 failure(s)`) |

As imagens foram baixadas do GHCR com `make release-pull`, sem login, e nada foi construído localmente.

## Como reproduzir

```shell
make env                   # .env com senhas aleatórias (uma vez)
# edite .env: IMAGE_TAG=<sha completo publicado pelo workflow release>
make release-pull          # baixa as duas imagens publicadas
make experiments           # sobe o ambiente, roda E01–E05 + diagnóstico e escreve artifacts/experiments/report.md
cp artifacts/experiments/report.md docs/evidence/experiments-report.md
```

O script (`scripts/run-experiments.sh`) roda num container `docker:cli` ligado à rede do ambiente de release. Por
isso, o host continua precisando só de `git`, `docker` e `make`. Cada execução:

- cria contas novas (Employee A, Employee B, Approver, Finance e Auditor) com senhas aleatórias mantidas só em
  memória;
- usa um sufixo aleatório nos e-mails, para que execuções repetidas não se misturem;
- registra cada verificação como PASS/FAIL. O target falha se alguma verificação falhar.

Nenhum token, senha, cabeçalho `Authorization` ou connection string aparece no relatório. Os logs são filtrados pelo
`reportId`, e os do ExpenseHub não registram segredos por desenho (ver README, *Processamento no worker*).

A versão em execução fica comprovada no topo do relatório e no E05. `GET /version`, o label
`org.opencontainers.image.revision` dos dois containers e a imagem de cada container mostram o mesmo `IMAGE_TAG`.

## E01 — Fluxo normal

**Objetivo:** mostrar o caminho feliz completo, da solicitação ao Excel, com um resultado conferível à mão.

**Procedimento:**

1. O Employee A cria um reembolso em cada estado, com valores conhecidos:

   | Estado | Valor |
   |---|---:|
   | Draft | R$ 10,00 |
   | Submitted | R$ 20,00 |
   | Approved | R$ 30,00 |
   | Rejected | R$ 40,00 |
   | Paid | R$ 50,00 |

   O Approver e o Finance são contas diferentes do dono.
2. Ele pede um relatório `Mine` do período.
3. O envelope é acompanhado até `Completed`.
4. O `.xlsx` é baixado.

**Conta feita à mão:** 5 reembolsos, total de R$ 150,00, 1 por estado com o próprio valor.

**O que aparece no relatório:**

- `POST /api/reports` → 202 com `Pending`.
- O mesmo id chega a `Completed`, com `5/150.00` e os cinco estados iguais à conta acima.
- `GET /excel` → 200, com `Content-Type` de planilha e arquivo ZIP válido. A planilha fica salva em
  `artifacts/experiments/` e pode ser aberta no Excel ou no LibreOffice, onde mostra os cinco estados e o total.
- Nos logs da API: `registered` → `published`. Nos do worker: `received` → `started` → `completed` → `acknowledged`.

**Conclusão:** o resultado calculado pelo worker confere com a conta manual, e o Excel reproduz os mesmos números
persistidos.

## E02 — Worker parado

**Objetivo:** provar que a solicitação aceita fica guardada no broker enquanto não há consumidor e que o **mesmo** id
é processado quando o worker volta.

**Procedimento:**

1. `docker compose -f compose.release.yaml stop worker`.
2. `POST /api/reports`.
3. Leitura da fila `expensehub.reports.requested` pela API de gerenciamento do RabbitMQ. É o mesmo número do painel,
   cujas estatísticas atualizam a cada ~5 s.
4. Consulta do status.
5. `up -d worker` e acompanhamento do **mesmo** id.

**O que aparece no relatório:**

- POST → 202.
- `Ready` sobe em 1 com o worker parado, e o status continua `Pending`.
- Depois de religar o worker, o mesmo id chega a `Completed` e `Ready` volta ao valor anterior.
- Nos logs do worker, filtrados pelo id: `received` → `started` → `completed` → `acknowledged as Completed`.

**Conclusão:** com o worker parado, a API aceita normalmente porque a mensagem foi confirmada pelo broker. O trabalho
espera na fila durável e é concluído sem novo POST.

## E03 — Duplicata após reinício

**Objetivo:** mostrar que reentregar uma solicitação já concluída, mesmo depois de reiniciar o worker, não produz
nenhum efeito novo.

**Procedimento:**

1. Com o relatório do E01 já `Completed`, rodar `docker compose restart worker`.
2. Publicar `{"reportId":"<id do E01>"}` com *delivery mode* 2 na fila, pela API de gerenciamento
   (`/api/exchanges/%2F/amq.default/publish`). É a mesma operação de *Queues → expensehub.reports.requested → Publish
   message* no painel.

**O que aparece no relatório:**

- A mensagem é roteada (`routed: true`).
- O status continua `Completed`.
- `completedAt` e `result.generatedAt` ficam **idênticos** aos de antes.
- Log `is already Completed; duplicate delivery ignored`, seguido de `acknowledged as Duplicate`.

**Conclusão:** a proteção contra duplicata é o estado persistido no banco, e não a memória do processo. Por isso ela
sobrevive ao reinício. A mensagem republicada é o mesmo `reportId`, não um novo POST (ver diagnóstico, item 3).

## E04 — Isolamento

**Objetivo:** reproduzir a matriz de autorização dos relatórios com contas reais.

**Procedimento:** o Employee A é dono do relatório do E01, e o Employee B e o Auditor são outras contas. Cada caso é
uma requisição real.

| Caso | Esperado |
|---|---|
| Anônimo lendo o relatório | 401 |
| Employee pedindo `Global` | 403 |
| Employee B lendo / baixando o relatório de A | 404 / 404 |
| Auditor pedindo `Global` | 202 |
| Auditor lendo / baixando o relatório de um Employee | 404 / 404 |

**Conclusão:** o escopo `Global` depende do perfil, e a leitura de um relatório depende de ser o solicitante. Um
relatório alheio responde 404 (e não 403), para não confirmar que o id existe. Nesta execução, com o CP3-I02 já
integrado, os sete casos da tabela passaram.

## E05 — Persistência e versão

**Objetivo:** provar que banco, chaves de autenticação e versão sobrevivem à recriação de API e worker.

**Procedimento:**

1. Guardar o token do Employee A e o resultado do E01.
2. Rodar `docker compose -f compose.release.yaml up -d --force-recreate api worker`, **sem** `down -v` nem remoção
   de volumes.
3. Consultar o mesmo relatório com o token antigo.
4. Ler `GET /version` e os labels.

**O que aparece no relatório:**

- O token antigo responde 200.
- Status, total e `result.generatedAt` são iguais aos de antes.
- `/version` e os labels `org.opencontainers.image.revision` de API e worker são iguais ao `IMAGE_TAG`.

**Conclusão:** o SQLite e as chaves de Data Protection ficam no volume `expensehub-data`, compartilhado por API e
worker, então nada se perde ao recriar containers. A versão em execução é a versão publicada.

## Diagnóstico

Tudo o que está abaixo aponta para o [relatório da execução](experiments-report.md): E01 e E02 para aceitação e
conclusão, E03 para a duplicata e a seção *Diagnóstico — broker indisponível e janela sem outbox* para as falhas.

### 1. Aceitação não é conclusão

Quando a API responde `202`, ela só está dizendo que **gravou o pedido como `Pending` e que o RabbitMQ confirmou o
recebimento da mensagem**. Ela não calculou nada. Quem calcula é o worker, mais tarde, e por isso o cliente precisa
consultar `GET /api/reports/{id}` até ver `Completed`. No E01, o envelope sai do POST como `Pending` e só depois vira
`Completed`; no E02 isso fica ainda mais claro, porque o `202` volta mesmo sem nenhum worker rodando.

### 2. Worker parado não é o mesmo que broker indisponível

São duas falhas diferentes, com respostas diferentes:

- **Worker parado (E02).** O broker está de pé e confirma a mensagem, então a API responde `202` normalmente. A
  mensagem fica em `Ready` na fila durável (1 mensagem, conferida no RabbitMQ) e o pedido continua `Pending`. Quando
  o worker volta, ele consome **o mesmo id** e termina em `Completed`. Ninguém precisa refazer nada.
- **Broker indisponível.** Parei o RabbitMQ e fiz um POST. A API gravou o pedido, mas não conseguiu a confirmação,
  esperou cerca de 5 segundos e respondeu **`503`** com `report.publish_unconfirmed` e o `reportId`. O log da API
  mostra `registered` e depois `persisted but its publication was not confirmed`. Quando o broker voltou, a API
  voltou a aceitar pedidos sem reiniciar, mas aquele pedido continuou `Pending`, porque nunca existiu mensagem dele.

### 3. Duplicata não é o mesmo que um novo POST

No E03 eu reiniciei o worker e republiquei o **mesmo** `{"reportId": ...}`. O worker leu o estado no banco, viu que já
estava `Completed`, registrou `duplicate delivery ignored` e deu `ack`; `completedAt` e `generatedAt` não mudaram. A
proteção está no estado salvo no banco, e não na memória do worker, por isso ela sobrevive ao reinício. Um novo
`POST /api/reports` seria outra coisa: cria **outro** id e calcula tudo de novo. Então um novo POST não prova
idempotência.

### 4. A janela sem outbox

O sistema grava no banco e publica no broker em dois passos separados, sem outbox transacional. Entre os dois existe
uma janela: se a publicação não é confirmada, o pedido fica `Pending` **sem mensagem** na fila. No experimento, depois
de religar o broker e reiniciar o worker, o pedido continuou `Pending`, porque não havia nada para consumir. Só
saiu desse estado quando eu republiquei o payload com o mesmo `reportId`, e aí ele chegou a `Completed`. Um novo POST
também resolveria, mas geraria outro relatório. Reconciliar ou republicar automaticamente esses pedidos exigiria um
outbox, que está fora do escopo do CP3; por enquanto, o `503` com o `reportId` é o que torna o problema visível e
recuperável.
