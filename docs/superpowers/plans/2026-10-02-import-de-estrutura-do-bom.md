# Import da estrutura a partir do BOM do CAD — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deixar quem monta a estrutura enviar o BOM indentado de uma montagem a partir da página do Agrupamento, conferir a proposta numa tela que casa com o catálogo, pede os sólidos e resolve as receitas divergentes, e confirmar numa transação que grava o catálogo e cria a Peça.

**Architecture:** O arquivo é lido no servidor. Um leitor por formato (CSV, XLSX), na `Infrastructure`, produz linhas cruas, e uma função pura na `Application` as transforma em **receitas por código**, gravadas em três tabelas de rascunho. O estado da conferência (árvore final, situações, comparativos, bloqueios) **não é gravado**: um avaliador puro o recalcula a cada leitura, sobrepondo o rascunho ao catálogo e expandindo pelo `PlanejadorDeCopia` que já existe. Os Componentes novos entram nessa sobreposição com **Id provisório negativo**. A confirmação usa o mesmo avaliador e grava catálogo e Peça a partir do plano em memória, sem reler o catálogo dentro da transação. No front, a página do Agrupamento ganha a entrada e a lista de rascunhos, e uma tela nova, `/importacoes/:id`, faz a conferência.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit, `DocumentFormat.OpenXml` (XLSX); React + TypeScript (Vite), React Router, Vitest + Testing Library + jsdom, Tailwind.

**Spec:** `docs/superpowers/specs/2026-10-02-import-de-estrutura-do-bom-design.md`, aprovada em 2026-10-02 (decisões D1–D17). Branch: `claude/eloquent-ritchie-ubtsh8`, sobre `c329d6b`. Os ajustes de `specs/06` e `CLAUDE.md` que oficializam a ordem (seção 11 da spec, "antes do plano") já estão em `bc43b3a`.

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass de Critical/Important → re-review). Única dispensa prevista: a **Task 12**, verificação no navegador, cujo produto é relatório. A justificativa vai escrita **antes** no ledger e no relatório. A Task 11 (documentação) **tem** review.
- **Base de cada task registrada ANTES de despachar o implementer** (SHA), e o pacote de review gerado com `scripts/review-package BASE HEAD`, nunca com `HEAD~1`. Ledger do plano em `.superpowers/sdd/2026-10-02-import-do-bom/`.
- **Schema muda, pelo caminho do `CLAUDE.md`:** `specs/02-modelo-de-dados.sql` primeiro, depois `db/alter-importacao-bom.sql` idempotente (formato de `db/alter-fase-3d.sql`: `-b -f 65001`), depois o mapeamento EF. Nada de `Add-Migration` nem `EnsureCreated`. `Componente`, receita padrão e `EstruturaItem` **não mudam**.
- Perfis de escrita de todas as rotas novas: `"PCP,Administrador"`, a mesma constante `PerfisDeEscrita` do `EstruturaController`. No front, o recurso `estrutura` de `web/src/auth/permissoes.ts`.
- Nomes de domínio em português, espelhando o DDL; nomes técnicos em inglês.
- **Citação por nome**, nunca `arquivo.ext:NN` nem distância relativa. Comentário de código **não cita** ledger, brief nem "Task N": o repositório é público. As decisões abaixo podem ser citadas como "decisão Pn do plano do import", e as da spec como "D*n* da spec do import".
- **O BOM pessoal do usuário não entra no repositório** (D17). Fixtures são sintéticas, escritas pelo teste.
- Backend: `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com `dotnet test Rastreamento.slnx -m:1`. Teste de banco **escopa a asserção** nas linhas que ele inseriu (prefixo por teste). Teste de Infrastructure que escreve em `dbo.Componente` entra em `[Collection(ColecaoQueEscreveEmComponente.Nome)]`.
- Front: `npm test -- --run` **e** `npm run build`. Primitivas de `web/src/components/`, cores só por token, `// @vitest-environment jsdom` + `afterEach(cleanup)`, mocks por `web/src/testes/api.ts`. Teste acha elemento por papel e nome acessível; `data-testid` só onde a seção "Interface" do `CLAUDE.md` admite.
- Texto de tela em português **com** acento. Mensagens de erro do backend seguem o padrão do arquivo vizinho (ASCII sem acento nos casos de uso), e a tela não as mostra cruas quando o padrão do vizinho é traduzir (ver o comentário de `enviarSolido`).
- `git pull` antes de todo commit, nos dois repositórios. No ledger, `git add` por caminho explícito, em chamada separada do `task-brief`/`review-package`.

## Bancada

```bash
bash scripts/estado
bash scripts/backend-na-nuvem              # na nuvem; local: docker compose up -d
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
cd web && npm ci && npm test -- --run && npm run build
```

**Baseline:** o ledger registra, medido em 2026-10-02 na máquina local, backend **979** (Api 310 · Application 511 · Infrastructure 158) e front **1139 testes / 68 arquivos**. **O pré-flight remede nesta máquina antes da Task 1** e escreve o número no ledger do plano. Cada implementer mede o delta da própria task.

## Decisões deste plano (onde a spec deixou a escolha)

- **P1 — Id provisório negativo.** Na sobreposição rascunho + catálogo, um registro casado usa o `ComponenteId`, e um registro "criar novo" usa `-registro.Id`. O `PlanejadorDeCopia` trabalha sobre `int` e não distingue os dois. Materiais e roteiro de Id negativo saem vazios, como os de um Componente sem receita.
- **P2 — Receita efetiva de cada código**, a que entra na sobreposição: a **lida** se o código é novo, se é casado e não diverge, ou se diverge com escolha `Importada`; a **do catálogo** se diverge com escolha `Catalogo`; a **lida, provisoriamente**, se diverge sem escolha. Nesse último caso a árvore mostrada é a do BOM e o bloqueio `DivergenciaSemEscolha` impede a confirmação. Ids alcançados só pelo catálogo usam a receita do catálogo.
- **P3 — Só o que está na árvore final é gravado.** A confirmação cria, reativa e grava receita **apenas** para os registros alcançados a partir da raiz na sobreposição. Um código que saiu por uma escolha `Catalogo` acima dele não vira Componente.
- **P4 — O estado é calculado, nunca gravado.** `AvaliadorDeImportacao` é puro e serve ao `GET` e à confirmação. O ciclo, a profundidade e o tamanho vêm do próprio `PlanejadorDeCopia` sobre a sobreposição, e por isso o ciclo no catálogo (seção 5.4 da spec) é detectado sem código novo de travessia: toda aresta sobreposta tem o pai alcançável da raiz, então um ciclo novo passa por ela.
- **P5 — A confirmação lê o catálogo fora da transação e planeja em memória** (a segunda saída da seção 7 da spec). Dentro da transação ela relê **só** a receita dos pais com escolha, para conferir a impressão, e grava. Isso é leitura por faixa estreita (`UQ_ComponenteFilhoPadrao` começa por `ComponentePaiId`), não a varredura de `LerReceitaCompletaAsync`. Os Ids alcançados só pelo catálogo têm o mesmo residual de `CriarPeca` hoje: uma mudança concorrente entre a leitura e o commit não é vista. A Task 7 mede.
- **P6 — Sem `ON DELETE CASCADE`.** As FKs entre as três tabelas, incluindo a circular `RaizId`, são `NO ACTION`. `ExcluirAsync` apaga na ordem filhos → `RaizId = NULL` → registros → cabeçalho → `ArquivoDeComponente` pendentes, numa transação. Isso evita os múltiplos caminhos de cascata que o SQL Server recusa.
- **P7 — `AtualizadoEm` no cabeçalho.** É uma coluna além do esboço da spec. Toda escrita no rascunho a atualiza, e é isso que faz o `ROWVERSION` do cabeçalho mudar quando só um registro ou um filho mudou. Ela também aparece na lista de rascunhos. A Task 11 acrescenta a coluna ao esboço da seção 3 da spec.
- **P8 — Raiz.** A raiz é um registro com `CodigoLido = NULL`, `DescricaoLida` igual ao nome do arquivo sem extensão e "criar novo" com código em branco, a menos que o arquivo traga o código. No reimport, a raiz é **sempre** o mesmo registro: casamento, dados do novo e sólido pendente preservados.
- **P9 — Cabeçalhos de coluna por tabela de apelidos.** Uma classe só, `ColunasDoBom`, normaliza o cabeçalho (maiúsculas, sem acento, sem pontuação, espaços colapsados) e procura nos apelidos. Os apelidos provisórios cobrem o SolidWorks em português e em inglês: `N DO ITEM`/`ITEM NO`/`ITEM`, `N DA PECA`/`PART NUMBER`/`NUMERO DA PECA`, `DESCRICAO`/`DESCRIPTION`, `QTD`/`QTY`/`QUANTIDADE`. Quando o BOM real chegar, a emenda mexe só nesta classe.
- **P10 — Quantidade.** Aceita `1,5` e `1.5`, sem separador de milhar. Uma célula numérica do XLSX vem como número. A faixa vale de `PlanejadorDeCopia.QuantidadeMinimaDaColuna` a `QuantidadeMaximaDaColuna`, com no máximo 4 casas decimais.
- **P11 — Limite do arquivo BOM: 5 MiB**, `LeitorDeBom.TamanhoMaximoEmBytes`, com `[RequestSizeLimit]` mais a margem multipart, no molde de `ComponentesController.EnviarSolido`. O front recusa antes de enviar, no mesmo padrão de `TAMANHO_MAXIMO_DO_SOLIDO_EM_BYTES`.
- **P12 — XLSX com `DocumentFormat.OpenXml`** (Microsoft, MIT, sem dependência transitiva de terceiros). `ClosedXML` foi descartada porque puxa dependências de licença diferente. Lê a **primeira** planilha. CSV: separador `;` ou `,`, detectado pela linha de cabeçalho. Codificação UTF-8 com BOM, senão Windows-1252, com `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`. Aspas no padrão RFC 4180.
- **P13 — Erro do arquivo volta como uma `mensagem` só, uma linha por erro** (`"Linha 7: quantidade invalida."`, separadas por `\n`), no `{ erro, mensagem }` que o `EstruturaController` já produz. O front quebra por `\n` e mostra uma lista. Assim nenhum contrato de erro novo é criado.
- **P14 — A versão do rascunho viaja como base64** (o `byte[]` do `ROWVERSION`, serializado pelo `System.Text.Json`). Toda escrita a manda no corpo (`versao`, ou o campo de formulário `versao` no multipart). Versão diferente da do banco → 409 `ImportacaoDesatualizada`.
- **P15 — Ao confirmar com bloqueio**, a resposta é 400 `{ erro: "ImportacaoComBloqueios" }`, sem a lista. A tela relê o `GET`, que traz a lista. Isso evita que a lista exista em dois contratos.

