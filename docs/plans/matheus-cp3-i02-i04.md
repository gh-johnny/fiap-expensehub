# Plano do Matheus — CP3-I02 (autorização) e CP3-I04 (testes)

**Integrante:** Matheus Rivera Montovaneli — RM555499
**Issues:** [CP3-I02](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/12) (15 pts) e
[CP3-I04](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/14) (15 pts)
**Extra obrigatório da equipe:** testes de mutação com Stryker.NET (`make mutation`).

Este plano é autossuficiente: siga as seções na ordem. Antes de começar, leia o [`README.md`](../../README.md)
(seção *Relatórios em segundo plano*) e o [`AGENTS.md`](../../AGENTS.md); as condições do projeto valem para você
também (sem datas, tudo via `make`, cobertura ≥ 95 %, matriz de testes e README atualizados no mesmo PR).

> **Regra de ouro:** quem não tiver commits novos no CP3 fica com nota 0. Todo commit seu tem que sair da sua máquina,
> com a sua identidade Git, sobre código que você entendeu e testou. Pode usar IA; não pode usar a conta de outra
> pessoa.

---

## 0. O que já existe e o que é seu

| Já pronto (João) | Seu trabalho |
|---|---|
| Rotas `POST/GET /api/reports`, `GET /{id}`, `GET /{id}/excel` | Matriz **perfil × escopo** no POST (403) |
| Solicitante vem do token; listagem, detalhe e Excel já filtram pelo criador (404 para alheio) | Provar o isolamento com testes para **Employee, Auditor e Admin** |
| Worker calcula com o escopo e o solicitante **persistidos** | Provar que nada vindo do cliente ou da mensagem muda isso |
| 212 testes unitários + 14 de integração, gate de 95 % | Suíte que mapeia **1:1 os cenários da rubrica** + testes HTTP em memória + mutação |

Hoje, **qualquer usuário autenticado consegue pedir `Global`**. Esse é o buraco que o I02 fecha. Sem ele, a nota tem
teto de 4,0 (*autorização sistematicamente ausente*), então ele é a prioridade.

## 1. Preparar o ambiente (uma vez)

Requisitos: `git`, Docker com Compose v2 e `make`. **Não** instale o .NET SDK: tudo roda em container.

```shell
# 1. Aceite o convite de colaborador do repositório gh-johnny/fiap-expensehub no GitHub.
# 2. Identidade Git: use o MESMO e-mail da sua conta GitHub (senão o commit não conta como seu).
git config --global user.name  "Matheus Rivera Montovaneli"
git config --global user.email "<e-mail da sua conta GitHub>"

# 3. Clone e suba tudo
git clone git@github.com:gh-johnny/fiap-expensehub.git
cd fiap-expensehub
make env          # gera .env com senhas aleatórias (nunca commitar)
make verify       # build + format + cobertura + integração: precisa passar ANTES de você mexer
make up           # RabbitMQ + API + worker; Scalar em http://127.0.0.1:5080/docs
```

Se `make up` falhar com *address pools fully subnetted* ou *port is already allocated*, veja *Solução de problemas*
no README.

## 2. CP3-I02 — Autorização e isolamento

**Branch:** `cp3-i02-report-authorization` · **PR:** `CP3-I02 — Autorização e isolamento de relatórios`

### 2.1 Regra a implementar

| Perfil | `Mine` | `Global` | Listagem / detalhe / Excel |
|---|---|---|---|
| Employee | Sim | Não → **403** | Só as próprias |
| Auditor | Não → **403** (salvo se também Employee) | Sim | Só as próprias (Auditor **não** lê solicitação alheia) |
| Employee + Auditor | Sim | Sim | Só as próprias |
| Admin, Approver ou Finance sem os perfis acima | **403** | **403** | Só as próprias (na prática, nenhuma) |
| Anônimo | 401 | 401 | 401 |

### 2.2 Passo a passo

