# Fase 2B — Sólido 3D da Peça: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** dar ao catálogo um upload de sólido STL guardado em blob, exibi-lo num viewer 3D, e passar a cobrar a segunda metade da regra 18 — Peça cujo `Componente` de origem não tem sólido é recusada.

**Architecture:** o binário vive em `dbo.ArquivoDeComponente` (`VARBINARY(MAX)`), e `dbo.Componente` aponta para ele por `ArquivoSolidoId`, **sem propriedade de navegação no EF** — é o desenho, não a disciplina, que impede um `Include` arrastar o blob para uma listagem paginada. A API ganha dois endpoints (`POST`/`GET .../solido`); a Application ganha um validador de STL puro e um caso de uso; a cobrança da regra 18 entra em `MontagemDeEstruturaUseCase.CriarPeca`, usando o `IReceitaPadraoRepository` que o caso de uso **já** recebe. No front, duas primitivas novas e uma prop nova no `SeletorComBusca`.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core Database First contra SQL Server, xUnit; React 19 + TypeScript + Vite, Vitest + Testing Library, Tailwind v4 por tokens; `three` (dependência nova, import dinâmico).

**Spec:** `docs/superpowers/specs/2026-09-12-fase-2b-solido-3d-design.md` — em conflito, **a spec ganha do texto deste plano**.

## Global Constraints

- **Formato aceito: STL, e só.** STEP sai do domínio. `.SLDPRT` nunca foi aceito.
- **Limite de arquivo: 16 MiB (16.777.216 bytes).** Teto do Kestrel é 30.000.000 bytes — a folga é pequena, dobrar o limite o ultrapassa.
- **`specs/02-modelo-de-dados.sql` é a fonte de verdade do schema.** Database First: o schema muda lá primeiro, o EF sai dele. Nunca `Add-Migration`.
- **Nomes de domínio em português, espelhando o DDL** (`ArquivoDeComponente`, `Componente`); nomes técnicos em inglês (`Repository`, `UseCase`, `DTO`, `Controller`).
- **Citação em comentário e prosa: pelo NOME**, nunca por `arquivo.ext:NN` nem por distância relativa ("quatro linhas acima"). Vale para todo texto novo deste plano.
- **Cores só pelos tokens** de `web/src/index.css`. Nenhum token novo nesta fase; `text-gray-*` e afins são reprovados por guarda executável.
- **Tela nova começa por `<Pagina>`**; campo, botão, banner, estado vazio e estado de carregando vêm das primitivas de `web/src/components/`.
- **Teste de tela** declara `// @vitest-environment jsdom` no topo, usa `web/src/testes/api.ts` e chama `afterEach(cleanup)` explicitamente — este projeto não usa `globals: true`.
- **`npm run build` faz parte do ciclo**, não só `npm test`: o Vitest não faz typecheck, e erro de tipo em `.test.tsx` quebra o build sem quebrar a suíte.
- **`dotnet build Rastreamento.slnx -warnaserror` tem de ficar em 0 warnings.**
- **Asserção sobre contagem global de tabela compartilhada é flaky por construção.** Escope pelo próprio prefixo do teste; `[Collection]` não atravessa processo.
- **Gating de perfil vai na AÇÃO, não no link**, e o `try/catch` do 403 é obrigatório: esconder botão não é segurança.

### Baseline medida (nesta bancada, em 2026-09-12, no `main` `f2ee813`)

| Suíte | Contagem | Como foi medida |
|---|---|---|
| Backend | **529** — Application 249 · Infrastructure 68 · Api 212 | `dotnet test --list-tests` por projeto (descobre por reflexão, não executa) |
| Front | **495 passando, 39 arquivos** | `npm test -- --run`, verde |

**Os deltas por task abaixo são ESTIMATIVA, não medição.** Cada task mede o próprio delta ao fechar e, se divergir, **corrige a baseline das tasks seguintes na mesma passada** — total absoluto propaga erro task a task, e fechar a contagem de uma não basta.

### Uma armadilha de ambiente que vale saber antes de começar

O arquivo `web/src/components/SeletorComBusca.test.tsx` tem **flake conhecido e anterior a esta fase** (~14% num dia sob carga concorrente, ~2-4% registrado antes, 0 em 10 com o arquivo isolado; dois testes distintos dele já falharam). **A Task 8 mexe justamente nesse arquivo.** Se ele falhar durante a Task 8: **grave o nome do teste que falhou antes de rodar de novo** — rodar de novo destrói a amostra — e não atribua a falha à task sem medir.

---

## Estrutura de arquivos

**Backend — criar:**

| Arquivo | Responsabilidade |
|---|---|
| `src/Rastreamento.Domain/Entities/ArquivoDeComponente.cs` | a entidade do arquivo |
| `src/Rastreamento.Domain/Abstractions/IArquivoDeComponenteRepository.cs` | contrato: gravar+vincular numa transação, ler o blob |
| `src/Rastreamento.Infrastructure/Persistence/ArquivoDeComponenteRepository.cs` | implementação EF |
| `src/Rastreamento.Infrastructure/Persistence/Configurations/ArquivoDeComponenteConfiguration.cs` | mapeamento |
| `src/Rastreamento.Application/Arquivos/ValidadorDeArquivoStl.cs` | validação pura das três camadas — sem banco, sem I/O |
| `src/Rastreamento.Application/Arquivos/SolidoDoComponenteUseCase.cs` | `Enviar` e `Obter` |
| `src/Rastreamento.Application/Arquivos/ArquivoDtos.cs` | `ArquivoDeSolidoDto` |
| `tests/Rastreamento.Application.Tests/Arquivos/ValidadorDeArquivoStlTests.cs` | as três camadas |
| `tests/Rastreamento.Application.Tests/Arquivos/SolidoDoComponenteUseCaseTests.cs` | o caso de uso, com fakes |
| `tests/Rastreamento.Infrastructure.Tests/Persistence/ArquivoDeComponenteMapeamentoTests.cs` | mapeamento contra SQL Server real |
| `tests/Rastreamento.Api.Tests/SolidoEndpointsTests.cs` | os dois endpoints ponta a ponta |
| `tests/TestData/StlDeTeste.cs` *(ver Task 3, Passo 1 — decide onde mora)* | gerador do cubo STL de 684 bytes |

**Backend — modificar:**

| Arquivo | O que muda |
|---|---|
| `specs/02-modelo-de-dados.sql` | tabela nova, `Componente.ArquivoSolidoId`, remoção de `ArquivoSolido`, comentário reescrito |
| `specs/01-dominio-e-regras-de-negocio.md` | regra 18 e glossário: STL, não "STEP ou STL" |
| `specs/06-roadmap-mvp.md` | "STEP ou STL" e a frase do STEP AP242; termo de hospedagem |
| `specs/03-arquitetura-tecnica.md`, `specs/00-visao-geral.md`, `CLAUDE.md` | VPS (Task 0) e `ALTER`s idempotentes (Task 1) |
| `src/Rastreamento.Domain/Entities/Componente.cs` | `ArquivoSolidoId`; o comentário de "não mapeadas de propósito" sai |
| `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs` | `DbSet<ArquivoDeComponente>` |
| `src/Rastreamento.Application/Cadastros/Dtos.cs` | `ComponenteDto.TemSolido` |
| `src/Rastreamento.Application/Cadastros/CadastroDeComponenteUseCase.cs` | `Projetar` preenche `TemSolido` |
| `src/Rastreamento.Api/Controllers/ComponentesController.cs` | os dois endpoints |
| `src/Rastreamento.Api/Program.cs` | DI do repositório e do caso de uso |
| `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs` | a guarda da regra 18 |
| `tests/Rastreamento.Application.Tests/Estrutura/CriarPecaTests.cs` | helper `Montar` semeia Componente com sólido |
| `tests/Rastreamento.Api.Tests/EstruturaEndpointsTests.cs` | helper `NovoComponente` semeia sólido |

**Front — criar:** `web/src/components/UploadDeSolido.tsx` (+ teste), `web/src/components/VisualizadorDeSolido.tsx` (+ teste).

**Front — modificar:** `web/src/api/cadastros.ts` (`temSolido`, `enviarSolido`, `urlDoSolido`), `web/src/testes/api.ts` (`respostaBinaria`), `web/src/components/SeletorComBusca.tsx` (+ teste), `web/src/pages/ComponenteDetalhePage.tsx`, `web/src/pages/AgrupamentoDetalhePage.tsx`, `web/package.json`.

---

## Task 0: A virada de hospedagem — on-premise para VPS

**Produto é documentação; nenhuma linha de código, nenhum teste.** Esta task **roda o gate de review** como qualquer outra: `specs/` vira texto do TCC, e a Fase 1C já mediu uma task de documentação, delta de teste zero, cuja review achou duas afirmações falsas que nenhum teste quebraria. **Não invoque a exceção de "produto não é código" aqui.**

**Files:**
- Modify: `specs/03-arquitetura-tecnica.md`
- Modify: `specs/00-visao-geral.md`
- Modify: `specs/06-roadmap-mvp.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: nada.
- Produces: nada em código. As tasks seguintes não dependem desta — ela vem primeiro por instrução do usuário, não por dependência técnica.

- [ ] **Step 1: Medir o alcance antes de editar**

```bash
grep -rc -i "on-premise" specs/ CLAUDE.md | grep -v ":0"
```

Esperado (medido em 2026-09-12): **8 ocorrências em 4 arquivos** — `specs/03-arquitetura-tecnica.md` com 5, e `specs/00-visao-geral.md`, `specs/06-roadmap-mvp.md` e `CLAUDE.md` com 1 cada. (A saída do `grep -c` vem no formato `arquivo:contagem`; **aquele número é contagem, não número de linha** — a regra de citação do projeto proíbe citar por `arquivo.ext:NN`, e uma saída de contagem colide com esse padrão léxico sem ser o defeito que a regra descreve. Daí a tabela em prosa aqui.) Se o número divergir, **o grep ganha**: registre o novo no relatório da task.

- [ ] **Step 2: Reescrever a hospedagem em `specs/03-arquitetura-tecnica.md`**

São 5 ocorrências, e cada uma quer tratamento próprio:

1. a linha de SQL Server em "Banco de dados" — passa a dizer que o SQL Server roda **na VPS**;
2. a linha de deploy final da mesma seção — o deploy aponta para a **VPS**, não para "o servidor on-premise real";
3. o título da seção **"Hospedagem on-premise"** → **"Hospedagem"**, com um parágrafo novo dizendo: VPS paga com domínio próprio, escolhida em 2026-09-12; motivo declarado pelo usuário — deixar o link de acesso mais "produto";
4. a linha de frontend estático — troca o termo; **preserve intacta** a atenção de ordem de pipeline (`UseStaticFiles`/`MapFallbackToFile` antes da guarda `/api`), que continua valendo e fica **mais** provável numa VPS de origem única;
5. a linha de CI/CD — deploy manual continua, agora "na VPS".

- [ ] **Step 3: Acrescentar a seção de dívida de endurecimento em `specs/03-arquitetura-tecnica.md`**

Na seção "Pontos em aberto" (que hoje diz que resta apenas o hosting exato — e essa frase **deixa de ser verdade**, então reescreva-a), acrescente os três itens, cada um com o gatilho **"obrigatório antes do primeiro deploy público"**:

1. **TLS é pré-requisito de funcionamento, não melhoria.** O cookie de refresh é gravado com `Secure = true`, e navegador não grava cookie `Secure` em HTTP. Sem TLS na VPS, o login funciona e o refresh **nunca**: a sessão morre em 15 minutos e o sintoma não aponta a causa. `UseHttpsRedirection` não existe em `src/`.
2. **`ForwardedHeaders` não está configurado.** Com proxy reverso na frente, o rate limit por IP do `/auth/login` vira global (todos compartilham o IP do proxy) e o log de auth grava o IP do proxy. As duas defesas continuam de pé medindo a coisa errada.
3. **Retrancar conta não tem limite, e a exposição mudou.** O trade-off já está registrado no `CLAUDE.md`; o que muda é que era risco de colega em rede interna e passa a ser risco de qualquer um. **Não é para consertar aqui** — é para o trade-off não continuar escrito como se o contexto fosse o mesmo.

**Escreva também que o endurecimento NÃO é desta fase:** vira item próprio na fila, em branch separada. Decisão do usuário, motivo registrado — misturar infraestrutura na branch da 2B poluiria a review de branch dela.

- [ ] **Step 4: `specs/00-visao-geral.md`**

A linha "Hospedagem" da tabela de contexto passa a dizer VPS com domínio próprio.

- [ ] **Step 5: `specs/06-roadmap-mvp.md` — esta é a que NÃO se reescreve como as outras**

A ocorrência vive no argumento contra ler `.SLDASM`: a API COM do SolidWorks exigiria SolidWorks licenciado na máquina do servidor, *"inviável para uma API web on-premise"*. **Ajuste o termo e preserve o argumento** — numa VPS ele fica **mais forte**, não mais fraco. Reescrever isso como se fosse a mesma troca de palavra das outras quatro enfraqueceria uma decisão de domínio já tomada.

- [ ] **Step 6: `CLAUDE.md` — a linha de stack e a correção da `SigningKey`**

Duas edições:

1. A linha de stack `- **Banco**: SQL Server, on-premise` passa a nomear a VPS.
2. Na seção "Ainda em aberto (deferido de propósito)", a menção a **`SigningKey` como segredo de ambiente** está **imprecisa e precisa ser corrigida**: `JwtOptionsValidator` **já recusa no startup** o valor de placeholder commitado e exige no mínimo 32 bytes. O que falta é **procedimento de deploy** (fornecê-la por variável de ambiente na VPS), não código. Deixá-la listada como dívida de código faz alguém reimplementar uma guarda que existe.

- [ ] **Step 7: Verificar que nenhuma afirmação nova é falsa**

```bash
grep -rn "UseHttpsRedirection\|ForwardedHeaders" src/ | grep -v "^src.*//" ; echo "---" ; grep -rn "Secure = true" src/Rastreamento.Api/Controllers/AuthController.cs ; echo "---" ; grep -rn "SigningKeyPlaceholder" src/Rastreamento.Application/Auth/JwtOptionsValidator.cs
```

Esperado: nada de `UseHttpsRedirection`/`ForwardedHeaders` como chamada real (só comentários), `Secure = true` presente, e o validador referenciando o placeholder. **Se qualquer um divergir, a afirmação correspondente da Step 3 está errada e tem de ser reescrita, não commitada.**

- [ ] **Step 8: Commit**

```bash
git add specs/03-arquitetura-tecnica.md specs/00-visao-geral.md specs/06-roadmap-mvp.md CLAUDE.md
git commit -m "docs(fase-2b): hospedagem passa de on-premise para VPS com dominio"
```

**Delta de teste: 0** (documentação). Baseline segue backend 529 / front 495.

---

## Task 1: Schema, e a virada de STEP para STL nas specs de domínio

**Files:**
- Modify: `specs/02-modelo-de-dados.sql`
- Modify: `specs/01-dominio-e-regras-de-negocio.md`
- Modify: `specs/06-roadmap-mvp.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: nada.
- Produces: o schema que a Task 2 mapeia — tabela `dbo.ArquivoDeComponente` com as colunas `Id`, `NomeOriginal`, `Conteudo`, `TamanhoEmBytes`, `Sha256`, `CriadoEm`, `CriadoPorUsuarioId`; e `dbo.Componente.ArquivoSolidoId INT NULL`.