## Contrato HTTP novo (fonte para o front)

```text
POST   /agrupamentos/{id}/importacoes             multipart: arquivo   → 201 ImportacaoDto
       400 { erro: "BomInvalido", mensagem: "Linha 3: ...\nLinha 9: ..." }   404 Agrupamento
GET    /agrupamentos/{id}/importacoes             → 200 ResumoDeImportacaoDto[]
GET    /importacoes/{id}                          → 200 ImportacaoDto                 404
PUT    /importacoes/{id}                          { versao, quantidadeDaPeca, requerRelatorioDimensional } → 200 ImportacaoDto
PUT    /importacoes/{id}/componentes/{cid}        { versao, componenteId | null, codigoNovo, descricaoNova, tipoNovo, escolhaDeReceita } → 200 ImportacaoDto
PUT    /importacoes/{id}/filhos/{fid}             { versao, quantidade } → 200 ImportacaoDto
POST   /importacoes/{id}/componentes/{cid}/solido multipart: arquivo, versao → 200 ImportacaoDto
GET    /importacoes/{id}/componentes/{cid}/solido → 200 octet-stream (o pendente)    404 sem pendente
POST   /importacoes/{id}/arquivo                  multipart: arquivo, versao → 200 ImportacaoDto   400 BomInvalido
DELETE /importacoes/{id}                          → 204
POST   /importacoes/{id}/confirmacao              { versao } → 201 EstruturaItemDto (a Peça)
       400 { erro: "ImportacaoComBloqueios" }
       409 { erro: "ReceitaDoCatalogoMudou", mensagem }   409 { erro: "ImportacaoDesatualizada" }
DELETE /agrupamentos/{id}                         → 409 { erro: "AgrupamentoComImportacao" } quando houver rascunho
```

```text
ImportacaoDto { id, agrupamentoId, nomeDoArquivo, criadoPor, criadoEm, atualizadoEm, versao,
                quantidadeDaPeca: number|null, requerRelatorioDimensional,
                raiz: NoDaImportacaoDto|null, componentes: SituacaoDoComponenteDto[], bloqueios: BloqueioDto[] }
NoDaImportacaoDto { registroId: number|null, componenteId: number|null, filhoId: number|null,
                    codigo, descricao, quantidadePorPai: number|null, origem: "Bom"|"Catalogo",
                    pendencias: ("Novo"|"Inativo"|"Divergente"|"SemSolido")[], filhos: NoDaImportacaoDto[] }
SituacaoDoComponenteDto { registroId, codigoLido, descricaoLida, componenteId, codigoDoCatalogo,
                    descricaoDoCatalogo, tipo, ativo, temSolido, temSolidoPendente,
                    nomeDoSolido, tamanhoDoSolidoEmBytes,
                    codigoNovo, descricaoNova, tipoNovo, divergente, escolhaDeReceita: "Catalogo"|"Importada"|null,
                    comparativo: { codigo, descricao, noCatalogo: number|null, noBom: number|null,
                                   situacao: "Igual"|"QuantidadeMuda"|"Entra"|"Sai" }[],
                    efeitoDeManterCatalogo: { retira, traz } | null, naArvoreFinal }
BloqueioDto { tipo: "SemSolido"|"DivergenciaSemEscolha"|"CodigoVazio"|"CodigoJaExiste"|"QuantidadeDaPecaAusente"
                    |"CicloNaReceita"|"EstruturaProfundaDemais"|"EstruturaGrandeDemais",
              registroId: number|null, componenteId: number|null, mensagem }
ResumoDeImportacaoDto { id, nomeDoArquivo, criadoPor, criadoEm, atualizadoEm }
```

Mensagens de `BloqueioDto.mensagem` em português com acento: elas são feitas para a tela, como `Detalhe` de `PlanejadorDeCopia`.

## Review Focus

1. **Quantidade com vírgula decimal num CSV pt-BR** (`"1,5"` com separador `;`): tem de virar 1,5, não 15 nem erro. Teste na Task 3 (`Csv_ptBR_com_virgula_decimal_le_quantidade_fracionaria`).
2. **Descrição com o separador ou aspas dentro** (`"CHAPA 1/4"", ACO 1020"`): é um campo só. Teste na Task 3 (`Csv_campo_entre_aspas_com_separador_e_aspas_duplas`).
3. **Acento num CSV Windows-1252** (`Rebarbação` sem BOM UTF-8): não pode virar `Rebarba��o`. O teste mede o code point, como manda a seção do `seed-demo` do `CLAUDE.md`. Task 3 (`Csv_windows1252_preserva_acento`).
4. **Escolher "catálogo" traz um filho sem sólido.** O bloqueio novo aparece, e voltar para "importada" o remove. Teste na Task 4 (`Escolher_catalogo_traz_filho_sem_solido_e_cria_bloqueio`).
5. **Reimport depois de decidir.** O código cuja receita lida não mudou **mantém** a escolha. O que mudou tem a escolha zerada. O sólido pendente e o casamento manual ficam nos dois casos. Teste na Task 6 (`Reimport_preserva_escolha_so_onde_a_receita_lida_nao_mudou`).

---

### Task 1: Persistência do rascunho — schema, entidades, mapeamento e repositório

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (três tabelas depois de `dbo.ArquivoDeComponente`/`Agrupamento`, na ordem das FKs)
- Create: `db/alter-importacao-bom.sql`
- Create: `src/Rastreamento.Domain/Entities/ImportacaoDeEstrutura.cs`, `ImportacaoDeEstruturaComponente.cs`, `ImportacaoDeEstruturaFilho.cs`
- Create: `src/Rastreamento.Domain/Abstractions/IImportacaoDeEstruturaRepository.cs`
- Modify: `src/Rastreamento.Domain/Abstractions/IComponenteRepository.cs`, `src/Rastreamento.Infrastructure/Persistence/ComponenteRepository.cs` (`ListarPorCodigosAsync`)
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/ImportacaoDeEstrutura*Configuration.cs` (três)
- Create: `src/Rastreamento.Infrastructure/Persistence/ImportacaoDeEstruturaRepository.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`, `src/Rastreamento.Api/Program.cs` (DI)
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ImportacaoDeEstruturaMapeamentoTests.cs`, `ImportacaoDeEstruturaRepositoryTests.cs`