1. **Crie `sources/ExpenseHub.Core/Reports/ReportAuthorization.cs`**, uma classe `public static` no mesmo estilo de
   `Services/ExpenseAuthorization.cs`:
   - `public static void EnsureCanRequest(CurrentActor actor, ReportScope scope)`;
   - `Mine` exige `RoleNames.Employee`; `Global` exige `RoleNames.Auditor`;
   - caso contrário, `throw new ApiProblemException(403, "report.scope_forbidden", "<mensagem curta, sem detalhes internos>")`;
   - XML doc (`/// <summary>`) em tudo que for `public`, porque o build falha sem isso.
2. **Chame a regra em `ReportService.RequestAsync`** (`sources/ExpenseHub.Core/Reports/ReportService.cs`), **logo depois**
   de `ReportScope scope = ParseScope(request.Scope);` e **antes** de `_store.Add(report);`. Assim, uma negação
   não grava nem publica nada.
3. **Defesa em profundidade no controller:** em `ReportsController.Create`, acrescente
   `[Authorize(Roles = RoleNames.Employee + "," + RoleNames.Auditor)]`. Admin, Approver e Finance sozinhos recebem 403 já
   no pipeline. Mantenha o `[Authorize]` da classe nas rotas GET: qualquer autenticado lista as **próprias**.
4. **Não mude** `EfReportStore.FindOwnedAsync`/`ListOwnedAsync`. Elas já filtram pelo criador no SQL. Seu papel aqui é
   provar isso com testes (passo 6).
5. **Documentação OpenAPI:** confira em `/docs` se o POST lista 403 (já deve listar, por ser rota protegida).
6. **Testes unitários** em `sources/ExpenseHub.UnitTests/Reports/ReportAuthorizationTests.cs`. Use os fakes
   existentes (`TestDoubles/ReportStoreFake`, `ReportPublisherFake`, `ListLogger`, `FixedTimeProvider`) e
   `[DataRow]` para a matriz:
   - todas as combinações de roles da tabela 2.1 × `Mine`/`Global` (permitido → grava e publica uma vez; negado →
     `ApiProblemException` 403 `report.scope_forbidden`, `_store.Reports` vazio e `_publisher.Published` vazio);
   - Auditor que pede o **detalhe** e o **Excel** de uma solicitação de um Employee → 404;
   - Admin que lista → lista vazia;
   - duas contas Employee: uma não vê, não detalha e não baixa a solicitação da outra (404);
   - **o solicitante nunca vem do cliente**: monte um `CreateReportRequest` válido e confirme que o `RequesterId`
     gravado é o do `CurrentActor`, não outro valor;
   - **o escopo chega intacto ao worker**: com `ReportProcessor`, mostre que `Mine` de um Employee + Auditor soma só
     os próprios e que `Global` de um Auditor soma todos. Veja `ReportProcessorTests.PersistedScopeDrivesTheCalculation`
     como modelo.
7. **Arquivo `.http`** (`sources/ExpenseHub.Api/ExpenseHub.Api.http`): acrescente um login de Auditor
   (`EXPENSEHUB_AUDITOR_EMAIL`) e as chamadas *Employee pede Global (403)*, *Auditor pede Global (202)* e
   *Auditor lê relatório do Employee (404)*. Nunca escreva tokens ou senhas no arquivo.
8. **README:**
   - na tabela de cenários de *Relatórios em segundo plano*, inclua a linha *Perfil sem permissão para o escopo → 403
     (`report.scope_forbidden`)*;
   - remova o aviso *"A matriz de perfil × escopo … chega com CP3-I02"*;
   - na *Matriz de testes*, acrescente a linha *Relatório — autorização*, apontando para `ReportAuthorizationTests`;
   - em *Andamento do CP3*, marque CP3-I02 como concluída.