- [ ] **Step 1: Acrescentar a tabela em `specs/02-modelo-de-dados.sql`, ANTES de `CREATE TABLE dbo.Componente`**

A ordem importa: `Componente` passa a ter FK para esta tabela, e ela tem FK para `dbo.Usuario` (que já está definida acima, na parte de auth).

```sql
/* Blob do arquivo de Componente. Tabela SEPARADA de dbo.Componente de proposito: o catalogo e
   listado paginado, e VARBINARY(MAX) na mesma linha convidaria a arrastar megabytes numa
   listagem. Serve as DUAS colunas de arquivo de Componente -- hoje so o solido tem consumidor
   (Fase 2B); a foto entra depois com uma FK nova e um validador diferente, sem mudar esta tabela. */
CREATE TABLE dbo.ArquivoDeComponente (
    Id                  INT IDENTITY(1,1)   NOT NULL,
    NomeOriginal        NVARCHAR(260)       NOT NULL, -- o nome que o usuario subiu: exibicao na tela e Content-Disposition do download
    Conteudo            VARBINARY(MAX)      NOT NULL,
    TamanhoEmBytes      INT                 NOT NULL, -- exibir tamanho sem tocar no blob. INT chega a 2 GB, muito acima do limite de 16 MiB da aplicacao
    Sha256              BINARY(32)          NOT NULL, -- integridade, e permite reconhecer subida repetida do mesmo arquivo
    CriadoEm            DATETIME2           NOT NULL CONSTRAINT DF_ArquivoDeComponente_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CriadoPorUsuarioId  INT                 NOT NULL,
    CONSTRAINT PK_ArquivoDeComponente PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ArquivoDeComponente_CriadoPorUsuario FOREIGN KEY (CriadoPorUsuarioId)
        REFERENCES dbo.Usuario(Id),
    -- Arquivo de zero byte nao e arquivo. O limite SUPERIOR (16 MiB) fica na aplicacao, nao aqui:
    -- excecao de CHECK sobe como SqlException e vira 500, e o cliente merece 400 -- mesmo criterio
    -- de Componente.Tipo e Agrupamento.Tipo.
    CONSTRAINT CK_ArquivoDeComponente_Tamanho CHECK (TamanhoEmBytes > 0)
);
```

- [ ] **Step 2: Em `dbo.Componente`, trocar `ArquivoSolido` por `ArquivoSolidoId`**

Remova a coluna `ArquivoSolido NVARCHAR(260) NULL` **e o bloco de comentário acima dela**, e ponha no lugar:

```sql
    -- Solido 3D (STL) do Componente, em dbo.ArquivoDeComponente. NULLABLE de proposito: a
    -- obrigatoriedade e de negocio e vale para Peca de Pedido, nao para toda linha de catalogo (um
    -- Componente 'Bruto' nao tem solido), e o banco nao consegue distinguir os dois casos aqui --
    -- ver regra 18 em 01. Quem cobra e MontagemDeEstruturaUseCase.CriarPeca.
    --
    -- Por que BLOB em tabela propria, e nao caminho de arquivo (2026-09-12, Fase 2B): esta coluna
    -- ERA NVARCHAR(260) com caminho relativo, e o comentario de entao argumentava contra
    -- VARBINARY(MAX). A reversao e a que aquela nota previa -- ela se fechava com "decisao
    -- reversivel enquanto ninguem gravar dado de verdade", e ninguem gravou: nenhum codigo jamais
    -- escreveu a coluna antiga. O ganho do blob e OPERACIONAL, nao de espaco (o binario ocupa
    -- disco igual nos dois desenhos): backup unico, sem pasta nem permissao de escrita como passo
    -- de deploy, e registro que nao pode divergir do arquivo.
    --
    -- O argumento antigo que a reversao CUSTA, registrado para nao ser redescoberto: o pipeline de
    -- silhuetas da busca por foto (fora das fases, condicionado a spike) queria o arquivo em disco
    -- para alimentar a ferramenta CAD. Com blob, ele tera de materializar um arquivo temporario.
    -- Custo pequeno e localizado, mas real.
    ArquivoSolidoId INT                 NULL,
```

E, na lista de constraints da tabela, acrescente:

```sql
    CONSTRAINT FK_Componente_ArquivoSolido FOREIGN KEY (ArquivoSolidoId)
        REFERENCES dbo.ArquivoDeComponente(Id),
```

`ArquivoFoto` fica **exatamente como está**.

- [ ] **Step 3: Aplicar no banco de dev**

O banco é descartável (autorização do dono do projeto), mas aqui não há motivo para recriá-lo: são três `ALTER` idempotentes.

```bash
docker compose up -d
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF OBJECT_ID('dbo.ArquivoDeComponente') IS NULL CREATE TABLE dbo.ArquivoDeComponente (Id INT IDENTITY(1,1) NOT NULL, NomeOriginal NVARCHAR(260) NOT NULL, Conteudo VARBINARY(MAX) NOT NULL, TamanhoEmBytes INT NOT NULL, Sha256 BINARY(32) NOT NULL, CriadoEm DATETIME2 NOT NULL CONSTRAINT DF_ArquivoDeComponente_CriadoEm DEFAULT (SYSUTCDATETIME()), CriadoPorUsuarioId INT NOT NULL, CONSTRAINT PK_ArquivoDeComponente PRIMARY KEY CLUSTERED (Id), CONSTRAINT FK_ArquivoDeComponente_CriadoPorUsuario FOREIGN KEY (CriadoPorUsuarioId) REFERENCES dbo.Usuario(Id), CONSTRAINT CK_ArquivoDeComponente_Tamanho CHECK (TamanhoEmBytes > 0));"
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Componente','ArquivoSolidoId') IS NULL ALTER TABLE dbo.Componente ADD ArquivoSolidoId INT NULL CONSTRAINT FK_Componente_ArquivoSolido FOREIGN KEY REFERENCES dbo.ArquivoDeComponente(Id);"
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Componente','ArquivoSolido') IS NOT NULL ALTER TABLE dbo.Componente DROP COLUMN ArquivoSolido;"
```

- [ ] **Step 4: Conferir que o banco bate com o `.sql`**

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento -h -1 \
  -Q "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Componente') ORDER BY column_id;"
```

Esperado: `ArquivoSolidoId` presente, **`ArquivoSolido` ausente**, `ArquivoFoto` presente.

- [ ] **Step 5: Os três `ALTER` do Step 3 vão para o `CLAUDE.md`**

Na seção de comandos, junto dos outros blocos idempotentes, com uma frase dizendo que **estes NÃO são no-op nesta máquina** (diferente dos `ALTER` de `ArquivoSolido`/`ArquivoFoto`, que são, porque o banco foi regenerado em 2026-08-04 a partir do `.sql`).

- [ ] **Step 6: A virada de STEP para STL — as 4 ocorrências**

```bash
grep -rn "STEP ou STL" specs/ CLAUDE.md
```

Esperado: **4** — duas em `specs/01-dominio-e-regras-de-negocio.md` (a linha do glossário de `Componente.ArquivoSolido` e o corpo da regra 18), uma em `specs/02-modelo-de-dados.sql` (já resolvida no Step 2), uma em `specs/06-roadmap-mvp.md`.

Nas de `01`, escreva **STL** e o motivo: STEP exigiria parser WASM de terceiros no navegador, enquanto o three.js lê STL nativo — decisão de 2026-09-12, do usuário. Mencione que `.SLDPRT` continua fora, por ser proprietário. **Na regra 18, troque também a referência a `Componente.ArquivoSolido` por `Componente.ArquivoSolidoId`**, senão a regra passa a citar uma coluna que não existe mais.

- [ ] **Step 7: A quinta consequência, que não é uma ocorrência de "STEP ou STL"**

```bash
grep -n "STEP" specs/06-roadmap-mvp.md
```

Além da ocorrência do Step 6, há **2** na alternativa descartada "STEP AP242/AP214 da montagem". O mérito delas não muda (parser pesado, part number ausente), **mas uma frase fica falsa**: o texto diz que o STEP da montagem *"seria o mesmo arquivo que já serve ao sólido"*. Com STL-only, não é mais. Reescreva **só essa frase**; preserve o resto do argumento.

- [ ] **Step 8: Conferir que o schema não é criado pelo EF, e commitar**

```bash
grep -rn "EnsureCreated\|Migrate()" src/ | grep -v "^src.*//" ; echo "(vazio = correto, Database First)"
dotnet build Rastreamento.slnx -warnaserror
git add specs/02-modelo-de-dados.sql specs/01-dominio-e-regras-de-negocio.md specs/06-roadmap-mvp.md CLAUDE.md
git commit -m "feat(fase-2b): schema do solido em blob, e o formato aceito passa a ser STL"
```

**Delta de teste: 0** (schema e prosa). Baseline segue backend 529 / front 495.

---

## Task 2: Entidade, mapeamento EF e repositório do arquivo

**Files:**
- Create: `src/Rastreamento.Domain/Entities/ArquivoDeComponente.cs`
- Create: `src/Rastreamento.Domain/Abstractions/IArquivoDeComponenteRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/ArquivoDeComponenteConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/ArquivoDeComponenteRepository.cs`
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/ArquivoDeComponenteMapeamentoTests.cs`
- Modify: `src/Rastreamento.Domain/Entities/Componente.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`
- Modify: `src/Rastreamento.Api/Program.cs`

**Interfaces:**
- Consumes: o schema da Task 1.
- Produces:
  - `class ArquivoDeComponente { int Id; string NomeOriginal; byte[] Conteudo; int TamanhoEmBytes; byte[] Sha256; DateTime CriadoEm; int CriadoPorUsuarioId; }`
  - `Componente.ArquivoSolidoId` → `int?`
  - `interface IArquivoDeComponenteRepository` com:
    - `Task<int> GravarEVincularComoSolidoAsync(int componenteId, ArquivoDeComponente arquivo, CancellationToken ct)` — devolve o `Id` do arquivo gravado
    - `Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(int componenteId, CancellationToken ct)`

- [ ] **Step 1: Escrever o teste de mapeamento que falha**

Crie `tests/Rastreamento.Infrastructure.Tests/Persistence/ArquivoDeComponenteMapeamentoTests.cs`. **Ele escreve em `dbo.Componente`, então entra na `ColecaoQueEscreveEmComponente`** — a coleção existente que serializa as classes que disputam essa tabela. Sem isso, o xUnit roda esta classe em paralelo com `ComponenteMappingTests` contra o mesmo banco.

