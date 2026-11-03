# Rubrica

A nota base é a soma das dez issues. Critérios parcialmente atendidos recebem pontuação proporcional quando ainda produzem evidência válida.

| Issue | Conteúdo | Peso |
|---|---|---:|
| I01 | Fundação da solução e Entity Framework Core | 4% |
| I02 | Identity, Admin e autenticação | 9% |
| I03 | Cadastro HTTP e gerenciamento de roles | 8% |
| I04 | Criar e editar rascunho | 7% |
| I05 | Enviar, listar e consultar | 7% |
| I06 | Ownership e matriz de acesso | 10% |
| I07 | Aprovar e reprovar com justificativa | 12% |
| I08 | Pagamento e histórico | 8% |
| I09 | Testes unitários | 10% |
| I10 | Qualidade de Código | 25% |
| **Total** |  | **100%** |

## I01 — Fundação — 4%

- solução, projetos e referências coerentes: 1%;
- provider, contexto e pacotes de Entity Framework Core: 1%;
- mapeamentos e persistência relacional: 1%;
- migrations ou procedimento equivalente e documentação de execução: 1%.

## I02 — Identity, Admin e autenticação — 9%

- Identity persistido e configurado: 2%;
- autenticação bearer e login funcional: 2%;
- roles obrigatórias e seed idempotente: 2%;
- somente conta Admin inicial e senha fora do código-fonte: 2%;
- rotas protegidas e respostas 401/403 coerentes: 1%.

## I03 — Cadastro e roles — 8%

- registro por HTTP/HTTPS sem role informada pelo cliente: 2%;
- listagem de usuários restrita a Admin: 2%;
- atribuição e remoção apenas de roles conhecidas: 2%;
- proteção contra remoção da própria role Admin e tratamento de erros: 2%.

## I04 — Criar e editar — 7%

- criação em Draft com proprietário do token: 2%;
- DTOs e validações de entrada: 2%;
- edição somente pelo proprietário e somente em Draft: 2%;
- respostas e persistência consistentes: 1%.

## I05 — Enviar, listar e consultar — 7%

- transição Draft para Submitted: 2%;
- listagem filtrada conforme perfil: 2%;
- detalhe sem vazamento de informação: 2%;
- conflitos e recursos inexistentes tratados corretamente: 1%.

## I06 — Ownership e matriz — 10%

- isolamento completo entre Employees: 3%;
- proibição de autoaprovação e autopagamento: 2%;
- filtros de Approver, Finance e Auditor: 2%;
- aplicação combinada de role, ownership e estado na camada adequada: 2%;
- respostas 401/403/404 coerentes: 1%.

## I07 — Aprovar e reprovar — 12%

- aprovação válida de Submitted para Approved: 3%;
- reprovação válida de Submitted para Rejected: 3%;
- justificativa obrigatória e persistida: 2%;
- bloqueio de ator indevido, proprietário e estado inválido: 2%;
- idempotência comportamental e conflito em repetição: 2%.

## I08 — Pagamento e histórico — 8%

- pagamento válido de Approved para Paid: 2%;
- registro de pagamento com ator e horário do servidor: 2%;
- histórico das ações e alterações relevantes: 2%;
- atomicidade lógica e consulta autorizada do histórico: 2%.

## I09 — Testes unitários — 10%

- regras de estado e transições: 2%;
- ownership e casos negativos de autorização contextual: 3%;
- aprovação, reprovação, justificativa e pagamento: 2%;
- histórico e validações: 1%;
- isolamento, legibilidade e capacidade de detectar regressões: 2%.

Testes de integração, end-to-end, interface, cobertura percentual e quantidade bruta de métodos não geram pontos.

## I10 — Qualidade de Código — 25%

Usar a pontuação 0–100 do relatório do workflow `code-quality`:

```text
pontos de I10 = score / 100 × 2,5
```

| Score | Pontos na nota |
|---:|---:|
| 100 | 2,50 |
| 90 | 2,25 |
| 80 | 2,00 |
| 60 | 1,50 |
| 0 | 0,00 |

A nota integral exige relatório 100, sem erros, warnings ou findings com desconto. Warnings não são convertidos em erros de compilação; eles reduzem o score conforme [code-quality-rules.md](code-quality-rules.md).

Alterar workflow, analisadores, `.editorconfig`, `Directory.Build.props`, scripts ou escopo para inflar a pontuação pode zerar I10.

## Gates

Depois de calcular a nota e a eventual recuperação, aplicar o menor teto válido:

| Condição | Nota máxima |
|---|---:|
| Projeto não compila no ambiente oficial | 2,0 |
| Autorização sistematicamente ausente | 4,0 |
| Sem testes unitários próprios significativos | 8,0 |

Falha do template, do ambiente oficial ou do corretor não gera gate para o aluno.

## Frontend opcional

Um frontend funcional pode recuperar até 1 ponto de perdas funcionais elegíveis.

Ele:

- não ultrapassa nota 10;
- não ultrapassa gates;
- deve integrar dados reais, tratar erros e possuir instruções;
- deve ser demonstrado em gravação curta anexada à pull request final.

Interface estática, dados simulados ou estética isolada não pontuam.

## Fórmula

```text
nota preliminar = soma das issues + recuperação elegível
nota limitada = mínimo(10, nota preliminar)
nota final = mínimo(nota limitada, menor gate aplicável)
```
