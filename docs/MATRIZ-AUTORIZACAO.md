# Matriz de autorização

Esta matriz é parte do contrato. Atributos de autorização por role devem ser combinados com validações de ownership e estado na camada de serviço.

| Operação | Employee | Approver | Finance | Auditor | Admin | Regra contextual |
|---|:---:|:---:|:---:|:---:|:---:|---|
| Registrar usuário | público | público | público | público | público | Cadastro nunca aceita role |
| Login | público | público | público | público | público | Credenciais válidas |
| Listar usuários | não | não | não | não | sim | Somente Admin |
| Alterar roles | não | não | não | não | sim | Roles conhecidas; não remover a própria role Admin |
| Criar reembolso | sim | não* | não* | não | não* | Role Employee; proprietário vem do token |
| Editar reembolso | sim | não* | não* | não | não* | Proprietário e estado Draft |
| Enviar reembolso | sim | não* | não* | não | não* | Proprietário e estado Draft |
| Listar reembolsos | próprios | Submitted | Approved/Paid | todos | não | Aplicar filtro antes de materializar dados |
| Consultar detalhe | próprio | Submitted | Approved/Paid | todos | não | Recurso fora do escopo retorna 404 |
| Aprovar | não | sim | não | não | não | Submitted e não proprietário |
| Reprovar | não | sim | não | não | não | Submitted, não proprietário e justificativa válida |
| Pagar | não | não | sim | não | não | Approved e não proprietário |
| Consultar histórico | próprio | visível | visível | todos | não | Mesma visibilidade do reembolso |

`*` Um usuário pode acumular roles. A permissão existe apenas se ele também possuir `Employee`.

## Casos negativos obrigatórios

- requisição anônima em rota protegida retorna `401`;
- usuário autenticado sem role retorna `403`;
- Employee não acessa reembolso de outro Employee;
- Employee não informa ou altera o proprietário;
- usuário não informa estado, ator ou horário;
- Approver não aprova nem reprova o próprio reembolso;
- Finance não paga o próprio reembolso;
- Auditor não altera dados;
- Admin não recebe acesso funcional apenas por ser Admin;
- transição fora do estado esperado retorna `409`;
- recurso fora do escopo de leitura retorna `404`.

## Regras de segurança

- Nunca confiar em `userId`, role, estado, ator ou timestamp enviados pelo cliente.
- Não usar apenas ocultação de rotas ou filtros no frontend como autorização.
- Não carregar todos os registros para depois filtrar em memória.
- Não permitir alteração direta de estado por um endpoint genérico de edição.
- Não armazenar senha em texto puro.