```csharp
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Infrastructure.Tests.Persistence;

[Collection(nameof(ColecaoQueEscreveEmComponente))]
public class ArquivoDeComponenteMapeamentoTests
{
  private static byte[] ConteudoDeTeste() => [0x01, 0x02, 0x03, 0x04];

  private static ArquivoDeComponente NovoArquivo(int usuarioId) =>
      new()
      {
        NomeOriginal = "peca-de-teste.stl",
        Conteudo = ConteudoDeTeste(),
        TamanhoEmBytes = ConteudoDeTeste().Length,
        Sha256 = SHA256.HashData(ConteudoDeTeste()),
        CriadoPorUsuarioId = usuarioId,
      };

  [Fact]
  public async Task Arquivo_grava_e_le_o_blob_de_volta_byte_a_byte()
  {
    await using var db = FabricaDeContexto.Criar();
    var usuarioId = await db.Usuarios.Select(u => u.Id).FirstAsync();

    var arquivo = NovoArquivo(usuarioId);
    db.Set<ArquivoDeComponente>().Add(arquivo);
    await db.SaveChangesAsync();

    db.ChangeTracker.Clear();
    var lido = await db.Set<ArquivoDeComponente>().AsNoTracking()
        .SingleAsync(a => a.Id == arquivo.Id);

    Assert.Equal(ConteudoDeTeste(), lido.Conteudo);
    Assert.Equal("peca-de-teste.stl", lido.NomeOriginal);
    Assert.Equal(32, lido.Sha256.Length);
    // CriadoEm vem do DEFAULT do banco (Database First), nao do C#: se o mapeamento tentar
    // gravar o default do DateTime, isto vira 0001-01-01.
    Assert.True(lido.CriadoEm > new DateTime(2020, 1, 1));

    db.Set<ArquivoDeComponente>().Remove(lido);
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task Componente_guarda_o_ArquivoSolidoId_e_a_listagem_nao_traz_o_blob()
  {
    await using var db = FabricaDeContexto.Criar();
    var usuarioId = await db.Usuarios.Select(u => u.Id).FirstAsync();

    var arquivo = NovoArquivo(usuarioId);
    db.Set<ArquivoDeComponente>().Add(arquivo);
    await db.SaveChangesAsync();

    var componente = new Componente
    {
      Codigo = $"ARQ-{Guid.NewGuid():N}"[..12],
      Descricao = "Componente do teste de arquivo",
      Tipo = "Fabricado",
      Ativo = true,
      ArquivoSolidoId = arquivo.Id,
    };
    db.Componentes.Add(componente);
    await db.SaveChangesAsync();

    db.ChangeTracker.Clear();
    // Escopado pelo PROPRIO id, nao por contagem global da tabela: Api.Tests escreve em
    // dbo.Componente em outro processo, e [Collection] nao atravessa processo.
    var lido = await db.Componentes.AsNoTracking().SingleAsync(c => c.Id == componente.Id);
    Assert.Equal(arquivo.Id, lido.ArquivoSolidoId);

    // A prova de que o blob nao vem junto e de DESENHO: `Componente` nao tem propriedade de
    // navegacao para `ArquivoDeComponente`, entao nao existe `Include` a escrever aqui. Se alguem
    // acrescentar a navegacao, esta linha para de compilar e o desenho volta a ser discutido.
    Assert.Empty(
        db.Model.FindEntityType(typeof(Componente))!.GetNavigations());

    db.Componentes.Remove(lido);
    await db.SaveChangesAsync();
    db.Set<ArquivoDeComponente>().Remove(
        await db.Set<ArquivoDeComponente>().SingleAsync(a => a.Id == arquivo.Id));
    await db.SaveChangesAsync();
  }
}
```

**Antes de escrever este arquivo, leia `tests/Rastreamento.Infrastructure.Tests/Persistence/ComponenteMappingTests.cs`** e use o mesmo mecanismo de criação de contexto que ele usa — o nome `FabricaDeContexto.Criar()` acima é um **espaço reservado pelo nome do papel**, não uma promessa de que a classe se chama assim. Se o projeto criar o contexto por outro caminho, use aquele e registre no relatório da task.

- [ ] **Step 2: Rodar e ver falhar**

```bash
docker compose up -d
dotnet test tests/Rastreamento.Infrastructure.Tests --filter "ArquivoDeComponenteMapeamentoTests"
```

Esperado: **falha de compilação** — `ArquivoDeComponente` não existe, `Componente.ArquivoSolidoId` não existe. É a falha certa.

- [ ] **Step 3: A entidade**

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// Blob de um arquivo de Componente. Tabela SEPARADA de Componente de proposito: o catalogo e
/// listado paginado, e um VARBINARY(MAX) na mesma linha convidaria a arrastar megabytes numa
/// listagem. Hoje so o solido (STL) tem consumidor; a foto entra depois pela mesma tabela.
/// </summary>
public class ArquivoDeComponente
{
  public int Id { get; set; }

  /// <summary>O nome que o usuario subiu: exibicao na tela e Content-Disposition do download.</summary>
  public string NomeOriginal { get; set; } = string.Empty;

  public byte[] Conteudo { get; set; } = [];

  /// <summary>
  /// Redundante com <c>Conteudo.Length</c> de proposito: permite exibir o tamanho sem SELECT no
  /// blob.
  /// </summary>
  public int TamanhoEmBytes { get; set; }

  public byte[] Sha256 { get; set; } = [];

  /// <summary>Vem do DEFAULT do banco (Database First), nao do C#.</summary>
  public DateTime CriadoEm { get; set; }

  public int CriadoPorUsuarioId { get; set; }
}
```

- [ ] **Step 4: `Componente` ganha a FK escalar, e o comentário antigo sai**

Em `src/Rastreamento.Domain/Entities/Componente.cs`, **remova** o comentário que diz que `ArquivoSolido`/`ArquivoFoto` não são mapeadas à espera da Fase 2, e ponha:

```csharp
  /// <summary>
  /// Solido 3D (STL) em <see cref="ArquivoDeComponente"/>. Nullable porque a obrigatoriedade e de
  /// negocio e vale para Peca de Pedido, nao para toda linha de catalogo (regra 18) — quem cobra e
  /// <c>MontagemDeEstruturaUseCase.CriarPeca</c>.
  ///
  /// <para>
  /// Escalar, SEM propriedade de navegacao, e isso e desenho e nao esquecimento: sem navegacao nao
  /// existe <c>Include</c> que arraste o VARBINARY(MAX) para uma listagem paginada de catalogo. O
  /// blob so e lido pelo repositorio do arquivo, por consulta propria.
  /// </para>
  /// </summary>
  public int? ArquivoSolidoId { get; set; }

  // ArquivoFoto existe em dbo.Componente e NAO e mapeada aqui: a foto esta fora do escopo da Fase
  // 2B (decisao do usuario). Coluna nullable, entao o INSERT do EF sem ela e valido.
```

- [ ] **Step 5: Mapeamento e `DbSet`**

`ArquivoDeComponenteConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ArquivoDeComponenteConfiguration : IEntityTypeConfiguration<ArquivoDeComponente>
{
  public void Configure(EntityTypeBuilder<ArquivoDeComponente> b)
  {
    b.ToTable("ArquivoDeComponente");
    b.HasKey(a => a.Id);
    b.Property(a => a.NomeOriginal).HasMaxLength(260).IsRequired();
    b.Property(a => a.Conteudo).IsRequired();
    b.Property(a => a.Sha256).HasColumnType("binary(32)").IsRequired();
    // CriadoEm e gravado pelo DEFAULT do banco: sem isto o EF manda o default do DateTime.
    b.Property(a => a.CriadoEm).ValueGeneratedOnAdd();
    // Sem HasDefaultValue: Database First — o default vive so no .sql.
  }
}
```

Em `RastreamentoDbContext`, acrescente ao lado dos outros:

```csharp
  public DbSet<ArquivoDeComponente> ArquivosDeComponente => Set<ArquivoDeComponente>();
```

- [ ] **Step 6: O contrato do repositório**

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IArquivoDeComponenteRepository
{
  /// <summary>
  /// Grava o arquivo e aponta <c>Componente.ArquivoSolidoId</c> para ele, numa UNICA transacao.
  /// Devolve o Id do arquivo gravado.
  ///
  /// <para>
  /// Precisa de transacao porque sao dois <c>SaveChanges</c>: o Id do arquivo so existe depois do
  /// primeiro, e sem navegacao (deliberado) o EF nao resolve a ligacao sozinho. Meio-termo —
  /// arquivo gravado sem ninguem apontando para ele — nao e estado alcancavel. Mesmo molde de
  /// transacao explicita de <c>SubstituirFilhosAsync</c>.
  /// </para>
  ///
  /// <para>
  /// Substituicao: o solido anterior do Componente, se houver, deixa de ser referenciado e
  /// PERMANECE na tabela. Consequencia aceita nesta fase — vira historico, nao lixo coletado: nao
  /// existe remocao de solido (a spec registra por que), e apagar o antigo aqui perderia o unico
  /// registro de que a peca teve outra geometria.
  /// </para>
  /// </summary>
  Task<int> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente arquivo, CancellationToken ct);

  /// <summary>
  /// O solido do Componente, COM o blob. Null quando o componente nao existe ou nao tem solido —
  /// o caso de uso traduz os dois para 404, porque a diferenca nao muda o que o cliente faz.
  /// </summary>
  Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(int componenteId, CancellationToken ct);
}
```

- [ ] **Step 7: A implementação**

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ArquivoDeComponenteRepository : IArquivoDeComponenteRepository
{
  private readonly RastreamentoDbContext _db;

  public ArquivoDeComponenteRepository(RastreamentoDbContext db) => _db = db;

  public async Task<int> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente arquivo, CancellationToken ct)
  {
    await using var transacao = await _db.Database.BeginTransactionAsync(ct);

    _db.ArquivosDeComponente.Add(arquivo);
    await _db.SaveChangesAsync(ct);

    var componente = await _db.Componentes.SingleAsync(c => c.Id == componenteId, ct);
    componente.ArquivoSolidoId = arquivo.Id;
    await _db.SaveChangesAsync(ct);

    await transacao.CommitAsync(ct);
    return arquivo.Id;
  }

  public async Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(
      int componenteId, CancellationToken ct)
  {
    // Duas consultas em vez de um JOIN com navegacao: a navegacao e justamente o que nao existe,
    // por desenho. O primeiro SELECT le SO o id — nao toca no blob.
    var arquivoId = await _db.Componentes.AsNoTracking()
        .Where(c => c.Id == componenteId)
        .Select(c => c.ArquivoSolidoId)
        .SingleOrDefaultAsync(ct);

    if (arquivoId is null) return null;

    return await _db.ArquivosDeComponente.AsNoTracking()
        .SingleOrDefaultAsync(a => a.Id == arquivoId.Value, ct);
  }
}
```

- [ ] **Step 8: Registrar no DI**

Em `Program.cs`, ao lado de `IComponenteRepository`:

```csharp
builder.Services.AddScoped<IArquivoDeComponenteRepository, ArquivoDeComponenteRepository>();
```

- [ ] **Step 9: Rodar e ver passar**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test tests/Rastreamento.Infrastructure.Tests --filter "ArquivoDeComponenteMapeamentoTests"
```

Esperado: **2 passando, 0 warnings no build**.

- [ ] **Step 10: Mutação — provar que o teste mede o que diz medir**

Duas mutações, e o que cada uma tem de matar:

1. Em `ArquivoDeComponenteConfiguration`, **remova** `b.Property(a => a.CriadoEm).ValueGeneratedOnAdd();` e rode. Esperado: `Arquivo_grava_e_le_o_blob_de_volta_byte_a_byte` **falha** na asserção de `CriadoEm` (ou no insert). Restaure.
2. Em `Componente`, acrescente uma propriedade de navegação (`public ArquivoDeComponente? ArquivoSolido { get; set; }`) e rode. Esperado: `Componente_guarda_o_ArquivoSolidoId_e_a_listagem_nao_traz_o_blob` **falha** no `Assert.Empty` das navegações. Restaure.

**Registre no relatório o resultado das duas, inclusive se alguma NÃO matar** — mutação que não mata é a informação mais valiosa aqui.

- [ ] **Step 11: Commit**

```bash
git add src/Rastreamento.Domain src/Rastreamento.Infrastructure src/Rastreamento.Api/Program.cs tests/Rastreamento.Infrastructure.Tests
git commit -m "feat(fase-2b): entidade, mapeamento e repositorio do arquivo de componente"
```

**Delta de teste estimado: +2** (Infrastructure). Baseline estimada ao fim: backend **531** (App 249 · Infra 70 · Api 212) / front 495. **Meça e corrija se divergir.**

---

## Task 3: Validador de STL e o caso de uso

**Files:**
- Create: `src/Rastreamento.Application/Arquivos/ValidadorDeArquivoStl.cs`
- Create: `src/Rastreamento.Application/Arquivos/ArquivoDtos.cs`
- Create: `src/Rastreamento.Application/Arquivos/SolidoDoComponenteUseCase.cs`
- Create: `tests/Rastreamento.Application.Tests/Arquivos/StlDeTeste.cs`
- Create: `tests/Rastreamento.Application.Tests/Arquivos/ValidadorDeArquivoStlTests.cs`
- Create: `tests/Rastreamento.Application.Tests/Arquivos/SolidoDoComponenteUseCaseTests.cs`
- Modify: `src/Rastreamento.Api/Program.cs`

**Interfaces:**
- Consumes: `IArquivoDeComponenteRepository` e `IComponenteRepository` (este já existe, com `ObterPorIdAsync` **rastreado**).
- Produces:
  - `static class ValidadorDeArquivoStl` com `const int TamanhoMaximoEmBytes = 16 * 1024 * 1024;` e `static string? Validar(string nomeOriginal, byte[] conteudo)` — devolve a mensagem do primeiro problema, ou `null` se estiver válido (mesmo molde de `Validar` em `CadastroDeComponenteUseCase`).
  - `sealed record ArquivoDeSolidoDto(string NomeOriginal, byte[] Conteudo)`
  - `sealed class SolidoDoComponenteUseCase` com
    `Task<Result> Enviar(int componenteId, string nomeOriginal, byte[] conteudo, int usuarioId, CancellationToken ct)`
    e `Task<Result<ArquivoDeSolidoDto>> Obter(int componenteId, CancellationToken ct)`.

- [ ] **Step 1: O gerador de STL de teste**

Crie `tests/Rastreamento.Application.Tests/Arquivos/StlDeTeste.cs`. **Gerado em código, não arquivo binário commitado** — assim o próprio teste exercita a fórmula `84 + 50n` em vez de confiar num blob opaco.

