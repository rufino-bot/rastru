# Miniatura isométrica do sólido — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Todo sólido enviado ganha uma miniatura isométrica (PNG 320 × 240, estilo "fosco com arestas") desenhada no
navegador e gravada junto com o STL; o detalhe do Componente a mostra e oferece "Gerar miniatura" para quem ficou sem; os
DTOs expõem `miniaturaVersao` para o Panorama.

**Architecture:** O navegador lê o STL escolhido, desenha com o three.js numa câmera ortográfica isométrica e manda STL e
PNG no mesmo `multipart` (D4). O servidor só valida (assinatura, `IHDR` 320 × 240, ≤ 512 KiB) e grava os dois em
`dbo.ArquivoDeComponente`, numa transação, com a FK nova `Componente.ArquivoMiniaturaId` (no import,
`ArquivoMiniaturaPendenteId`, que segue o ciclo do STL pendente). A versão exposta é o `Sha256` da miniatura em hexa,
nunca o id do arquivo. A tela lê a imagem pelo `apiFetch` e a guarda em memória por versão.

**Tech Stack:** .NET 10 / C# (xUnit; fakes em `Application.Tests`; SQL Server real em `Infrastructure.Tests` e
`Api.Tests`), EF Core Database First, SQL. Front: React + TypeScript + Vite, three.js 0.186 (`STLLoader`,
`EdgesGeometry`), Vitest + jsdom.

**Spec:** `docs/superpowers/specs/2026-11-17-miniatura-do-solido-design.md` (aprovada em 2026-11-17). As referências
C1–C15 e D1–D5 abaixo são dessa spec; leia as seções 2.2, 5 e 6 antes de qualquer task.

## Global Constraints

- Sessão de nuvem: antes da primeira task de backend, `bash scripts/backend-na-nuvem` (instala o SDK, sobe o SQL Server e
  cria o banco). Depois da Task 1, aplicar `db/alter-miniatura.sql` no banco de dev (comando na Task 1).
- `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; a suíte roda com `dotnet test Rastreamento.slnx -m:1`,
  **uma execução por vez**. No front, `npm test -- --run --maxWorkers=2` **e** `npm run build` (em `web/`).
- Schema nasce em `specs/02-modelo-de-dados.sql` primeiro; nada de `Add-Migration`/`EnsureCreated`.
- A imagem: PNG, **320 × 240** px, fundo transparente (C1). Limite **512 KiB** (`524288` bytes) (C7).
- Câmera: ortográfica, de `(1, 1, 1)` para o centro, `up = (0, 1, 0)`, **sem girar o STL**; folga de **8%** sobre a esfera
  envolvente (C2).
- Estilo C (C3): `MeshLambertMaterial` `#C9D3D1` com `polygonOffset` (fator 1, unidades 1); `AmbientLight` 0,55;
  `DirectionalLight` 0,6 em `(0.6, 1.4, 1.0)`; arestas `EdgesGeometry(geo, 20)` em `LineBasicMaterial` `#2F3A38`.
- **A versão é o `Sha256` em hexadecimal minúsculo** (64 caracteres), nunca o id do arquivo (C12; o comentário de
  `ComponenteDto` proíbe expor id de arquivo).
- Mensagens de erro do backend em português **sem acento**, como as de `ValidadorDeArquivoStl`. O 409 dos cadastros é
  `{ erro }`, sem código (`CadastroControllerBase.TraduzirResultado`).
- Perfis de escrita da miniatura: os do sólido (`PerfisDeEscrita` do `ComponentesController`: PCP e Administrador; no
  import, os do `ImportacaoController`).
- Front: nenhum `<button>` cru, nenhuma cor fora dos tokens, `// @vitest-environment jsdom` e `afterEach(cleanup)` nos
  testes de tela, `web/src/testes/api.ts` para mockar a API. Chamada nova passa o caminho **sem** `/api`.
- Comentário cita o alvo **pelo nome**, nunca por `arquivo:linha` (`CLAUDE.md`, «Convenção de citação»).

## Modelo por task (decisão do usuário, 2026-11-17)

O implementer das tasks de **código mais pesado** roda no modelo **`fable`**: Tasks **3, 4 e 6** (backend: transação,
corrida e o ciclo do import) e **7, 9 e 10** (front: desenho com three.js, fluxo do upload e o "Gerar miniatura"). As Tasks
1, 2, 5 e 8 usam o modelo padrão da sessão. As reviews de task seguem o fluxo da skill, e a review de branch inteira
roda no modelo mais capaz, como manda o `CLAUDE.md`.

## Review Focus

1. **Sólido trocado por um celular sem WebGL** depois de já ter miniatura: a miniatura antiga tem de sumir (C6), senão o
   Panorama mostra a peça velha. Teste na Task 3: `Enviar_sem_miniatura_zera_a_miniatura_anterior`.
