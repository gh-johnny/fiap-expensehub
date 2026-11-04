---
title: Divisão de trabalho e autoria do ExpenseHub
status: internal
created: 2026-10-03
last-updated: 2026-10-03
last-reviewed: 2026-10-03
---

# Divisão de trabalho e autoria do ExpenseHub

Plano de execução para João, Matheus e André, com carga estimada igual, contribuições técnicas individuais, revisão cruzada e commits próprios. Complementa o [plano técnico](PLANO.md), que continua sendo a referência para contratos, matriz de autorização, estados e critérios de qualidade. Público: os três integrantes. Execução iniciada em 03/10/2026. Os pacotes abaixo descrevem a distribuição planejada; o [registro de execução](EXECUCAO.md) identifica entregas e validações reais, inclusive a autoria efetivamente utilizada.

A entrega principal é a API obrigatória. Os extras escolhidos são documentação interativa com Scalar, demonstração HTTP automatizada e provas de persistência em SQLite. Frontend, deploy e paginação não entram na distribuição principal.

## 1. Integrantes e identificação dos commits

| Integrante e nome de autoria esperado | RM | Conta GitHub informada | E-mail informado para commits |
|---|---|---|---|
| João Marcelo Furtado Romero | RM555199 | gh-johnny | jmfurtadoromero@gmail.com |
| Matheus Rivera Montovaneli | RM555499 | imneli | contatodoneli@gmail.com |
| André Nakamatsu Rocha | RM555004 | andrenakarocha | andrenakarocha@hotmail.com |