```csharp
namespace Rastreamento.Application.Tests.Arquivos;

/// <summary>
/// STL binario valido, gerado em codigo. Um cubo tem 6 faces, 2 triangulos cada = 12 triangulos,
/// logo 84 + 50*12 = 684 bytes EXATOS. A geometria e irrelevante para a validacao (nenhuma camada
/// olha as coordenadas); o que importa e a FORMA do arquivo.
/// </summary>
public static class StlDeTeste
{
  public const int Triangulos = 12;

  public const int TamanhoEsperado = 84 + 50 * Triangulos;   // 684

  public static byte[] CuboBinario() => Binario(Triangulos);

  /// <summary>STL binario bem-formado com a contagem pedida. `n` = 0 produz um arquivo de 84
  /// bytes, que e estruturalmente coerente e ainda assim nao e solido nenhum.</summary>
  public static byte[] Binario(int triangulos)
  {
    var bytes = new byte[84 + 50 * triangulos];
    // Os 80 bytes de cabecalho ficam zerados de proposito: um STL binario de verdade costuma
    // trazer texto ali, e ha arquivos reais cujo cabecalho comeca com "solid" — e por isso que o
    // validador tenta a forma BINARIA primeiro.
    BitConverter.GetBytes(triangulos).CopyTo(bytes, 80);
    return bytes;
  }

  /// <summary>Binario cujo cabecalho comeca com "solid": a armadilha classica de sniffing.</summary>
  public static byte[] BinarioComCabecalhoQueDizSolid()
  {
    var bytes = Binario(Triangulos);
    "solid cubo exportado"u8.ToArray().CopyTo(bytes, 0);
    return bytes;
  }

  public static byte[] Ascii() => System.Text.Encoding.UTF8.GetBytes(
      """
      solid cubo
        facet normal 0 0 1
          outer loop
            vertex 0 0 0
            vertex 1 0 0
            vertex 1 1 0
          endloop
        endfacet
      endsolid cubo
      """);
}
```

- [ ] **Step 2: Escrever os testes do validador, que falham**

Crie `ValidadorDeArquivoStlTests.cs`:

```csharp
using Rastreamento.Application.Arquivos;

namespace Rastreamento.Application.Tests.Arquivos;

public class ValidadorDeArquivoStlTests
{
  [Fact]
  public void Stl_binario_bem_formado_passa()
  {
    Assert.Null(ValidadorDeArquivoStl.Validar("cubo.stl", StlDeTeste.CuboBinario()));
  }

  [Fact]
  public void O_cubo_de_teste_tem_684_bytes_exatos()
  {
    // Guarda a fixture, nao o validador: se o gerador quebrar, os outros testes passariam a
    // afirmar sobre um arquivo diferente do que este plano descreve.
    Assert.Equal(684, StlDeTeste.CuboBinario().Length);
    Assert.Equal(StlDeTeste.TamanhoEsperado, StlDeTeste.CuboBinario().Length);
  }

  [Fact]
  public void Stl_ascii_bem_formado_passa()
  {
    Assert.Null(ValidadorDeArquivoStl.Validar("cubo.stl", StlDeTeste.Ascii()));
  }

  [Fact]
  public void Binario_cujo_cabecalho_comeca_com_solid_passa_como_binario()
  {
    // A armadilha que obriga a tentar a forma binaria ANTES da ASCII: este arquivo satisfaz
    // "comeca com solid" e NAO e ASCII. Se a ordem inverter, ele e lido como ASCII e a fórmula
    // 84+50n nunca e conferida.
    Assert.Null(ValidadorDeArquivoStl.Validar(
        "cubo.stl", StlDeTeste.BinarioComCabecalhoQueDizSolid()));
  }

  [Fact]
  public void Extensao_diferente_de_stl_e_recusada()
  {
    var erro = ValidadorDeArquivoStl.Validar("cubo.step", StlDeTeste.CuboBinario());
    Assert.NotNull(erro);
    Assert.Contains(".stl", erro);
  }

  [Theory]
  [InlineData("CUBO.STL")]
  [InlineData("cubo.Stl")]
  public void Extensao_stl_e_aceita_em_qualquer_caixa(string nome)
  {
    Assert.Null(ValidadorDeArquivoStl.Validar(nome, StlDeTeste.CuboBinario()));
  }

  [Fact]
  public void Arquivo_vazio_e_recusado()
  {
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", []));
  }

  [Fact]
  public void Acima_do_limite_de_16_MiB_e_recusado()
  {
    var grande = new byte[ValidadorDeArquivoStl.TamanhoMaximoEmBytes + 1];
    var erro = ValidadorDeArquivoStl.Validar("cubo.stl", grande);
    Assert.NotNull(erro);
    Assert.Contains("16", erro);
  }

  [Fact]
  public void Binario_com_contagem_de_triangulos_que_nao_bate_com_o_tamanho_e_recusado()
  {
    // O coracao da terceira camada: 12 triangulos declarados, um triangulo a menos gravado.
    var bytes = StlDeTeste.Binario(StlDeTeste.Triangulos);
    var truncado = bytes[..^50];
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", truncado));
  }

  [Fact]
  public void Binario_com_zero_triangulos_e_recusado()
  {
    // 84 bytes, estruturalmente coerente, e nao e solido nenhum.
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", StlDeTeste.Binario(0)));
  }

  [Fact]
  public void Pdf_renomeado_para_stl_e_recusado()
  {
    // Este e o caso que motiva a terceira camada existir. Sem ela, o arquivo sobe e o defeito
    // aparece la no viewer, longe da causa.
    var pdf = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", pdf));
  }

  [Fact]
  public void Texto_que_comeca_com_solid_mas_nao_tem_facet_e_recusado()
  {
    var quase = System.Text.Encoding.UTF8.GetBytes("solid mentira\nendsolid mentira\n");
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", quase));
  }
}
```

- [ ] **Step 3: Rodar e ver falhar**

```bash
dotnet test tests/Rastreamento.Application.Tests --filter "ValidadorDeArquivoStlTests"
```

Esperado: **falha de compilação** — `ValidadorDeArquivoStl` não existe.

- [ ] **Step 4: Implementar o validador**

```csharp
using System.Text;

namespace Rastreamento.Application.Arquivos;

/// <summary>
/// Valida um arquivo de solido em tres camadas: extensao, tamanho e ESTRUTURA. A terceira e a que
/// importa — sem ela um PDF renomeado para .stl sobe, e o defeito so aparece no viewer, longe da
/// causa.
///
/// <para>
/// Puro de proposito: sem banco, sem I/O, sem <c>IFormFile</c>. Assim a camada mais sujeita a
/// arquivo real estranho e a mais barata de testar.
/// </para>
/// </summary>
public static class ValidadorDeArquivoStl
{
  /// <summary>
  /// 16 MiB. Raciocinio: /50 bytes por triangulo = ~335 mil triangulos, malha fina de sobra para
  /// peca de metalurgia, e cabe no limite default do corpo de requisicao do Kestrel (30.000.000
  /// bytes). A folga e pequena — dobrar este numero ultrapassa o teto do Kestrel, e ai deixa de
  /// ser constante e passa a ser configuracao de pipeline.
  /// </summary>
  public const int TamanhoMaximoEmBytes = 16 * 1024 * 1024;

  private const int CabecalhoBinarioEmBytes = 80;

  private const int ContagemEmBytes = 4;

  private const int BytesPorTriangulo = 50;

  private const string ErroDeExtensao = "O arquivo do solido deve ter extensao .stl.";

  private const string ErroDeArquivoVazio = "O arquivo esta vazio.";

  private static readonly string ErroDeTamanho =
      $"O arquivo passa de 16 MiB ({TamanhoMaximoEmBytes} bytes).";

  private const string ErroDeEstrutura =
      "O arquivo nao e um STL valido (nem binario nem ASCII).";

  /// <summary>
  /// Mensagem do primeiro problema, ou null se estiver valido. Molde de <c>Validar</c> em
  /// <c>CadastroDeComponenteUseCase</c>.
  /// </summary>
  public static string? Validar(string nomeOriginal, byte[] conteudo)
  {
    if (!nomeOriginal.EndsWith(".stl", StringComparison.OrdinalIgnoreCase))
      return ErroDeExtensao;

    if (conteudo.Length == 0) return ErroDeArquivoVazio;
    if (conteudo.Length > TamanhoMaximoEmBytes) return ErroDeTamanho;

    // BINARIO PRIMEIRO, e a ordem e a regra, nao preferencia: ha STL binario de verdade cujos 80
    // bytes de cabecalho comecam com "solid" (o exportador escreve um texto livre ali). Testando
    // ASCII primeiro, esse arquivo seria lido como ASCII e a formula 84+50n nunca seria conferida.
    if (EhBinarioCoerente(conteudo)) return null;
    if (EhAsciiCoerente(conteudo)) return null;

    return ErroDeEstrutura;
  }

  /// <summary>
  /// 80 bytes de cabecalho + uint32 little-endian com a contagem + 50 bytes por triangulo. Logo o
  /// arquivo coerente tem EXATAMENTE 84 + 50n bytes, com n lido do proprio arquivo.
  /// </summary>
  private static bool EhBinarioCoerente(byte[] conteudo)
  {
    if (conteudo.Length < CabecalhoBinarioEmBytes + ContagemEmBytes) return false;

    var triangulos = BitConverter.ToUInt32(conteudo, CabecalhoBinarioEmBytes);
    // Zero triangulo e estruturalmente coerente e nao e solido nenhum — recusa aqui, nao adiante.
    if (triangulos == 0) return false;

    // Em long para a multiplicacao nao estourar antes da comparacao: `triangulos` e uint32.
    var esperado = (long)CabecalhoBinarioEmBytes + ContagemEmBytes + (long)triangulos * BytesPorTriangulo;
    return conteudo.Length == esperado;
  }

  private static bool EhAsciiCoerente(byte[] conteudo)
  {
    // Le so o comeco: um ASCII de 16 MiB nao precisa virar string inteira para provar a forma.
    var amostra = Encoding.UTF8.GetString(
        conteudo, 0, Math.Min(conteudo.Length, 4096));

    return amostra.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase)
        && amostra.Contains("facet normal", StringComparison.OrdinalIgnoreCase);
  }
}
```

- [ ] **Step 5: Rodar e ver passar**

```bash
dotnet test tests/Rastreamento.Application.Tests --filter "ValidadorDeArquivoStlTests"
```

Esperado: **13 passando** (11 `[Fact]` + 2 casos do `[Theory]`).

- [ ] **Step 6: Mutação do validador — três, e a terceira é a que interessa**

1. **Inverta a ordem** das duas tentativas (`EhAsciiCoerente` antes de `EhBinarioCoerente`). Esperado: `Binario_cujo_cabecalho_comeca_com_solid_passa_como_binario` continua passando (o ASCII-check acha "solid" mas **não** acha "facet normal" nos 4096 primeiros bytes de um binário zerado, então cai no binário de qualquer forma) — **se for isso que acontecer, o teste NÃO está medindo a ordem, e isso tem de ir para o relatório.** Considere fortalecer a fixture fazendo o cabeçalho conter também `facet normal`, e diga no relatório o que mediu.
2. **Troque `conteudo.Length == esperado` por `>=`.** Esperado: `Binario_com_contagem_de_triangulos_que_nao_bate_com_o_tamanho_e_recusado` **falha**? Não: truncar deixa o arquivo **menor**, e `>=` continua recusando. **Acrescente então um teste com bytes de sobra no fim** (`bytes.Concat(new byte[10])`), que é o caso que só `==` pega. Isto é um defeito deste plano achado por mutação — corrija-o na task e registre.
3. **Remova a guarda `triangulos == 0`.** Esperado: `Binario_com_zero_triangulos_e_recusado` falha. Restaure.

- [ ] **Step 7: O DTO e o caso de uso — teste primeiro**

Crie `SolidoDoComponenteUseCaseTests.cs`. Use os fakes existentes de `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` para `IComponenteRepository`; **leia o arquivo antes** e reaproveite o fake de componente que já está lá, em vez de escrever um segundo. Para `IArquivoDeComponenteRepository`, escreva um fake novo no próprio arquivo de teste:

```csharp
using Rastreamento.Application.Arquivos;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Arquivos;

public class FakeArquivoDeComponenteRepo : IArquivoDeComponenteRepository
{
  /// <summary>Id alto e fora da faixa dos ids de catalogo usados nos testes (1, 2, 10, 11), para
  /// que uma projecao que devolva o campo errado nao acerte por coincidencia numerica.</summary>
  private int _proximoId = 700;

  public List<ArquivoDeComponente> Gravados { get; } = [];

  public Dictionary<int, ArquivoDeComponente> SolidoPorComponente { get; } = [];

  public List<int> VinculadosA { get; } = [];

  public Task<int> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente arquivo, CancellationToken ct)
  {
    arquivo.Id = _proximoId++;
    Gravados.Add(arquivo);
    VinculadosA.Add(componenteId);
    SolidoPorComponente[componenteId] = arquivo;
    return Task.FromResult(arquivo.Id);
  }

  public Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(
      int componenteId, CancellationToken ct) =>
      Task.FromResult(SolidoPorComponente.GetValueOrDefault(componenteId));
}

public class SolidoDoComponenteUseCaseTests
{
  private const int UsuarioId = 42;

  [Fact]
  public async Task Enviar_grava_o_arquivo_e_vincula_ao_componente()
  {
    var (useCase, componentes, arquivos) = Montar(ComponenteDeCatalogo(10));

    var resultado = await useCase.Enviar(
        10, "cubo.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    var gravado = Assert.Single(arquivos.Gravados);
    Assert.Equal("cubo.stl", gravado.NomeOriginal);
    Assert.Equal(StlDeTeste.TamanhoEsperado, gravado.TamanhoEmBytes);
    Assert.Equal(32, gravado.Sha256.Length);
    Assert.Equal(UsuarioId, gravado.CriadoPorUsuarioId);
    // Vinculado ao componente PEDIDO, nao a qualquer um: com um componente so no fake, um literal
    // no lugar do parametro passaria (achado B11 da Fase 1A).
    Assert.Equal(10, Assert.Single(arquivos.VinculadosA));
    Assert.Equal(StlDeTeste.CuboBinario(), gravado.Conteudo);
    _ = componentes;
  }

  [Fact]
  public async Task Enviar_para_componente_inexistente_da_NaoEncontrado_e_nao_grava()
  {
    var (useCase, _, arquivos) = Montar();   // nenhum componente cadastrado

    var resultado = await useCase.Enviar(
        999, "cubo.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Empty(arquivos.Gravados);
  }

  [Fact]
  public async Task Enviar_arquivo_invalido_da_Validacao_e_nao_grava()
  {
    var (useCase, _, arquivos) = Montar(ComponenteDeCatalogo(10));

    var resultado = await useCase.Enviar(
        10, "cubo.stl", [1, 2, 3], UsuarioId, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Empty(arquivos.Gravados);
  }

  [Fact]
  public async Task Enviar_valida_ANTES_de_ler_o_componente()
  {
    // Ordem que importa: arquivo invalido para componente inexistente responde VALIDACAO, nao
    // NaoEncontrado. Sem isto, um cliente descobre quais ids de componente existem mandando
    // arquivo lixo — o erro vira oraculo de enumeracao de catalogo.
    var (useCase, _, _) = Montar();

    var resultado = await useCase.Enviar(
        999, "cubo.step", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
  }

  [Fact]
  public async Task Substituir_grava_o_novo_e_deixa_o_componente_apontando_para_ele()
  {
    var (useCase, _, arquivos) = Montar(ComponenteDeCatalogo(10));

    await useCase.Enviar(10, "antigo.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);
    await useCase.Enviar(10, "novo.stl", StlDeTeste.Ascii(), UsuarioId, CancellationToken.None);

    Assert.Equal(2, arquivos.Gravados.Count);
    Assert.Equal("novo.stl", arquivos.SolidoPorComponente[10].NomeOriginal);
  }

  [Fact]
  public async Task Obter_devolve_nome_e_conteudo()
  {
    var (useCase, _, _) = Montar(ComponenteDeCatalogo(10));
    await useCase.Enviar(10, "cubo.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    var resultado = await useCase.Obter(10, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("cubo.stl", resultado.Valor!.NomeOriginal);
    Assert.Equal(StlDeTeste.CuboBinario(), resultado.Valor!.Conteudo);
  }

  [Fact]
  public async Task Obter_de_componente_sem_solido_da_NaoEncontrado()
  {
    var (useCase, _, _) = Montar(ComponenteDeCatalogo(10));

    var resultado = await useCase.Obter(10, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
  }
}
```

**Complete o arquivo com os dois helpers** `Montar(params Componente[] componentes)` e `ComponenteDeCatalogo(int id)`, no molde de `Montar` em `CriarPecaTests` — o `Montar` devolve a tupla `(SolidoDoComponenteUseCase, <fake de componente>, FakeArquivoDeComponenteRepo)` e `ComponenteDeCatalogo` cria um `Componente` com `Id`, `Codigo`, `Descricao`, `Tipo = "Fabricado"` e `Ativo = true`. Use o fake de `IComponenteRepository` que já existe nos `Fakes.cs` de `Cadastros`.

- [ ] **Step 8: Rodar e ver falhar**

```bash
dotnet test tests/Rastreamento.Application.Tests --filter "SolidoDoComponenteUseCaseTests"
```

Esperado: falha de compilação — `SolidoDoComponenteUseCase` e `ArquivoDeSolidoDto` não existem.

- [ ] **Step 9: Implementar o DTO e o caso de uso**

`ArquivoDtos.cs`:

```csharp
namespace Rastreamento.Application.Arquivos;

/// <summary>
/// O solido pronto para o controller responder. NAO carrega Id nem hash: o cliente identifica o
/// recurso pelo id do COMPONENTE (e por ele que a rota pergunta), e expor um id de arquivo abriria
/// um segundo caminho para o mesmo recurso.
/// </summary>
public sealed record ArquivoDeSolidoDto(string NomeOriginal, byte[] Conteudo);
```

`SolidoDoComponenteUseCase.cs`:

```csharp
using System.Security.Cryptography;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Arquivos;

/// <summary>
/// Envio e leitura do solido 3D de um Componente (regra 18, Fase 2B). Substitui em vez de
/// versionar do ponto de vista do Componente: o arquivo anterior continua na tabela, mas ninguem
/// aponta para ele. NAO existe remocao — ver a spec da fase.
/// </summary>
public sealed class SolidoDoComponenteUseCase
{
  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";

  private const string ErroDeSolidoNaoEncontrado = "Este Componente nao tem solido.";

  private readonly IComponenteRepository _componentes;
  private readonly IArquivoDeComponenteRepository _arquivos;

  public SolidoDoComponenteUseCase(
      IComponenteRepository componentes, IArquivoDeComponenteRepository arquivos)
  {
    _componentes = componentes;
    _arquivos = arquivos;
  }

  public async Task<Result> Enviar(
      int componenteId, string nomeOriginal, byte[] conteudo, int usuarioId, CancellationToken ct)
  {
    // Valida ANTES de ler o componente, e a ordem e deliberada: se a existencia do componente
    // fosse checada primeiro, mandar arquivo lixo para ids sequenciais distinguiria "existe" de
    // "nao existe" pelo TIPO do erro, e o endpoint viraria oraculo de enumeracao de catalogo.
    var invalido = ValidadorDeArquivoStl.Validar(nomeOriginal, conteudo);
    if (invalido is not null) return Result.Falha(invalido, TipoDeErro.Validacao);

    if (await _componentes.ObterPorIdAsync(componenteId, ct) is null)
      return Result.Falha(ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    var arquivo = new ArquivoDeComponente
    {
      NomeOriginal = nomeOriginal,
      Conteudo = conteudo,
      TamanhoEmBytes = conteudo.Length,
      Sha256 = SHA256.HashData(conteudo),
      CriadoPorUsuarioId = usuarioId,
    };
    await _arquivos.GravarEVincularComoSolidoAsync(componenteId, arquivo, ct);

    return Result.Ok();
  }

  /// <summary>
  /// Componente inexistente e componente sem solido respondem o MESMO NaoEncontrado: a diferenca
  /// nao muda o que o cliente faz, e o repositorio ja devolve null nos dois casos.
  /// </summary>
  public async Task<Result<ArquivoDeSolidoDto>> Obter(int componenteId, CancellationToken ct)
  {
    var arquivo = await _arquivos.ObterSolidoDoComponenteAsync(componenteId, ct);
    return arquivo is null
        ? Result<ArquivoDeSolidoDto>.Falha(ErroDeSolidoNaoEncontrado, TipoDeErro.NaoEncontrado)
        : Result<ArquivoDeSolidoDto>.Ok(new ArquivoDeSolidoDto(arquivo.NomeOriginal, arquivo.Conteudo));
  }
}
```

- [ ] **Step 10: Registrar no DI, rodar e ver passar**

Em `Program.cs`, ao lado do repositório da Task 2:

```csharp
builder.Services.AddScoped<SolidoDoComponenteUseCase>();
```

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test tests/Rastreamento.Application.Tests --filter "Arquivos"
```

Esperado: **20 passando** (13 do validador + 7 do caso de uso), 0 warnings.

- [ ] **Step 11: Mutação do caso de uso**

1. **Troque a ordem**: mova a validação para **depois** da leitura do componente. Esperado: `Enviar_valida_ANTES_de_ler_o_componente` **falha**. Restaure.
2. **Troque `componenteId` por um literal `10`** na chamada a `GravarEVincularComoSolidoAsync`. Esperado: `Enviar_grava_o_arquivo_e_vincula_ao_componente` **passa** (o componente do teste É o 10) — ou seja, essa asserção **não** pega a mutação. Se for o caso, **acrescente um teste com dois componentes no fake** e envie para o segundo. Registre no relatório.

- [ ] **Step 12: Commit**

```bash
git add src/Rastreamento.Application/Arquivos src/Rastreamento.Api/Program.cs tests/Rastreamento.Application.Tests/Arquivos
git commit -m "feat(fase-2b): validador de STL em tres camadas e caso de uso do solido"
```

**Delta de teste estimado: +20** (Application). Baseline estimada ao fim: backend **551** (App 269 · Infra 70 · Api 212) / front 495.

---

## Task 4: Os dois endpoints, e `ComponenteDto.TemSolido`

**Files:**
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs`
- Modify: `src/Rastreamento.Application/Cadastros/CadastroDeComponenteUseCase.cs`
- Modify: `src/Rastreamento.Api/Controllers/ComponentesController.cs`
- Create: `tests/Rastreamento.Api.Tests/SolidoEndpointsTests.cs`

**Interfaces:**
- Consumes: `SolidoDoComponenteUseCase.Enviar` / `.Obter` da Task 3; `UsuarioDaSessao()` de `CadastroControllerBase` (devolve `int?`, lendo a claim `sub`).
- Produces:
  - `ComponenteDto(int Id, string Codigo, string Descricao, string Tipo, bool Ativo, bool TemSolido)` — **campo novo no FIM do record**, para não reordenar o posicional existente.
  - `POST /api/componentes/{id}/solido` (multipart, campo `arquivo`), `GET /api/componentes/{id}/solido`.

- [ ] **Step 1: Medir quem consome `ComponenteDto` antes de mexer**

```bash
grep -rn "ComponenteDto(" src/ tests/ --include=*.cs | grep -v "record ComponenteDto"
grep -rn "new ComponenteDto" src/ tests/ --include=*.cs
```

O campo novo entra **no fim** justamente para minimizar isto, mas **toda construção posicional precisa do argumento novo**. Registre no relatório quantos pontos foram tocados — é o delta real desta task, e ele é mecânico, não zero.

- [ ] **Step 2: `TemSolido` no DTO e na projeção**

Em `Dtos.cs`, substitua o record e atualize o `<remarks>` acima dele — que hoje diz que as colunas de arquivo não são expostas porque "upload e a regra 18 são trabalho da Fase 2". **Essa frase deixa de ser verdade nesta task**:

```csharp
/// <remarks>
/// `TemSolido` e booleano, e nao o id do arquivo, de proposito: a tela nao precisa do id — a rota
/// do binario e `GET componentes/{id}/solido`, pelo id do COMPONENTE — e expor um id de arquivo
/// abriria um segundo caminho para o mesmo recurso. `ArquivoFoto` continua de fora: a foto esta
/// fora do escopo da Fase 2B.
/// </remarks>
public sealed record ComponenteDto(
    int Id, string Codigo, string Descricao, string Tipo, bool Ativo, bool TemSolido);
```

Em `CadastroDeComponenteUseCase`, o `Projetar`:

```csharp
  private static ComponenteDto Projetar(Componente c) =>
      new(c.Id, c.Codigo, c.Descricao, c.Tipo, c.Ativo, c.ArquivoSolidoId is not null);
```

- [ ] **Step 3: Escrever os testes de endpoint, que falham**

Crie `tests/Rastreamento.Api.Tests/SolidoEndpointsTests.cs`. **Leia `EstruturaEndpointsTests.cs` antes** e siga o mesmo molde de fixture: criar as linhas, guardar os ids, apagar tudo no `DisposeAsync` na ordem de FK. Os casos:

```csharp
  [Fact]
  public async Task Post_de_STL_valido_grava_e_o_componente_passa_a_ter_solido()
  {
    // POST multipart com o cubo de 684 bytes -> 200/204; em seguida GET /componentes/{id}
    // responde TemSolido = true.
  }

  [Fact]
  public async Task Get_devolve_o_binario_com_o_nome_original_no_Content_Disposition()
  {
    // O corpo tem de voltar byte a byte igual ao que subiu, e o header tem de citar "cubo.stl".
  }

  [Fact]
  public async Task Get_de_componente_sem_solido_da_404() { }

  [Fact]
  public async Task Post_de_arquivo_que_nao_e_STL_da_400() { }

  [Fact]
  public async Task Post_de_componente_inexistente_da_404() { }

  [Fact]
  public async Task Post_sem_perfil_de_escrita_da_403()
  {
    // Perfil que NAO e Administrador nem PCP. Molde: o teste equivalente de ComponentesController.
  }

  [Fact]
  public async Task Get_e_permitido_a_qualquer_autenticado()
  {
    // Leitura e de todos: o gate e o [Authorize] de classe, como em `Obter`.
  }

  [Fact]
  public async Task Post_sem_autenticacao_nenhuma_da_401() { }
```

Monte o corpo multipart assim (o nome do campo, `arquivo`, tem de casar com o parâmetro do controller):

```csharp
using var conteudo = new MultipartFormDataContent();
var arquivo = new ByteArrayContent(StlDeTesteDaApi.CuboBinario());
arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
conteudo.Add(arquivo, "arquivo", "cubo.stl");
var resposta = await cliente.PostAsync($"/api/componentes/{componenteId}/solido", conteudo);
```

