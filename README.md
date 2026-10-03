---
title: ExpenseHub — API de reembolsos
status: internal
created: 2026-10-03
last-updated: 2026-10-03
last-reviewed: 2026-10-03
---

# ExpenseHub — API de reembolsos

Checkpoint de C# da FIAP: API ASP.NET Core 10 com persistência SQLite/EF Core, Identity bearer e autorização por perfil, proprietário e estado. As 13 rotas obrigatórias, histórico e pagamento estão implementados. Documentação interativa, demo automatizada e integração final das PRs estão em andamento.

| Integrante | RM |
|---|---|
| João Marcelo Furtado Romero | RM555199 |
| Matheus Rivera Montovaneli | RM555499 |
| André Nakamatsu Rocha | RM555004 |

Prazo do enunciado: **13/10/2026**. Repositório público criado por template: [checkpoint-expensehub-fiap](https://github.com/gh-johnny/checkpoint-expensehub-fiap).

## Preparação

Instalar um SDK .NET 10 completo, incluindo o runtime ASP.NET Core 10. A validação local usa SDK **10.0.401** e runtime **10.0.12**. Conferir `dotnet --info` e `dotnet --list-runtimes`; uma instalação parcial não basta. PowerShell 7 e Gitleaks 8.30.1 são necessários para reproduzir o corretor.

```shell
dotnet tool restore
dotnet restore ./sources/ExpenseHub.slnx
dotnet build ./sources/ExpenseHub.slnx --no-restore
```

## Banco SQLite

Provider: `Microsoft.EntityFrameworkCore.Sqlite` **10.0.12**. Identity EF e Design usam a mesma versão. A ferramenta `dotnet-ef` é local ao repositório, no manifesto `.config/dotnet-tools.json`.

A conexão padrão é `Data Source=expensehub.db`. Para outro arquivo, configurar `ConnectionStrings__ExpenseHub` no ambiente. Não versionar credenciais, banco, WAL ou SHM. Não é necessário um servidor de banco para compilar.

```shell
dotnet ef database update --project ./sources/ExpenseHub.Api --startup-project ./sources/ExpenseHub.Api
dotnet run --project ./sources/ExpenseHub.Api --no-launch-profile
```

Antes do primeiro início, definir `Seed__AdminEmail` e `Seed__AdminPassword` em configuração externa. A senha precisa atender às regras do Identity. O seed cria somente o Admin inicial, com identidade estável; reinícios preservam sua conta e senha existente. As ferramentas de migration não exigem essas credenciais.

Usar a URL exibida pelo host para consultar `GET /health`. Para fixar uma URL local, configurar `ASPNETCORE_URLS` no ambiente. O banco é criado/atualizado pela migration; iniciar o processo sozinho não substitui esse comando.

A migration contém as tabelas de Identity, Expense, ExpenseCategory, ExpenseHistory e PaymentRecord. A categoria inicial `General` é um dado de domínio; não cria usuários. Revisão concorrente, pagamento único, histórico por revisão e FKs são definidos no mapeamento relacional.

## Autenticação

`POST /register` recebe email/password e cria conta sem roles. Um campo adicional de role não concede permissão. `POST /login?useCookies=false` recebe as credenciais e devolve accessToken/refreshToken nativos do Identity. Nas requisições protegidas, usar o accessToken no header Authorization como bearer; não tratá-lo como JWT.

As cinco roles são Admin, Employee, Approver, Finance e Auditor. O seed não cria usuários desses perfis. `GET /api/admin/users` lista contas e roles para Admin. `PUT /api/admin/users/{id}/roles` substitui o conjunto de roles em uma transação e retorna 204. Roles desconhecidas retornam 400; remover o próprio Admin retorna 403. Lista vazia é permitida para outro usuário. Após alterar roles, o usuário precisa fazer novo login. Não inserir token ou senha em exemplos versionados.

## Despesas e histórico

Criar e editar recebem somente `description`, `amount` e `expenseDate` (ISO `yyyy-MM-dd`). Descrição e justificativa são normalizadas com Trim e aceitam de 10 a 500 caracteres. Valor usa decimal, de 0,01 a 2.147.483.647; data não pode ser futura no calendário UTC do servidor. Owner, IDs, estado, atores e horários são definidos pela API.

| Operação | Perfil e condição |
|---|---|
| POST /api/expenses | Employee; cria Draft e retorna 201 com Location. |
| PUT /api/expenses/{id} | Employee proprietário; somente Draft. |
| POST /api/expenses/{id}/submit | Employee proprietário; Draft → Submitted. |
| POST /api/expenses/{id}/approve | Approver não proprietário; Submitted → Approved. |
| POST /api/expenses/{id}/reject | Approver não proprietário; Submitted → Rejected; body com reason. |
| POST /api/expenses/{id}/pay | Finance não proprietário; Approved → Paid. |
| GET /api/expenses, GET /api/expenses/{id}, GET /api/expenses/{id}/history | União dos escopos de leitura descritos abaixo. |

Employee lê todas as próprias despesas. Approver lê Submitted. Finance lê Approved/Paid. Auditor lê todas. Admin isolado administra contas, sem permissão implícita de despesas. Roles acumulam permissões, preservando a proibição de autoaprovação, autorreprovação e autopagamento. Auditor + Employee pode executar as ações concedidas por Employee.

Histórico usa revisão crescente e registra criação, edição efetiva e transições, com ator e UTC. Edição idêntica retorna 200 sem revisão/evento novos. Paid e Rejected são finais. Repetir transição retorna 409, mesmo quando o Approver perdeu a leitura do recurso após decidir. Alteração, evento e pagamento são persistidos no mesmo SaveChanges. Índices únicos protegem histórico por revisão e pagamento por despesa.

Erros usam ProblemDetails com code e traceId: 400 entrada inválida, 401 credencial ausente/inválida, 403 sem permissão, 404 ausente/invisível na leitura, 409 estado incompatível ou concorrência. Falhas inesperadas de provider continuam 500; somente conflitos conhecidos são traduzidos para 409.

## Testes e qualidade

```shell
dotnet test ./sources/ExpenseHub.slnx
pwsh -NoProfile -File ./tests/Invoke-CodeQuality.Unit.Tests.ps1
pwsh -NoProfile -File ./tests/Invoke-CodeQuality.E2E.ps1
pwsh -NoProfile -File ./scripts/Invoke-CodeQuality.ps1 -Ci
```

O workflow oficial permanece intacto. O score está no artefato `code-quality-report`; verificar `score.final`, findings e commit analisado. Os **111 testes unitários** verificam as regras com store substituto e relógio fixo, sem EF, banco ou rede. O projeto separado **ExpenseHub.IntegrationTests** contém oito provas SQLite reais: migrations/seed, round-trip e escopo SQL, constraints, concorrência entre contextos e rollback de roles, histórico e pagamento. Cada fixture usa um arquivo exclusivo com migrations; não usa EF InMemory. Duas inversões temporárias de regras foram detectadas pelos unitários e restauradas; detalhes no registro de execução.

## Documentação e participação

- [Execução e validações realizadas](docs/EXECUCAO.md)
- [Plano técnico e critérios de conclusão](docs/PLANO.md)
- [Divisão de trabalho, pacotes e autoria](docs/DIVISAO-TRABALHO.md)
- [Enunciado](docs/ENUNCIADO.md)
- [Requisitos e 13 endpoints](docs/REQUISITOS.md)
- [Matriz de autorização](docs/MATRIZ-AUTORIZACAO.md)
- [Rubrica](docs/RUBRICA.md)
- [Processo GitHub](docs/PROCESSO-GITHUB.md)
- [Uso de IA](docs/USO-DE-IA.md)
- [Regras de qualidade](docs/code-quality-rules.md)

Cada integrante precisa de mais de um commit próprio e contribuição real, preservados na main. Commits desta sessão usam a identidade autenticada de João; as contribuições de Matheus e André continuam previstas para participação individual, sem simulação de autoria e sem trailers de coautoria de IA.

SHA base da cópia do grupo, antes das contribuições: `8f4dac931306f061ba2a3c4aeac1ceac5d520b9e`. Esse é o ponto inicial para auditar participação; o histórico do template original não foi herdado.