**Interfaces:**
- Produces (Domain):
  - `ImportacaoDeEstrutura { int Id; int AgrupamentoId; string NomeDoArquivo; int? RaizId; decimal? QuantidadeDaPeca; bool RequerRelatorioDimensional; int CriadoPorUsuarioId; DateTime CriadoEm; DateTime AtualizadoEm; byte[] Versao; List<ImportacaoDeEstruturaComponente> Componentes }`
  - `ImportacaoDeEstruturaComponente { int Id; int ImportacaoId; string? CodigoLido; string DescricaoLida; int? ComponenteId; string? CodigoNovo; string? DescricaoNova; string? TipoNovo; string? EscolhaDeReceita; byte[]? ImpressaoDaReceitaDoCatalogo; int? ArquivoSolidoPendenteId; List<ImportacaoDeEstruturaFilho> Filhos }` (`Filhos` = as linhas em que ele é o **pai**)
  - `ImportacaoDeEstruturaFilho { int Id; int PaiId; int FilhoId; int Ordem; decimal QuantidadeLida; decimal Quantidade }`
  - `IImportacaoDeEstruturaRepository`:
    - `Task<ImportacaoDeEstrutura?> ObterAsync(int id, CancellationToken ct)`: rastreada, com `Componentes` e `Componentes.Filhos`
    - `Task<IReadOnlyList<ResumoDeImportacao>> ListarDoAgrupamentoAsync(int agrupamentoId, CancellationToken ct)`, ordenada por `CriadoEm` decrescente
    - `Task AdicionarAsync(ImportacaoDeEstrutura importacao, CancellationToken ct)`: grava cabeçalho, registros e filhos, e depois o `RaizId` (P6)
    - `Task SalvarAsync(ImportacaoDeEstrutura importacao, byte[] versaoEsperada, CancellationToken ct)`: põe `AtualizadoEm = UtcNow`, usa `versaoEsperada` como valor original do token e lança `ConflitoDeConcorrenciaException` quando não bate
    - `Task ExcluirAsync(int id, CancellationToken ct)`: a ordem de P6, numa transação
    - `Task<bool> ExisteNoAgrupamentoAsync(int agrupamentoId, CancellationToken ct)`
    - `Task<string> ObterNomeDoAutorAsync(int usuarioId, CancellationToken ct)`: `Usuario.NomeCompleto` (o `IUsuarioRepository` só busca por nome de usuário)
    - `Task<int> GravarArquivoPendenteAsync(ArquivoDeComponente arquivo, CancellationToken ct)`
    - `Task<ArquivoDeComponente?> ObterArquivoAsync(int arquivoId, CancellationToken ct)`
    - `Task ExcluirArquivosAsync(IReadOnlyCollection<int> arquivoIds, CancellationToken ct)`
  - `record ResumoDeImportacao(int Id, string NomeDoArquivo, string CriadoPor, DateTime CriadoEm, DateTime AtualizadoEm)` (`CriadoPor` = `Usuario.NomeCompleto`)
  - `IComponenteRepository.ListarPorCodigosAsync(IReadOnlyCollection<string> codigos, CancellationToken ct) -> Task<IReadOnlyList<Componente>>`: inclui inativos, e a comparação é a do banco (CI)

- [ ] **Step 1: DDL em `02` e o `alter`.** A forma é a da seção 3 da spec, mais `AtualizadoEm DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME())` (P7). Todas as FKs são `NO ACTION` (P6). As constraints são:
  - `UX_ImportacaoDeEstruturaComponente_Codigo` filtrado (`WHERE CodigoLido IS NOT NULL`);
  - `CK_ImportacaoDeEstruturaComponente_CasadoOuNovo`: `ComponenteId IS NULL OR (CodigoNovo IS NULL AND DescricaoNova IS NULL AND TipoNovo IS NULL)`;
  - `CK_..._Escolha`: `EscolhaDeReceita IN ('Catalogo','Importada')`;
  - `CK_..._TipoNovo`: o domínio de `CK_Componente_Tipo`;
  - `UQ_ImportacaoDeEstruturaFilho (PaiId, FilhoId)`;
  - `CK_..._Quantidade (Quantidade > 0 AND QuantidadeLida > 0)`;
  - `CK_..._NaoAutoReferencia (PaiId <> FilhoId)`.

  O `alter` repete o DDL sob `IF OBJECT_ID(...) IS NULL`, cria a FK `RaizId` depois das duas tabelas, e roda duas vezes sem erro. **Comentário no DDL** explicando o índice filtrado e a ausência de cascata (por nome, sem número de linha).
- [ ] **Step 2: Aplicar no banco de dev e conferir.** Execute o `alter` duas vezes (`docker compose cp` + `sqlcmd -b -f 65001`). Esperado: 0 erros nas duas, e `SELECT name FROM sys.indexes WHERE name = 'UX_ImportacaoDeEstruturaComponente_Codigo'` devolve 1 linha com `has_filter = 1`.
- [ ] **Step 3: Testes de mapeamento que falham.** Em `ImportacaoDeEstruturaMapeamentoTests` (com `TesteComBanco`, prefixo por teste):
  - `Grava_e_le_o_rascunho_inteiro_com_raiz_registros_e_filhos`: ida e volta de um cabeçalho com 3 registros e 2 filhos, e `RaizId` aponta o registro raiz.
  - `Dois_registros_sem_codigo_coexistem_no_mesmo_rascunho`.
  - `Codigo_repetido_no_mesmo_rascunho_e_recusado_pelo_indice`: `DbUpdateException` com 2601.
  - `Mesmo_codigo_em_rascunhos_diferentes_e_aceito`.
  - `Versao_muda_quando_so_um_filho_muda_via_SalvarAsync`.
- [ ] **Step 4: Rodar e ver falhar.** `dotnet test tests/Rastreamento.Infrastructure.Tests --filter ImportacaoDeEstrutura`. Esperado: FAIL (tipos inexistentes).
- [ ] **Step 5: Entidades, configurações (`HasPrecision(18,4)`, `IsRowVersion()` em `Versao`, `HasFilter` no índice), `DbSet`s e o repositório.** O `ExcluirAsync` segue P6.
- [ ] **Step 6: Testes do repositório que falham, e depois passam.** Em `ImportacaoDeEstruturaRepositoryTests`:
  - `ExcluirAsync_apaga_rascunho_registros_filhos_e_arquivos_pendentes`: conta as linhas do próprio rascunho e o `ArquivoDeComponente` pendente, que vão a 0.
  - `SalvarAsync_com_versao_velha_lanca_ConflitoDeConcorrenciaException`.
  - `ListarDoAgrupamentoAsync_traz_nome_do_autor_e_ordena_do_mais_recente`.
  - `ListarPorCodigosAsync_ignora_caixa_e_traz_inativo`: em `ColecaoQueEscreveEmComponente`.
- [ ] **Step 7: Rodar.** `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "ImportacaoDeEstrutura|ListarPorCodigos"` → PASS; `dotnet build Rastreamento.slnx -warnaserror` → 0 warnings.
- [ ] **Step 8: Commit** — `feat(importacao): rascunho do import do BOM no schema, no EF e no repositorio`.

### Task 2: Receitas por código a partir das linhas do BOM (função pura)

**Files:**
- Create: `src/Rastreamento.Application/Importacao/BomDtos.cs`, `MontadorDeReceitasDoBom.cs`
- Test: `tests/Rastreamento.Application.Tests/Importacao/MontadorDeReceitasDoBomTests.cs`

**Interfaces:**
- Produces:
  - `record LinhaCruaDoBom(int NumeroDaLinha, string Nivel, string? Codigo, string Descricao, string Quantidade)`. `Nivel` é o "Nº do item" como veio (`"1.2.3"`). `Quantidade` é texto, ou o número da célula XLSX formatado com `CultureInfo.InvariantCulture`.
  - `record ErroDoBom(int? Linha, string Mensagem)` com `ToString() => Linha is null ? Mensagem : $"Linha {Linha}: {Mensagem}"`
  - `record ComponenteDoBom(int Chave, string? Codigo, string Descricao)`. Chave `0` = raiz.
  - `record FilhoDoBom(int PaiChave, int FilhoChave, int Ordem, decimal Quantidade)`
  - `record BomMontado(IReadOnlyList<ComponenteDoBom> Componentes, IReadOnlyList<FilhoDoBom> Filhos, IReadOnlyList<ErroDoBom> Erros)`
  - `static BomMontado MontadorDeReceitasDoBom.Montar(string nomeDoArquivo, IReadOnlyList<LinhaCruaDoBom> linhas)`