2. **"Gerar miniatura" enquanto outra pessoa troca o sólido**: o hash não bate e o servidor recusa (C15), e a tela diz
   isso em vez de um erro genérico. Testes na Task 4 (`Miniatura_avulsa_de_solido_trocado_e_recusada`) e na Task 10
   (`gerar miniatura com solido trocado mostra a recusa`).
3. **Reimport que tira do arquivo um registro com STL e miniatura pendentes**: os dois arquivos somem, nenhum órfão
   (C5). Teste na Task 6: `Reimport_apaga_solido_e_miniatura_pendentes_do_registro_que_saiu`.
4. **Casar com um Componente do catálogo que já tem miniatura e mandar STL pendente sem miniatura**: a conferência não
   mostra a imagem do catálogo, e a confirmação zera a miniatura do Componente (C6 aplicado antes, C12). Teste na Task 6:
   `Casado_com_solido_pendente_sem_miniatura_confirma_sem_miniatura`.
5. **Recarga da tela com a mesma versão**: a imagem não é baixada de novo; com versão nova, é (C11). Teste na Task 8:
   `nao busca de novo a mesma versao`.

---

## Mapa de arquivos

| Arquivo | Responsabilidade | Task |
|---|---|---|
| `specs/02-modelo-de-dados.sql`, `db/alter-miniatura.sql` | as duas colunas e FKs | 1 |
| `src/Rastreamento.Domain/Entities/Componente.cs`, `ImportacaoDeEstruturaComponente.cs` | `ArquivoMiniaturaId`, `ArquivoMiniaturaPendenteId` | 1 |
| `src/Rastreamento.Infrastructure/Persistence/` (configuração EF das duas entidades) | mapeamento | 1 |
| `src/Rastreamento.Application/Arquivos/ValidadorDeMiniatura.cs` | validação pura do PNG | 2 |
| `src/Rastreamento.Domain/Abstractions/IArquivoDeComponenteRepository.cs` + `ArquivoDeComponenteRepository.cs` | gravar sólido com miniatura, miniatura avulsa, leitura, hashes por id | 3, 4, 5 |
| `src/Rastreamento.Application/Arquivos/SolidoDoComponenteUseCase.cs` | envio conjunto, avulso, leitura | 3, 4 |
| `src/Rastreamento.Api/Controllers/ComponentesController.cs` | campo `miniatura`, `POST`/`GET …/miniatura` | 3, 4 |
| `src/Rastreamento.Application/Cadastros/Dtos.cs`, `CadastroDeComponenteUseCase.cs` | `MiniaturaVersao` | 5 |
| `src/Rastreamento.Application/Importacao/*` + `ImportacaoDeEstruturaRepository.cs` + `ImportacaoController.cs` | pendente, reimport, descarte, confirmação, avaliação, leitura | 6 |
| `web/src/solido/miniatura.ts`, `web/src/solido/hash.ts` | desenho e hash no navegador | 7 |
| `web/src/api/cadastros.ts`, `web/src/api/importacao.ts` | clientes HTTP | 7 |
| `web/src/components/MiniaturaDoSolido.tsx` | exibição com cache por versão | 8 |
| `web/src/components/UploadDeSolido.tsx` + chamadores | desenhar e enviar junto | 9 |
| `web/src/pages/ComponenteDetalhePage.tsx` | mostrar e "Gerar miniatura" | 10 |
| `specs/01`, `specs/05`, `specs/06`, `CLAUDE.md` | documentação, cada parte na task que a faz nascer | 1, 3, 4, 6, 8 |

---

### Task 1: Schema, migração e mapeamento

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (tabelas `dbo.Componente` e `dbo.ImportacaoDeEstruturaComponente`; o comentário
  "a foto entra depois com uma FK nova" em `dbo.ArquivoDeComponente` passa a dizer que a miniatura entrou por FK própria e
  a foto continua por vir)
- Create: `db/alter-miniatura.sql`
- Modify: `src/Rastreamento.Domain/Entities/Componente.cs`, `src/Rastreamento.Domain/Entities/ImportacaoDeEstruturaComponente.cs`
- Modify: a configuração EF das duas entidades em `src/Rastreamento.Infrastructure/Persistence/` (ache pelo nome da
  propriedade `ArquivoSolidoId` / `ArquivoSolidoPendenteId`)
- Modify: `specs/01-dominio-e-regras-de-negocio.md` (o termo "Miniatura" no glossário, ao lado de
  `Componente.ArquivoSolidoId`: imagem isométrica gerada do sólido, opcional, nunca condição de nada)
