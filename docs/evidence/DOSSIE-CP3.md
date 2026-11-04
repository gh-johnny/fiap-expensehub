# Dossiê CP3 — Experimentos e diagnóstico

**CP3-I06** · Racass/checkpoint-csharpracass-expensehub#16 · André Nakamatsu Rocha (RM555004)

Este dossiê registra os cinco experimentos obrigatórios (E01–E05) e o diagnóstico de falhas do ExpenseHub, executados
**com as imagens publicadas** no GHCR, por meio de `compose.release.yaml`. Os resultados completos (ids, status,
respostas e logs filtrados por `reportId`) estão em [`experiments-report.md`](experiments-report.md), gerado por
`make experiments` e copiado para cá sem edição.

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
relatório alheio responde 404 (e não 403), para não confirmar que o id existe. A regra Employee + `Global` → 403 vem
do CP3-I02, e o relatório final só fica todo PASS com o I02 integrado.

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

> Rascunho a ser revisado e reescrito pelo autor com as próprias palavras antes da entrega.

### 1. Aceitação não é conclusão

O `202 Accepted` significa que a solicitação foi **gravada como `Pending` e a mensagem foi confirmada pelo broker**
(publisher confirms). O relatório ainda não foi calculado. Quem calcula é o worker, depois, e o cliente acompanha
pelo `GET /api/reports/{id}` até `Completed` ou `Failed`. No E01 e no E02, o envelope sai do POST como `Pending` e só
depois chega a `Completed`.

### 2. Worker parado não é o mesmo que broker indisponível

- **Worker parado (E02):** o broker está de pé e confirma a mensagem. A API responde 202, a mensagem espera em
  `Ready` e é processada quando o worker volta. Não há perda nem ação manual.
- **Broker indisponível** (seção *Diagnóstico* do relatório):
  - Com o RabbitMQ parado, a API ainda grava a solicitação, mas não recebe confirmação.
  - Depois do tempo máximo de publicação (~5 s), responde **503** com `code: report.publish_unconfirmed` e o
    `reportId` persistido. Nos logs aparecem `registered` e `persisted but its publication was not confirmed`.
  - Quando o broker volta, a API passa a aceitar de novo sem ser reiniciada, mas **aquela** solicitação continua
    `Pending`, porque nunca houve mensagem para ela.

### 3. Duplicata não é o mesmo que novo POST

Republicar o mesmo `{"reportId"}` (E03) não tem efeito: o worker vê o estado terminal e só faz o `ack`. Um novo
`POST /api/reports` é outra coisa: cria **outro** id, com outro `createdAt`, e é calculado de novo com os dados
daquele momento. Por isso, um novo POST não prova idempotência, e a prova da duplicata usa o mesmo id.

### 4. A janela sem outbox

Não existe outbox transacional. A gravação no banco e a publicação no broker são dois passos, e entre eles há uma
janela: se a publicação não for confirmada (broker fora ou processo caindo entre os dois passos), a solicitação fica
`Pending` **sem mensagem** na fila.

O relatório mostra o que acontece nesse caso:

- reiniciar o worker **não** a recupera, e ela continua `Pending`, porque não há o que consumir;
- a saída é republicar o payload `{"reportId":"<id>"}` pelo painel, e então o mesmo id chega a `Completed`;
- outra saída é um novo POST, que gera outro relatório.

Republicação automática (outbox e reconciliação) está fora do escopo do CP3. O 503 com o `reportId` é o que torna a
situação visível e recuperável.
