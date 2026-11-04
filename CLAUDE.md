# CLAUDE.md

As regras deste repositório estão no `AGENTS.md`, importado abaixo. Ele é a referência única:
não duplique regras aqui.

@AGENTS.md

## Lembretes para o Claude Code

- Sem prazos nem datas em nenhum artefato (condição 1 do `AGENTS.md`).
- Toda entrega traz um extra, roda em container, tem target no `Makefile`, mantém a cobertura unitária
  ≥ 95 %, atualiza a *Matriz de testes* e o `README.md` no mesmo PR.
- Comandos .NET rodam via `make` (containers), nunca com o SDK do host.
- Commits sem `Co-authored-by`; o integrante que pediu a mudança é o autor.