- Modify: `specs/06-roadmap-mvp.md` (seção nova «Miniatura isométrica do sólido», antes da do Panorama e antes da 3C,
  com o item "miniatura no chão de fábrica — depois do Panorama") e `CLAUDE.md` (a exceção de ordem: miniatura → Panorama
  antes da 3C; o bloco do `db/alter-miniatura.sql` em «Comandos», no formato dos anteriores)
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ArquivoDeComponenteMapeamentoTests.cs`

**Interfaces:**
- Produces: `Componente.ArquivoMiniaturaId : int?`; `ImportacaoDeEstruturaComponente.ArquivoMiniaturaPendenteId : int?`;
  colunas `dbo.Componente.ArquivoMiniaturaId INT NULL` (`FK_Componente_ArquivoMiniatura`) e
  `dbo.ImportacaoDeEstruturaComponente.ArquivoMiniaturaPendenteId INT NULL`
  (`FK_ImportacaoDeEstruturaComponente_ArquivoMiniaturaPendente`), ambas para `dbo.ArquivoDeComponente(Id)`.

- [ ] **Step 1: Escrever o teste de mapeamento que falha**

`Componente_grava_e_le_ArquivoMiniaturaId` (grava um `ArquivoDeComponente`, um Componente com `ArquivoMiniaturaId`
apontando para ele, relê com outro contexto e afirma o id) e
`Registro_da_importacao_grava_e_le_ArquivoMiniaturaPendenteId`. Mesma `[Collection]` dos testes vizinhos que escrevem em
`Componente` (`ColecaoQueEscreveEmComponente`); asserção só sobre as linhas do próprio teste.

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Infrastructure.Tests --filter ArquivoMiniatura`;
  esperado: erro de compilação (propriedade inexistente).

- [ ] **Step 3: Schema, `db/alter-miniatura.sql` (idempotente: `IF COL_LENGTH(...) IS NULL ALTER TABLE ... ADD ...`, uma
  coluna e uma FK por bloco), entidades e mapeamento.** Aplicar no banco de dev:

```bash
MSYS_NO_PATHCONV=1 docker compose cp db/alter-miniatura.sql sqlserver:/tmp/alter-miniatura.sql
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/alter-miniatura.sql
```

Rodar o script uma segunda vez e ver que não muda nada (idempotência).

- [ ] **Step 4: Rodar e ver passar** — o mesmo filtro; esperado: PASS. Depois `dotnet build Rastreamento.slnx -warnaserror`.

- [ ] **Step 5: Commit** — `feat(miniatura): colunas da miniatura no schema, migracao e mapeamento`.

---

### Task 2: `ValidadorDeMiniatura`

**Files:**
- Create: `src/Rastreamento.Application/Arquivos/ValidadorDeMiniatura.cs`
- Create: `tests/Rastreamento.Application.Tests/Arquivos/PngDeTeste.cs` (helper: `byte[] Png(int largura, int altura,
  int bytesExtras = 0)` — assinatura PNG + chunk `IHDR` com a largura e a altura em big-endian; CRC não é conferido)
- Test: `tests/Rastreamento.Application.Tests/Arquivos/ValidadorDeMiniaturaTests.cs`

**Interfaces:**
- Produces: `public static class ValidadorDeMiniatura { public const int Largura = 320; public const int Altura = 240;
  public const int TamanhoMaximoEmBytes = 512 * 1024; public static string? Validar(byte[] conteudo); }` — `null` quando
  válida, a frase de recusa quando não. Também `PngDeTeste.Png(...)` para as tasks seguintes.

- [ ] **Step 1: Testes que falham**

```csharp
[Fact] public void Png_320_por_240_passa() => Assert.Null(ValidadorDeMiniatura.Validar(PngDeTeste.Png(320, 240)));
[Fact] public void Vazio_recusa() => Assert.Equal("A miniatura esta vazia.", ValidadorDeMiniatura.Validar([]));
[Fact] public void Sem_assinatura_png_recusa() =>
    Assert.Equal("A miniatura nao e um PNG.", ValidadorDeMiniatura.Validar(StlDeTeste.Binario()));
[Theory, InlineData(321, 240), InlineData(320, 241), InlineData(240, 320)]
public void Outra_dimensao_recusa(int l, int a) =>
    Assert.Equal("A miniatura precisa ter 320 x 240 pixels.", ValidadorDeMiniatura.Validar(PngDeTeste.Png(l, a)));
[Fact] public void Acima_de_512_KiB_recusa() =>
    Assert.Equal("A miniatura passa de 512 KiB.", ValidadorDeMiniatura.Validar(PngDeTeste.Png(320, 240, 512 * 1024)));
[Fact] public void Png_truncado_antes_do_IHDR_recusa() =>
    Assert.Equal("A miniatura nao e um PNG.", ValidadorDeMiniatura.Validar(PngDeTeste.Png(320, 240)[..12]));
```

(Use o helper de STL que já existe em `StlDeTeste`; se o nome do método for outro, use o que gera um STL binário válido.)

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Application.Tests --filter ValidadorDeMiniatura`.
- [ ] **Step 3: Implementar.** Ordem: vazio → tamanho → assinatura (8 bytes `89 50 4E 47 0D 0A 1A 0A`) → `IHDR` no offset
  12 com largura (offset 16) e altura (offset 20) big-endian. Sem decodificar a imagem.
- [ ] **Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** — `feat(miniatura): validador puro da miniatura`.

---

### Task 3: Envio conjunto e leitura no cadastro

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IArquivoDeComponenteRepository.cs`,
  `src/Rastreamento.Infrastructure/Persistence/ArquivoDeComponenteRepository.cs`