Os nomes/e-mails e os logins de João e Matheus foram informados pelo usuário. Os commits de João estão associados a gh-johnny. O login [andrenakarocha](https://github.com/andrenakarocha) foi identificado em consulta autenticada à API pública de usuários do GitHub, que retornou exatamente o e-mail informado; a consulta anônima retorna email=null. Isso identifica a conta de André, sem comprovar contribuição sua. A associação do e-mail de Matheus ainda não foi verificada. Foram enviados convites de escrita a imneli e andrenakarocha; ambos aguardam aceite. Os três devem possuir acesso ao repositório e registrar contribuições próprias na `main`. Convite pendente não comprova acesso aceito nem participação.

O [processo oficial](https://github.com/Racass/checkpoint-csharpracass-expensehub/blob/58c8405387f5e9ae9725ad964917e7634a870b74/docs/PROCESSO-GITHUB.md) exige **mais de um commit por integrante** e evolução real. A [política de IA](https://github.com/Racass/checkpoint-csharpracass-expensehub/blob/58c8405387f5e9ae9725ad964917e7634a870b74/docs/USO-DE-IA.md) exige revisão e validação e não admite falsificação de autoria ou histórico. Por isso, cada aluno executa, compreende e registra sua contribuição; trocar o nome de um único executor entre três perfis não atende ao critério individual.

Convenção desta entrega: commits individuais, sem trailers `Co-authored-by`. Isso atende ao pedido de não adicionar coautoria de IA e mantém os três alunos identificados pelos próprios commits. A assistência de IA, quando precisar ser descrita, entra como método de trabalho na documentação ou PR, sem atribuir à ferramenta uma identidade de aluno.

## 2. Como a carga foi equilibrada

**Total estimado: 120 unidades de esforço; 40 por integrante; oito pacotes por pessoa.** Unidade de esforço compara complexidade, incerteza e trabalho de validação. Não representa hora, ponto da rubrica, número de linhas ou garantia de duração.

| Integrante | Pacotes | Esforço estimado | Parcela | Revisão principal |
|---|---:|---:|---:|---|
| João | J01–J08 | 40 | 33,33% | Revisa A01–A08, de André. |
| Matheus | M01–M08 | 40 | 33,33% | Revisa J01–J08, de João. |
| André | A01–A08 | 40 | 33,33% | Revisa M01–M08, de Matheus. |

Todo pacote inclui implementação ou correção pertinente, testes relevantes, documentação afetada e atendimento aos analisadores. Todos fazem código funcional, testes unitários, ao menos uma prova de integração e validação final. A revisão cruzada faz parte da capacidade reservada; não transforma o revisor em autor do código revisado.

Há oito marcos de commit previstos por pessoa, um por pacote. São exemplos de mudanças coesas, **não uma quota artificial**: um pacote pode precisar de vários commits de evolução real, ou compartilhar uma alteração indivisível com outro pacote do mesmo autor. Não separar uma mudança apenas para igualar contagens. O requisito individual continua sendo mais de um commit real preservado na `main`.

Reavaliar esforço ao fechar I03, I06 e I08. Se a carga restante de alguém exceder a dos demais em mais de quatro unidades, transferir um pacote ainda não iniciado e registrar responsável, motivo e nova estimativa na PR. Atualizar esta matriz para preservar a divisão próxima de um terço por integrante. Não redistribuir retroativamente autoria de trabalho já concluído.

## 3. Matriz dos pacotes

| Pacote | Responsável | Esforço | Entrega | Referência principal |
|---|---|---:|---|---|
| J01 | João | 7 | Ambiente reproduzível, EF/IdentityDbContext, mapeamentos e migration inicial | I01 |
| J02 | João | 7 | Identity bearer, cadastro/login públicos, roles e Admin inicial | I02; cadastro de I03 |
| J03 | João | 5 | Envio Draft → Submitted e auditoria da operação | I05 |
| J04 | João | 5 | Erros HTTP, correlação e tradução precisa de conflitos de persistência | I05–I08 |
| J05 | João | 4 | Substitutos dos unitários e auditoria de regressões de I09 | I09 |
| J06 | João | 3 | Projeto/fixture de integração e prova de migration/seed idempotente | Extra SQLite; I01/I02 |
| J07 | João | 5 | Workflow adicional de demo e artefatos sanitizados | Extra CI; I10 |
| J08 | João | 4 | Guia de setup, ambiente limpo e fechamento de evidências de qualidade | I01/I10 |
| M01 | Matheus | 7 | Administração de usuários/roles com substituição atômica | I03 |
| M02 | Matheus | 7 | Criação/edição de Draft, DTOs, validação e auditoria | I04 |
| M03 | Matheus | 7 | Store EF, consultas SQL autorizadas, listagem e detalhe | I05/I06 |
| M04 | Matheus | 4 | Consulta autorizada de histórico e ordenação por revisão | I08 |
| M05 | Matheus | 3 | Unitários de fronteiras, leituras, histórico e administração | I09 |
| M06 | Matheus | 4 | Provas SQLite de tipos/FKs e rollback de roles | Extra SQLite |
| M07 | Matheus | 5 | Scalar e contrato OpenAPI interativo | Extra documentação |
| M08 | Matheus | 3 | Guia HTTP, conferência dos 13 endpoints e checklist de entrega | I10; documentação |
| A01 | André | 4 | Entidades, estados, revisão e contratos de operação | I01; domínio transversal |
| A02 | André | 6 | Ator, ownership e matriz de autorização nos serviços | I06 |
| A03 | André | 6 | Aprovação/reprovação e justificativa auditável | I07 |
| A04 | André | 5 | Pagamento único e operação lógica completa | I08 |
| A05 | André | 5 | Unitários de estados, permissões combinadas, decisões e pagamento | I09 |
| A06 | André | 6 | Provas SQLite de concorrência, unicidade e rollback financeiro | Extra SQLite |
| A07 | André | 6 | Demo HTTP com assertivas, falhas e relatórios JSON/Markdown | Extra demonstração |
| A08 | André | 2 | Guia de estados/autorização e conferência individual da entrega | I10; documentação |

## 4. Entregas de João

### J01 — Fundação relacional e ambiente — 7 unidades

**Arquivos previstos:** `sources/ExpenseHub.Api/ExpenseHub.Api.csproj`, `Persistence/ExpenseHubDbContext.cs`, configurações EF, `Migrations/`, manifesto local de ferramentas e registro do contexto em `Program.cs`.

Preparar SDK/runtime completos e ferramentas locais, preservando a baseline do professor. Integrar entidades de A01 com Identity, SQLite e pacotes alinhados. Definir FKs, índices de leitura, estados conhecidos, token de concorrência, pagamento único e histórico único por despesa/revisão. Criar migration depois de fechar os modelos; não gerar migrations simultâneas em outras branches.

Aceite: restore/build normais com zero warnings; migration aplicada a banco vazio; IDs e relações coerentes; nenhum banco ou binário versionado. O workaround de diagnóstico do SDK não entra na configuração da solução. Dependência: modelos de A01; revisão: Matheus.

### J02 — Identity, autenticação e seed — 7 unidades

**Arquivos previstos:** `Persistence/IdentitySeed.cs`, configuração Identity e mapeamento de `/register` e `/login` em `Program.cs`.

Configurar bearer nativo, cinco roles e exatamente um Admin inicial com senha externa ao código. Registro público não aceita roles; contas funcionais são criadas por HTTP. Seed é idempotente, com falha explícita de configuração sem exposição de segredo. Explicar novo login após mudança de roles em conjunto com M01.

Aceite: login válido/inválido, 401 sem token, 403 com role insuficiente, registro sem escalada, seed repetido sem duplicação. Guardas gerais estão presentes desde as primeiras rotas protegidas. Dependência: J01; revisão: Matheus.

### J03 — Envio de despesa — 5 unidades

**Arquivos previstos:** método de envio em `Services/ExpenseService.cs`, ação `submit` em `Controllers/ExpensesController.cs` e unitários correspondentes.

Implementar Draft → Submitted para Employee proprietário, com ator/tempo do servidor, revisão incrementada e histórico na mesma persistência. A mutação usa carregamento próprio para escrita, preservando conflito 409 em repetição. Não usar a visibilidade de leitura para autorizar a transição.

Aceite: fluxo válido; outro proprietário negado; anônimo/perfil insuficiente; estado incompatível ou repetido sem alteração/histórico adicional. Dependências: M02, fronteira de M03 e autorização inicial de A02; revisão: Matheus.

### J04 — Erros, correlação e conflitos — 5 unidades

**Arquivos previstos:** `Infrastructure/ApiExceptionHandler.cs`, configuração de ProblemDetails e trecho de persistência de `Persistence/EfExpenseStore.cs`, em janela coordenada com Matheus.

Padronizar `code` e `traceId`, preservar semântica 400/401/403/404/409 e registrar falhas inesperadas sem segredos. Traduzir `DbUpdateConcurrencyException` e violações conhecidas de unicidade conforme o plano técnico; não transformar todo erro de banco em 409. Garantir descarte do estado pendente da requisição após falha, sem retry cego.

Aceite: 404 ausente/invisível indistinguível; conflito não revela dados do recurso; erro inesperado continua sendo 500; logs sem senha/token/corpo integral. Conferir lifetime do handler e logging em .NET 10. Dependências: contratos de erro acordados e store de M03; revisão: Matheus.

### J05 — Isolamento dos unitários e prova de regressão — 4 unidades

**Arquivos previstos:** `sources/ExpenseHub.UnitTests/TestDoubles/`, testes de submit, contratos de erro de aplicação e relatório de validação na PR de I09.

Criar os substitutos mínimos de store/Identity e relógio controlado efetivamente necessários. Auditar os testes dos três integrantes quanto a isolamento, clareza e comportamento detectado. Em cópia descartável, introduzir um defeito por vez em ownership, repetição e justificativa; confirmar que a suíte acusa os defeitos e descartar essa cópia.

Aceite: unitários sem SQLite/rede/serviços externos; sem estado estático compartilhado; asserts sobre resultados e ausência de efeitos; três defeitos realmente detectados, com evidência. Testes existentes do corretor não são apresentados como autoria do grupo. Dependências: testes funcionais de J03, M05 e A05; revisão: Matheus.

### J06 — Fixture SQLite e bootstrap — 3 unidades

**Arquivos previstos:** `sources/ExpenseHub.IntegrationTests/ExpenseHub.IntegrationTests.csproj`, fixture compartilhado de persistência, testes de migrations/seed e inclusão do projeto na solução.

Preparar SQLite descartável por teste com migrations reais e contextos independentes. Gerar credenciais de fixture em memória; disponibilizar abertura de contexto novo para leitura do estado persistido. Escrever prova de schema e seed executado duas vezes. Matheus e André reutilizam o fixture, sem alterar sua estrutura em paralelo.

Aceite: um Admin inicial, cinco roles, categoria mínima sem duplicação; descarte de conexões e arquivos; execução paralela sem colisão; analisadores da baseline ativos. Dependências: J01/J02; revisão: Matheus.

### J07 — Workflow adicional de demonstração — 5 unidades

**Arquivo previsto:** `.github/workflows/demo.yml`. O workflow original e os scripts do corretor são preservados.

Executar por PR e acionamento manual: build, banco temporário, credencial Admin gerada e mascarada, migration, processo próprio da API, readiness com prazo, script de A07, publicação dos relatórios e limpeza. Usar permissões mínimas, sem depender de credenciais permanentes de uma conta de demonstração.

Aceite: resultado respeita exit code da demo; falha publica relatório diagnóstico sanitizado quando disponível; cleanup encerra apenas o PID iniciado pelo job; artefato identifica o SHA efetivamente analisado. Dependências: A07 e aplicação funcional; revisão: Matheus.

### J08 — Setup reproduzível e fechamento de qualidade — 4 unidades

**Arquivos previstos:** seção de setup do `README.md`, guia de execução e evidências na PR de I10.

Documentar SDK/runtime, configuração externa, migrations e comandos normais. Reproduzir em clone limpo, executar corretor completo e ler relatório por projeto e SHA. Coordenar as correções distribuindo cada finding ao autor do módulo; não acumular sozinho as correções de todos. Verificar o relatório da `main` depois do último merge.

Aceite: score 100, zero erros/warnings, nenhum cap/finding bloqueante, acesso do professor e SHA registrado. Matheus confere endpoints; André confere autorização e autoria; os três corrigem suas próprias pendências. Dependências: fechamento funcional e dos extras escolhidos; revisão: Matheus.

## 5. Entregas de Matheus

### M01 — Administração de usuários e roles — 7 unidades

**Arquivos previstos:** `Controllers/AdminUsersController.cs`, `Services/UserRoleService.cs`, `Persistence/IdentityUserRoleStore.cs` e DTOs administrativos.

Implementar listagem Admin e substituição integral por `List<string>` de roles conhecidas. Validar todo o conjunto e proteção contra própria remoção de Admin antes de qualquer alteração. Lista vazia é permitida para outro usuário. Usar transação explícita para os múltiplos saves de UserManager, com rollback em falha. Não aceitar role fornecida no cadastro público.

Aceite: atribuir/remover, usuário inexistente, role inválida, auto-rebaixamento, conjunto vazio e acesso de perfil indevido; respostas sem hash/credencial. Dependência: J02; revisão: André.

### M02 — Criação e edição de Draft — 7 unidades

**Arquivos previstos:** `ExpenseDraftRequest.cs`, DTO de saída, métodos de create/update no serviço e ações correspondentes no controller.

Receber descrição, amount decimal e expenseDate; servidor controla ID, owner, estado, categoria, ator e tempo. Validar os limites sem impor duas casas decimais não exigidas. Criar Draft com histórico e permitir edição somente ao proprietário enquanto Draft. Registrar valores anteriores/novos em alteração efetiva; PUT sem mudança segue a convenção de ausência de histórico/revisão adicional.

Aceite: fronteiras válidas/inválidas, data controlada, tentativa de mass assignment, edição alheia e após submit; operações negadas não persistem. Dependências: J01/J02, modelos e autorização inicial de André, store acordado com J01; revisão: André.

### M03 — Store e consultas autorizadas — 7 unidades

**Arquivos previstos:** `Services/IExpenseStore.cs`, `Persistence/EfExpenseStore.cs`, métodos list/detail no serviço e controller.

Implementar a fronteira específica de despesas, sem repository genérico nem IQueryable exposto aos consumidores. Separar leitura visível de carregamento para alteração. Aplicar o escopo de A02 em SQL antes da materialização, com projeção, AsNoTracking pertinente, ordenação determinística e cancelamento. Construir o store mínimo necessário para M02 antes de completar list/detail.

Aceite: isolamento de Employees, união de roles, Approver Submitted, Finance Approved/Paid, Auditor tudo e Admin sem permissão funcional implícita; detalhe invisível/ausente 404. Sem carregar tudo e filtrar em memória. João acrescenta tradução de conflitos em J04 na janela combinada. Dependências: contexto de J01 e contratos de A02; revisão: André.

### M04 — Histórico autorizado — 4 unidades

**Arquivos previstos:** resposta de histórico, consulta correspondente no store/serviço e ação `/history` no controller.

Reusar a visibilidade de leitura da despesa, sem confundi-la com a autorização das ações de escrita. Ordenar por Revision; devolver ação, ator, UTC, estados, justificativa e alterações pertinentes, sem campos secretos. Participar da conferência de todos os fluxos que escrevem histórico.

Aceite: recurso invisível/ausente 404; Employee vê o próprio; Auditor vê todos; Approver perde leitura depois da decisão; sequência completa e correta mesmo com timestamps iguais. Dependências: M03 e histórico iniciado por M02/J03, completado por A03/A04; revisão: André.

### M05 — Unitários de fronteiras e consultas — 3 unidades

**Arquivos previstos:** testes em `Expenses/`, `Administration/` e grupos de consulta/histórico no UnitTests.

Complementar os testes escritos em M01–M04 com valores 9/10/500/501, mínimo/máximo monetário, data atual/futura, edição sem mudança, escopos combinados e proteção de roles. Conferir o escopo pedido ao store e a resposta do serviço sem fingir que um teste com substituto comprova tradução SQL.

Aceite: asserts sobre resultado e invariantes, atores/relógios explícitos, nenhum banco/rede e nenhuma validação baseada apenas em chamada de mock. Regressões de limites e visibilidade são detectadas. Dependências: métodos de M01–M04; revisão: André.

### M06 — Tipos, relações e rollback administrativo — 4 unidades

**Arquivos previstos:** testes de persistência de tipos/FKs e de transação de roles no IntegrationTests.

Usar o fixture de J06 para round-trip de decimal, DateOnly e UTC, FKs inexistentes e constraints declaradas. Provocar falha controlada durante substituição de roles em fixture e confirmar que o conjunto anterior permanece inteiro; conferir por contexto novo. Injeção de falha fica apenas no teste.

Aceite: limites monetários sem perda, datas coerentes, FK realmente imposta e nenhuma atualização parcial de roles. Os testes não entram como unitários pontuáveis. Dependências: J06, mapeamentos de J01 e adapter de M01; revisão: André.

### M07 — Scalar e OpenAPI — 5 unidades

**Arquivos previstos:** `Documentation/BearerSecurityTransformer.cs`, configuração OpenAPI/Scalar e metadados das operações, coordenados com os donos dos controllers.

Implementar `/docs` e `/openapi/v1.json` em Development com as versões definidas no plano técnico. Configurar bearer do Identity corretamente, manter login/register públicos, tags, OperationIds únicos, schemas e exemplos. Não preencher credenciais na interface; desabilitar Agent. João integra o trecho de Program.cs fornecido por Matheus em janela exclusiva, sem assumir sua autoria.

Aceite: 13 métodos/rotas documentados, limites e estados coerentes, schemas de erro úteis, autenticação interativa funcionando no navegador e sem declaração falsa de JWT. A demo automatiza schema; Matheus comprova a interface visual. Dependência: contratos obrigatórios estabilizados; revisão: André.

### M08 — Guia HTTP e conferência dos contratos — 3 unidades

**Arquivos previstos:** exemplos HTTP sem segredo, seções de endpoints/demonstração no README e checklist da PR final.

Documentar payloads, obtenção de token, novo login após roles, fluxos válidos e interpretação dos erros. Rodar os 13 endpoints contra a versão candidata e revisar o documento OpenAPI de M07. Conferir que filtros ou envelope paginado opcionais não mudaram o contrato obrigatório.

Aceite: outra pessoa reproduz o fluxo com placeholders/ambiente, todos os endpoints têm evidência e relatórios apontam para o SHA correto. Dependências: API, M07 e demo de A07; revisão: André.

## 6. Entregas de André

### A01 — Domínio e contratos iniciais — 4 unidades

**Arquivos previstos:** `Models/Expense.cs`, estados, `ExpenseCategory.cs`, `ExpenseHistory.cs`, `PaymentRecord.cs` e contratos mínimos de operação.

Definir entidades com setters restritos, um único amount decimal, Revision inicial 1 e dados necessários ao histórico. Consolidar com os três os nomes/assinaturas consumidos pelo serviço e store. Regras de transição são exercidas pelo domínio/serviço conforme a estrutura existente, sem criar uma hierarquia de abstrações para cada ação.

Aceite: nenhum estado extra ou edição arbitrária, histórico identificável por revisão, pagamento associado à despesa e contratos usados pelas entregas seguintes. João mantém o mapeamento EF e a migration. Dependência: leitura conjunta do contrato; revisão: João.

### A02 — Ator e autorização contextual — 6 unidades

**Arquivos previstos:** `Services/CurrentActor.cs`, `Services/ExpenseAuthorization.cs`, construção do ator no limite HTTP e contratos de escopo.

Obter UserId e roles das claims, passar snapshot imutável ao serviço e implementar permissões por ação/estado/propriedade. Roles acumulam por união; Admin isolado não acessa despesas; Auditor isolado não escreve; nenhuma combinação permite autoaprovação, autoreprovação ou autopagamento. Entregar guardas mínimas junto de M02/J03; I06 completa a auditoria da matriz.

Aceite: matriz nos serviços, independentemente do controller; 401/403/404 coerentes; escopo utilizado por M03 antes de materializar; caminhos de leitura e mutação distintos. Dependência: identidade de J02 e contratos de A01; revisão: João.

### A03 — Aprovação e reprovação — 6 unidades

**Arquivos previstos:** `RejectExpenseRequest.cs`, métodos approve/reject do serviço e ações correspondentes no controller.

Transitar Submitted para Approved/Rejected somente com Approver não proprietário. Exigir justificativa válida na reprovação. Aplicar revisão, histórico e ator/UTC do servidor em uma unidade de persistência. Repetição retorna 409 mesmo que o Approver tenha perdido a visibilidade para GET.

Aceite: decisões válidas, justificativa 9/10/500/501, perfis indevidos, recurso próprio, estado incompatível e repetição; nenhum histórico adicional em falha. Dependências: A02, J03, M03 e J04; revisão: João.

### A04 — Pagamento e atomicidade lógica — 5 unidades

**Arquivos previstos:** método pay do serviço, ação correspondente no controller e composição de PaymentRecord/histórico.

Permitir Approved → Paid somente para Finance não proprietário. Valor do pagamento vem da despesa; servidor define ator/UTC. Alteração, pagamento e histórico persistem juntos por uma chamada lógica ao store. Constraint de J01 protege o vínculo único; erro de persistência nunca é devolvido como sucesso.

Aceite: fluxo pago, autopagamento/perfil negado, estado incompatível, repetição 409 sem novo registro e PaymentRecord coerente. A06 comprova transação real; o unitário comprova regras/composição, sem alegar provar o provider. Dependências: A02/A03 e J04; revisão: João.

### A05 — Unitários de decisões e permissões — 5 unidades

**Arquivos previstos:** testes de `Authorization/`, estados, approve/reject/pay e composição do histórico no UnitTests.

Completar uma matriz de transições por estado e perfis simples/acumulados. Conferir proibições sobre recurso próprio, ausência de efeitos em falha, origem do ator/tempo, razão preservada e valores do pagamento. Testes das ações já acompanham A02–A04; este pacote fecha lacunas relevantes, sem duplicar assertions para aumentar contagem.

Aceite: testes detectam remoção de ownership, permissão indevida e repetição; relógio fixo, substitutos locais e nenhuma dependência de banco. Dependências: A02–A04 e substitutos mínimos de J05; revisão: João.

### A06 — Concorrência e rollback relacional — 6 unidades

**Arquivos previstos:** testes de `Concurrency/` e persistência financeira no IntegrationTests.

Montar dois contextos com leitura na revisão N; um aprova e persiste N+1, outro tenta decisão com leitura antiga. Confirmar conflito e uma única decisão/histórico por terceiro contexto. Testar pagamento duplicado e falhas controladas de insert no histórico/pagamento para provar rollback completo. Evitar teste que depende da sorte do escalonamento de threads.

Aceite: nenhum pagamento ou histórico parcial, estado anterior preservado em falha, unicidade real e nenhuma sobrescrita concorrente. Fixtures de falha não geram modo/endpoint público. Dependências: J06, mapeamentos de J01, conflitos de J04 e ações de A03/A04; revisão: João.

### A07 — Demonstração HTTP e relatórios — 6 unidades

**Arquivo previsto:** `scripts/Invoke-ExpenseHubDemo.ps1`; saídas em `artifacts/demo/report.json` e `report.md`, sem versionar artefatos gerados.

Criar usuários por HTTP, conceder roles via Admin, autenticar cada ator e executar as assertivas previstas no plano técnico. Cobrir os dois fluxos finais, fronteiras, isolamento, roles acumuladas, autoações, repetições, bearer e OpenAPI. Conferir efeitos via proprietário/Auditor depois de decisões que retirem visibilidade do Approver.

Aceite: status mais invariantes; cenário não executado não aparece aprovado; relatórios com esperado/observado, duração, UTC, traceId e SHA; sem tokens/senhas. Qualquer divergência obrigatória devolve exit code não zero. Fornecer a João o contrato de parâmetros/variáveis para J07 e a Matheus as verificações OpenAPI. Dependências: API funcional e M07; revisão: João.

### A08 — Guia de autorização e validação individual — 2 unidades

**Arquivos previstos:** seção de autorização/estados no README ou guia vinculado e evidências da PR final.

Explicar matriz, roles acumuladas, leitura versus mutação e os cinco estados, com exemplos de códigos HTTP. Reexecutar a demo no candidato final e conferir o histórico individual dos três alunos, usando o procedimento da seção 11. Cada integrante precisa conseguir explicar seu código e seus testes.

Aceite: documentação coerente com API, identidade dos commits conferida, ausência de coautoria de IA e evidências rastreáveis. Pendências de autoria não são corrigidas reescrevendo commits de outro aluno. Dependências: integração final; revisão: João.

## 7. Arquivos compartilhados e integração

Responsável por um arquivo coordena as alterações e sua integração. Autoria permanece com quem fez e validou cada contribuição. Não transformar o responsável pelo arquivo em assinante das mudanças dos colegas.

| Superfície | Responsável pela coordenação | Outros autores e regra de edição |
|---|---|---|
| `Models/` | André | João comenta requisitos de mapeamento; André entrega modelos antes da migration. |
| DbContext, configurações EF e migrations | João | Matheus/André pedem ajustes; uma migration por vez, sem modificar migration já compartilhada silenciosamente. |
| `Program.cs` e manifesto/pacotes | João | Matheus fornece Scalar; André fornece construção de ator; integrar uma janela por vez. |
| DTOs de Draft e respostas de despesa | Matheus | Nomes acordados antes de uso nos serviços/OpenAPI/demo. |
| DTO de rejeição e regra da justificativa | André | Matheus descreve o schema, sem duplicar regra com limites diferentes. |
| `ExpenseService.cs` | Matheus | Matheus: create/update/list/detail/history; João: submit; André: approve/reject/pay. Editar métodos acordados em sequência. |
| `ExpensesController.cs` | Matheus | Mesma divisão por ação do serviço; fechar uma janela antes de outra pessoa editar. |
| `IExpenseStore.cs` e `EfExpenseStore.cs` | Matheus | João: tradução de conflitos em J04; André: requisitos de operação atômica, sem criar store paralelo. |
| Administração/adapter Identity | Matheus | João mantém seed/autenticação; não misturar seed com criação de usuários da demo. |
| `CurrentActor` e autorização | André | Matheus consome escopos; João consome permissão de submit. |
| Handler de erro e correlação | João | Matheus/André usam o mesmo formato; não criam códigos incompatíveis. |
| UnitTests: doubles compartilhados | João | Cada aluno é dono dos testes dos seus métodos; doubles novos precisam de consumidor real. |
| IntegrationTests: projeto/fixture | João | Matheus: tipos/FKs/roles; André: concorrência/pagamento/rollback. Arquivos de teste separados. |
| OpenAPI e Scalar | Matheus | Metadados nos controllers entram em janelas coordenadas com autores das ações. |
| Script de demo | André | Matheus revisa contratos HTTP; João integra execução no workflow. |
| Workflow adicional | João | Mudanças da demo alinhadas com André; workflow oficial preservado. |
| README | João | Matheus: HTTP/Scalar; André: estados/autorização; editar seções em sequência. |

Antes de iniciar um arquivo compartilhado: informar pacote, métodos/seção e janela; sincronizar branch; concluir a mudança coesa e comunicar liberação. Alterações simultâneas no mesmo método ficam suspensas até integrar a primeira. Não criar partial classes, novas camadas ou duplicar serviços apenas para evitar coordenação.

Em conflito, os autores envolvidos resolvem juntos, preservando regras e testes de ambos. Não apagar silenciosamente a contribuição do colega nem usar force push para descartar commits de uma branch compartilhada. Rever os métodos afetados e repetir apenas as verificações pertinentes antes da integração.

## 8. Issues, branches e pull requests

Há uma PR principal por issue oficial. Vários alunos podem contribuir com commits próprios na mesma branch da issue; isso não elimina a divisão por pacote. I09 e I10 são transversais desde o início, e suas PRs de fechamento consolidam lacunas/evidências; não adiam testes e qualidade para o fim.

| Issue | Peso oficial | Branch | Responsável pela PR | Pacotes/contribuições principais |
|---|---:|---|---|---|
| I01 | 4% | `i01-foundation-ef` | João | J01; A01; fronteira mínima de M03 quando necessária. |
| I02 | 9% | `i02-identity-auth` | João | J02; testes de bootstrap próprios. |
| I03 | 8% | `i03-user-roles` | Matheus | M01; registro público de J02; negativos desde a implementação. |
| I04 | 7% | `i04-expense-draft` | Matheus | M02; autorização inicial de A02; store mínimo de M03. |
| I05 | 7% | `i05-submit-query` | Matheus | J03; consultas de M03; erros de J04. |
| I06 | 10% | `i06-ownership-access` | André | A02; tradução SQL de M03; revisão contextual dos três. |
| I07 | 12% | `i07-approve-reject` | André | A03; conflitos de J04; testes já presentes. |
| I08 | 8% | `i08-payment-history` | André | A04; histórico de M04; conflitos de J04. |
| I09 | 10% | `i09-unit-tests` | João | J05/M05/A05; testes prévios de todos; provas de regressão. |
| I10 | 25% | `i10-code-quality` | João | J08/M08/A08; cada autor corrige os findings dos seus arquivos. |

Extras têm PRs próprias: `extra-sqlite-proofs` com J06/M06/A06; `extra-scalar` com M07; `extra-http-demo` com A07; `extra-demo-ci` com J07. Elas entram depois dos contratos de que dependem, sem serem apresentadas como novas issues pontuadas.

Em cada PR: citar `Racass/checkpoint-csharpracass-expensehub#N`, descrever comportamento e decisões, listar pacotes/autores, comandos realmente executados e evidências. Não usar palavras de fechamento automático para as issues originais. O revisor de cada pacote é o da seção 2; numa PR com vários autores, distribuir revisão dos trechos, sem autor aprovar a própria alteração.

Preservar commits individuais no merge. Preferir merge commit para branches compartilhadas; não usar squash que substitua contribuições de vários alunos por um único autor. Não reescrever datas, autores ou mensagens para simular evolução. O SHA de entrega é o final da `main`, e seu relatório precisa corresponder a essa versão após o merge.

## 9. Marcos de commit previstos

As mensagens abaixo são sugestões em inglês para mudanças reais. Implementação já acompanha seus testes relevantes; não publicar código sem guardas apenas para reservar um commit de segurança depois. Adequar a mensagem ao diff efetivo. Não executar os exemplos como um roteiro para trocar identidades.

| Autor | Pacote | Exemplo de mensagem |
|---|---|---|
| João | J01 | `feat(persistence): configure relational schema and migrations` |
| João | J02 | `feat(auth): configure Identity bearer and idempotent admin seed` |
| João | J03 | `feat(expenses): submit owned drafts with audited transitions` |
| João | J04 | `feat(api): map persistence conflicts and correlated errors` |
| João | J05 | `test(expenses): isolate rules and verify regression detection` |
| João | J06 | `test(persistence): verify migrations and repeated bootstrap` |
| João | J07 | `ci(demo): run HTTP scenarios and publish sanitized reports` |
| João | J08 | `docs(setup): document clean checkout validation and quality evidence` |
| Matheus | M01 | `feat(admin): replace user roles atomically` |
| Matheus | M02 | `feat(expenses): create and edit validated owned drafts` |
| Matheus | M03 | `feat(expenses): enforce authorized SQL queries` |
| Matheus | M04 | `feat(history): expose authorized revision-ordered events` |
| Matheus | M05 | `test(expenses): cover input boundaries and visibility rules` |
| Matheus | M06 | `test(persistence): verify data mappings and role rollback` |
| Matheus | M07 | `feat(docs): add Scalar with Identity bearer documentation` |
| Matheus | M08 | `docs(api): document request flows and endpoint verification` |
| André | A01 | `feat(domain): define expenses and revision-based audit records` |
| André | A02 | `feat(auth): enforce contextual expense permissions` |
| André | A03 | `feat(expenses): approve and reject submitted requests` |
| André | A04 | `feat(payments): persist unique audited expense payments` |
| André | A05 | `test(auth): cover state transitions and combined-role restrictions` |
| André | A06 | `test(persistence): verify concurrency and financial rollback` |
| André | A07 | `feat(demo): assert HTTP workflows and generate execution reports` |
| André | A08 | `docs(auth): document access rules and final participation checks` |

Mínimo de qualidade por commit: diff coeso, nomes/estilo da baseline, nenhuma credencial/artefato, autoria correta e comportamento explicado. Antes da PR, executar os checks aplicáveis à solução; um commit intermediário não é evidência de pacote concluído. A participação vem de código, testes, decisões e correções compreendidas por cada aluno, não apenas de aparecer numa tabela.

## 10. Cronograma e dependências

Ponto de partida em 03/10: ambiente completo e repositório público por template preparados; fundação validada localmente. Datas abaixo são metas, não registro de atividades realizadas. Os testes relevantes acompanham cada dia de implementação; resultados efetivos constam no registro de execução.

| Data/meta | João | Matheus | André | Condição de saída |
|---|---|---|---|---|
| 03/10 | J01: ambiente/contexto/migration; registrar base do grupo | Contratos M01–M03; store mínimo acordado | A01: modelos/estados; contrato inicial A02 | I01 compila e banco vazio migra; três colaboradores preparados. |
| 04/10 | J02: Identity/seed/bearer | M01: Admin/roles e negativos | A02: ator/guardas por ação | I02/I03 funcionais; cadastro sem escalada. |
| 05/10 | J03: submit; início J04 | M02: Draft/edição com testes | A02: ownership integrado; início A05 | I04 e submit corretos, sem escrita alheia. |
| 06/10 | J04: erros/conflitos; doubles mínimos J05 | M03: list/detail e SQL autorizado | A02/A05: matriz completa | I05/I06 verdes; roles combinadas e isolamento provados. |
| 07/10 | J06: fixture/schema/seed | M04: histórico; M05: fronteiras/consultas | A03: approve/reject e testes | I07 verde; histórico existente desde criação/edição. |
| 08/10 | Apoio pontual J04; leitura de findings próprios | M04: histórico completo; M06: integração | A04: pay; início A06 | Núcleo I01–I08 completo; sem pagamento/histórico parcial. |
| 09/10 | J05: auditoria I09; fechar J06 | M05/M06: fechar lacunas; início M07 | A05/A06: regressões e provas SQLite | Unitários significativos e integrações previstas passam. |
| 10/10 | J07: workflow de demo | M07: Scalar; início M08 | A07: script/relatórios; início A08 | Interface executa login; demo e CI extra geram evidências. |
| 11/10 | J08: clone limpo/corretor | M08: 13 endpoints e guia HTTP | A08: autorização/demo/autoria | Sem pendências funcionais; findings atribuídos e corrigidos. |
| 12/10 | Score/relatório da main e setup final | Revisão final dos contratos/documentação | Revisão final dos estados e dos três autores | SHA final validado, acesso público/professor e dois ou mais commits reais por aluno. |
| 13/10 | Entrega do SHA/URL acordados | Conferência da URL entregue | Conferência das evidências entregues | Entrega concluída; versão avaliada identificada. |

Sequência crítica: A01/J01 → J02/M01 → M02/J03/M03/A02 → A03 → A04/M04. J04, testes e higiene acompanham essa sequência. J06 libera M06/A06; API e M07 liberam A07; A07 libera J07. Não tratar dependência como permissão para merge de uma funcionalidade obrigatória sem suas guardas.

Se o núcleo não fechar em 08/10, replanejar os extras e reservar 11–12/10 para correção/validação. Nenhum integrante fica encarregado apenas de extras enquanto sua contribuição funcional ainda está pendente. Não iniciar filtros/paginação até todos os critérios obrigatórios e os extras escolhidos estarem concluídos.

## 11. Procedimento individual de autoria e auditoria

Cada aluno usa seu próprio clone/sessão e sua própria autenticação GitHub. Verificar o e-mail informado nas configurações da própria conta: o GitHub associa commits pelo endereço de autoria, conforme a [documentação de e-mail de commits](https://docs.github.com/en/account-and-profile/how-tos/email-preferences/setting-your-commit-email-address). O nome Git é identificação textual; autenticar um push não muda quem aparece como autor do commit.

Antes de trabalhar, no clone do próprio aluno:

```shell
git config --show-origin --get user.name
git config --show-origin --get user.email
git var GIT_AUTHOR_IDENT
git var GIT_COMMITTER_IDENT
```

Conferir se a identidade pertence ao próprio aluno. Se não pertencer, corrigir a configuração pessoal antes de criar novos commits. Não usar configuração de outro integrante, scripts de rotação de perfil, backdating ou amend de autoria para transformar trabalho individual em três participantes. Não solicitar ou compartilhar senha/token de outra conta.

Registrar na PR inicial o **SHA base do repositório do grupo**, imediatamente após sua criação por template e antes dos pacotes do grupo. O SHA upstream `58c840...` não serve como base de intervalo se o novo repositório não herdou aquele histórico. Importação do template não conta como uma entrega funcional do aluno.

Depois dos merges e antes da entrega, com a `main` final sincronizada, definir `EXPENSEHUB_BASE_SHA` para esse SHA registrado e executar:

```shell
: "${EXPENSEHUB_BASE_SHA:?Informe o SHA base registrado na PR inicial}"
git rev-parse HEAD
git shortlog -sne --no-merges "${EXPENSEHUB_BASE_SHA}..HEAD"
git log --no-merges --format='%h | %an <%ae> | %cn <%ce> | %s' "${EXPENSEHUB_BASE_SHA}..HEAD"
git log --no-merges --format='%B' "${EXPENSEHUB_BASE_SHA}..HEAD"
```

Esses comandos são de leitura e aplicam-se depois de existir o Git do grupo. Examinar author e committer separadamente: um merge feito pelo responsável da PR não substitui os commits de autoria dos alunos. Uma identidade diferente de committer pode decorrer do mecanismo de merge/rebase; analisar sua origem, sem acusar ou validar autoria automaticamente só por esse campo.

Checklist de A08, validado pelos três:

- Os três e-mails esperados aparecem em commits de autoria na `main`, além da importação/base do template.
- Cada integrante possui pelo menos dois commits próprios com mudanças reais, sem contar merge vazio ou mera importação.
- Abrir os commits no GitHub e conferir associação ao perfil correto; apenas encontrar um e-mail no log local não comprova esse vínculo.
- Os exemplos da seção 9 correspondem aos diffs reais; cada aluno explica sua implementação, seus testes e correções.
- Nenhuma mensagem de commit do intervalo contém trailer `Co-authored-by`; nenhuma conta de IA figura como autora das contribuições do grupo.
- Branches/PRs do grupo mantêm os commits individuais; não há squash coletivo apagando contribuições.
- O SHA exato da `main` coincide com o relatório oficial e com a evidência de demo entregue.

Se houver pendência de contribuição de um aluno, ela precisa ser resolvida com trabalho e commits próprios antes de fechar a entrega, ou com alinhamento do formato com o professor. Não corrigir contagem fabricando autor ou reaproveitando mudança alheia sob outro e-mail.

## 12. Critério de conclusão por pacote e fontes

Um pacote só termina quando o responsável atende seus critérios, executa checks pertinentes, registra os próprios commits, responde à revisão cruzada e vincula a evidência à PR. Usar estados simples no acompanhamento: não iniciado, em andamento, aguardando revisão, concluído. Este documento não marca nada concluído sem prova.

Validação comum: restore/build/test normais, qualidade da baseline, unitários isolados e ausência de segredo/artefato. Validar a integração SQLite quando a mudança atinge persistência, o schema/Scalar quando atinge documentação HTTP e a demo quando atinge seus cenários. Score do corretor e sucesso da demo são evidências distintas; integração não substitui I09.

Fontes de planejamento revalidadas em 03/10/2026 no SHA upstream `58c8405387f5e9ae9725ad964917e7634a870b74`: [enunciado](https://github.com/Racass/checkpoint-csharpracass-expensehub/blob/58c8405387f5e9ae9725ad964917e7634a870b74/docs/ENUNCIADO.md), [rubrica](https://github.com/Racass/checkpoint-csharpracass-expensehub/blob/58c8405387f5e9ae9725ad964917e7634a870b74/docs/RUBRICA.md), processo GitHub e política de IA citados acima. Os pesos oficiais somam 100%; as 120 unidades de esforço e todos os pacotes desta divisão são estimativas do grupo.

O [plano técnico](PLANO.md) define detalhes dos 13 endpoints, modelo, atomicidade, limites, testes, OpenAPI, demo e corretor. A pasta local já contém Git e a implementação inicial. Os caminhos de código deste documento são entregas previstas ou em execução; somente o registro de execução e evidências vinculadas permitem declará-las concluídas.