- [ ] **Step 1: Testes que falham.** Cada um afirma `Erros` exatos (linha e trecho da mensagem) ou a forma exata de `Componentes` e `Filhos`:
  - `Bom_de_dois_niveis_vira_raiz_com_receitas_por_codigo`: linhas `1 A 2`, `1.1 B 3`, `2 C 1`. A raiz (chave 0, `Codigo` nulo, `Descricao` = nome do arquivo sem extensão) tem filhos A×2 e C×1, e A tem B×3.
  - `Codigo_repetido_em_dois_pais_vira_um_componente_so`: B sob A e sob C, com a mesma receita → um `ComponenteDoBom` B.
  - `Mesmo_filho_repetido_sob_o_mesmo_pai_soma`: A sob a raiz duas vezes (2 e 3) → `FilhoDoBom(0, A, _, 5)`.
  - `Mesmo_codigo_com_filhos_diferentes_e_erro_do_arquivo`: o erro cita o código e as duas linhas.
  - `Ciclo_entre_codigos_e_erro_do_arquivo`: A contém B, e B contém A.
  - `Nivel_que_pula_degrau_e_erro`: `1` seguido de `1.2.3`.
  - `Quantidade_com_virgula_e_com_ponto_le_igual`: `"1,5"` e `"1.5"` → 1.5m.
  - `Quantidade_invalida_zero_negativa_ou_com_mais_de_4_casas_e_erro`.
  - `Linha_sem_codigo_vira_componente_proprio_com_codigo_nulo`: duas linhas sem código → duas chaves distintas.
  - `Arvore_acima_de_NosMaximos_e_erro`: gerada em laço, com `PlanejadorDeCopia.NosMaximos + 1` nós expandidos.
  - `Profundidade_acima_de_ProfundidadeMaxima_e_erro`.
  - `Lista_vazia_e_erro_de_arquivo_vazio`.
  - `Todos_os_erros_vem_juntos`: duas linhas inválidas → 2 erros.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test tests/Rastreamento.Application.Tests --filter MontadorDeReceitasDoBom`. Esperado: FAIL (compilação).
- [ ] **Step 3: Implementar `Montar`.** O código é normalizado com `Trim()`, e o vazio vira `null`. A comparação entre códigos é `StringComparer.OrdinalIgnoreCase`, o mesmo efeito da collation CI do banco. O pai de uma linha é a última linha com nível igual ao prefixo dela. A raiz recebe as linhas de um segmento só. O ciclo é detectado por DFS sobre as receitas montadas. A contagem de nós expandidos usa as quantidades por pai e segue a mesma regra do `PlanejadorDeCopia`: cada ocorrência conta.
- [ ] **Step 4: Rodar** → PASS.
- [ ] **Step 5: Commit** — `feat(importacao): monta as receitas por codigo a partir das linhas do BOM`.

### Task 3: Leitores de CSV e XLSX

**Files:**
- Create: `src/Rastreamento.Application/Importacao/ILeitorDeBom.cs`
- Create: `src/Rastreamento.Infrastructure/Importacao/LeitorDeBom.cs`, `ColunasDoBom.cs`, `LeitorDeCsv.cs`, `LeitorDeXlsx.cs`
- Modify: `src/Rastreamento.Infrastructure/Rastreamento.Infrastructure.csproj` (`DocumentFormat.OpenXml`), `src/Rastreamento.Api/Program.cs` (DI `ILeitorDeBom` → `LeitorDeBom`, singleton)
- Test: `tests/Rastreamento.Infrastructure.Tests/Importacao/LeitorDeBomTests.cs` (sem banco)

**Interfaces:**
- Produces:
  - `interface ILeitorDeBom { ResultadoDaLeituraDoBom Ler(string nomeDoArquivo, byte[] conteudo); }`
  - `record ResultadoDaLeituraDoBom(IReadOnlyList<LinhaCruaDoBom> Linhas, IReadOnlyList<ErroDoBom> Erros)`
  - `LeitorDeBom.TamanhoMaximoEmBytes = 5 * 1024 * 1024` (P11). Despacha por extensão (`.csv`, `.xlsx`, sem distinguir caixa); outra extensão vira erro sem linha.
  - `ColunasDoBom.Localizar(IReadOnlyList<string> cabecalho) -> (int Nivel, int Codigo, int Descricao, int Quantidade)?` (P9). Coluna obrigatória ausente vira erro `"coluna 'X' nao encontrada"`, citando o nome canônico. O código é a única que pode faltar no conteúdo, nunca no cabeçalho.

- [ ] **Step 1: Testes que falham.** Os arquivos são montados em memória pelo teste. O XLSX é gerado com `SpreadsheetDocument.Create` num `MemoryStream`.
  - `Csv_ptBR_com_virgula_decimal_le_quantidade_fracionaria` (Review Focus 1)
  - `Csv_campo_entre_aspas_com_separador_e_aspas_duplas` (Review Focus 2): `Descricao == "CHAPA 1/4\", ACO 1020"`
  - `Csv_windows1252_preserva_acento` (Review Focus 3): `Descricao.Length == 10` para `Rebarbação`
  - `Csv_utf8_com_bom_preserva_acento`
  - `Csv_com_virgula_como_separador`
  - `Cabecalho_em_ingles_do_SolidWorks_e_reconhecido`: `ITEM NO.;PART NUMBER;DESCRIPTION;QTY.`
  - `Cabecalho_sem_coluna_de_quantidade_e_erro_que_nomeia_a_coluna`
  - `Xlsx_le_primeira_planilha_com_quantidade_numerica`
  - `Xlsx_le_nivel_salvo_como_texto`: `"1.10"` não pode virar `1.1`
  - `Extensao_desconhecida_e_erro`
  - `Linhas_em_branco_no_fim_sao_ignoradas`
  - `NumeroDaLinha_e_o_do_arquivo_contando_o_cabecalho`
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test tests/Rastreamento.Infrastructure.Tests --filter LeitorDeBom`. Esperado: FAIL.
- [ ] **Step 3: Implementar (P12).**
  - CSV: um parser próprio, de estado simples (fora e dentro de aspas). Não entra biblioteca nova para isso.
  - XLSX: `DocumentFormat.OpenXml`. A célula de tipo `SharedString` é resolvida pela `SharedStringTable`. A célula de **nível** é sempre lida como texto (o `CellValue` cru). Se ela vier numérica (`1.1`), o leitor recusa com erro pedindo a coluna como texto, porque `1.10` e `1.1` seriam indistinguíveis.
- [ ] **Step 4: Rodar** → PASS. `dotnet build -warnaserror` → 0 warnings, e `dotnet list package --vulnerable` sem achado no pacote novo.
- [ ] **Step 5: Commit** — `feat(importacao): leitores de BOM em CSV e XLSX`.

### Task 4: Avaliador da conferência (função pura)

**Files:**
- Create: `src/Rastreamento.Application/Importacao/AvaliadorDeImportacao.cs`, `ImportacaoDtos.cs`
- Test: `tests/Rastreamento.Application.Tests/Importacao/AvaliadorDeImportacaoTests.cs`

**Interfaces:**
- Consumes: as entidades da Task 1; `ReceitaDoCatalogo`, `PlanejadorDeCopia.Planejar`, `NoPlanejado` (existentes).
- Produces:
  - `record CatalogoParaAvaliacao(ReceitaDoCatalogo Receita, IReadOnlyDictionary<int, Componente> Componentes, IReadOnlySet<string> CodigosExistentes, IReadOnlyDictionary<int, MetadadoDeSolido> Solidos)`. `Componentes` cobre os casados e os alcançados só pelo catálogo. `CodigosExistentes` usa `OrdinalIgnoreCase`.
  - `static IReadOnlySet<int> AvaliadorDeImportacao.IdsAlcancaveis(ImportacaoDeEstrutura r, ReceitaDoCatalogo receita)`: os Ids **positivos** alcançados na sobreposição. O caso de uso os carrega antes de chamar `Avaliar`.
  - `static AvaliacaoDeImportacao AvaliadorDeImportacao.Avaliar(ImportacaoDeEstrutura r, CatalogoParaAvaliacao c)`
  - `record AvaliacaoDeImportacao(NoDaImportacaoDto? Raiz, IReadOnlyList<SituacaoDoComponenteDto> Componentes, IReadOnlyList<BloqueioDto> Bloqueios, NoPlanejado? Plano, ReceitaDoCatalogo Sobreposicao)`. `Plano` é o da quantidade da Peça (ou 1 quando ela é nula, só para montar a árvore).
  - `static byte[] AvaliadorDeImportacao.Impressao(IEnumerable<(int FilhoId, decimal Quantidade)> receita)`: SHA-256 de `"{filhoId}:{quantidade:F4};"` ordenado por `FilhoId`, com cultura invariante.
  - Os DTOs do contrato HTTP (`NoDaImportacaoDto`, `SituacaoDoComponenteDto`, `LinhaDoComparativoDto`, `EfeitoDto`, `BloqueioDto`) como records em `ImportacaoDtos.cs`, com os nomes de campo do contrato.