- Modify: `src/Rastreamento.Application/Arquivos/SolidoDoComponenteUseCase.cs`, `ArquivoDtos.cs`
- Modify: `src/Rastreamento.Api/Controllers/ComponentesController.cs`
- Modify: `specs/05-api-endpoints.md` (o `POST …/solido` com o campo `miniatura` e o limite novo; o `GET …/miniatura`)
- Test: `tests/Rastreamento.Application.Tests/Arquivos/SolidoDoComponenteUseCaseTests.cs` (o fake do repositório mora
  nele), `tests/Rastreamento.Infrastructure.Tests/Persistence/ArquivoDeComponenteRepositoryTests.cs`,
  `tests/Rastreamento.Api.Tests/SolidoEndpointsTests.cs`

**Interfaces:**
- Consumes: `ValidadorDeMiniatura.Validar`, `PngDeTeste.Png` (Task 2); as colunas da Task 1.
- Produces:
  - `Task<int?> GravarEVincularComoSolidoAsync(int componenteId, ArquivoDeComponente solido, ArquivoDeComponente?
    miniatura, CancellationToken ct)` — **substitui** a assinatura atual; grava os dois na mesma transação e põe
    `ArquivoMiniaturaId` no id da miniatura ou em `null` (C6).
  - `Task<ArquivoDeComponente?> ObterMiniaturaDoComponenteAsync(int componenteId, CancellationToken ct)`.
  - `SolidoDoComponenteUseCase.Enviar(int componenteId, string nomeOriginal, byte[] conteudo, byte[]? miniatura, int
    usuarioId, CancellationToken ct)` — valida o STL, depois a miniatura (C8); `NomeOriginal` da miniatura é
    `"miniatura.png"`.
  - `SolidoDoComponenteUseCase.ObterMiniatura(int componenteId, CancellationToken ct) : Task<Result<ArquivoDeMiniaturaDto>>`
    com `public sealed record ArquivoDeMiniaturaDto(byte[] Conteudo, string Versao)` (`Versao` = hexa minúsculo do
    `Sha256`).
  - `public static string VersaoDe(byte[] sha256)` em `ArquivoDtos.cs` (`Convert.ToHexStringLower`) — usada pelas tasks
    5 e 6.
  - `POST /componentes/{id}/solido` aceita `IFormFile? miniatura`; `RequestSizeLimit` passa a
    `ValidadorDeArquivoStl.TamanhoMaximoEmBytes + ValidadorDeMiniatura.TamanhoMaximoEmBytes + MargemDoCorpoMultipartEmBytes`.
  - `GET /componentes/{id}/miniatura` → `image/png`, `ETag: "<versao>"`, `Cache-Control: private, no-cache`; 304 quando
    `If-None-Match` bate; 404 sem miniatura ou sem Componente.

- [ ] **Step 1: Testes que falham (Application)** — `Enviar_com_miniatura_grava_os_dois`,
  `Enviar_sem_miniatura_zera_a_miniatura_anterior` (o fake guarda `ArquivoMiniaturaId` do Componente; com miniatura
  anterior, um envio sem miniatura deixa `null`), `Miniatura_invalida_recusa_o_envio_inteiro` (nada gravado; erro
  `"A miniatura nao e um PNG."`), `Stl_invalido_recusa_antes_de_olhar_a_miniatura` (erro do STL, mesmo com miniatura
  inválida), `ObterMiniatura_de_componente_sem_miniatura_e_NaoEncontrado`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** repositório, caso de uso e controller. A projeção de leitura não traz `Conteudo` de quem não
  pediu. O `TraduzirResultado` cuida de 400/404.
- [ ] **Step 4: Testes de Infrastructure e Api** — `Gravar_solido_com_miniatura_liga_as_duas_FKs_na_mesma_transacao`;
  `Post_solido_com_miniatura_e_Get_miniatura_devolve_o_png_com_ETag`; `Get_miniatura_com_If_None_Match_igual_devolve_304`;
  `Post_solido_sem_miniatura_apaga_a_ligacao_da_anterior`; `Post_solido_com_miniatura_de_outra_dimensao_e_400_e_nao_troca_o_solido`;
  e atualizar `RequestSizeLimit_do_envio_de_solido_usa_o_limite_do_validador_mais_a_margem` para o limite novo.
- [ ] **Step 5: Rodar** `dotnet test Rastreamento.slnx -m:1 --filter "Solido|Miniatura|ArquivoDeComponente"` e o build.
- [ ] **Step 6: Commit** — `feat(miniatura): miniatura junto com o solido e a leitura dela`.

---

### Task 4: Miniatura avulsa ("Gerar miniatura")

**Files:**
- Modify: os mesmos repositório, caso de uso e controller da Task 3; `specs/05-api-endpoints.md` (a rota avulsa e as
  recusas)
