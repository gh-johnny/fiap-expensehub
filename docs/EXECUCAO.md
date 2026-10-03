---
title: Execução e validação do ExpenseHub
status: internal
created: 2026-10-03
last-updated: 2026-10-03
last-reviewed: 2026-10-03
---

# Execução e validação do ExpenseHub

| Integrante | RM |
|---|---|
| João Marcelo Furtado Romero | RM555199 |
| Matheus Rivera Montovaneli | RM555499 |
| André Nakamatsu Rocha | RM555004 |

Execução do [plano técnico](PLANO.md) e da [divisão de trabalho](DIVISAO-TRABALHO.md), iniciada em 03/10/2026. Este registro contém resultados observados; cronograma e distribuição não comprovam participação ou funcionalidades implementadas.

## Repositório e autoria

Repositório público, criado por template e sem fork: [gh-johnny/checkpoint-expensehub-fiap](https://github.com/gh-johnny/checkpoint-expensehub-fiap). Base do grupo: `8f4dac931306f061ba2a3c4aeac1ceac5d520b9e`. A árvore inicial coincide com o template upstream `58c8405387f5e9ae9725ad964917e7634a870b74`.

A [PR de planejamento #1](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/1) foi integrada após aprovação humana, com merge `ec3955913abdc6f772f1c31d871771d8fe551708`. Seu commit de trabalho é `f6535c68e87caf550b9223ac393ccb9c0912b457`.

A conta autenticada nesta sessão é João. Os commits usam sua configuração real, sem coautoria de IA. A participação de Matheus e André permanece pendente de contribuições próprias; o desenvolvimento automatizado não cria evidência de autoria deles. O login GitHub de André ainda não foi informado.

## Ambiente verificado

- SDK .NET 10.0.401 e runtime ASP.NET Core 10.0.12, instalados fora do repositório.
- PowerShell 7.6.6 e Gitleaks 8.30.1, igualmente fora da árvore de análise.
- SHA512 do SDK e SHA256 do Gitleaks conferidos com as fontes de distribuição.
- SDK/runtime antigos do sistema mantidos. Os comandos da sessão usam a instalação completa no PATH; não dependem de propriedades que desabilitem pruning.

## Estado por entrega

| Entrega | Estado verificado |
|---|---|
| Preparação e planejamento | Repositório, ferramentas e PR de planejamento concluídos. |
| I01 — fundação relacional | Implementada e validada localmente; commit/PR de fundação em preparação. |
| I02–I08 — API funcional | Ainda não implementadas. |
| I09 — unitários próprios | Ainda não implementados; ausência de testes no template não é evidência funcional. |
| I10 — qualidade final | Pendente do código e SHA finais; baseline e fundação local com score 100. |
| Scalar, demo HTTP e integração SQLite | Ainda não implementados. |
| Participação individual dos três | Pendente de contribuições próprias de Matheus e André. |

## Evidências da fundação

EF Core SQLite, Identity EF e Design 10.0.12; ferramenta local dotnet-ef 10.0.12. Entidades mínimas, mapeamentos e migration `20261003035912_InitialSchema` criados. IDs/atores/estado/revisão usam setters restritos ao código da aplicação.

Validações executadas:

1. Restore e build normais: zero erros, zero warnings.
2. Migration aplicada a SQLite vazio em arquivo temporário externo ao repositório.
3. Segunda aplicação informa banco atualizado, sem nova alteração.
4. Inspeção SQLite confirma tabelas de domínio/Identity, categoria General, zero usuários iniciais, FKs e índices únicos de pagamento e histórico por revisão.
5. Testes do corretor: 15 unitários e 10 E2E passaram.
6. Análise oficial executada localmente na árvore de trabalho: score 100/100, status passed.

Essa análise local foi feita antes do commit da fundação; o SHA do relatório ainda identifica o HEAD de planejamento. Portanto, não é relatório de entrega do código novo. A PR de I01 precisa de seu próprio artefato de CI, e a entrega final precisa do relatório da main após o último merge.

O build ainda não comprova autorização, transições nem atomicidade real das operações futuras. Provas funcionais, unitários próprios, integração e demo serão acrescentados às respectivas entregas antes de serem declarados concluídos.