- [ ] **Step 1: Testes que falham.** Montam o rascunho e o catálogo em memória. Cada teste afirma os valores exatos do contrato.
  - `Componente_novo_entra_com_id_negativo_e_pendencia_Novo` (P1)
  - `Casado_sem_receita_nao_diverge_e_usa_a_lida`
  - `Casado_com_receita_igual_nao_diverge`
  - `Casado_com_receita_diferente_diverge_e_traz_comparativo_de_um_nivel`: as quatro situações (`Igual`, `QuantidadeMuda`, `Entra`, `Sai`) numa receita só
  - `Casado_folha_no_BOM_com_receita_no_catalogo_diverge`
  - `Divergencia_sem_escolha_mostra_a_arvore_do_BOM_e_bloqueia` (P2)
  - `Escolher_catalogo_tira_galho_so_do_BOM_e_traz_galho_so_do_catalogo`: `naArvoreFinal == false` para o código que saiu (P3)
  - `Escolher_catalogo_traz_filho_sem_solido_e_cria_bloqueio` (Review Focus 4)
  - `Efeito_de_manter_catalogo_conta_nos_expandidos_que_saem_e_entram`
  - `Mesmo_codigo_em_duas_ocorrencias_tem_uma_situacao_e_aparece_duas_vezes_na_arvore`
  - `Bruto_sem_solido_nao_bloqueia`; `Novo_marcado_Bruto_nao_bloqueia`
  - `Solido_pendente_satisfaz_a_exigencia`
  - `Casado_inativo_tem_pendencia_Inativo_e_nao_bloqueia`
  - `Novo_com_codigo_vazio_bloqueia_CodigoVazio`; `Novo_com_codigo_que_ja_existe_bloqueia_CodigoJaExiste`
  - `Quantidade_da_peca_nula_bloqueia`
  - `Escolha_importada_que_fecha_ciclo_com_o_catalogo_bloqueia_CicloNaReceita`: a mensagem é o `Detalhe` do planejador (P4)
  - `Impressao_independe_da_ordem_das_linhas`
- [ ] **Step 2: Rodar e ver falhar.** `--filter AvaliadorDeImportacao` → FAIL.
- [ ] **Step 3: Implementar.** A sobreposição segue P1 e P2, e a raiz é `IdDe(r.RaizId)`. A expansão é **uma** chamada a `PlanejadorDeCopia.Planejar`. A árvore DTO sai do `NoPlanejado` mapeando cada Id de volta ao registro (negativo → `-registroId`, positivo casado → registro, positivo não casado → `origem: "Catalogo"`). O `filhoId` vem da linha `ImportacaoDeEstruturaFilho` do par (pai, filho), quando a aresta é do BOM. O efeito de "catálogo" de um código divergente sai de contar os nós expandidos dos filhos só-BOM e só-catálogo dele, numa ocorrência.
- [ ] **Step 4: Rodar** → PASS.
- [ ] **Step 5: Commit** — `feat(importacao): avaliador puro da conferencia`.

### Task 5: Criar, ler, listar e descartar — caso de uso, rotas e a guarda do Agrupamento

**Files:**
- Create: `src/Rastreamento.Application/Importacao/ImportacaoDeEstruturaUseCase.cs`
- Create: `src/Rastreamento.Api/Controllers/ImportacaoController.cs`
- Modify: `src/Rastreamento.Api/Program.cs` (DI), `src/Rastreamento.Application/Cadastros/CadastroDeAgrupamentoUseCase.cs` (a guarda nova no excluir) e o fake dele
- Test: `tests/Rastreamento.Application.Tests/Importacao/ImportacaoDeEstruturaUseCaseTests.cs` (+ `Fakes.cs` da pasta), `tests/Rastreamento.Api.Tests/ImportacaoEndpointsTests.cs`, e o teste do excluir em `AgrupamentosEndpointsTests`

**Interfaces:**
- Consumes: Tasks 1–4; `IEstruturaRepository.LerReceitaCompletaAsync`, `IReceitaPadraoRepository.ObterComponentesPorIdAsync`, `IArquivoDeComponenteRepository.ObterMetadadoDoSolidoAsync`, `IAgrupamentoRepository`; o nome do autor por `ObterNomeDoAutorAsync` (Task 1).
- Produces:
  - `Task<Result<ImportacaoDto>> Criar(int agrupamentoId, string nomeDoArquivo, byte[] conteudo, int usuarioId, CancellationToken ct)`
  - `Task<Result<ImportacaoDto>> Obter(int id, CancellationToken ct)`
  - `Task<Result<IReadOnlyList<ResumoDeImportacaoDto>>> Listar(int agrupamentoId, CancellationToken ct)`
  - `Task<Result> Descartar(int id, CancellationToken ct)`
  - `private Task<ImportacaoDto> ProjetarAsync(ImportacaoDeEstrutura r, CancellationToken ct)`: carrega o catálogo, os ids alcançáveis e os componentes, avalia e projeta. É o retorno de **toda** escrita das Tasks 6 e 7.
  - `private Task<CatalogoParaAvaliacao> CarregarCatalogoAsync(ImportacaoDeEstrutura r, CancellationToken ct)`
  - Constantes: `ErroDeBomInvalido = "BomInvalido"`, `ErroDeImportacaoDesatualizada = "ImportacaoDesatualizada"`; e, em `CadastroDeAgrupamentoUseCase`, `AgrupamentoComImportacao`.
  - Controller `[Authorize]`, rotas do contrato, `PerfisDeEscrita = "PCP,Administrador"`, o `Traduzir`/`Recusar` no molde de `EstruturaController`, e o `[RequestSizeLimit]` de P11 no `POST`.

- [ ] **Step 1: Testes de caso de uso que falham** (fakes):
  - `Criar_casa_por_codigo_sem_diferenciar_caixa_e_cria_novo_para_o_resto`: `ab-01` casa com `AB-01`.
  - `Criar_com_bom_invalido_devolve_BomInvalido_com_uma_linha_por_erro` (P13).
  - `Criar_preenche_codigo_descricao_e_tipo_do_novo`: `Montagem` se tem filhos, senão `Fabricado`; a raiz segue P8.
  - `Criar_casa_com_inativo`.
  - `Criar_em_agrupamento_inexistente_e_NaoEncontrado`.
  - `Descartar_apaga_tudo`.
- [ ] **Step 2: Testes de API que falham** (`ImportacaoEndpointsTests`, banco real, prefixo por teste, CSV sintético montado no teste):
  - `Post_cria_rascunho_e_get_devolve_o_mesmo_estado`
  - `Post_com_csv_invalido_responde_400_BomInvalido_com_mensagem_por_linha`
  - `Operador_recebe_403_em_toda_escrita`: as rotas de escrita desta task
  - `Lista_do_agrupamento_traz_o_rascunho_criado`
  - `Delete_responde_204_e_o_get_seguinte_404`
  - `Excluir_agrupamento_com_rascunho_responde_409_AgrupamentoComImportacao`: este fica em `AgrupamentosEndpointsTests`. A ordem de verificação é existe → Pedido `Aberto` → vazio → rascunho (seção 3 da spec).
- [ ] **Step 3: Rodar e ver falhar.** `--filter Importacao` nos dois projetos, e o teste do Agrupamento.
- [ ] **Step 4: Implementar** caso de uso, controller, DI e a guarda.
- [ ] **Step 5: Rodar** → PASS; `PerfisDeEscritaDeclaradosTests` e `RegistroDeDependenciasTests` continuam verdes (se eles enumeram controllers ou serviços, a rota e o serviço novos entram neles).
- [ ] **Step 6: Commit** — `feat(importacao): criar, ler, listar e descartar o rascunho`.

### Task 6: Editar o rascunho — Peça, casamento, escolha, quantidade, sólido pendente e reimport

