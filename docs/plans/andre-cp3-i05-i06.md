# Plano do André — CP3-I05 (publicação e execução) e CP3-I06 (experimentos)

**Integrante:** André Nakamatsu Rocha — RM555004
**Issues:** [CP3-I05](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/15) (15 pts) e
[CP3-I06](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/16) (10 pts)
**Extras obrigatórios da equipe:** varredura de vulnerabilidades + SBOM nas imagens publicadas, e `make experiments`
automatizando os experimentos.

Este plano é autossuficiente: siga as seções na ordem. Antes de começar, leia o [`README.md`](../../README.md)
(seções *Arquitetura*, *Comandos*, *Relatórios em segundo plano*) e o [`AGENTS.md`](../../AGENTS.md). As condições do
projeto valem para você também: sem datas, tudo via `make`, cobertura ≥ 95 %, matriz de testes e README atualizados
no mesmo PR.

> **Regra de ouro:** quem não tiver commits novos no CP3 fica com nota 0. Todo commit seu tem que sair da sua máquina,
> com a sua identidade Git, sobre código que você entendeu e testou. Pode usar IA; não pode usar a conta de outra
> pessoa.

---

## 0. O que já existe e o que é seu

| Já pronto (João) | Seu trabalho |
|---|---|
| Dockerfiles multi-stage da API (`sources/ExpenseHub.Api/Dockerfile`) e do worker (`sources/ExpenseHub.Worker/Dockerfile`) | **Identificar a versão** (commit) nas imagens e na aplicação em execução |
| `compose.yaml` que **constrói** as imagens localmente (desenvolvimento) | `compose.release.yaml`, que **só usa imagens publicadas** |
| CI que testa e constrói as imagens | Workflow que **publica** as duas imagens no GHCR **depois** dos testes |
| RabbitMQ com volume, fila quorum durável, SQLite compartilhado em `/data`, `make env` | Documentar a execução reproduzível e provar a persistência |
| Fluxo verificado manualmente | **Dossiê E01–E05** com evidências sanitizadas e o diagnóstico |

Ordem recomendada: o I05 primeiro (o I06 precisa das imagens publicadas). O E04 depende do CP3-I02 do Matheus estar na
`main`.

## 1. Preparar o ambiente (uma vez)

Requisitos: `git`, Docker com Compose v2 e `make`. **Não** instale o .NET SDK: tudo roda em container.

```shell
# 1. Passe seu login do GitHub ao João e aceite o convite de colaborador de gh-johnny/fiap-expensehub.
# 2. Identidade Git: use o MESMO e-mail da sua conta GitHub (senão o commit não conta como seu).
git config --global user.name  "André Nakamatsu Rocha"
git config --global user.email "<e-mail da sua conta GitHub>"

# 3. Clone e suba tudo
git clone git@github.com:gh-johnny/fiap-expensehub.git
cd fiap-expensehub
make env          # gera .env com senhas aleatórias (nunca commitar)
make verify       # precisa passar ANTES de você mexer
make up           # RabbitMQ + API + worker; Scalar em http://127.0.0.1:5080/docs
```

Se `make up` falhar com *address pools fully subnetted* ou *port is already allocated*, veja *Solução de problemas*
no README.

## 2. CP3-I05 — Publicação e execução reproduzível

**Branch:** `cp3-i05-release-images` · **PR:** `CP3-I05 — Imagens publicadas por commit e Compose reproduzível`

### 2.1 Versão identificada pelo commit (imagem e processo)

1. Nos **dois** Dockerfiles, acrescente `ARG GIT_SHA=unknown` no estágio `runtime`, mais:
   - `ENV APP_VERSION=$GIT_SHA`;
   - labels OCI: `org.opencontainers.image.revision=$GIT_SHA`,
     `org.opencontainers.image.source=https://github.com/gh-johnny/fiap-expensehub` e
     `org.opencontainers.image.title` (`expensehub-api` / `expensehub-worker`).