9. **Verifique e prove no ambiente real:**

   ```shell
   make verify
   make up
   ```

   Com o Scalar ou o `.http`, usando **duas contas Employee e uma Auditor** (crie com `/register` e dê as roles como
   Admin), confira: anônimo → 401; Employee + `Global` → 403; Employee + `Mine` → 202; Auditor + `Global` → 202;
   Auditor lendo e baixando o relatório do Employee → 404; Employee B lendo o do Employee A → 404. Anote os ids e os
   status no PR, **sem tokens**.

**Critérios de aceite (rubrica I02):** autenticação e permissão por perfil e escopo (5); listagem, detalhe e Excel
isolados por solicitante, inclusive Auditor (5); solicitante e escopo controlados pelo servidor até o processamento (5).

**Commits sugeridos** (mensagens em inglês, Conventional Commits):

```text
feat(reports): enforce the profile x scope matrix on report requests
test(reports): cover scope permissions and requester isolation
docs(reports): document report authorization and isolation tests
```

## 3. CP3-I04 — Testes próprios significativos

**Branch:** `cp3-i04-report-tests` (crie a partir da branch do I02 depois do merge dele) · **PR:**
`CP3-I04 — Suíte de cenários da rubrica, testes HTTP em memória e mutação`

A rubrica dá pontos por **comportamento verificado**, não por quantidade de testes nem por percentual de cobertura.
Seu objetivo é deixar a correção trivial: cada cenário mínimo do enunciado tem **um teste com o mesmo nome**.

### 3.1 Suíte de cenários da rubrica

Crie `sources/ExpenseHub.UnitTests/Rubric/Cp3ScenarioTests.cs`, com **um** `[TestMethod]` por linha da tabela abaixo
(seção 8 da apostila). Não copie testes existentes: escreva os seus, com asserções sobre **resultado e efeito**
(o que foi gravado, publicado ou devolvido), não só "não lançou exceção".

| Método | Cenário | Asserção mínima |
|---|---|---|
| `KnownDataAndInclusiveLimits` | Dados conhecidos e limites inclusivos | Contagem e soma por estado e no total |
| `NoEligibleExpense` | Nenhum reembolso elegível | Cinco estados e totais zerados |
| `StartAfterEnd` | Data inicial posterior à final | 400 e **nenhuma** publicação |
| `EmployeeRequestsGlobal` | Employee pede Global | 403 sem processamento autorizado |
| `ForeignRequest` | Solicitação de outro usuário | Detalhe e resultado não expostos (404) |
| `SameMessageAgain` | Mesma mensagem de novo | Nenhum novo resultado nem efeito de conclusão |
| `QueryAfterExpenseChanges` | Consulta depois de alterar um reembolso | Resultado persistido, sem recálculo |
| `CompletedDownload` | Download do concluído | Planilha válida, cabeçalhos certos e números iguais ao resultado salvo |
| `PendingFailedOrForeignDownload` | Download pendente, `Failed` ou alheio | 409 para o próprio não concluído; 404 para o alheio, sem arquivo |

### 3.2 Testes HTTP em memória (o pipeline de verdade, sem banco nem broker)

Os testes de serviço não pegam erros de atributo `[Authorize]`, de *model binding* ou de status HTTP. Crie
`sources/ExpenseHub.UnitTests/Api/ReportsHttpTests.cs`. O modelo é `Api/OpenApiDocumentTests.cs`, que já sobe a API
num `TestServer` em memória:

- registre `ReportService` com `ReportStoreFake` e `ReportPublisherFake` (sem SQLite, sem RabbitMQ);
- registre um **esquema de autenticação de teste** (`AuthenticationHandler<AuthenticationSchemeOptions>`) que lê
  usuário e roles de cabeçalhos como `X-Test-User` e `X-Test-Roles` e falha quando eles não existem;
- registre `ApiExceptionHandler`/`AddProblemDetails` como no `Program.cs`, para que os status e o `code` sejam os
  reais.

Cubra:
- anônimo → 401;
- data `2026-13-45` → 400 vindo do *model binding*;
- Employee + `Global` → 403;
- POST válido → 202 com `Location`;
- GET alheio → 404;
- Excel `Pending` → 409;
- Excel `Completed` → 200 com `Content-Type` de planilha e `Content-Disposition` contendo `report-{id}.xlsx`;
- broker sem confirmação → 503 com `reportId` no corpo.