**Files:**
- Modify: `src/Rastreamento.Application/Importacao/ImportacaoDeEstruturaUseCase.cs`, `ImportacaoDtos.cs` (DTOs de entrada), `src/Rastreamento.Api/Controllers/ImportacaoController.cs`
- Test: os dois arquivos de teste da Task 5

**Interfaces:**
- Produces (todos devolvem `Task<Result<ImportacaoDto>>`, e versão velha devolve `Conflito` com `ImportacaoDesatualizada`):
  - `AlterarPeca(int id, AlteracaoDaPecaDto dto, CancellationToken ct)`, com `record AlteracaoDaPecaDto(string Versao, decimal? QuantidadeDaPeca, bool RequerRelatorioDimensional)`
  - `AlterarComponente(int id, int registroId, AlteracaoDeComponenteDto dto, CancellationToken ct)`, com `record AlteracaoDeComponenteDto(string Versao, int? ComponenteId, string? CodigoNovo, string? DescricaoNova, string? TipoNovo, string? EscolhaDeReceita)`
  - `AlterarFilho(int id, int filhoId, AlteracaoDeFilhoDto dto, CancellationToken ct)`, com `record AlteracaoDeFilhoDto(string Versao, decimal Quantidade)`
  - `EnviarSolidoPendente(int id, int registroId, string versao, string nomeOriginal, byte[] conteudo, int usuarioId, CancellationToken ct)`
  - `Task<Result<ArquivoDeSolidoDto>> ObterSolidoPendente(int id, int registroId, CancellationToken ct)`
  - `Reimportar(int id, string versao, string nomeDoArquivo, byte[] conteudo, CancellationToken ct)`

- [ ] **Step 1: Testes de caso de uso que falham.**
  - `Casar_manualmente_limpa_os_campos_do_novo_e_trocar_para_novo_preenche_com_o_lido`: respeita o `CK_..._CasadoOuNovo`.
  - `Casar_com_componente_inexistente_e_NaoEncontrado`.
  - `TipoNovo_fora_do_dominio_e_Validacao`.
  - `Escolher_receita_grava_a_impressao_do_catalogo_vista`.
  - `Escolha_em_codigo_que_nao_diverge_e_Validacao`.
  - `Trocar_o_casamento_zera_a_escolha`.
  - `Quantidade_do_filho_fora_da_faixa_da_coluna_e_Validacao`.
  - `Solido_pendente_invalido_usa_a_mensagem_do_ValidadorDeArquivoStl`.
  - `Novo_solido_pendente_substitui_e_apaga_o_anterior`.
  - `Reimport_preserva_escolha_so_onde_a_receita_lida_nao_mudou` (Review Focus 5).
  - `Reimport_preserva_solido_pendente_casamento_manual_e_dados_do_novo_por_codigo`.
  - `Reimport_apaga_pendente_de_codigo_que_saiu_do_arquivo`.
  - `Reimport_mantem_a_raiz` (P8).
  - `Escrita_com_versao_velha_e_Conflito_ImportacaoDesatualizada`.
- [ ] **Step 2: Testes de API que falham.**
  - `Put_componente_e_get_refletem_o_casamento_manual`
  - `Post_solido_pendente_e_get_devolve_o_mesmo_binario`
  - `Post_solido_pendente_acima_do_limite_e_recusado`: no molde de `SolidoEndpointsTests`
  - `Put_com_versao_velha_responde_409`
  - `Reimport_com_csv_invalido_responde_400_e_nao_altera_o_rascunho`
- [ ] **Step 3: Rodar e ver falhar.**
- [ ] **Step 4: Implementar.**
  - Toda escrita relê o rascunho, valida, aplica, chama `SalvarAsync(r, Convert.FromBase64String(dto.Versao))` e devolve `ProjetarAsync`.
  - O sólido pendente passa pelo `ValidadorDeArquivoStl.Validar`. O arquivo anterior do registro é apagado por `ExcluirArquivosAsync`.
  - O reimport roda leitura e montagem (Tasks 2 e 3) e casa os códigos como o `Criar` da Task 5. Ele substitui os registros e filhos, transferindo por código (`OrdinalIgnoreCase`) o casamento manual, os dados do novo, o pendente e a escolha. A escolha só é transferida se a receita lida do código for igual à anterior.
- [ ] **Step 5: Rodar** → PASS.
- [ ] **Step 6: Commit** — `feat(importacao): edicao do rascunho, solido pendente e reimport`.

### Task 7: Confirmação e medição da suíte

**Files:**
- Modify: `src/Rastreamento.Application/Importacao/ImportacaoDeEstruturaUseCase.cs`, `src/Rastreamento.Api/Controllers/ImportacaoController.cs`
- Test: os dois arquivos de teste da Task 5 e `tests/Rastreamento.Api.Tests/ConfirmacaoDeImportacaoTests.cs`

**Interfaces:**
- Consumes: `IExecucaoRepository` + a extensão `ExecutarAsync` de `Falhas`, `IComponenteRepository`, `IReceitaPadraoRepository.ListarFilhosAsync` e `SubstituirFilhosAsync`, `IEstruturaRepository.GravarArvoreAsync`, `MontadorDeArvoreDeEstrutura` (a projeção da Peça, como em `CriarPeca`).
- Produces: `Task<Result<EstruturaItemDto>> Confirmar(int id, string versao, CancellationToken ct)`. Códigos: `ImportacaoComBloqueios` (Validação), `ReceitaDoCatalogoMudou` (Conflito, com `Detalhe` nomeando os códigos) e `ImportacaoDesatualizada`.

- [ ] **Step 1: Testes de caso de uso que falham.**
  - `Confirmar_com_bloqueio_e_ImportacaoComBloqueios_e_nao_escreve_nada`
  - `Confirmar_com_impressao_diferente_e_ReceitaDoCatalogoMudou_e_zera_a_escolha`: zerar a escolha é a **única** escrita, fora da transação que falhou
  - `Confirmar_cria_novos_reativa_inativos_grava_solidos_e_receitas_so_da_arvore_final` (P3)
  - `Confirmar_nao_toca_na_receita_de_codigo_com_escolha_Catalogo`
  - `Confirmar_apaga_o_rascunho`
- [ ] **Step 2: Testes de API que falham** (`ConfirmacaoDeImportacaoTests`, banco real):
  - `Peca_confirmada_e_identica_a_criada_pelo_Nova_Peca_a_partir_do_mesmo_catalogo`: confirma um import. Em seguida, num Agrupamento irmão, faz `POST /agrupamentos/{id}/estrutura` com o mesmo Componente raiz e a mesma quantidade, e compara as duas árvores (código, quantidade, `quantidadePorPai`, materiais, roteiro), ignorando Ids.
  - `Catalogo_alterado_entre_escolha_e_confirmacao_responde_409_ReceitaDoCatalogoMudou`
  - `Confirmar_com_bloqueio_responde_400_ImportacaoComBloqueios`
  - `Depois_de_confirmar_o_get_do_rascunho_e_404_e_o_agrupamento_tem_a_peca`
- [ ] **Step 3: Rodar e ver falhar.**
- [ ] **Step 4: Implementar (P5).** A ordem é a da seção 7 da spec.
  - **Fora da transação:** `ObterAsync`, `CarregarCatalogoAsync` e `Avaliar`. Com bloqueio, devolve `ImportacaoComBloqueios`.
  - **Dentro de `ExecutarAsync`:**
    1. Relê o rascunho. O retry de 1205 limpa o `ChangeTracker` (`ExecucaoRepository`), então nada rastreado de fora sobrevive.
    2. Confere a versão.
    3. Relê `ListarFilhosAsync` de cada pai com escolha e compara a impressão. Se diferir, devolve `Falha`, o que desfaz tudo. A escolha é zerada **depois**, numa escrita própria.
    4. Insere os Componentes novos da árvore final e monta o mapa Id provisório → Id real.
    5. Reativa os inativos e vincula os sólidos pendentes (`ArquivoSolidoId`). O arquivo antigo de um casado fica como `GravarEVincularComoSolidoAsync` já deixa.
    6. Chama `SubstituirFilhosAsync` com a receita lida, Ids traduzidos, para cada registro da árvore final cuja receita efetiva é a lida.
    7. Converte o `Plano` com os Ids traduzidos em `NoParaGravar` (raiz com `RequerRelatorioDimensional`) e chama `GravarArvoreAsync`.
    8. Apaga o rascunho, menos os arquivos agora vinculados.
    9. Projeta a Peça pelo `MontadorDeArvoreDeEstrutura`.

  **Não** valide a receita de novo por `ReceitaPadraoUseCase`: o ciclo já foi barrado pelo avaliador (P4), e os filhos inativos foram reativados no passo 5. Um comentário no código diz isso por nome.
