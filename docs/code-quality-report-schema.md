# Schema do relatório de qualidade

O arquivo `artifacts/code-quality/report.json` é a fonte de dados para
integrações e para o futuro visualizador HTML.

O schema formal está em
[`code-quality-report.schema.json`](code-quality-report.schema.json).

## Versão

`schemaVersion` utiliza versionamento próprio. A versão atual é `2.0`.

- Alteração compatível: novo campo opcional.
- Alteração incompatível: remoção, renomeação ou mudança de tipo exige
  nova versão principal.

## Estrutura

| Campo | Uso |
|---|---|
| `repository` | Raiz, branch e commit analisados |
| `status` | `passed`, `failed` ou `not-scored` |
| `score` | Pontuação bruta, final, categorias e tetos |
| `summary` | Contagens consolidadas |
| `tools` | Versões do .NET e Gitleaks |
| `projects` | Resultado de restore, build e format por projeto |
| `findings` | Diagnósticos normalizados |

Quando não existe nenhum `.csproj` em `sources/`, `status` é
`not-scored`, `score.raw` e `score.final` são `null` e as categorias
também não recebem nota. Findings bloqueantes ainda podem tornar o
status `failed`, mesmo sem projetos.

## Finding

Cada finding representa uma combinação de regra, arquivo e mensagem.
Ocorrências repetidas no mesmo arquivo são agrupadas:

```json
{
  "id": "IDE0011",
  "source": "analyzer",
  "category": "style",
  "severity": "warning",
  "blocking": false,
  "title": "Diagnóstico IDE0011",
  "message": "Add braces to 'if' statement.",
  "file": "sources/aula-01/Program.cs",
  "line": 10,
  "column": 1,
  "project": "sources/aula-01/Aula01.csproj",
  "occurrenceCount": 3,
  "locations": [
    { "line": 10, "column": 1 },
    { "line": 18, "column": 1 },
    { "line": 26, "column": 1 }
  ],
  "recommendation": "Consulte a mensagem e ajuste o código.",
  "documentationUrl": "https://learn.microsoft.com/..."
}
```

O HTML deve usar `findings` como a coleção principal e pode agrupar por
categoria, regra, projeto ou arquivo sem analisar logs de texto.

## Compatibilidade com LLMs

O arquivo `report.md` contém os mesmos resultados em formato textual
determinístico. Ele é mais adequado para anexar a uma conversa com uma
LLM porque explicita pontuação, bloqueantes, arquivos e recomendações.