O gerador de STL vive em `Application.Tests` (Task 3) e **`Api.Tests` não referencia aquele projeto de teste**. Duas saídas — escolha uma, e **escreva no relatório qual e por quê**: (a) duplicar o gerador em `Api.Tests` como `StlDeTesteDaApi`, aceitando a duplicação de ~15 linhas de fixture; ou (b) mover o gerador para um lugar que os dois referenciem. **Meça antes de decidir:** veja se já existe projeto ou pasta de fixtures compartilhada entre os projetos de teste; se não existir, criar um projeto novo só para isso é caro demais para 15 linhas, e (a) é a resposta certa.

- [ ] **Step 4: Rodar e ver falhar**

```bash
docker compose up -d
dotnet test tests/Rastreamento.Api.Tests --filter "SolidoEndpointsTests"
```

Esperado: falha — as rotas não existem (404 onde o teste espera 200).

- [ ] **Step 5: Os endpoints**

Em `ComponentesController`, injete o caso de uso novo ao lado do que já existe (o construtor passa a receber os dois) e acrescente:

```csharp
  /// <summary>
  /// Envia (ou SUBSTITUI) o solido 3D do Componente. `RequestSizeLimit` espelha o limite do
  /// validador: sem ele, um arquivo de 20 MiB seria lido inteiro em memoria antes de a validacao
  /// dizer que nao servia. Passar do limite responde 413, nao 400 — e resposta do pipeline, nao do
  /// caso de uso.
  /// </summary>
  [HttpPost("{id:int}/solido")]
  [Authorize(Roles = PerfisDeEscrita)]
  [RequestSizeLimit(ValidadorDeArquivoStl.TamanhoMaximoEmBytes)]
  public async Task<IActionResult> EnviarSolido(
      int id, IFormFile arquivo, CancellationToken ct)
  {
    var usuarioId = UsuarioDaSessao();
    if (usuarioId is null) return Unauthorized();

    using var memoria = new MemoryStream();
    await arquivo.CopyToAsync(memoria, ct);

    var resultado = await _solido.Enviar(
        id, arquivo.FileName, memoria.ToArray(), usuarioId.Value, ct);

    return TraduzirResultado(resultado);
  }

  /// <summary>
  /// SEM `[Authorize(Roles)]`: leitura e de qualquer autenticado, e o gate e o `[Authorize]` de
  /// classe — molde de `Obter`. Serve o download E o viewer: um endpoint, dois consumidores.
  /// </summary>
  [HttpGet("{id:int}/solido")]
  public async Task<IActionResult> ObterSolido(int id, CancellationToken ct)
  {
    var resultado = await _solido.Obter(id, ct);
    if (!resultado.Sucesso) return NotFound();

    return File(
        resultado.Valor!.Conteudo, "application/octet-stream", resultado.Valor!.NomeOriginal);
  }
```

**Confira duas coisas no código existente antes de colar:** que `TraduzirResultado` aceita um `Result` sem valor (é o que `DefinirAtivo` já usa) e que o `using` de `Rastreamento.Application.Arquivos` foi acrescentado. Se `TraduzirResultado` não servir para este caso, use o mesmo caminho que `Cadastrar` usa para `TipoDeErro`, e registre a diferença no relatório.