2. Crie `sources/ExpenseHub.Core/BuildInfo.cs`, uma classe `public static` com XML doc, que lê `APP_VERSION` do
   ambiente (padrão `"unknown"`). Escreva o teste unitário em `sources/ExpenseHub.UnitTests/BuildInfoTests.cs`. O gate
   de 95 % continua valendo.
3. API (`sources/ExpenseHub.Api/Program.cs`): mapeie `GET /version` anônimo, devolvendo `{ "commit": "<sha>" }`, ao
   lado do `/health` existente. No `ApiDocumentation.cs`, essa rota deve receber a tag `System`; o teste
   `Api/OpenApiDocumentTests` tem um `DataRow` de tags que você pode estender.
4. Worker (`sources/ExpenseHub.Worker/Program.cs`): registre no log, ao iniciar, `ExpenseHub worker {Commit} starting`.
5. `compose.yaml` (desenvolvimento): passe `args: { GIT_SHA: local }` no `build` dos dois serviços.

### 2.2 Publicação no GHCR somente depois dos testes

Crie `.github/workflows/release.yml`. **Não** altere o `build.yml` do professor.

- Gatilhos: `push` na `main` e `workflow_dispatch`.
- Job `verify`: `actions/checkout` + `make verify` (o mesmo gate do CI).
- Job `publish`:
  - `needs: verify`, para que os testes rodem **antes** de publicar;
  - `permissions: { contents: read, packages: write, id-token: write, attestations: write }`;
  - `docker/login-action` no `ghcr.io` com `${{ secrets.GITHUB_TOKEN }}`;
  - `docker/build-push-action` para cada imagem, com `build-args: GIT_SHA=${{ github.sha }}` e tags
    `ghcr.io/gh-johnny/fiap-expensehub-api:${{ github.sha }}` e `ghcr.io/gh-johnny/fiap-expensehub-worker:${{ github.sha }}`.
    Uma tag extra `main` é opcional, mas **nunca** a única: a correção usa o SHA.
  - **Extra:** `provenance: true` e `sbom: true` no build-push, mais uma varredura Trivy
    (`aquasecurity/trivy-action`) em cada imagem, falhando em vulnerabilidades `CRITICAL`. Escreva no README o que
    foi encontrado.
- Escreva o resumo do job (`$GITHUB_STEP_SUMMARY`) com as **referências completas** das duas imagens e os digests.
- Depois do primeiro publish, no GitHub: *Packages → fiap-expensehub-api / -worker → Package settings → Change
  visibility → Public* (e *Connect repository*). Assim o professor faz `docker pull` **sem credenciais**. Registre isso
  no README.

### 2.3 Compose que executa as imagens publicadas

1. Crie `compose.release.yaml`, com `name: fiap-expensehub-release`. Ele é igual ao `compose.yaml`, mas:
   - **sem `build:`**;
   - `image: ghcr.io/gh-johnny/fiap-expensehub-api:${IMAGE_TAG?set IMAGE_TAG in .env}` (idem para o worker).

   Use a forma `${VAR?mensagem}` (sem `:`) e valores sensíveis entre aspas, como no `compose.yaml`: o detector de
   segredos do professor rejeita `PASSWORD:?` sem aspas.
2. Em `.env.example`, acrescente `IMAGE_TAG=` com o comentário *SHA completo do commit publicado*. O `make env`
   continua gerando as senhas.
3. No `Makefile`, crie uma seção `## Release (published images)` com:
   - `release-pull`;
   - `release-up` (`docker compose -f compose.release.yaml up -d --wait`);
   - `release-ps`;
   - `release-logs`;
   - `release-stop-worker` / `release-start-worker` / `release-restart-worker`;
   - `release-recreate` (`up -d --force-recreate --wait api worker`, **sem** remover volumes);
   - `release-down`;
   - `release-version`, que mostra o `/version` da API e
     `docker inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}'` dos dois containers.
4. **Não** apague volumes em nenhum target de verificação (`down -v` é proibido como prova).

### 2.4 README (seção nova *Publicação e execução*)

