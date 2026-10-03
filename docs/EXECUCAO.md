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
| I01 — fundação relacional | [PR #2](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/2), commit `5b6fa16cb98704ad3739beeddc0a3d35329e542b`; CI score 100 sem findings. Aberta, sem merge. |
| I02 — Identity/bearer/seed | [PR #3](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/3), commit `2066fa0ff4704f26a2ea3a3ccf1acff312b747c9`, CI score 100. Prova 403 concluída em I03. |
| I03 — administração | Implementada; 14 testes unitários e 59 verificações HTTP passaram, inclusive rollback real após falha do provider. |
| I04–I08 — despesas | Ainda não implementadas. |
| I09 — unitários próprios | 14 testes de administração passaram; matriz de despesas e transições pendente. |
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

## Evidências de Identity

Build após Identity: zero erros/warnings. Factory de contexto permite migration sem credenciais de bootstrap. Em SQLite temporário externo ao repositório, a API foi iniciada duas vezes: cinco roles, uma conta inicial Admin e nenhum usuário duplicado pelo seed.

Provas HTTP observadas: Admin recebe bearer; credencial inválida retorna 401; rota Identity protegida retorna 401 sem token ou com token inválido e 200 com bearer válido; cadastro HTTP com tentativa de role Admin permanece sem roles. A conta registrada persiste no segundo início, enquanto o seed continua com somente um Admin inicial. Credenciais geradas em memória e tokens não foram impressos.

A prova 403 por role insuficiente foi concluída nas rotas administrativas de I03. O [CI de I01](https://github.com/gh-johnny/checkpoint-expensehub-fiap/actions/runs/37095828121) já fornece relatório do SHA da fundação com status passed, score 100 e zero findings/warnings/erros/caps.


## Evidências de administração

As rotas administrativas validam Admin no controller e no serviço. Ator imutável, lista de roles conhecida, substituição do conjunto completo e bloqueio de remoção do próprio Admin. O adaptador usa uma transação do mesmo contexto Identity, com rollback mesmo quando o cancellation token foi cancelado.

Os 14 testes unitários próprios passaram sem EF, SQLite ou rede, com zero warnings. A prova HTTP local executou 59 verificações: 401 e header bearer, 403 para role ausente e cada perfil não administrativo, lista de contas sem hashes, mudança de roles com novo login, token antigo sem privilégios novos, conjunto vazio, roles desconhecidas/nulas, conta ausente e bloqueio de remoção do próprio Admin.

Uma trigger temporária no SQLite descartável provocou falha na inserção da nova role após remoção da anterior. A API devolveu 500 genérico, com code/traceId; a role Employee e o concurrency stamp anteriores permaneceram intactos. Removida a trigger, a troca voltou a funcionar. Credenciais e tokens ficaram somente em memória. Essa prova externa será incorporada aos testes de integração reproduzíveis; não substitui sua entrega.
