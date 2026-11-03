# Enunciado — ExpenseHub

## Cenário

Uma empresa precisa controlar solicitações de reembolso. Funcionários registram despesas, aprovadores analisam os pedidos, o setor financeiro registra pagamentos e auditores consultam todo o histórico.

O sistema deve proteger cada operação considerando:

- identidade autenticada;
- perfil do usuário;
- proprietário do reembolso;
- estado atual;
- regra de transição.

## Formato

- Trabalho em grupos de até 3 pessoas.
- Prazo: 13 de outubro de 2026.
- Entrega em repositório público criado por **Use this template**.
- Os commits serão utilizados como parte da avaliação da participação dos alunos.
- Todos os integrantes do grupo devem possuir mais de um commit no repositório.
- Inteligência Artificial (IA) permitida.
- Sem deploy obrigatório.
- Frontend opcional.

## Objetivo técnico

Construir uma API REST (Representational State Transfer) com:

- ASP.NET Core;
- ASP.NET Core Identity;
- Entity Framework Core;
- banco relacional;
- autenticação bearer;
- autorização por roles;
- regras de ownership no serviço;
- testes unitários.

## Entrega esperada

O repositório deve:

- compilar;
- executar conforme as instruções;
- implementar as issues do backlog central;
- possuir testes unitários significativos;
- passar pelo pipeline de qualidade;
- não conter segredos, binários ou artefatos locais.

## Fora de escopo

- deploy;
- Docker;
- anexos;
- leitura automática de comprovantes;
- integração bancária;
- múltiplas moedas;
- aprovação multinível;
- notificações;
- reabertura ou cancelamento;
- frontend obrigatório;
- testes de integração obrigatórios.

Consulte [REQUISITOS.md](REQUISITOS.md) para o contrato funcional e [RUBRICA.md](RUBRICA.md) para a pontuação.
