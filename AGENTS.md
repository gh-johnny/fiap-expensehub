# AGENTS.md

Instruções para qualquer pessoa ou agente (Claude Code, Copilot, Codex…) que trabalhe neste repositório.
O `CLAUDE.md` importa este arquivo; **este é o documento de regras de referência**.

## Contexto

- **Disciplina:** FIAP — C# Software Development. Professor: Rafael Cassiolato.
- **Produto:** ExpenseHub — API corporativa de reembolsos (CP2) evoluída com relatórios em segundo plano,
  RabbitMQ, worker C# e exportação Excel (CP3).
- **Especificação comum:** issues do repositório original
  [`Racass/checkpoint-csharpracass-expensehub`](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues)
  (CP2: `#1`–`#10`; CP3: `#11`–`#18`, prefixo `CP3`). As issues originais **nunca** são fechadas por nós.
- **Equipe:**

| Integrante | RM | Frente no CP3 |
|---|---|---|
| João Marcelo Furtado Romero | RM555199 | Fundação, CP3-I01, CP3-I03 |
| Matheus Rivera Montovaneli | RM555499 | CP3-I02, CP3-I04 — [plano](docs/plans/matheus-cp3-i02-i04.md) |
| André Nakamatsu Rocha | RM555004 | CP3-I05, CP3-I06 — [plano](docs/plans/andre-cp3-i05-i06.md) |

CP3-I07 (qualidade) é transversal: cada pessoa responde pela qualidade do próprio diff.

## Condições do projeto (não negociáveis)

1. **Sem prazos nem datas.** Não escrever prazo, data de entrega ou cronograma em código, README, planos,
   PRs ou commits. Datas de calendário só aparecem quando são *dados* (ex.: `fromDate` de um relatório).
2. **Sempre acima do esperado.** Toda entrega cumpre o requisito **e** traz pelo menos um extra útil,
   registrado na seção *Extras* do `README.md`. Extra nunca substitui requisito.
3. **100% containerizado.** O host precisa apenas de `git`, `docker` (com Compose) e `make`. Build, testes,
   cobertura, migrations, execução da API, do worker e do RabbitMQ rodam em containers. Nenhuma instrução
   pode exigir SDK .NET, PowerShell ou outra ferramenta instalada no host.
4. **Tudo via Makefile.** Todo comando recorrente tem um target (`make help` lista todos). Comando novo
   entra no `Makefile` e no `README.md` no mesmo PR.
5. **Cobertura unitária ≥ 95 %** de linhas **e** de branches, verificada por `make coverage` e pelo CI.
   Abaixo disso o build falha. Exclusões ficam **somente** no bloco de cobertura de
   `sources/ExpenseHub.UnitTests/ExpenseHub.UnitTests.csproj` e se limitam a: migrations, código gerado,
   composition roots (`Program.cs`) e adaptadores de I/O real (EF Core, RabbitMQ), que são cobertos pelos
   testes de integração. Nunca excluir código para atingir a meta.
6. **Matriz de testes.** Todo requisito, regra de autorização e cenário da rubrica aparece na seção
   *Matriz de testes* do `README.md`, apontando para o(s) teste(s) que o comprovam. O CI executa uma matriz
   (suítes × sistemas operacionais).
7. **Melhores práticas.**
   - Não alterar `Directory.Build.props`, `.editorconfig`, `.github/workflows/build.yml`,
     `scripts/Invoke-CodeQuality.ps1` nem severidades de analisadores. **0 erros e 0 warnings.**
   - Testes unitários sem banco, rede ou RabbitMQ reais (use fakes/doubles). Integração fica em
     `ExpenseHub.IntegrationTests`.
   - Autorização no servidor: solicitante, escopo e estado nunca vêm do cliente.
   - Valores monetários em `decimal`; instantes em UTC via `TimeProvider`.
   - Sem segredos, bancos, binários ou artefatos locais versionados (`.env` real nunca entra no Git).
   - Logs estruturados e correlacionados (`traceId`, `reportId`), sem tokens, senhas ou connection strings.
8. **README único e vivo.** O `README.md` é a única documentação do projeto. Todo PR que muda
   comportamento, contrato, comando ou configuração atualiza o README no mesmo PR. Não criar documentos
   paralelos; as exceções são `docs/plans/` (planos de trabalho por integrante), `docs/evidence/` (dossiê de
   experimentos exigido na entrega) e os documentos do professor herdados do template em `docs/` (especificação,
   não editar).

## Fluxo de trabalho

- Uma branch e um PR por issue: `cp3-iNN-descricao-curta`. Título: `CP3-INN — <resumo>`.
- Descrição do PR cita a issue original, por exemplo `Racass/checkpoint-csharpracass-expensehub#11`,
  **sem** `Closes`, `Fixes` ou `Resolves`.
- Conventional Commits em inglês: `feat(reports): …`, `test(worker): …`, `build(docker): …`, `docs: …`.
- Cada integrante commita o próprio trabalho com a própria identidade Git. Sem trailers `Co-authored-by`.
  Uso de IA é permitido e pode ser descrito no PR; o autor revisa, entende e testa o que entrega.
- Antes de abrir PR: `make verify` (build + testes + cobertura + formatação) precisa passar.

## Mapa do código

```text
sources/
├── ExpenseHub.slnx
├── ExpenseHub.Core/              # domínio, serviços, EF Core (DbContext, migrations, stores)
├── ExpenseHub.Api/               # host HTTP: controllers, ProblemDetails, OpenAPI/Scalar
├── ExpenseHub.Worker/            # host do worker: consumo RabbitMQ com ack manual e processamento de relatórios
├── ExpenseHub.UnitTests/         # MSTest, sem I/O real — gate de cobertura
└── ExpenseHub.IntegrationTests/  # SQLite real e provas relacionais
```

## Comandos essenciais

```shell
make help        # lista todos os targets
make verify      # build + testes + cobertura (gate 95 %) — rodar antes de todo PR
make up          # RabbitMQ + API + worker em containers
make logs        # logs correlacionados
make down        # para o ambiente (volumes preservados)
```