- Test: os mesmos arquivos de teste da Task 3

**Interfaces:**
- Consumes: Task 3.
- Produces:
  - `public enum ResultadoDaMiniaturaAvulsa { Gravada, ComponenteInexistente, SemSolido, SolidoMudou }` (Domain,
    junto da interface).
  - `Task<ResultadoDaMiniaturaAvulsa> GravarMiniaturaAvulsaAsync(int componenteId, ArquivoDeComponente miniatura, byte[]
    sha256DoSolido, CancellationToken ct)` — **na mesma transação**, lê o Componente e o `Sha256` do sólido atual,
    compara e só então grava e liga (fecha a corrida de C15).
  - `SolidoDoComponenteUseCase.EnviarMiniatura(int componenteId, byte[] miniatura, string solidoSha256, int usuarioId,
    CancellationToken ct) : Task<Result>` — hash ausente ou que não seja hexa de 64 caracteres: 400 `"Informe o hash do
    solido de que a miniatura foi desenhada."`; miniatura inválida: 400 do validador; `ComponenteInexistente`: 404;
    `SemSolido`: 409 `"Este Componente nao tem solido para gerar a miniatura."`; `SolidoMudou`: 409 `"O solido mudou
    depois que a miniatura foi desenhada. Gere de novo."`.
  - `POST /componentes/{id}/miniatura` (`PerfisDeEscrita`), `multipart` com `miniatura` e o campo `solidoSha256`;
    `RequestSizeLimit(ValidadorDeMiniatura.TamanhoMaximoEmBytes + MargemDoCorpoMultipartEmBytes)`; 204.

- [ ] **Step 1: Testes que falham** — `Miniatura_avulsa_grava_e_liga`, `Miniatura_avulsa_sem_solido_e_conflito`,
  `Miniatura_avulsa_de_solido_trocado_e_recusada`, `Miniatura_avulsa_com_hash_malformado_e_400`,
  `Miniatura_avulsa_substitui_a_existente` (Application); `Post_miniatura_avulsa_com_hash_certo_e_204` e
  `Post_miniatura_avulsa_por_operador_e_403` (Api).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar** o filtro da Task 3 e o build.
- [ ] **Step 5: Commit** — `feat(miniatura): rota avulsa que confere o solido`.

---

### Task 5: `miniaturaVersao` nos DTOs do Componente

**Files:**
- Modify: `IArquivoDeComponenteRepository.cs` / `ArquivoDeComponenteRepository.cs`, `src/Rastreamento.Application/Cadastros/Dtos.cs`,
  `CadastroDeComponenteUseCase.cs`, `specs/05-api-endpoints.md` (o campo nas respostas de `GET /componentes` e
  `GET /componentes/{id}`)
- Test: `tests/Rastreamento.Application.Tests/Cadastros/` (o arquivo de testes do `CadastroDeComponenteUseCase`), e o
  teste de endpoint de listagem/detalhe de Componentes em `tests/Rastreamento.Api.Tests/`

**Interfaces:**
- Consumes: `VersaoDe` (Task 3).
- Produces:
  - `Task<IReadOnlyDictionary<int, byte[]>> ObterSha256Async(IReadOnlyCollection<int> arquivoIds, CancellationToken ct)`
    — projeção sem `Conteudo`; id inexistente não aparece. O Panorama vai usar este método.
  - `ComponenteDto(..., bool TemSolido, string? MiniaturaVersao)` e `ComponenteDetalheDto(..., string? MiniaturaVersao)`
    (o campo vai **por último** nos dois). O remarks de `ComponenteDto` ganha uma frase: o hash é versão, não endereço,
    e por isso não contraria a regra de não expor id.
  - `Listar` faz **uma** consulta de hashes para a página inteira, não uma por item.

- [ ] **Step 1: Testes que falham** — `Listar_traz_a_versao_da_miniatura_de_quem_tem` (dois Componentes, um com
  miniatura: versão = hexa do `Sha256`, o outro `null`), `Detalhe_traz_a_versao_da_miniatura`,
  `Listar_consulta_os_hashes_uma_vez_por_pagina` (o fake conta chamadas).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** Ajustar os chamadores do construtor dos dois DTOs.
- [ ] **Step 4: Rodar** `dotnet test Rastreamento.slnx -m:1 --filter "Componente"` e o build.
- [ ] **Step 5: Commit** — `feat(miniatura): versao da miniatura nos DTOs do Componente`.

---

### Task 6: A miniatura no import do BOM

**Files:**
- Modify: `src/Rastreamento.Application/Importacao/ImportacaoDeEstruturaUseCase.Edicao.cs` (`EnviarSolidoPendente`,
  `Reimportar`, leitura nova), `ImportacaoDeEstruturaUseCase.Confirmacao.cs` (`Novo`, `Casado`, passos 4 e 5,
  `RegistrosComSolidoLigado`), `ImportacaoDeEstruturaUseCase.cs` (o dicionário de metadados), `AvaliadorDeImportacao.cs`
  (`Situacao`), `ImportacaoDtos.cs` (`SituacaoDoComponenteDto`)
