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
| I03 — administração | [PR #4](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/4), SHA e19c526; CI score 100, zero findings; 59 verificações HTTP. |
| I04 — Draft | [PR #5](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/5), SHA 1dc5d44; CI score 100, zero findings; 36 verificações HTTP. |
| I05 — envio/consulta | [PR #6](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/6), SHA 50c3b5f; CI score 100 com três warnings de imports, corrigidos em I07; 53 verificações HTTP. |
| I06 — matriz | [PR #7](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/7), SHA 2384264; CI score 100 com os três warnings ainda presentes; matriz HTTP com 445 verificações. |
| I07 — decisões | [PR #8](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/8), SHA fc21f19; CI score 100, zero findings/warnings; 40 verificações HTTP. |
| I08 — pagamento/histórico | [PR #9](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/9), SHA a1ab905; CI score 100, zero findings; 65 verificações HTTP e rollback real. |
| I09 — unitários próprios | [PR #10](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/10), SHA 6ea57f9; 111 testes; CI score 100, zero findings/warnings. |
| M01 — integração SQLite | [PR #11](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/11), SHA 30bbd4b; oito testes; CI score 100, zero findings/warnings. |
| I10 — qualidade final | Pendente do SHA final integrado na main. |
| M02 — Scalar/OpenAPI | [PR #12](https://github.com/gh-johnny/checkpoint-expensehub-fiap/pull/12), SHA 39dcc7a; CI score 100, zero findings/warnings; Chromium executou login e consulta protegida com 200. |
| M03 — demo automatizada/CI adicional | Demo HTTP implementada: 104 cenários passaram; prova negativa retorna erro com relatório failed. Executor completo passou com build sem warnings, 119 testes e migration; workflow ainda aguarda publicação. |
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

A análise local inicial foi feita antes do commit da fundação; não servia como artefato do código novo. O CI de push posterior confirmou o SHA de I01 com score 100. A entrega final ainda precisa do relatório da main após o último merge.

O build ainda não comprova autorização, transições nem atomicidade real das operações futuras. Provas funcionais, unitários próprios, integração e demo serão acrescentados às respectivas entregas antes de serem declarados concluídos.

## Evidências de Identity

Build após Identity: zero erros/warnings. Factory de contexto permite migration sem credenciais de bootstrap. Em SQLite temporário externo ao repositório, a API foi iniciada duas vezes: cinco roles, uma conta inicial Admin e nenhum usuário duplicado pelo seed.

Provas HTTP observadas: Admin recebe bearer; credencial inválida retorna 401; rota Identity protegida retorna 401 sem token ou com token inválido e 200 com bearer válido; cadastro HTTP com tentativa de role Admin permanece sem roles. A conta registrada persiste no segundo início, enquanto o seed continua com somente um Admin inicial. Credenciais geradas em memória e tokens não foram impressos.

A prova 403 por role insuficiente foi concluída nas rotas administrativas de I03. O [CI de I01](https://github.com/gh-johnny/checkpoint-expensehub-fiap/actions/runs/37095828121) já fornece relatório do SHA da fundação com status passed, score 100 e zero findings/warnings/erros/caps.


## Evidências de administração

As rotas administrativas validam Admin no controller e no serviço. Ator imutável, lista de roles conhecida, substituição do conjunto completo e bloqueio de remoção do próprio Admin. O adaptador usa uma transação do mesmo contexto Identity, com rollback mesmo quando o cancellation token foi cancelado.

Os 14 testes unitários próprios passaram sem EF, SQLite ou rede, com zero warnings. A prova HTTP local executou 59 verificações: 401 e header bearer, 403 para role ausente e cada perfil não administrativo, lista de contas sem hashes, mudança de roles com novo login, token antigo sem privilégios novos, conjunto vazio, roles desconhecidas/nulas, conta ausente e bloqueio de remoção do próprio Admin.

Uma trigger temporária no SQLite descartável provocou falha na inserção da nova role após remoção da anterior. A API devolveu 500 genérico, com code/traceId; a role Employee e o concurrency stamp anteriores permaneceram intactos. Removida a trigger, a troca voltou a funcionar. Credenciais e tokens ficaram somente em memória. Essa prova externa será incorporada aos testes de integração reproduzíveis; não substitui sua entrega.


## Evidências do fluxo completo e da integração

As 13 rotas foram exercitadas por HTTP em bancos descartáveis. Os totais acima pertencem a provas por etapa; não representam uma única demo versionada. A matriz de leitura verificou as 32 combinações das cinco roles, despesas próprias/alheias e cinco estados. Os estados dessa matriz foram preparados diretamente no fixture externo; criação de contas e Drafts ocorreu por HTTP.

Aprovação/reprovação verificaram autoação 403 mesmo com roles acumuladas, justificativa normalizada e repetição 409 sem evento extra. Approver isolado recebe 404 na leitura após decidir, mas continua recebendo 409 na repetição da ação. Pagamento deriva ator, valor e UTC; seu evento e PaymentRecord são gravados juntos. Falhas SQLite controladas demonstraram rollback da criação quando o histórico falha e preservação de Approved/revisão/histórico sem pagamento quando PaymentRecord falha.

Unitários: 111 testes passaram sem warnings. Duas alterações temporárias em ExpenseAuthorization foram executadas fora dos commits: inversão da comparação de estado detectada por um teste e inversão da proibição de autoação detectada por cinco casos. Os arquivos foram restaurados e a suíte completa voltou a passar. Essa prova cobre esses dois defeitos simples; não constitui taxa geral de mutation testing.

Integração: oito testes MSTest em projeto separado, com SQLite em arquivo exclusivo, Pooling=False, migrations reais e contextos novos para conferir resultados. Cobrem seed de um Admin/cinco roles, decimal máximo/DateOnly/UTC, projeções sem tracking e escopo SQL, FK não convertida indevidamente em 409, pagamento único, revisão desatualizada em dois contextos e rollback após falhas de histórico, pagamento e substituição de roles. As injeções de falha existem somente nos fixtures. Usuários adicionais nesses testes são dados de fixture; a API de demonstração os cria por HTTP.

Os relatórios de I05 e I06 conservaram três warnings de imports. O [CI de I07](https://github.com/gh-johnny/checkpoint-expensehub-fiap/actions/runs/37099216907) confirma a correção com zero findings/warnings no SHA fc21f195e79523a14b6b8e77d1edd768973084a2. Os [CIs de I08](https://github.com/gh-johnny/checkpoint-expensehub-fiap/actions/runs/37099585828) e [I09](https://github.com/gh-johnny/checkpoint-expensehub-fiap/actions/runs/37099777114) também passaram com score 100 e zero findings.


## Evidências de referência interativa

Scalar.AspNetCore 2.17.13, OpenAPI nativo e transformers em ApiDocumentation. A segurança é inferida dos metadados de autorização de cada operação: públicas permanecem sem bearer; protegidas referenciam Bearer/http. Entrada e saída usam DTOs e respostas tipadas. O schema inclui limites, DateOnly como date, decimal, estados por nome, code e traceId de ProblemDetails e exemplos sem credenciais.

Uma prova HTTP executou 65 verificações: presença dos 13 métodos/rotas obrigatórios, IDs únicos, tags, segurança, limites, formatos, enum de estado, response DTO e exemplos. Depois da revisão do schema, outras verificações confirmaram os três campos obrigatórios de Draft e code/traceId explícitos nos erros.

Chromium carregou `/docs`, sem erros de JavaScript. A renderização foi inspecionada em captura anterior à entrada de credenciais. Pela interface Scalar, executou login 200, recebeu accessToken, configurou Bearer e executou GET /api/admin/users com 200 e uma conta Admin. Credenciais ficaram no processo de teste; nenhum HTML/captura após sua entrada ou token foi persistido. Agent desabilitado confirmado na configuração renderizada. Essas provas de navegador foram locais; o futuro workflow de demo HTTP não deve ser apresentado como teste de browser.


## Evidências da demonstração reproduzível

A [demo versionada](DEMO.md) executou 104 cenários HTTP com SQLite descartável: rotas/OpenAPI, bearer e roles, isolamento, Draft/edição, validações, Paid, Rejected, histórico e proibição de autoação com todas as roles. Os relatórios JSON/Markdown não incluem credenciais, bearer, headers ou corpos. Inspeção também confirmou ausência das credenciais e do token Admin usados na execução.

Uma execução com o host já encerrado terminou com saída 1, status failed, zero cenários aprovados e uma falha Health sem status HTTP observado. Falha real não é convertida em sucesso. O leitor da demo decodifica UTF-8 quando PowerShell devolve application/problem+json como bytes. O executor usa migrations reais em arquivo exclusivo, credencial gerada e processo próprio; seu workflow publica somente relatórios sanitizados. O ciclo completo passou localmente: restore, build sem warnings, 111 unitários, oito integrações, migration e 104 cenários HTTP. O diretório temporário foi removido. O workflow ainda precisa do relatório do SHA publicado; o relatório local identifica a árvore de trabalho e não comprova um novo commit.