- [ ] **Step 5: Rodar** → PASS. `dotnet test Rastreamento.slnx -m:1` inteiro → verde.
- [ ] **Step 6: Medição (risco da seção 7 da spec).** Rode `dotnet test tests/Rastreamento.Api.Tests` **10 vezes** seguidas, com `--logger trx`. Registre as vermelhas e o nome de cada teste que falhou, no relatório da task e no ledger do plano. Critério: **0 vermelhas em 10**. Uma vermelha volta ao diagnóstico (`superpowers:systematic-debugging`). Ela não pode ser repetida até passar.
- [ ] **Step 7: Commit** — `feat(importacao): confirmacao grava catalogo e Peca numa transacao`.

### Task 8: Front — cliente da API e a entrada na página do Agrupamento

**Files:**
- Create: `web/src/api/importacao.ts`, `web/src/api/importacao.test.ts`
- Create: `web/src/importacao/ImportacoesEmConferencia.tsx` + `.test.tsx`
- Modify: `web/src/pages/AgrupamentoDetalhePage.tsx` + `.test.tsx`, `web/src/api/cadastros.ts` (`ResultadoExclusao` ganha `'AgrupamentoComImportacao'`)

**Interfaces:**
- Produces (`api/importacao.ts`):
  - os tipos do contrato HTTP (`ImportacaoDto`, `NoDaImportacaoDto`, `SituacaoDoComponenteDto`, `BloqueioDto`, `ResumoDeImportacaoDto`, `EscolhaDeReceita = 'Catalogo' | 'Importada'`);
  - `TAMANHO_MAXIMO_DO_BOM_EM_BYTES = 5 * 1024 * 1024`;
  - `class ErroDeBom extends ErroDeApi { linhas: string[] }`;
  - as funções:
    - `criarImportacao(agrupamentoId: number, arquivo: File): Promise<ImportacaoDto>`, que lança `ErroDeBom` no 400 `BomInvalido`
    - `listarImportacoes(agrupamentoId: number): Promise<ResumoDeImportacaoDto[]>`
    - `obterImportacao(id: number): Promise<ImportacaoDto>`
    - `alterarPeca(id, versao, quantidadeDaPeca: number | null, requerRelatorioDimensional: boolean)`
    - `alterarComponente(id, registroId, versao, alteracao)`
    - `alterarFilho(id, filhoId, versao, quantidade)`
    - `enviarSolidoPendente(id, registroId, versao, arquivo)`
    - `caminhoDoSolidoPendente(id, registroId): string`
    - `reimportar(id, versao, arquivo)`
    - `descartarImportacao(id): Promise<void>`
    - `confirmarImportacao(id, versao): Promise<NoDaEstrutura | 'ImportacaoComBloqueios' | 'ReceitaDoCatalogoMudou' | 'ImportacaoDesatualizada'>`

    Todas as escritas devolvem `Promise<ImportacaoDto>` e lançam `ErroDeApi` com status 409 na versão velha.
  - `ImportacoesEmConferencia({ agrupamentoId: number, versao: number })`: a seção "Importações em conferência". Some quando a lista é vazia. Cada item é um `ItemComAcao` com arquivo, autor e "atualizado em" (`formatarDataHora`), um link "Continuar" para `/importacoes/:id` e "Descartar" pela `Confirmacao`. `versao` é um contador que a página incrementa para forçar a releitura.

- [ ] **Step 1: Testes que falham.**
  - `api/importacao.test.ts`:
    - `criarImportacao_manda_multipart_sem_content_type`
    - `criarImportacao_400_BomInvalido_vira_ErroDeBom_com_uma_linha_por_erro`
    - `confirmarImportacao_mapeia_os_tres_codigos`
  - `ImportacoesEmConferencia.test.tsx`: os três estados (carregando, vazio = nada renderizado, erro via `mensagemDeErro`); `Continuar_aponta_para_a_conferencia`; `Descartar_pede_confirmacao_e_recarrega`.
  - `AgrupamentoDetalhePage.test.tsx`:
    - `Importar_BOM_aparece_so_para_quem_escreve_em_estrutura`
    - `Importar_BOM_abre_painel_com_campo_de_arquivo`: achado por `getByRole('form', { name: 'Importar BOM' })`
    - `Arquivo_acima_de_5_MiB_e_recusado_sem_enviar`
    - `Sucesso_navega_para_a_conferencia`
    - `BomInvalido_lista_os_erros_dentro_do_painel`
    - `Excluir_agrupamento_com_rascunho_mostra_a_mensagem_propria`
- [ ] **Step 2: Rodar e ver falhar.** `cd web && npx vitest run src/api/importacao src/importacao src/pages/AgrupamentoDetalhePage`.
- [ ] **Step 3: Implementar.**
  - O botão "Importar BOM" fica no `acao` da `Pagina`, ao lado do "Nova Peça", sob `usePodeEscrever('estrutura')`.
  - O `PainelDeEscrita` "Importar BOM" segue a seção "Interface" do `CLAUDE.md`: `enviando` durante o envio, o erro de escrita dentro do painel e o `useDevolverFoco` para o botão. Abrir este painel fecha os outros dois da página, que nunca coexistem.
  - A seção `ImportacoesEmConferencia` entra abaixo da árvore.
  - A página já tem 724 linhas, e por isso a seção nasce em `web/src/importacao/`.
- [ ] **Step 4: Rodar** → PASS; `npm test -- --run` e `npm run build` verdes.
- [ ] **Step 5: Commit** — `feat(importacao): entrada do import do BOM na pagina do Agrupamento`.

### Task 9: Front — tela de conferência: árvore, faixa da Peça e confirmação

**Files:**
- Create: `web/src/pages/ConferenciaDeImportacaoPage.tsx` + `.test.tsx`
- Create: `web/src/components/ArvoreDaImportacao.tsx` + `.test.tsx`
- Modify: `web/src/App.tsx` (rota `/importacoes/:id` dentro do `AppShell`)

**Interfaces:**
- Consumes: `api/importacao.ts` (Task 8).
- Produces:
  - `ArvoreDaImportacao({ raiz: NoDaImportacaoDto, selecionado: number | null, aoSelecionar: (registroId: number | null, componenteId: number | null) => void, aoAlterarQuantidade?: (filhoId: number, quantidade: number) => void })`. Cada linha é um `<button>` com nome acessível = código + descrição, e mostra a quantidade por pai editável (por `Campo`) só quando `filhoId` existe. As pílulas `pendencias` seguem a seção 8 da spec: `atencao` para `Divergente`, `SemSolido` e `Inativo`, `neutro` para `Novo`. As ocorrências do código selecionado ficam destacadas, com `aria-current` na selecionada e um indicador textual "mesmo código" nas outras. A árvore é própria, e não a `ArvoreDeEstrutura`: o contrato desta é o `NoDaEstrutura` real, com ações de nó e posições que não existem no rascunho.
  - `ConferenciaDeImportacaoPage`: `<Pagina titulo="Conferência da importação">` com três regiões na ordem da seção 8 da spec. A região 1 (painel fixo) é um espaço reservado nesta task, preenchido na Task 10, com o `registroId` selecionado guardado em `useState`. A região 2 é a faixa da Peça: quantidade, Relatório Dimensional, pílulas-resumo que levam ao primeiro nó da pendência, **Confirmar** desabilitado com a lista de bloqueios visível e Descartar pela `Confirmacao`. A região 3 é a `ArvoreDaImportacao`.
  - O estado da tela é o `ImportacaoDto` mais recente. **Toda** escrita o substitui pela resposta. Um 409 `ImportacaoDesatualizada` relê o `GET` e mostra "Outra pessoa alterou esta importação; a tela foi atualizada."