- Modify: `src/Rastreamento.Domain/Abstractions/IImportacaoDeEstruturaRepository.cs` e
  `src/Rastreamento.Infrastructure/Persistence/ImportacaoDeEstruturaRepository.cs` (`ExcluirAsync`,
  `DesligarSolidosPendentesAsync`, `ObterMetadadosAsync`); `MetadadoDeSolido` ganha `byte[] Sha256` (a projeção o
  inclui; o nome continua, e o XML doc diz que serve também à miniatura)
- Modify: `src/Rastreamento.Api/Controllers/ImportacaoController.cs`; `specs/05-api-endpoints.md` (o campo `miniatura` no
  `POST` pendente, o `GET …/miniatura` pendente, `miniaturaVersao` e `miniaturaPendente` no registro)
- Test: `tests/Rastreamento.Application.Tests/Importacao/ImportacaoDeEstruturaUseCaseTests.Edicao.cs`,
  `AvaliadorDeImportacaoTests.cs`, `tests/Rastreamento.Api.Tests/ImportacaoEndpointsTests.cs`,
  `ConfirmacaoDeImportacaoTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3 (`ValidadorDeMiniatura`, `VersaoDe`, `PngDeTeste`).
- Produces:
  - `EnviarSolidoPendente(int id, int registroId, string versao, string nomeOriginal, byte[] conteudo, byte[]? miniatura,
    int usuarioId, CancellationToken ct)` — valida STL e depois miniatura (C8); grava os dois pendentes; `aposSalvar`
    apaga os **dois** anteriores; `aoPerderACorrida` restaura os dois ids e apaga os dois novos.
  - `ObterMiniaturaPendente(int id, int registroId, CancellationToken ct) : Task<Result<ArquivoDeMiniaturaDto>>`.
  - `SituacaoDoComponenteDto` ganha `string? MiniaturaVersao` e `bool MiniaturaPendente` (no fim). Regra de C12: **com STL
    pendente**, `MiniaturaVersao` = versão da miniatura pendente (ou `null`) e `MiniaturaPendente = true` se ela existe;
    **sem STL pendente**, a do catálogo e `MiniaturaPendente = false`.
  - `PlanoDeGravacao.Novo` e `.Casado` ganham `int? ArquivoMiniaturaId`; na confirmação, todo registro com STL pendente
    escreve `ArquivoMiniaturaId` no Componente (inclusive `null`: C6), e o desligamento zera os dois pendentes.
  - `POST /importacoes/{id}/componentes/{registroId}/solido` aceita `IFormFile? miniatura` (limite como na Task 3);
    `GET /importacoes/{id}/componentes/{registroId}/miniatura` → `image/png`, 404 sem corpo nos três casos.

- [ ] **Step 1: Testes que falham (Application)** — `Solido_pendente_com_miniatura_grava_os_dois`,
  `Trocar_solido_pendente_apaga_solido_e_miniatura_anteriores`, `Reimport_apaga_solido_e_miniatura_pendentes_do_registro_que_saiu`,
  `Corrida_perdida_apaga_solido_e_miniatura_novos`, e no avaliador os três casos de C12:
  `Versao_da_miniatura_pendente_quando_ha_solido_pendente`, `Sem_versao_quando_o_solido_pendente_veio_sem_miniatura`
  (mesmo com o catálogo tendo miniatura), `Versao_do_catalogo_sem_solido_pendente`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** o caso de uso, o avaliador, o repositório (`ExcluirAsync` coleta e apaga os dois tipos de
  pendente; `DesligarSolidosPendentesAsync` zera as duas colunas) e o controller.
- [ ] **Step 4: Testes de Api** — `Confirmar_liga_solido_e_miniatura_pendentes_ao_Componente_novo`,
  `Casado_com_solido_pendente_sem_miniatura_confirma_sem_miniatura`, `Descartar_rascunho_apaga_a_miniatura_pendente`,
  `Get_miniatura_pendente_inexistente_e_404_sem_corpo`.
- [ ] **Step 5: Rodar** `dotnet test Rastreamento.slnx -m:1 --filter "Importacao"` e o build.
- [ ] **Step 6: Commit** — `feat(miniatura): miniatura pendente no import do BOM`.

---

### Task 7: Desenho, hash e clientes HTTP no front

**Files:**
- Create: `web/src/solido/miniatura.ts`, `web/src/solido/hash.ts`
- Modify: `web/src/api/cadastros.ts`, `web/src/api/importacao.ts`
- Test: `web/src/solido/miniatura.test.ts`, `web/src/solido/hash.test.ts`, os testes existentes dos dois clientes (se
  houver; senão, no arquivo de teste mais próximo que já cubra `enviarSolido`)

**Interfaces:**
- Produces:
  - `export const LARGURA_DA_MINIATURA = 320`, `ALTURA_DA_MINIATURA = 240`, e as constantes de C3 com nome.
  - `export function cameraIsometrica(centro: [number, number, number], raio: number): { posicao: [number, number, number];
    meiaAltura: number; meiaLargura: number; perto: number; longe: number }` — pura: posição `centro + normalizar(1,1,1) ×
    raio × 4`, `meiaAltura = raio × 1.08`, `meiaLargura = meiaAltura × 320/240`.
  - `export async function desenharMiniatura(stl: ArrayBuffer): Promise<Blob | null>` — `import()` dinâmico de `three` e
    `STLLoader`, como o `VisualizadorDeSolido`; `WebGLRenderer({ alpha: true, antialias: true, preserveDrawingBuffer:
    true })`, `setPixelRatio(1)`, `setSize(320, 240)`, `canvas.toBlob(..., 'image/png')`; descarta o renderer e as
    geometrias no fim; **qualquer** exceção vira `null` (C13).
  - `export async function sha256Hex(dados: ArrayBuffer): Promise<string | null>` — `crypto.subtle.digest`, hexa
    minúsculo; `null` sem `crypto.subtle` (contexto inseguro; spec seção 8).
  - `enviarSolido(componenteId: number, arquivo: File, miniatura: Blob | null): Promise<void>` (campo `miniatura` com nome
    de arquivo `miniatura.png` só quando não nulo); `enviarMiniatura(componenteId: number, miniatura: Blob, solidoSha256:
    string): Promise<void>`; `caminhoDaMiniatura(componenteId: number): string`; `ComponenteDto.miniaturaVersao: string |
    null` (e no detalhe); `enviarSolidoPendente(..., miniatura: Blob | null)`; `caminhoDaMiniaturaPendente(id: number,
    registroId: number): string`; `miniaturaVersao: string | null` e `miniaturaPendente: boolean` no tipo do registro da
    importação.

- [ ] **Step 1: Testes que falham** — `cameraIsometrica`: `olha de (1,1,1) a partir do centro` (a posição menos o centro é
  paralela a `(1,1,1)`), `folga de 8 por cento sobre o raio`, `quadro 4 por 3`; `sha256Hex`: `hash de "abc"` =
  `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`, `sem crypto.subtle devolve null`;
  `desenharMiniatura`: `devolve null quando o WebGL nao existe` (no jsdom, sem mock: o `WebGLRenderer` lança e a função
  devolve `null`); `enviarSolido` manda `miniatura` no `FormData` só quando há blob.
- [ ] **Step 2: Rodar e ver falhar** — `npm test -- --run src/solido src/api`.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar e ver passar**; `npm run build`.
- [ ] **Step 5: Commit** — `feat(miniatura): desenho isometrico, hash e clientes HTTP`.

---

### Task 8: Primitiva `MiniaturaDoSolido`

**Files:**
- Create: `web/src/components/MiniaturaDoSolido.tsx`, `web/src/components/MiniaturaDoSolido.test.tsx`
- Modify: `CLAUDE.md` (bullet novo na «Interface»: exibir a miniatura usa `MiniaturaDoSolido`, que busca pelo `apiFetch`
  com cache por versão, nunca `<img src="/api/...">`, porque o `<img>` não manda o token)

**Interfaces:**
- Consumes: `apiFetch` (`web/src/api/client.ts`).
- Produces: `export function MiniaturaDoSolido(props: { caminho: string; versao: string | null; alt: string; className?:
  string })` — sem versão, o espaço neutro (`bg-fundo`, borda `borda`, proporção 4:3, `role="img"` com o `alt`); com
  versão, busca uma vez por `caminho + versao` (cache de módulo: `Map<string, Promise<string | null>>` com a URL de
  `blob:`), mostra o espaço neutro enquanto carrega e quando a busca falha. Exporta `limparCacheDeMiniaturas()` para os
  testes.

- [ ] **Step 1: Testes que falham** — `sem versao mostra o espaco neutro com o nome`, `com versao mostra a imagem`
  (`getByRole('img', { name })` com `src` `blob:`; `URL.createObjectURL` mockado), `nao busca de novo a mesma versao`
  (dois `render` com o mesmo caminho e versão → um `fetch`), `versao nova busca de novo`, `falha na busca mostra o espaco
  neutro`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar e ver passar**; `npm run build`.
- [ ] **Step 5: Commit** — `feat(miniatura): primitiva MiniaturaDoSolido com cache por versao`.

---

### Task 9: `UploadDeSolido` desenha e envia junto

**Files:**
- Modify: `web/src/components/UploadDeSolido.tsx` e `UploadDeSolido.test.tsx`
- Modify: os chamadores — `web/src/pages/ComponenteDetalhePage.tsx` e
  `web/src/importacao/PainelDoComponenteDaImportacao.tsx` (os dois usos) e os testes deles
  (`ComponenteDetalhePage.test.tsx`, `ConferenciaDeImportacaoPage.test.tsx`)

**Interfaces:**
- Consumes: `desenharMiniatura` (Task 7), `enviarSolido`/`enviarSolidoPendente` (Task 7).
- Produces: prop `enviar: (arquivo: File, miniatura: Blob | null) => Promise<void>`. Fluxo: checagem de tamanho de hoje →
  status "Gerando miniatura…" → `desenharMiniatura(await arquivo.arrayBuffer())` → status "Enviando…" → `enviar`. Com
  miniatura `null`, depois do envio bem-sucedido, um texto neutro (`role="status"`, sem tom de erro): "O sólido foi
  enviado, mas a miniatura não pôde ser gerada neste aparelho." (C13).

- [ ] **Step 1: Testes que falham** (`vi.mock('../solido/miniatura')`) — `desenha e envia o solido com a miniatura`,
  `mostra gerando miniatura e depois enviando`, `sem miniatura envia so o solido e avisa sem tom de erro`,
  `arquivo grande demais nao chega a desenhar` (o `desenharMiniatura` não é chamado).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** e ajustar os dois chamadores (passam a miniatura adiante ao cliente HTTP).
- [ ] **Step 4: Rodar** `npm test -- --run` (a suíte inteira, porque três arquivos de teste mudam) e `npm run build`.
- [ ] **Step 5: Commit** — `feat(miniatura): upload do solido gera e envia a miniatura`.

---

### Task 10: Miniatura e "Gerar miniatura" no detalhe do Componente

**Files:**
- Modify: `web/src/pages/ComponenteDetalhePage.tsx`, `ComponenteDetalhePage.test.tsx`

**Interfaces:**
- Consumes: `MiniaturaDoSolido` (Task 8), `desenharMiniatura`, `sha256Hex`, `enviarMiniatura`, `caminhoDaMiniatura`,
  `caminhoDoSolido` (Task 7).
- Produces: na seção do sólido, a `MiniaturaDoSolido` (alt: "{código} — {descrição}") ao lado do upload, quando
  `temSolido`. Sob `usePodeEscrever('componentes')`, com `temSolido && miniaturaVersao === null`, um `Botao` secundário
  "Gerar miniatura": baixa o STL (`apiFetch(caminhoDoSolido(id))` → `arrayBuffer`), calcula `sha256Hex`, desenha, envia
  por `enviarMiniatura` e recarrega o Componente. Trava durante a escrita ("Gerando…"). Falhas: desenho `null` → "A
  miniatura não pôde ser gerada neste aparelho."; hash `null` → "Este endereço não permite gerar a miniatura. Use o
  endereço com HTTPS."; recusa do servidor → a `mensagemDeErro` dela (a frase do 409 de C15 chega como está), num
  `BannerDeErro` da seção.

- [ ] **Step 1: Testes que falham** — `mostra a miniatura quando o componente tem`, `oferece gerar miniatura a quem
  escreve quando falta`, `nao oferece gerar miniatura a quem so le`, `gerar miniatura envia o hash do solido baixado e
  recarrega`, `gerar miniatura com solido trocado mostra a recusa` (409 com `{ erro }`), `gerar miniatura sem webgl avisa`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar** `npm test -- --run` e `npm run build`.
- [ ] **Step 5: Commit** — `feat(miniatura): miniatura e gerar miniatura no detalhe do Componente`.

---

### Task 11: Conferência do usuário no navegador (task do usuário)

**Produto não é código** (classe 1 da seção «Pular a review» do `CLAUDE.md`): esta task não tem review de código; o
controlador **para e avisa o usuário** ao chegar aqui. A review de branch inteira só é despachada **depois** dela.

- [ ] **Step 1 (controlador):** subir API e front locais com a massa de demonstração; preparar dois STL de teste (um
  pequeno, um perto de 16 MiB), e, se ajudar, capturas por agente das telas (servem de apoio, não substituem a
  conferência).
- [ ] **Step 2 (usuário):** no PC, subir um STL num Componente e ver a miniatura no detalhe; trocar o sólido e ver a
  imagem trocar; usar "Gerar miniatura" num Componente que ficou sem; subir um STL pendente na conferência do import,
  confirmar e ver a miniatura no Componente.
- [ ] **Step 3 (usuário):** no **celular Android**, subir o STL grande e medir o tempo entre escolher o arquivo e o
  "Enviando…". Teto: **5 s** (C14). Passou do teto: volta à conversa antes de qualquer código (reduzir arestas não entra
  de antemão).
- [ ] **Step 4:** registrar no ledger o que o usuário viu e decidiu; ajustes viram tasks novas antes da review de branch.

---

## Depois das tasks

Review de branch inteira (modelo mais capaz), fix wave, e as contagens do `CLAUDE.md` que mudarem (`UploadDeSolido`,
`VisualizadorDeSolido`, primitivas) remedidas com o comando registrado ao lado de cada uma. O plano do Panorama é
escrito depois que esta branch estiver pronta para o merge.