### 3.3 Extra: testes de mutação (Stryker.NET)

Cobertura mostra o que foi executado; mutação mostra se os testes **pegam** um bug. Isso é o extra da sua entrega:

1. Adicione `dotnet-stryker` ao manifesto `.config/dotnet-tools.json`, numa versão estável publicada.
2. Crie o target `mutation` no `Makefile`, no mesmo padrão de `coverage`: roda no container, faz `$(TOOLS)` e executa
   o Stryker contra `ExpenseHub.Core` usando `ExpenseHub.UnitTests`, com relatório HTML em `artifacts/mutation/`.
   Comece mutando só `Reports/**`.
3. Registre no README o *mutation score* obtido e o comando. Se algum mutante sobreviver em regra importante (por
   exemplo `<=` virando `<` no período), escreva o teste que o mata.

### 3.4 Fechamento do I04

- `make verify` passa e a cobertura continua ≥ 95 % (linhas **e** branches).
- No README:
  - na *Matriz de testes*, acrescente a coluna ou linhas da `Cp3ScenarioTests` e dos `ReportsHttpTests`;
  - em *Testes, cobertura e qualidade*, explique o comando reproduzível `make test-unit` (sem dependências
    externas), o `make mutation` e o score;
  - em *Extras*, inclua os testes de mutação;
  - em *Andamento*, marque CP3-I04.
- Não use `Assert.IsTrue(x >= y)` (analisador MSTEST0037): use `Assert.IsGreaterThanOrEqualTo`. Nomes de campos
  privados começam com `_`, inclusive `static readonly` (`make format` acusa).

**Critérios de aceite (rubrica I04):** cálculo, vazio e período (5); negação por perfil e escopo e isolamento (5);
duplicata, resultado persistido e Excel com asserções de comportamento (5).

## 4. Como abrir cada PR

1. `git switch main && git pull`, depois `git switch -c <branch>`.
2. Commits pequenos e coesos, com a sua identidade.
3. `make verify` precisa passar localmente.
4. `git push -u origin <branch>` e abra o PR no **nosso** repositório, contra `main`.
5. Na descrição, cite a issue **sem** palavras de fechamento, por exemplo
   `Implementa Racass/checkpoint-csharpracass-expensehub#12`. Nunca use `Closes`, `Fixes` ou `Resolves`.
6. Use o checklist do [processo do professor](../PROCESSO-GITHUB.md) e espere o CI (`code-quality` e `ci`) ficar
   verde.
7. Peça revisão ao João; o André revisa se o João não puder.

## 5. Armadilhas conhecidas

| Sintoma | Causa | Correção |
|---|---|---|
| `error CS1591` no build | Membro `public` sem XML doc | `/// <summary>` em tudo que é público, inclusive testes |
| `IDE2003` / `IDE0055` no build | Falta linha em branco depois de um bloco `}` ou a formatação está errada | `make format-fix` |
| `CA1873` no build | Argumento de log que aloca com o log desligado | Envolver em `if (_logger.IsEnabled(...))` ou usar `[LoggerMessage]` |
| Cobertura abaixo de 95 % | Código novo sem teste ou branch sem caso negativo | Ver `artifacts/coverage/report/index.html`; **nunca** excluir arquivo para passar |
| Pontuação do professor cai para 9 | Senha literal em YAML/JSON versionado | Só `"${VAR}"` com aspas; segredos ficam no `.env` |

## 6. Ponto extra individual (opcional)

Publicação no **seu** LinkedIn, com **suas** palavras: a ferramenta, como ajuda a empresa a controlar gastos, o que
você aprendeu (autorização por perfil e escopo, testes de mutação, …) e as tecnologias usadas. Envie o link no Teams.
Texto igual ao de outro integrante zera o bônus dos dois.