Documente:
- as referências completas das imagens e como obtê-las sem credenciais;
- o fluxo `make env` → editar `IMAGE_TAG` → `make release-pull` → `make release-up` → `make release-version`;
- o banco (SQLite no volume, migrations aplicadas pela API, mesmo arquivo para API e worker);
- o RabbitMQ (volume `rabbitmq-data`, hostname fixo, fila quorum durável);
- os segredos (só no `.env`).

Na *Matriz de testes*, acrescente a linha `BuildInfo`; em *Extras*, Trivy + SBOM; em *Andamento*, marque CP3-I05.

**Critérios de aceite (rubrica I05):** duas imagens publicadas e identificadas, com testes antes da publicação (5);
Compose executando as imagens publicadas e o RabbitMQ, com configuração documentada (5); banco e armazenamento
compatíveis, inicialização reproduzível e segredos fora do repositório (5).

**Commits sugeridos:**

```text
feat(build): stamp the commit into images and expose GET /version
ci(release): publish api and worker images to GHCR after tests
build(compose): run the published images with compose.release.yaml
docs(release): document published images and reproducible execution
```

## 3. CP3-I06 — Experimentos e diagnóstico

**Branch:** `cp3-i06-experiments` · **PR:** `CP3-I06 — Dossiê E01–E05 e diagnóstico`

O dossiê fica em `docs/evidence/DOSSIE-CP3.md`. É uma exceção à regra do README único, porque se trata de um
artefato de entrega. Imagens de apoio ficam em `docs/evidence/img/`. Use **as imagens publicadas**
(`make release-*`) e registre o `IMAGE_TAG` no topo do dossiê.

Para cada experimento, registre:
- objetivo;
- comandos exatos;
- **ids** das solicitações;
- respostas HTTP (status e campos relevantes);
- trechos de log **filtrados pelo id**: `docker compose -f compose.release.yaml logs worker | grep <reportId>`;
- uma conclusão de uma ou duas frases.

**Nunca** cole tokens, senhas, headers `Authorization` ou connection strings.

| Experimento | Procedimento | O que precisa aparecer |
|---|---|---|
| **E01 — Fluxo normal** | Cadastre Employee, Approver e Finance (via `/register` + roles pelo Admin). Crie reembolsos com valores conhecidos e leve-os a estados diferentes. Peça o relatório `Mine`, guarde o id, acompanhe até `Completed`, baixe o Excel e abra no Excel ou LibreOffice. | Envelope `Pending` → `Completed`; `result` conferido contra a conta feita à mão; print da planilha com os cinco estados e o total |
| **E02 — Worker parado** | `make release-stop-worker`; peça um relatório; no painel do RabbitMQ veja a mensagem em **Ready** (as estatísticas atualizam a cada ~5 s); confirme `Pending`; `make release-start-worker`; acompanhe **o mesmo id**. | Ready = 1 com o worker parado; o mesmo id chega a `Completed`; logs `received` → `started` → `completed` → `acknowledged` |
| **E03 — Duplicata** | Com um relatório `Completed`: `make release-restart-worker`. No painel: *Queues → expensehub.reports.requested → Publish message*, com *Delivery mode* 2 e payload `{"reportId":"<id>"}`. | Log `already Completed; duplicate delivery ignored` e `acknowledged as Duplicate`; `completedAt` e `result.generatedAt` idênticos antes e depois |
| **E04 — Isolamento** (depois do CP3-I02) | Duas contas Employee e uma Auditor. | Employee + `Global` → 403; Employee B lendo e baixando o relatório do A → 404; Auditor + `Global` → 202; Auditor lendo e baixando o relatório de um Employee → 404 |
| **E05 — Persistência e versão** | `make release-recreate` (recria **só** API e worker, sem remover volumes); consulte um relatório anterior; `make release-version`. | Mesmo resultado de antes; `/version` e labels com o `IMAGE_TAG`; o token antigo continua válido (chaves persistidas em `/data/keys`) |

### 3.1 Diagnóstico (seção final do dossiê)

Explique, **com as suas palavras** e apontando para as evidências acima:

