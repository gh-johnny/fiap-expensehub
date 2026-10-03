---
title: ExpenseHub — API de reembolsos
status: internal
created: 2026-10-03
last-updated: 2026-10-03
last-reviewed: 2026-10-03
---

# ExpenseHub — API de reembolsos

Checkpoint de C# da FIAP: API ASP.NET Core 10 com persistência SQLite/EF Core, Identity bearer e regras de acesso por perfil, proprietário e estado. A execução está em andamento. Nesta primeira entrega, a fundação relacional e a migration estão implementadas; os fluxos HTTP entram nas entregas seguintes.

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

Usar a URL exibida pelo host para consultar `GET /health`. Para fixar uma URL local, configurar `ASPNETCORE_URLS` no ambiente. O banco é criado/atualizado pela migration; iniciar o processo sozinho não substitui esse comando.

A migration contém as tabelas de Identity, Expense, ExpenseCategory, ExpenseHistory e PaymentRecord. A categoria inicial `General` é um dado de domínio; não cria usuários. Revisão concorrente, pagamento único, histórico por revisão e FKs são definidos no mapeamento relacional.

## Testes e qualidade

```shell
dotnet test ./sources/ExpenseHub.slnx
pwsh -NoProfile -File ./tests/Invoke-CodeQuality.Unit.Tests.ps1
pwsh -NoProfile -File ./tests/Invoke-CodeQuality.E2E.ps1
pwsh -NoProfile -File ./scripts/Invoke-CodeQuality.ps1 -Ci
```

O workflow oficial permanece intacto. O score está no artefato `code-quality-report`; verificar `score.final`, findings e commit analisado. Os testes funcionais próprios estão previstos nas próximas entregas; sucesso de build não comprova autorização ou fluxo de reembolso.

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