- [ ] **Step 6: Rodar tudo e ver passar**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx
```

Esperado: suíte inteira verde. **Atenção:** o `TemSolido` da Step 2 pode ter quebrado testes que constroem `ComponenteDto` posicionalmente — conserte-os aqui, e conte-os como parte do delta desta task.

- [ ] **Step 7: Mutação**

1. **Remova `[Authorize(Roles = PerfisDeEscrita)]`** do POST. Esperado: `Post_sem_perfil_de_escrita_da_403` falha. Restaure.
2. **Acrescente `[Authorize(Roles = PerfisDeEscrita)]` ao GET.** Esperado: `Get_e_permitido_a_qualquer_autenticado` falha. Restaure. (Esta mutação é a que prova que o teste de leitura mede a ausência da restrição, e não só o caminho feliz.)
3. **Troque `arquivo.FileName` por um literal `"x.stl"`.** Esperado: `Get_devolve_o_binario_com_o_nome_original_no_Content_Disposition` falha. Restaure.

- [ ] **Step 8: Commit**

```bash
git add src/Rastreamento.Application/Cadastros src/Rastreamento.Api/Controllers/ComponentesController.cs tests/
git commit -m "feat(fase-2b): endpoints de envio e leitura do solido, e TemSolido no DTO"
```

**Delta de teste estimado: +8** (Api), mais os ajustes mecânicos do DTO (delta 0, mas trabalho real). Baseline estimada ao fim: backend **559** (App 269 · Infra 70 · Api 220) / front 495.

---

## Task 5: A cobrança da regra 18

**Files:**
- Modify: `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs`
- Modify: `tests/Rastreamento.Application.Tests/Estrutura/CriarPecaTests.cs`
- Modify: `tests/Rastreamento.Api.Tests/EstruturaEndpointsTests.cs`

**Interfaces:**
- Consumes: `IReceitaPadraoRepository.ObterComponenteAsync(int id, CancellationToken ct)` → `Componente?`. **Já está injetado** em `MontagemDeEstruturaUseCase` como `_catalogo`: nenhuma dependência nova.
- Produces: nada para tasks seguintes.

- [ ] **Step 1: Medir o estrago antes de causá-lo**

```bash
grep -c "Componentes.Add" tests/Rastreamento.Application.Tests/Estrutura/CriarPecaTests.cs
grep -cE "\[Fact\]|\[Theory\]" tests/Rastreamento.Application.Tests/Estrutura/CriarPecaTests.cs
```

Esperado (medido em 2026-09-12): **2** e **15**. Ou seja: `FakeReceitaPadraoRepo.Componentes` nasce vazia, e **quase todo** teste de `CriarPeca` hoje cria Peça sem que exista Componente no catálogo do fake. A guarda nova busca o Componente, então **esses testes passariam a falhar** — não por regressão, e sim porque o cenário deles ficou incompleto.

**A saída é a helper, não os 15 testes um a um:** `Montar` passa a semear um Componente **com sólido**, e os testes que querem o caso negativo semeiam o seu explicitamente.

- [ ] **Step 2: Escrever os testes novos, que falham**

Em `CriarPecaTests.cs`:

```csharp
  [Fact]
  public async Task Peca_de_Componente_sem_solido_e_recusada_regra_18()
  {
    var (useCase, estruturas, _, catalogo) = Montar(
        new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    // Sobrescreve o componente semeado por `Montar`: este NAO tem solido.
    catalogo.Componentes.Clear();
    catalogo.Componentes.Add(ComponenteSemSolido(1));

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    // A mensagem diz O QUE FAZER, nao so que falhou: sem isso o usuario sabe que nao pode e nao
    // sabe por onde sair.
    Assert.Contains("solido", resultado.Erro, StringComparison.OrdinalIgnoreCase);
    Assert.Empty(estruturas.Gravadas);
  }

  [Fact]
  public async Task Item_ad_hoc_continua_podendo_nascer_sem_solido_a_regra_18_e_so_da_Peca()
  {
    // A regra 18 vale para PECA (no raiz). `AcrescentarFilho` com ComponenteId nulo continua
    // valido: e a constraint CK_EstruturaItem_PecaTemComponente que garante que a Peca tem
    // Componente, e so a Peca.
    // Monte uma Peca valida (com solido) e acrescente um filho ad-hoc; espere sucesso.
  }

  [Fact]
  public async Task Peca_de_Componente_inexistente_da_NaoEncontrado_e_nao_500()
  {
    // Lacuna PRE-EXISTENTE que esta guarda fecha de graca: antes dela, um ComponenteId inexistente
    // passava pela Application e estourava na FK do banco como 500. Nenhum teste cobria isso —
    // medido em 2026-09-12, `CriarPecaTests` nao tinha caso de componente inexistente.
    var (useCase, estruturas, _, catalogo) = Montar(
        new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    catalogo.Componentes.Clear();

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 999, Quantidade: 1m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Empty(estruturas.Gravadas);
  }

  [Fact]
  public async Task A_guarda_de_solido_roda_DEPOIS_da_de_agrupamento()
  {
    // Agrupamento inexistente + componente sem solido responde NaoEncontrado do AGRUPAMENTO.
    // Ordem deliberada: sem ela, um cliente descobre se um agrupamento existe pelo tipo do erro.
    var (useCase, _, _, catalogo) = Montar();   // nenhum Agrupamento
    catalogo.Componentes.Clear();
    catalogo.Componentes.Add(ComponenteSemSolido(1));

    var resultado = await useCase.CriarPeca(
        99, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Contains("Agrupamento", resultado.Erro);
  }
```

Acrescente os dois helpers ao arquivo — `ComponenteSemSolido(int id)` e `ComponenteComSolido(int id)`, o segundo com `ArquivoSolidoId = 700 + id` (id alto, fora da faixa dos ids de catálogo dos testes, para não acertar por coincidência numérica) — e **altere `Montar` e `MontarComPedido` para semear `ComponenteComSolido(1)` e `ComponenteComSolido(2)`** no `FakeReceitaPadraoRepo`.

Escreva junto, como comentário no `Montar`, **por que** ele semeia: sem o Componente com sólido, a guarda da regra 18 recusa toda Peça e 15 testes falham por cenário incompleto, não por regressão.

- [ ] **Step 3: Rodar e ver falhar — e conferir COMO falha**

```bash
dotnet test tests/Rastreamento.Application.Tests --filter "CriarPecaTests"
```

Esperado: os testes novos falham (a guarda não existe, então a Peça é criada) e **os antigos continuam passando** (a semeadura do `Montar` não muda o comportamento deles). Se algum antigo quebrar **agora**, antes da guarda, a semeadura mudou algo que não devia — investigue antes de seguir.

- [ ] **Step 4: Implementar a guarda**

Em `MontagemDeEstruturaUseCase`, acrescente as constantes:

```csharp
  /// <summary>
  /// Segunda metade da regra 18, cobrada a partir da Fase 2B. A primeira metade e a constraint
  /// CK_EstruturaItem_PecaTemComponente (Fase 2), que garante que a Peca tem ONDE pendurar o
  /// solido; esta garante que o solido esta LA. Nao e CHECK porque um CHECK nao alcanca outra
  /// tabela, e ArquivoSolidoId e nullable por causa do Componente do tipo 'Bruto'.
  /// </summary>
  private const string ErroDeSolidoObrigatorio =
      "Este Componente nao tem solido 3D (regra 18). Envie o arquivo STL no cadastro do "
      + "Componente antes de criar a Peca.";

  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";
```

E em `CriarPeca`, **depois** da guarda de agrupamento e **antes** de `PlanejarCopiaDoCatalogo`:

```csharp
    // Regra 18, segunda metade. Depois do agrupamento de proposito: o tipo do erro nao deve
    // distinguir "agrupamento existe" para quem so chuta ids. Usa `_catalogo`, que o caso de uso
    // ja recebe — nenhuma dependencia nova.
    //
    // Vale so para a PECA (no raiz). `AcrescentarFilho` nao passa por aqui, e e por isso que Item
    // ad-hoc continua valido.
    var componenteDaPeca = await _catalogo.ObterComponenteAsync(nova.ComponenteId, ct);
    if (componenteDaPeca is null)
      return Result<EstruturaItemDto>.Falha(ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);
    if (componenteDaPeca.ArquivoSolidoId is null)
      return Result<EstruturaItemDto>.Falha(ErroDeSolidoObrigatorio, TipoDeErro.Validacao);
```

**Remova o comentário antigo** que dizia que a segunda metade da regra 18 não é cobrada ali e que quem fecha é a 2B — ele deixa de ser verdade exatamente aqui, e um comentário que afirma o contrário do código é pior que nenhum.

- [ ] **Step 5: Rodar e ver passar**

```bash
dotnet test tests/Rastreamento.Application.Tests --filter "CriarPecaTests"
```

Esperado: **19 passando** (15 antigos + 4 novos).

- [ ] **Step 6: Os testes de endpoint de estrutura também criam Peça**

`EstruturaEndpointsTests` cria Componente por um helper próprio, `NovoComponente(prefixo)`, e cria Peça por HTTP contra o banco real. Com a guarda, essas Peças passam a ser recusadas.

**O conserto é no helper, que é ponto único:** `NovoComponente` passa a inserir também um `ArquivoDeComponente` (use o cubo de 684 bytes) e a ligar `ArquivoSolidoId`. O `DisposeAsync` precisa apagar as linhas novas **antes** dos Componentes que as referenciam — a ordem de FK agora tem um elo a mais, e errá-la produz falha de FK no teardown, não no teste.

Acrescente **um** teste de endpoint aqui:

```csharp
  [Fact]
  public async Task Post_de_Peca_com_Componente_sem_solido_da_400_regra_18()
  {
    // Componente criado SEM passar pelo helper (ou com o vinculo removido), para provar a regra
    // no nivel HTTP e nao so no caso de uso.
  }
```

- [ ] **Step 7: Suíte inteira**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx
```

Esperado: verde. Se aparecer vermelho intermitente em `ComponenteMappingTests`, **grave o nome do teste antes de rodar de novo** e verifique se é o flake de tabela compartilhada já registrado — não o atribua a esta task sem medir.

- [ ] **Step 8: Mutação — a que importa nesta task**

1. **Apague as duas linhas da guarda de sólido.** Esperado: `Peca_de_Componente_sem_solido_e_recusada_regra_18` **e** o teste de endpoint novo falham. Se a suíte ficar **verde** sem a guarda, o teste não existe de verdade — pare e conserte.
2. **Mova a guarda para ANTES da de agrupamento.** Esperado: `A_guarda_de_solido_roda_DEPOIS_da_de_agrupamento` falha. Restaure.
3. **Troque `ArquivoSolidoId is null` por `is not null`.** Esperado: vários testes antigos falham (toda Peça válida passa a ser recusada) — confirma que o caminho feliz está coberto.

- [ ] **Step 9: Commit**

```bash
git add src/Rastreamento.Application/Estrutura tests/
git commit -m "feat(fase-2b): cobra a regra 18 -- Peca exige solido no Componente de origem"
```

**Delta de teste estimado: +5** (App +4, Api +1). Baseline estimada ao fim: backend **564** (App 273 · Infra 70 · Api 221) / front 495.

---

## Task 6: Front — o upload

**Files:**
- Modify: `web/src/testes/api.ts`
- Modify: `web/src/api/cadastros.ts`
- Create: `web/src/components/UploadDeSolido.tsx`
- Create: `web/src/components/UploadDeSolido.test.tsx`
- Modify: `web/src/pages/ComponenteDetalhePage.tsx`

**Interfaces:**
- Consumes: `POST /componentes/{id}/solido` (multipart, campo `arquivo`), `GET /componentes/{id}` (para `temSolido`).
- Produces:
  - `web/src/testes/api.ts`: `export function respostaBinaria(bytes: Uint8Array, nomeDoArquivo?: string, status?: number): Response`
  - `web/src/api/cadastros.ts`: `ComponenteDto` ganha `temSolido: boolean`; `export async function enviarSolido(componenteId: number, arquivo: File): Promise<void>`; `export function caminhoDoSolido(componenteId: number): string`
  - `web/src/components/UploadDeSolido.tsx`: `export function UploadDeSolido({ componenteId, temSolido, aoEnviar }: { componenteId: number; temSolido: boolean; aoEnviar: () => void })`

- [ ] **Step 1: `respostaBinaria` no helper de teste**

O `respostaJson` que já existe não serve para binário. Acrescente, seguindo o estilo do arquivo:

```typescript
/**
 * Resposta binária pronta para `vi.stubGlobal('fetch', ...)`. Existe porque `respostaJson` não
 * serve ao sólido: o viewer consome `arrayBuffer()`, e um corpo JSON faria o loader receber texto.
 *
 * O `Content-Disposition` é opcional porque só o teste de download o afirma — os demais só olham
 * os bytes.
 */
export function respostaBinaria(
  bytes: Uint8Array,
  nomeDoArquivo?: string,
  status = 200,
): Response {
  const headers: Record<string, string> = { 'Content-Type': 'application/octet-stream' }
  if (nomeDoArquivo) headers['Content-Disposition'] = `attachment; filename="${nomeDoArquivo}"`
  return new Response(bytes, { status, headers })
}
```

- [ ] **Step 2: `temSolido` e as duas funções no client**

Em `web/src/api/cadastros.ts`:

```typescript
export interface ComponenteDto {
  id: number
  descricao: string
  codigo: string
  tipo: string
  ativo: boolean
  temSolido: boolean
}
```

**Mantenha a ordem dos campos existentes como está** — acima está reordenado por engano neste plano; copie a ordem do arquivo real e apenas acrescente `temSolido: boolean` ao fim. E:

```typescript
/**
 * O caminho do binário do sólido, SEM o prefixo `/api` — quem o aplica é o `rota()` de
 * `client.ts`. Exportado em vez de embutido nos dois consumidores (upload e viewer) para a rota
 * existir num lugar só.
 */
export function caminhoDoSolido(componenteId: number): string {
  return `/componentes/${componenteId}/solido`
}

/**
 * Envia (ou substitui) o sólido. `FormData` sem `Content-Type` explícito de propósito: quem põe o
 * boundary é o browser, e fixar o header à mão produz um corpo que o servidor não consegue
 * separar. O `apiFetch` não fixa `Content-Type`, então não há nada a remover.
 */
export async function enviarSolido(componenteId: number, arquivo: File): Promise<void> {
  const corpo = new FormData()
  corpo.append('arquivo', arquivo)
  const resp = await apiFetch(caminhoDoSolido(componenteId), { method: 'POST', body: corpo })
  if (!resp.ok) throw await erroDaResposta(resp)
}
```

**Use o mesmo mecanismo de erro que as outras funções do arquivo usam** — `erroDaResposta` acima é um espaço reservado pelo papel; leia `cadastros.ts` e siga o que está lá.

- [ ] **Step 3: Escrever o teste do `UploadDeSolido`, que falha**

```tsx
// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { fireEvent } from '@testing-library/react'
import { UploadDeSolido } from './UploadDeSolido'
import { respostaJson } from '../testes/api'
import * as client from '../api/client'

afterEach(cleanup)

function arquivoStl(nome = 'cubo.stl'): File {
  // 684 bytes: o cubo de 12 triângulos. O conteúdo não importa aqui — a validação de forma é do
  // backend, e este teste prova o ENVIO, não o formato.
  return new File([new Uint8Array(684)], nome, { type: 'application/octet-stream' })
}

describe('UploadDeSolido', () => {
  it('mostra que o componente ainda não tem sólido', () => {
    render(<UploadDeSolido componenteId={7} temSolido={false} aoEnviar={() => {}} />)
    expect(screen.getByText(/sem sólido/i)).toBeTruthy()
  })

  it('envia o arquivo escolhido como multipart e avisa o pai', async () => {
    const aoEnviar = vi.fn()
    const fetchMock = vi.fn(() => Promise.resolve(respostaJson({})))
    vi.stubGlobal('fetch', fetchMock)
    // Inicialize o client do jeito que os outros testes de tela deste projeto inicializam
    // (`inicializar` / `_resetParaTeste`): leia um teste de tela existente e copie o arranjo.

    render(<UploadDeSolido componenteId={7} temSolido={false} aoEnviar={aoEnviar} />)
    const campo = screen.getByLabelText(/sólido/i) as HTMLInputElement
    fireEvent.change(campo, { target: { files: [arquivoStl()] } })

    await waitFor(() => expect(aoEnviar).toHaveBeenCalled())
    const corpo = fetchMock.mock.calls[0][1]?.body
    expect(corpo).toBeInstanceOf(FormData)
    // O nome do campo é contrato com o parâmetro `arquivo` do controller: errá-lo dá 400 no
    // servidor e nada na tela.
    expect((corpo as FormData).get('arquivo')).toBeInstanceOf(File)
    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/componentes/7/solido')
  })

  it('mostra erro quando o envio falha, e não avisa o pai', async () => {
    const aoEnviar = vi.fn()
    vi.stubGlobal('fetch', vi.fn(() =>
      Promise.resolve(respostaJson({ erro: 'O arquivo nao e um STL valido.' }, 400))))

    render(<UploadDeSolido componenteId={7} temSolido={false} aoEnviar={aoEnviar} />)
    fireEvent.change(screen.getByLabelText(/sólido/i), { target: { files: [arquivoStl()] } })

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())
    expect(aoEnviar).not.toHaveBeenCalled()
  })

  it('mostra estado de enviando enquanto a requisição está em voo', async () => {
    // Promise que não resolve, para o estado intermediário ser observável.
  })

  it('quando já tem sólido, oferece substituir e baixar', () => {
    render(<UploadDeSolido componenteId={7} temSolido aoEnviar={() => {}} />)
    expect(screen.getByText(/substituir/i)).toBeTruthy()
    expect(screen.getByRole('link', { name: /baixar/i })).toBeTruthy()
  })
})
```

**Antes de escrever, abra um teste de tela existente** (por exemplo o de `ComponentesPage`) e copie dele o arranjo de inicialização do client e o uso de `fetchPorRota`/`respostaJson` — o bloco acima marca o lugar, não a forma exata.

- [ ] **Step 4: Rodar e ver falhar**

```bash
cd web && npm test -- --run UploadDeSolido
```

Esperado: falha — o módulo não existe.

- [ ] **Step 5: Implementar o `UploadDeSolido`**

Regras que o componente tem de respeitar, e que a review vai conferir:

- **Nada escrito à mão**: use `Campo`, `Botao`, `BannerDeErro` de `web/src/components/`. O `<input type="file">` vai **dentro** do `Campo`, pelo padrão de `children` com `idDoCampo` que as outras telas usam.
- **Cores só por token.** Nenhum token novo: "sem sólido" é ausência de dado, não estado de negócio, então `text-tinta-fraca`, nunca `negativo`.
- **`accept=".stl"`** no input — conveniência de seletor de arquivo, **não** validação. A validação real é do backend, e o `catch` do 400 é obrigatório.
- **Erro por `mensagemDeErro(erro, fallback)`**, com fallback próprio ("Não foi possível enviar o sólido.").
- **O link de baixar** aponta para o caminho do sólido. **Atenção medida:** o download por link precisa do `Authorization: Bearer`, que um `<a href>` não manda — então **ou** o link busca por `apiFetch` e monta um object URL, **ou** ele não existe. Decida na task e **escreva a decisão**: um `<a>` cru para o endpoint responderia 401 e o usuário veria "erro" sem explicação. O caminho de menor risco é buscar com `apiFetch`, criar `URL.createObjectURL(blob)` e disparar o download; se escolher isso, **revogue o object URL** depois.
- **Sem `data-testid`**: o input tem rótulo, e o botão tem texto. `getByLabelText`/`getByRole` alcançam os dois.

- [ ] **Step 6: Integrar na `ComponenteDetalhePage`**

O ponto de inserção é **depois do bloco de cabeçalho** (o `<div>` que mostra código e descrição) e **antes da primeira `<Secao>`**. Duas exigências:

- **Só sob `usePodeEscrever`** para a parte de envio — o mesmo hook que a tela já usa para a receita padrão. A parte de leitura (que existe sólido, e baixar) é de todos.
- **Depois de enviar, a tela tem de reler o componente**, senão `temSolido` continua falso e a interface mente. É para isso que o `aoEnviar` existe.

- [ ] **Step 7: Rodar e ver passar, e conferir o build**

```bash
cd web && npm test -- --run && npm run build
```

Esperado: suíte verde e build limpo. **`npm test` verde não prova que compila** — o Vitest não faz typecheck.

- [ ] **Step 8: Mutação**

1. **Troque o nome do campo do `FormData`** de `arquivo` para `file`. Esperado: o teste de envio falha na asserção do `get('arquivo')`. Restaure.
2. **Remova o `aoEnviar()`** depois do sucesso. Esperado: o teste de envio falha. Restaure.
3. **Faça o `catch` do erro chamar `aoEnviar()`.** Esperado: o teste de erro falha no `not.toHaveBeenCalled`. Restaure.

- [ ] **Step 9: Commit**

```bash
git add web/src/testes/api.ts web/src/api/cadastros.ts web/src/components/UploadDeSolido.tsx web/src/components/UploadDeSolido.test.tsx web/src/pages/ComponenteDetalhePage.tsx
git commit -m "feat(fase-2b): upload do solido na tela do Componente"
```

**Delta de teste estimado: +5** (front). Baseline estimada ao fim: backend 564 / front **500**.

---

## Task 7: Front — o viewer 3D

**Files:**
- Modify: `web/package.json`
- Create: `web/src/components/VisualizadorDeSolido.tsx`
- Create: `web/src/components/VisualizadorDeSolido.test.tsx`
- Modify: `web/src/pages/ComponenteDetalhePage.tsx`

**Interfaces:**
- Consumes: `caminhoDoSolido` da Task 6; `GET /componentes/{id}/solido`.
- Produces: `export function VisualizadorDeSolido({ componenteId }: { componenteId: number })`.

- [ ] **Step 1: Instalar a dependência**

```bash
cd web && npm install three && npm install --save-dev @types/three
```

**Confira o que aconteceu, não assuma:** rode `npm run build` em seguida. Se o `three` já publicar os próprios tipos, o `@types/three` é desnecessário e sai; se não, ele fica. **Registre no relatório qual dos dois casos foi, com a versão instalada** — isto é medição, não conhecimento prévio.

- [ ] **Step 2: Escrever o teste, que falha**

O jsdom **não tem WebGL**, então este teste cobre o **estado da tela**, não o render. É decisão de desenho da spec, e o teste tem de dizer isso por escrito:

```tsx
// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { fireEvent } from '@testing-library/react'
import { VisualizadorDeSolido } from './VisualizadorDeSolido'
import { respostaBinaria } from '../testes/api'

afterEach(cleanup)

// O jsdom não implementa WebGL: nenhum teste aqui prova que o sólido APARECE. O que se prova é o
// que o componente controla — buscar, os três estados, e não carregar o three.js sem clique. O
// canvas é coberto pela verificação manual em navegador (Task 9), declarada na spec.
vi.mock('three', () => ({ /* dublê mínimo: só o que o componente importa */ }))

describe('VisualizadorDeSolido', () => {
  it('não busca o sólido nem carrega o three.js antes do clique', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    render(<VisualizadorDeSolido componenteId={7} />)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('busca o binário quando o usuário pede para visualizar', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684))))
    vi.stubGlobal('fetch', fetchMock)

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/componentes/7/solido')
  })

  it('mostra carregando enquanto busca', async () => { /* promise pendente */ })

  it('mostra erro quando a busca falha', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(0), undefined, 404))))
    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())
  })
})
```

- [ ] **Step 3: Rodar e ver falhar**

```bash
cd web && npm test -- --run VisualizadorDeSolido
```

- [ ] **Step 4: Implementar o viewer**

Exigências:

- **`await import('three')` e `await import('three/examples/jsm/loaders/STLLoader.js')` dentro do handler do clique**, nunca no topo do módulo. É o que mantém o bundle principal sem os ~600 KB: o público do viewer é o desktop do PCP/Admin, e o operador no Android não deve pagar por uma tela que nunca abre.
- **Três estados** (carregando via `EstadoCarregando`, erro via `BannerDeErro`, pronto), cada um com teste que morre se o estado sumir.
- **O binário vem por `apiFetch` + `arrayBuffer()`** — não por `<img>`/`<a>`, que não mandam o Bearer.
- **Limpeza ao desmontar**: `renderer.dispose()`, cancelar o loop de animação, e não escrever no estado depois de desmontado (o padrão `cancelado` que as telas deste projeto já usam nos `useEffect`).
- **O `<canvas>` precisa de rótulo acessível** (`aria-label`), porque um canvas sem nome é opaco para leitor de tela — e é por isso que o viewer não substitui a descrição textual do componente.
- **Botão de "Visualizar"**: use a primitiva `Botao`. Este **não** é caso da exceção de "botão de chrome" — tem rótulo textual e é ação de conteúdo, exatamente o que a `Botao` serve.

- [ ] **Step 5: Integrar na tela e rodar tudo**

O viewer entra ao lado do `UploadDeSolido`, e só aparece quando `temSolido` é verdadeiro — não há o que visualizar quando não há arquivo.

```bash
cd web && npm test -- --run && npm run build
```

Esperado: verde e build limpo. **Confira no build que o `three` saiu num chunk separado** (a saída do Vite lista os chunks); se ele entrar no bundle principal, o import dinâmico não está funcionando e a decisão de §2.3 da spec foi perdida. Registre o tamanho dos chunks no relatório.

- [ ] **Step 6: Mutação**

1. **Mova o `import('three')` para o topo do módulo** (import estático). Esperado: `não busca o sólido nem carrega o three.js antes do clique` **provavelmente continua passando** — o teste mede o fetch, não o import. **Se for isso, o teste não cobre a decisão do import dinâmico, e a única prova é o chunk do build.** Escreva isso no relatório em vez de afirmar cobertura que não existe.
2. **Remova o `EstadoCarregando`.** Esperado: o teste de carregando falha. Restaure.

- [ ] **Step 7: Commit**

```bash
git add web/package.json web/package-lock.json web/src/components/VisualizadorDeSolido.tsx web/src/components/VisualizadorDeSolido.test.tsx web/src/pages/ComponenteDetalhePage.tsx
git commit -m "feat(fase-2b): viewer 3D do solido, com three.js carregado sob demanda"
```

**Delta de teste estimado: +4** (front). Baseline estimada ao fim: backend 564 / front **504**.

---

## Task 8: Front — a marca "sem sólido" no seletor

**⚠️ Esta task mexe no arquivo com flake conhecido.** Se `SeletorComBusca.test.tsx` falhar, **grave o nome do teste que falhou antes de rodar de novo** e confronte com os dois já registrados (`seleciona com Enter depois de navegar com a seta para baixo` e `anuncia o estado do combobox por ARIA`). Rodar de novo destrói a amostra.

**Files:**
- Modify: `web/src/components/SeletorComBusca.tsx`
- Modify: `web/src/components/SeletorComBusca.test.tsx`
- Modify: `web/src/pages/AgrupamentoDetalhePage.tsx`

**Interfaces:**
- Consumes: `ComponenteDto.temSolido` da Task 6.
- Produces: `SeletorComBusca` passa a aceitar `exigirSolido?: boolean` (default `false`).

- [ ] **Step 1: Escrever os testes, que falham**

Acrescente a `SeletorComBusca.test.tsx`, no estilo do arquivo:

```tsx
  it('sem exigirSolido, componente sem sólido é selecionável — o comportamento de hoje não muda', async () => {
    // Este é o teste que protege os dois consumidores existentes (a receita padrão na tela do
    // Componente e o painel de acrescentar filho): o default `false` tem de deixá-los intactos.
  })

  it('com exigirSolido, o item sem sólido aparece marcado e não é selecionável', async () => {
    // Aparece: o usuário procura um código que sabe que existe e tem de ACHÁ-LO. Esconder faria
    // ele não achar e não saber por quê.
    // Não selecionável: aria-disabled, e o clique não chama `aoSelecionar`.
  })

  it('com exigirSolido, Enter no item sem sólido também não seleciona', async () => {
    // A guarda fica em `selecionar`, não só no onClick — senão o teclado contorna a regra.
  })

  it('com exigirSolido, item COM sólido continua selecionável', async () => { })
```

- [ ] **Step 2: Rodar e ver falhar**

```bash
cd web && npm test -- --run SeletorComBusca
```

- [ ] **Step 3: Implementar**

Em `SeletorComBusca.tsx`:

```typescript
interface Props {
  rotulo: string
  valorSelecionado: ComponenteDto | null
  aoSelecionar: (componente: ComponenteDto) => void
  /**
   * Quando verdadeiro, componente sem sólido aparece MARCADO e não selecionável (regra 18: Peça
   * exige sólido no Componente de origem). Default `false` porque os dois consumidores anteriores
   * — a receita padrão e o painel de acrescentar filho — escolhem Item, que pode ser ad-hoc.
   *
   * Isto NÃO é validação: a fronteira real é o 400 de `CriarPeca`. É aviso, para o usuário não
   * montar a árvore inteira antes de descobrir.
   */
  exigirSolido?: boolean
}
```

A guarda vai em **`selecionar`** (a função por onde clique e `Enter` passam), não só no `onClick` — senão o teclado contorna a regra. No `<li>`: `aria-disabled` quando bloqueado, e uma marca textual usando **`text-tinta-fraca`** (nenhum token novo; "sem sólido" é ausência de dado, não estado de negócio, e verde/vermelho estão reservados).

- [ ] **Step 4: Ligar no lugar certo, e só nele**

Em `AgrupamentoDetalhePage.tsx`, `exigirSolido` vai **apenas** no `SeletorComBusca` do **formulário de criar Peça** — o do topo da tela, dentro do `podeEscrever &&`, ao lado do campo de quantidade.

**Não** vai no `SeletorComBusca` do **painel de acrescentar filho, modo catálogo**: Item pode ser ad-hoc, e a regra 18 é só da Peça. Ligá-lo ali seria inventar uma regra que o domínio não tem.

- [ ] **Step 5: Rodar tudo**

```bash
cd web && npm test -- --run && npm run build
```

- [ ] **Step 6: Mutação**

1. **Mova a guarda do `selecionar` para o `onClick` do `<li>`.** Esperado: o teste do `Enter` falha. Restaure.
2. **Ligue `exigirSolido` também no painel de acrescentar filho.** Esperado: algum teste da `AgrupamentoDetalhePage` sobre filho de catálogo falha. **Se a suíte ficar verde, o caso não está coberto** — acrescente o teste e registre.
3. **Troque o default de `exigirSolido` para `true`.** Esperado: o teste "sem exigirSolido... não muda" falha.

- [ ] **Step 7: Commit**

```bash
git add web/src/components/SeletorComBusca.tsx web/src/components/SeletorComBusca.test.tsx web/src/pages/AgrupamentoDetalhePage.tsx
git commit -m "feat(fase-2b): seletor marca componente sem solido ao escolher Peca"
```

**Delta de teste estimado: +4** (front, mais os que a mutação 2 exigir). Baseline estimada ao fim: backend 564 / front **508**.

---

## Task 9: Verificação manual em navegador

**Produto é evidência, não código. Delta de teste: 0.** Se esta task dispensar o gate de review, **a justificativa vai escrita ANTES, no ledger E no relatório** — justificativa reconstruída depois é indistinguível de racionalização, e o `CLAUDE.md` já mediu um caso em que "produto não é código" foi invocado errado.

**Pré-requisito que não se pode contornar:** nenhum dos 54 Componentes do `seed-demo` tem sólido, e o seed **não muda nesta fase**. Então **subir um STL é o primeiro passo da verificação**, não um atalho — e isso é bom: o caminho verificado passa a ser o mesmo que o usuário real percorre.

- [ ] **Step 1: Preparar o ambiente e gerar um STL de verdade**

```bash
bash scripts/estado
docker compose up -d
```

Gere um cubo STL binário de 684 bytes em disco (o mesmo formato da fixture) para subir pela tela. Se quiser um sólido mais interessante de olhar, qualquer STL público serve — mas **registre qual arquivo usou e o tamanho dele**.

- [ ] **Step 2: Os casos a verificar, um por um, com evidência**

| # | O que verificar | Como se sabe que passou |
|---|---|---|
| V1 | Upload de STL válido na tela do Componente | a tela passa a dizer que tem sólido, sem recarregar à mão |
| V2 | O viewer abre e o sólido gira | screenshot do canvas com a geometria |
| V3 | O `three` só carrega no clique | aba de rede: o chunk do three aparece **depois** do clique, não no load |
| V4 | Substituir o sólido | o nome exibido muda para o do arquivo novo |
| V5 | Arquivo que não é STL (renomeie um PDF para `.stl`) | 400 com mensagem legível na tela, não erro genérico |
| V6 | Criar Peça com Componente **sem** sólido | o item aparece **marcado** no seletor e não é selecionável |
| V7 | Criar Peça com Componente **com** sólido | sucesso, árvore montada |
| V8 | Gating de perfil | com o usuário `operador` (perfil sem escrita), o upload **não aparece** e a leitura continua |

**V8 precisa do terceiro usuário, que não está no seed.** Ele existe só no banco desta máquina e pode ter sido perdido numa regeneração; o bloco idempotente para recriá-lo (`operador`/`Admin@123`, perfil `Operador`, reusando o hash do `admin`) está no `CLAUDE.md`. **Confira se ele existe antes de começar** — sem um usuário sem-escrita não há como provar V8, porque `admin` e `pcp` **escrevem**.

- [ ] **Step 3: Escrever o relatório**

Um caso por linha, com o que foi observado. **Divergência encontrada é resultado, não fracasso** — registre-a com o que aconteceu, e não a conserte em silêncio: conserto sem medição é o padrão que este projeto já pegou três vezes.

---

## Auto-revisão deste plano

**Cobertura da spec, seção por seção:**

| Seção da spec | Task |
|---|---|
| §2.1 STL apenas | Task 1 (Steps 6 e 7) |
| §2.2 blob em tabela própria | Tasks 1 e 2 |
| §2.3 viewer sob demanda | Task 7 |
| §2.4 marca, não esconder | Task 8 |
| §2.5 sem `DELETE` | **por ausência** — nenhuma task cria o endpoint, e o contrato do repositório escreve por quê |
| §3 Task 0 (VPS) | Task 0 |
| §4 modelo de dados | Tasks 1 e 2 |
| §4.3 sem navegação | Task 2 (Step 4, e a mutação 2 do Step 10) |
| §5 API | Task 4 |
| §5.1 validação em três camadas | Task 3 |
| §5.2 `TemSolido` | Task 4 (Steps 1 e 2) |
| §6 cobrança da regra 18 | Task 5 |
| §7 front | Tasks 6, 7, 8 |
| §8 testes e o que não é testável | declarado na Task 7 (Step 2) e verificado na Task 9 |
| §9 escopo | nenhuma task toca `ArquivoFoto` nem busca por foto |
| §10 ordem | a numeração das tasks **é** a ordem, e a Task 5 vem depois da 4 de propósito |
| §11 riscos | seed sem sólido (Task 9), 16 MiB (Task 3), viewer sem cobertura (Task 7), `ComponenteDto` (Task 4 Step 1) |

**Defeitos deste plano, achados na própria auto-revisão e deixados VISÍVEIS em vez de escondidos:**

1. **A mutação 2 do Step 6 da Task 3 acha um teste que falta.** Trocar `==` por `>=` na fórmula binária **não** é pega pelo teste de truncamento (truncar deixa menor, e `>=` também recusa). O plano manda acrescentar o caso de "bytes de sobra no fim" quando isso for confirmado. Está escrito como instrução, não escondido.
2. **A mutação 1 do Step 6 da Task 3 pode não matar.** A fixture com cabeçalho `"solid"` talvez não prove a ordem das tentativas, porque o ASCII-check também exige `facet normal`. O plano manda medir e fortalecer a fixture, e proíbe afirmar cobertura que não existe.
3. **A mutação 1 do Step 6 da Task 7 provavelmente não mata.** Nenhum teste de jsdom distingue import estático de dinâmico — a única prova é o chunk do build, e o plano manda registrar o tamanho dos chunks em vez de alegar cobertura.
4. **A mutação 2 do Step 11 da Task 3 passa por coincidência numérica** (o componente do teste é o 10, que é o literal). O plano manda acrescentar o segundo componente. Mesmo formato do achado B11 da Fase 1A.
5. **`FabricaDeContexto.Criar()` (Task 2) e `erroDaResposta` (Task 6) são espaços reservados pelo NOME DO PAPEL, não promessas de API.** O plano diz explicitamente para ler o arquivo real e usar o mecanismo que está lá. São os dois únicos lugares assim, e estão marcados como tais.
6. **A ordem dos campos de `ComponenteDto` no Step 2 da Task 6 está errada de propósito no bloco de código, e o texto logo abaixo diz isso** — copie a ordem do arquivo real.

**Consistência de tipos:** `ArquivoSolidoId` é `int?` em toda parte; `TemSolido`/`temSolido` é `bool`/`boolean`; `caminhoDoSolido` é a única fonte da rota nos dois consumidores do front; `Validar` devolve `string?` no mesmo molde de `CadastroDeComponenteUseCase`; `Enviar` devolve `Result` (sem valor) e `Obter` devolve `Result<ArquivoDeSolidoDto>`.

**Baseline final estimada:** backend **564** (App 273 · Infra 70 · Api 221), front **508**. **São estimativas.** Cada task mede e corrige as seguintes.
