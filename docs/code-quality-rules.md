# Regras do pipeline de qualidade

O pipeline produz uma pontuação independente de 0 a 100. Ela auxilia o
aluno e o professor, mas não altera automaticamente a nota acadêmica.
A pontuação só é calculada depois que existe ao menos um `.csproj` em
`sources/`; antes disso, o relatório usa o status `not-scored`.

## Categorias

Cada categoria vale 20 pontos:

1. Build
2. Segurança
3. Higiene do repositório
4. Estilo
5. Boas práticas

Uma regra desconta pontos uma única vez, mesmo quando possui várias
ocorrências. Todas as ocorrências continuam disponíveis no relatório.

## Tetos

| Condição | Pontuação máxima | Workflow |
|---|---:|---|
| Segredo detectado | 9 | Falha |
| Restore ou build falhou | 59 | Falha |
| Arquivo proibido rastreado | 59 | Falha |

Se mais de um teto for aplicável, prevalece o menor.

## Regras FIAP

| ID | Categoria | Desconto | Bloqueia | Descrição |
|---|---|---:|:---:|---|
| `FIAP1001` | Higiene | 20 | Sim | `bin`, `obj`, `.vs`, DLL, EXE, PDB ou estado local rastreado |
| `FIAP1002` | Segurança | 20 | Sim | Campo sensível com valor versionado |
| `FIAP2002` | Higiene | 4 | Não | `.gitignore` ausente |
| `FIAP2003` | Higiene | 2 | Não | README ausente |
| `FIAP2004` | Boas práticas | 2 | Não | Target framework fora do suporte |
| `FIAP2005` | Higiene | 2 | Não | Scaffolding `WeatherForecast` remanescente |
| `FIAP2006` | Higiene | 2 | Não | Dados de execução versionados |
| `FIAP2101` | Segurança | 5 | Não | Possível senha persistida em texto puro |
| `FIAP2102` | Segurança | 2 | Não | Possível segredo em arquivo local ainda não rastreado |
| `FIAP3001` | Boas práticas | 2 | Não | `CountAsync > 0` em vez de `AnyAsync` |
| `FIAP3002` | Boas práticas | 2 | Não | `Thread.Sleep` bloqueando a thread |
| `FIAP3003` | Boas práticas | 2 | Não | Consulta síncrona ao banco em controller |
| `FIAP3004` | Boas práticas | 2 | Não | Entidade recebida diretamente pela API |
| `FIAP3005` | Boas práticas | 2 | Não | DTO sem validação declarativa |
| `FIAP4001` | Boas práticas | 4 | Não | Projeto de teste existente falhou |

IDs `FIAP000x` não descontam pontos. Em execução local, eles são
informativos ou warnings. No CI, `FIAP0096` e `FIAP0097` bloqueiam o
workflow porque indicam que o Gitleaks não concluiu uma análise válida.

## Regras oficiais

- `IDE*`: 1 ponto na categoria Estilo por ID.
- `CA*`: 2 pontos na categoria Boas práticas por ID.
- `CA21*`, `CA30*`, `CA53*` e `CA54*`: 4 pontos na categoria Segurança
  por ID.
- Erros de compilação são cobertos pelo bloqueante de build.

O `.editorconfig` mantém uma baseline curada. Regras de alto ruído ou
baixo valor didático, como `ConfigureAwait` e logging de alta
performance, permanecem desabilitadas.

## O que não é automatizado

O pipeline não substitui revisão humana. Ele não avalia:

- aderência ao enunciado;
- correção de regras de negócio;
- arquitetura adequada ao problema;
- qualidade da experiência do usuário;
- plágio ou autoria;
- compreensão do aluno sobre o código.
