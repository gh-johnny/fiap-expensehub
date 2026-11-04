---
title: Demonstração automatizada do ExpenseHub
status: internal
created: 2026-10-03
last-updated: 2026-10-03
last-reviewed: 2026-10-03
---

# Demonstração automatizada do ExpenseHub

| Integrante | RM |
|---|---|
| João Marcelo Furtado Romero | RM555199 |
| Matheus Rivera Montovaneli | RM555499 |
| André Nakamatsu Rocha | RM555004 |

Guia para executar e conferir a API com requisições reais. Fontes: [demo HTTP](../scripts/Invoke-ExpenseHubDemo.ps1), [executor](../scripts/Invoke-ExpenseHubVerification.ps1), [workflow adicional](../.github/workflows/demo.yml) e [registro de execução](EXECUCAO.md). As provas SQLite específicas continuam no projeto de integração; Scalar também foi verificado em navegador local.

## Execução completa

Pré-requisitos: SDK .NET 10 completo com runtime ASP.NET Core 10, PowerShell 7 e acesso aos pacotes NuGet. Executar na raiz do repositório:

```powershell
pwsh -NoProfile -File ./scripts/Invoke-ExpenseHubVerification.ps1
```

O executor restaura a ferramenta dotnet-ef e a solução, compila, executa os 111 unitários e oito testes SQLite, aplica migrations a um arquivo temporário exclusivo, inicia a API em Development e chama a demo. Porta local é selecionada durante a execução; a prontidão é verificada por /health. O Admin inicial recebe uma credencial aleatória gerada em memória. A configuração é passada pelo ambiente, sem argumentos com senhas. Em GitHub Actions, as credenciais geradas são registradas em add-mask antes das requisições.

O executor encerra somente seu processo de API, restaura as variáveis de ambiente e apaga seu diretório temporário ao concluir. Não usa o banco padrão da aplicação. Logs do host e resultados temporários de testes não entram no artefato de demonstração.

## Conferência do resultado

Os arquivos `artifacts/demo/report.json` e `artifacts/demo/report.md` contêm SHA informado, resultado, cenários, status esperado/observado, duração e traceId dos erros. O JSON agrega contagens em `summary`. Na execução local sem SHA explícito, `commit` informa `working-tree`; isso não comprova um commit publicado. Para uma revisão identificada, passar `-CommitSha` com o SHA realmente em execução.

```powershell
$report = Get-Content ./artifacts/demo/report.json -Raw | ConvertFrom-Json
$report | Select-Object commit, status, summary
```

Sucesso exige saída de processo zero, `status=passed` e `summary.failed=0`. A versão atual executa **104 cenários HTTP**, com assertivas adicionais de dados e histórico. Falha interrompe a demo e retorna saída não zero. Se restore/build/test/migration/startup falhar, o executor produz relatório de bootstrap com fase identificada, sem declarar cenários HTTP concluídos. Cada execução substitui os dois relatórios anteriores.

## Cobertura da demo

- Contrato OpenAPI: 13 rotas obrigatórias, IDs únicos, bearer nas rotas protegidas, limites e formato de data.
- Cadastro, login opaco e atribuição administrativa: roles injetadas ignoradas, novo login após mudança, token antigo sem os privilégios novos, role desconhecida/nula e remoção do próprio Admin bloqueadas.
- Isolamento por proprietário/perfil, ausência e invalidez de bearer, Admin sem acesso implícito a despesas e Auditor sem ação de escrita.
- Draft: campos definidos pelo servidor, edição com snapshots, edição idêntica sem evento novo, descrição/valor/data inválidos sem persistência.
- Fluxo Draft → Submitted → Approved → Paid, histórico com revisão crescente e UTC, perda de leitura do Approver após decidir, repetição 409 e estado final.
- Fluxo Draft → Submitted → Rejected, justificativa normalizada e inválida, repetição 409 e estado final.
- Conta com todas as roles: criação e envio permitidos, autoaprovação/autorreprovação/autopagamento proibidos, decisão e pagamento por terceiros.

Os perfis adicionais são cadastrados por HTTP durante a demo; o seed da API continua criando somente o Admin. Nomes de contas usam um identificador de execução. O teste completo usa banco novo; chamar apenas a demo em servidor existente modifica esse banco, acrescentando contas e despesas de teste.

## Usar um host de teste existente

O host deve ter migrations aplicadas, Admin inicial conhecido e ambiente Development para o documento OpenAPI. Configurar `Seed__AdminEmail` e `Seed__AdminPassword` externamente com as credenciais atuais desse Admin, então executar:

```powershell
pwsh -NoProfile -File ./scripts/Invoke-ExpenseHubDemo.ps1 -BaseUrl http://localhost:5000
```

Esse comando não inicia nem encerra o host e não limpa o banco existente. Preferir o executor completo para isolamento. Senhas, tokens, headers e corpos HTTP não são gravados nos relatórios. Os artefatos estão ignorados pelo Git.

## Arquivo HTTP manual

[ExpenseHub.Api.http](../sources/ExpenseHub.Api/ExpenseHub.Api.http) contém as 13 rotas obrigatórias e os dois fluxos completos, para a extensão [REST Client do VS Code](https://github.com/Huachao/vscode-restclient#request-variables). Variáveis do processo do editor fornecem a configuração; respostas nomeadas de login fornecem os tokens. Enviar cada login antes das requisições que referenciam seu accessToken, e enviar cada criação antes das ações que referenciam seu ID.

| Variáveis de ambiente | Conteúdo |
|---|---|
| EXPENSEHUB_BASE_URL | URL do host de teste, sem barra final. |
| Seed__AdminEmail, Seed__AdminPassword | Credenciais atuais do Admin inicial. |
| EXPENSEHUB_ACCOUNT_EMAIL, EXPENSEHUB_ACCOUNT_PASSWORD | Conta a cadastrar; senha externa compartilhada pelos três perfis deste exemplo de teste. |
| EXPENSEHUB_ACCOUNT_ID, EXPENSEHUB_ACCOUNT_ROLE | ID retornado pela listagem Admin e role a conceder. |
| EXPENSEHUB_EMPLOYEE_EMAIL, EXPENSEHUB_APPROVER_EMAIL, EXPENSEHUB_FINANCE_EMAIL | Emails de três contas distintas. |
| EXPENSEHUB_EXPENSE_DATE | Data ISO yyyy-MM-dd, sem ser futura no calendário UTC. |

Preparar Employee, Approver e Finance repetindo cadastro, listagem Admin e atribuição para cada conta, com as variáveis correspondentes. Depois, executar os três logins nomeados e as requisições na ordem: criar/editar/listar/ler/enviar/aprovar/pagar/histórico; criar outra despesa/enviar/reprovar/histórico. As variáveis devem estar disponíveis ao processo do editor. Nenhuma senha/token precisa ser inserida no arquivo. A conferência local enviou 26 requisições reais a partir dos templates e das referências de resposta; a interface da extensão não foi automatizada.

## Workflow e alcance das provas

O workflow `expensehub-demo` roda em push, pull request e disparo manual. Faz checkout do SHA da implementação, executa o mesmo script e publica `expensehub-demo-report` por 30 dias, inclusive em falha. Em PR, relatório e checkout usam o SHA da branch de origem. O workflow oficial `code-quality` permanece intacto e fornece o score separadamente.

A demo HTTP não é teste de navegador. A prova de login e consulta protegida pela interface Scalar foi realizada em Chromium local e está descrita no registro de execução. Constraints, concorrência e rollback por falhas controladas são conferidos pelos oito testes de integração SQLite, não por status HTTP isolado.