1. **Aceitação × conclusão:** o 202 significa *gravado e confirmado pelo broker*, não *calculado*.
2. **Worker parado × broker indisponível:**
   - com o worker parado, a API continua aceitando (202) e as mensagens esperam em *Ready*;
   - com o broker parado, a API responde **503** com o `reportId` em cerca de 5 s e a solicitação fica `Pending`.

   Prove o segundo caso: `docker compose -f compose.release.yaml stop rabbitmq` → POST → 503 → `start rabbitmq`.
3. **Duplicata × novo POST:** republicar o mesmo `reportId` não tem efeito; um novo POST cria **outro** id.
4. **Janela sem outbox:** se o processo cair entre gravar e publicar, a solicitação fica `Pending` sem mensagem.
   Reiniciar o worker **não** a recupera; a saída é republicar o payload pelo painel ou fazer um novo POST.

### 3.2 Extra: `make experiments`

Escreva `scripts/run-experiments.sh` (bash, executado pelo `make`, sem dependências além de `curl`, `python3` ou `jq`
e Docker) e o target `make experiments`. O script deve:
- criar as contas com senhas aleatórias em memória;
- rodar E01, E02, E03 e E05 contra o `compose.release.yaml`;
- escrever `artifacts/experiments/report.md` com ids, status e resultados, sem tokens.

O README de *Comandos* passa a citar o target. O dossiê continua sendo seu texto; o script só torna tudo
reproduzível.

**Critérios de aceite (rubrica I06):** E01 com Excel e E02 correlacionando a mesma solicitação (3); E03 após
reinício (2); E04 (2); E05 (2); explicação das falhas e da janela sem outbox (1).

**Commits sugeridos:**

```text
docs(evidence): record E01 normal flow and E02 stopped worker
docs(evidence): record E03 duplicate, E04 isolation and E05 persistence
docs(evidence): explain acceptance, broker outage and the no-outbox window
feat(scripts): automate the experiments with make experiments
```

## 4. Como abrir cada PR

1. `git switch main && git pull`, depois `git switch -c <branch>`.
2. Commits pequenos e coesos, com a sua identidade.
3. `make verify` precisa passar localmente.
4. `git push -u origin <branch>` e abra o PR no **nosso** repositório, contra `main`.
5. Na descrição, cite a issue **sem** palavras de fechamento, por exemplo
   `Implementa Racass/checkpoint-csharpracass-expensehub#15`. Nunca use `Closes`, `Fixes` ou `Resolves`.
6. Use o checklist do [processo do professor](../PROCESSO-GITHUB.md) e espere o CI ficar verde.
7. Peça revisão ao Matheus; o João revisa se o Matheus não puder.

## 5. Armadilhas conhecidas

| Sintoma | Causa | Correção |
|---|---|---|
| Pontuação do professor cai para 9 (FIAP1002) | Senha literal ou `PASSWORD:?` sem aspas em YAML versionado | Só `"${VAR}"` / `"${VAR?mensagem}"` entre aspas |
| `denied` no `docker pull` do GHCR | Pacote ainda privado | Mudar a visibilidade para *Public* (seção 2.2) |
| `manifest unknown` | `IMAGE_TAG` não é o SHA completo publicado | Copiar o SHA do resumo do workflow `release` |
| Ready = 0 no painel com o worker parado | Estatísticas do painel atualizam a cada ~5 s | Esperar e recarregar; confira também `GET /api/reports/{id}` = `Pending` |
| Tokens inválidos depois do E05 | Volumes removidos | Nunca usar `down -v`; as chaves ficam em `/data/keys` |
| `make up` sem rede | Pools de endereço do Docker esgotados | `compose.override.yaml` local, como no README |

## 6. Ponto extra individual (opcional)

Publicação no **seu** LinkedIn, com **suas** palavras: a ferramenta, como ajuda a empresa a controlar gastos, o que
você aprendeu (imagens versionadas, GHCR, mensageria e falhas observáveis, …) e as tecnologias usadas. Envie o link no
Teams. Texto igual ao de outro integrante zera o bônus dos dois.
