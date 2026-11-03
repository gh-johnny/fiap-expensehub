# Requisitos e contratos

## Perfis

| Role | Responsabilidade |
|---|---|
| `Employee` | Criar, editar, enviar e consultar os próprios reembolsos |
| `Approver` | Consultar enviados e aprovar ou reprovar reembolsos não próprios |
| `Finance` | Consultar aprovados/pagos e registrar pagamento não próprio |
| `Auditor` | Consultar todos os reembolsos e históricos, sem escrita |
| `Admin` | Consultar usuários e administrar roles |

Roles acumulam permissões, mas ninguém pode aprovar ou pagar o próprio reembolso.

## Usuários e seed

O seed de usuários deve criar apenas uma conta Admin e vinculá-la à role `Admin`.
Nenhum outro usuário pode ser criado pelo seed. As roles `Admin`, `Employee`,
`Approver`, `Finance` e `Auditor` devem existir para o funcionamento da aplicação.

Demais usuários devem ser criados por requisições HTTP ou HTTPS. O cadastro não pode aceitar roles.

O Admin deve atribuir ou remover roles por endpoints protegidos. Após uma alteração de role, o usuário deve autenticar novamente.

## Entidades mínimas

- `Expense`
- `ExpenseCategory`
- `ExpenseHistory`
- `PaymentRecord`

Cada reembolso possui um único valor. Não implemente coleção de itens.

## Campos e validações

| Campo | Regra |
|---|---|
| Identificador | Gerado pelo servidor |
| Proprietário | Obtido do usuário autenticado |
| Descrição | Obrigatória, entre 10 e 500 caracteres |
| Valor | Entre R$ 0,01 e `Int32.MaxValue` (R$ 2.147.483.647,00); usar `decimal` |
| Data da despesa | Válida e não futura |
| Estado | Definido exclusivamente pelo servidor |
| Justificativa | Obrigatória na reprovação, entre 10 e 500 caracteres |
| Atores e horários | Derivados da identidade e do servidor |

## Estados e transições

| Estado atual | Ação | Autorização | Próximo estado |
|---|---|---|---|
| inexistente | criar | Employee | `Draft` |
| `Draft` | editar | proprietário | `Draft` |
| `Draft` | enviar | proprietário | `Submitted` |
| `Submitted` | aprovar | Approver, exceto proprietário | `Approved` |
| `Submitted` | reprovar | Approver, exceto proprietário | `Rejected` |
| `Approved` | pagar | Finance, exceto proprietário | `Paid` |

`Rejected` e `Paid` são finais.

Não existem reabertura, cancelamento, exclusão ou reenvio. Repetir uma transição retorna conflito e não cria histórico duplicado.

## Endpoints obrigatórios

| Método e rota | Operação |
|---|---|
| `POST /register` | Registrar usuário sem role privilegiada |
| `POST /login` | Autenticar usuário |
| `GET /api/admin/users` | Listar usuários como Admin |
| `PUT /api/admin/users/{id}/roles` | Alterar roles como Admin |
| `POST /api/expenses` | Criar rascunho |
| `PUT /api/expenses/{id}` | Editar rascunho próprio |
| `GET /api/expenses` | Listar conforme o perfil |
| `GET /api/expenses/{id}` | Consultar detalhe visível |
| `POST /api/expenses/{id}/submit` | Enviar rascunho |
| `POST /api/expenses/{id}/approve` | Aprovar enviado |
| `POST /api/expenses/{id}/reject` | Reprovar com justificativa |
| `POST /api/expenses/{id}/pay` | Registrar pagamento |
| `GET /api/expenses/{id}/history` | Consultar histórico |

## Acesso por rota e perfil

`Sim*` significa que o perfil pode acessar a rota, mas a operação depende também
de ownership, estado atual ou visibilidade do recurso. Roles podem ser acumuladas;
cada coluna descreve a permissão concedida por aquela role.

| Método e rota | Employee | Approver | Finance | Auditor | Admin |
|---|:---:|:---:|:---:|:---:|:---:|
| `POST /register` | Sim | Sim | Sim | Sim | Sim |
| `POST /login` | Sim | Sim | Sim | Sim | Sim |
| `GET /api/admin/users` | Não | Não | Não | Não | Sim |
| `PUT /api/admin/users/{id}/roles` | Não | Não | Não | Não | Sim |
| `POST /api/expenses` | Sim | Não | Não | Não | Não |
| `PUT /api/expenses/{id}` | Sim* | Não | Não | Não | Não |
| `GET /api/expenses` | Sim* | Sim* | Sim* | Sim | Não |
| `GET /api/expenses/{id}` | Sim* | Sim* | Sim* | Sim | Não |
| `POST /api/expenses/{id}/submit` | Sim* | Não | Não | Não | Não |
| `POST /api/expenses/{id}/approve` | Não | Sim* | Não | Não | Não |
| `POST /api/expenses/{id}/reject` | Não | Sim* | Não | Não | Não |
| `POST /api/expenses/{id}/pay` | Não | Não | Sim* | Não | Não |
| `GET /api/expenses/{id}/history` | Sim* | Sim* | Sim* | Sim | Não |

As rotas `POST /register` e `POST /login` são públicas; a indicação `Sim` significa
que usuários desses perfis também podem acessá-las. Nas demais rotas, uma role
marcada como `Não` não concede acesso. Um usuário com múltiplas roles recebe a
união das permissões, sem eliminar as proibições de autoaprovação e autopagamento.

## Respostas HTTP

- `400 Bad Request`: entrada inválida;
- `401 Unauthorized`: credencial ausente ou inválida;
- `403 Forbidden`: autenticado sem permissão para a operação;
- `404 Not Found`: recurso inexistente ou fora do escopo de leitura;
- `409 Conflict`: transição incompatível ou repetida.

Use `ProblemDetails` ou estrutura equivalente. Mensagens humanas podem variar, mas status e contrato devem ser consistentes.

## Persistência

Escolha um provider relacional compatível com Entity Framework Core:

- Microsoft SQL Server LocalDB;
- Oracle Database;
- SQLite;
- outro provider documentado.

A escolha não pontua. A aplicação deve persistir dados, e o procedimento de configuração deve estar documentado.

## Histórico

Registrar:

- ação;
- reembolso;
- ator;
- instante em Universal Time Coordinated (UTC);
- estado anterior;
- estado posterior;
- justificativa da reprovação;
- alterações realizadas enquanto o reembolso estava em Draft.

A alteração e o histórico correspondente devem ser persistidos na mesma operação lógica.