- [ ] **Step 1: Testes que falham.**
  - `ArvoreDaImportacao.test.tsx`:
    - `Clicar_no_no_chama_aoSelecionar`
    - `Pilulas_de_pendencia_usam_atencao_e_Novo_usa_neutro`: afirma a classe do token, como os testes de `Pilula`
    - `Ocorrencias_do_mesmo_codigo_ficam_destacadas_juntas`
    - `Quantidade_so_e_editavel_em_aresta_do_BOM`
  - `ConferenciaDeImportacaoPage.test.tsx`:
    - os três estados
    - `Confirmar_fica_desabilitado_e_lista_os_bloqueios`
    - `Pilula_de_resumo_seleciona_o_primeiro_no_da_pendencia`
    - `Confirmar_com_sucesso_volta_ao_agrupamento`
    - `Confirmar_com_ImportacaoComBloqueios_rele_o_estado`
    - `ReceitaDoCatalogoMudou_mostra_a_mensagem_e_rele`
    - `Versao_velha_rele_e_avisa`
    - `Descartar_pede_confirmacao_e_volta_ao_agrupamento`
    - `Alterar_quantidade_da_peca_envia_a_versao_atual`
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar** → PASS; `npm test -- --run`, `npm run build` e `semCorForaDaPaleta.test.ts` verdes.
- [ ] **Step 5: Commit** — `feat(importacao): tela de conferencia com arvore, faixa da Peca e confirmacao`.

### Task 10: Front — painel fixo do Componente selecionado

**Files:**
- Create: `web/src/importacao/PainelDoComponenteDaImportacao.tsx` + `.test.tsx`, `web/src/importacao/ComparativoDeReceita.tsx` + `.test.tsx`
- Modify: `web/src/components/UploadDeSolido.tsx`, `VisualizadorDeSolido.tsx` + testes, `web/src/pages/ComponenteDetalhePage.tsx` (novo jeito de chamar as duas), `web/src/pages/ConferenciaDeImportacaoPage.tsx` (região 1)

**Interfaces:**
- Modifies:
  - `VisualizadorDeSolido({ caminho: string })`, no lugar de `componenteId`.
  - `UploadDeSolido({ caminho: string, enviar: (arquivo: File) => Promise<void>, temSolido, nomeDoSolido, tamanhoDoSolidoEmBytes, aoEnviar })`, no lugar de `componenteId`.

  A `ComponenteDetalhePage` passa `caminho={caminhoDoSolido(id)}` e `enviar={(a) => enviarSolido(id, a)}`. Os testes existentes das duas primitivas mudam só na montagem das props.
- Produces:
  - `PainelDoComponenteDaImportacao({ importacao: ImportacaoDto, registroId: number | null, componenteId: number | null, aoMudar: (novo: ImportacaoDto) => void })`, `position: sticky` no topo.
    - À esquerda, o visualizador e o upload: do pendente quando existe, senão do sólido do catálogo, e upload sempre para o pendente.
    - À direita:
      - código e descrição, com a do BOM ao lado quando difere;
      - o casamento: `SeletorComBusca` "Casar com outro Componente", ou o botão "Criar novo" com os `Campo`s código, descrição e `Tipo` (o `Tipo` só para registro novo);
      - quando `divergente`, o `ComparativoDeReceita` e um grupo de rádio **sem valor inicial**, "Manter a receita do catálogo" / "Usar a receita importada", com o efeito ("Manter a do catálogo retira N itens do BOM e traz M do catálogo") visível antes de salvar.
    - Nó só do catálogo (`registroId` nulo) mostra o Componente em leitura, com o upload desabilitado e o texto "Este item vem da receita do catálogo; o sólido se envia no cadastro dele."
  - `ComparativoDeReceita({ linhas })`: tabela com cabeçalho "Filho / Catálogo hoje / BOM / Situação".

- [ ] **Step 1: Testes que falham.**
  - `PainelDoComponenteDaImportacao.test.tsx`:
    - `Troca_de_no_selecionado_troca_o_conteudo`
    - `Escolha_de_receita_nasce_sem_opcao_marcada`
    - `Efeito_de_manter_catalogo_aparece_antes_de_salvar`
    - `Upload_vai_para_o_solido_pendente_com_a_versao`
    - `Casar_com_outro_pelo_SeletorComBusca_envia_componenteId`
    - `Criar_novo_mostra_campos_e_Tipo`
    - `No_so_do_catalogo_fica_em_leitura`
  - `ComparativoDeReceita.test.tsx`: as quatro situações com texto legível.
  - `UploadDeSolido` e `VisualizadorDeSolido`: `Usa_o_caminho_recebido`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar**, e trocar o espaço reservado da região 1.
- [ ] **Step 4: Rodar** → PASS; `npm test -- --run` e `npm run build` verdes.
- [ ] **Step 5: Commit** — `feat(importacao): painel do Componente com casamento, solido e escolha de receita`.

### Task 11: Documentação

**Files:**
- Modify:
  - `specs/05-api-endpoints.md`: seção nova "Importação da estrutura", com o contrato desta página, e o 409 novo em `DELETE /agrupamentos/{id}`, no corpo e na tabela de erros
  - `specs/01-dominio-e-regras-de-negocio.md`: regra nova, numerada depois da última, com D3, D5 e D6 da spec; a regra 18 ganha a remissão "no import, a exigência vale para a árvore inteira, exceto `Bruto`"
  - `specs/04-fluxos-de-usuario.md`: o fluxo do PCP ganha "Importar a estrutura de um BOM"
  - `CLAUDE.md`: em "Comandos", o bloco do `db/alter-importacao-bom.sql`, no molde do da 3D. Na seção "Interface", a regra "`data-testid`" **só se** alguma task o tiver usado; remeça com o comando registrado lá e diga a métrica.
  - `docs/superpowers/specs/2026-08-04-fase-1b-componente-design.md`: o apêndice ganha uma linha apontando a spec nova
  - `docs/superpowers/specs/2026-10-02-import-de-estrutura-do-bom-design.md`: a seção 3 ganha `AtualizadoEm` (P7) e a nota "sem cascata" (P6)

- [ ] **Step 1: Escrever.** Toda contagem citada é remedida no momento, com o comando e a data. Nada de número copiado de outra seção.
- [ ] **Step 2: Conferir.**
  - `grep -rnE "[A-Za-z0-9_./-]+\.(cs|ts|tsx|css|sql|json|md|html):[0-9]+" specs/ CLAUDE.md` **não** pode subir em relação à medição registrada no `CLAUDE.md` (5).
  - Toda rota do contrato desta página aparece em `05`.
- [ ] **Step 3: Commit** — `docs(importacao): endpoints, regra de dominio, fluxo e comandos do import do BOM`.

### Task 12: Verificação no navegador (sem código)

Dispensa de review registrada **antes** no ledger e no relatório: o produto é evidência, não código (primeira classe da seção "Pular a review" do `CLAUDE.md`).

- [ ] **Step 1:** Sobe backend e front com o banco de demo. Usa um CSV **sintético** com 3 níveis, um código repetido em dois pais, uma linha sem part number, um código casando com Componente do demo com receita diferente e um casando com inativo.
- [ ] **Step 2:** Percorre como `pcp`:
  1. importa;
  2. vê as pendências;
  3. escolhe "catálogo" num código e confere que o efeito anunciado bate com a árvore;
  4. volta para "importada";
  5. envia STLs;
  6. dá F5 no meio e confirma que nada se perdeu;
  7. confirma;
  8. confere a Peça na página do Agrupamento e as receitas no cadastro de Componentes.

  Como `operador`, recriado à mão conforme o `CLAUDE.md`, confirma que o botão "Importar BOM" não aparece e que a conferência não oferece escrita.
- [ ] **Step 3:** Faz o reimport com o mesmo arquivo alterado em uma quantidade e confirma o Review Focus 5 na tela.
- [ ] **Step 4:** Grava o relatório no ledger do plano, com capturas por passo e qualquer descumprimento de spec como achado.

## Self-review do plano (feito na escrita)

- **Cobertura da spec:**
  - seção 3 → Task 1;
  - seção 4 → Tasks 2 e 3;
  - seção 5.1 → Tasks 5 e 6;
  - seção 5.2 → Tasks 4 e 6;
  - seção 5.3 → Tasks 1, 5 e 6;
  - seção 5.4 → Task 4;
  - seção 6 → Tasks 5 a 7;
  - seção 7 → Task 7;
  - seção 8 → Tasks 8 a 10;
  - seção 9 → os testes de cada task e a medição da Task 7;
  - seção 10 → P9, P10 e P12 (suposições isoladas em `ColunasDoBom` e no leitor);
  - seção 11 ("junto com a implementação") → Tasks 1 e 11.
- **Consistência de nomes:** `registroId` é o Id de `ImportacaoDeEstruturaComponente` em toda parte, no contrato, no avaliador e no front. `filhoId` é o de `ImportacaoDeEstruturaFilho`. `componenteId` é sempre o de `dbo.Componente`.
- **Fora deste plano, de propósito:** Materiais do Toolbox (esperam o BOM real, seção 10 da spec), expiração de rascunho e histórico de confirmações (seção 12 da spec).
