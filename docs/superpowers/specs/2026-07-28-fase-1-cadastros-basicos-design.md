# Fase 1 — Cadastros Básicos (Spec de Implementação)

> **Para o `writing-plans`:** este documento é a **spec** da Fase 1. As specs de domínio
> (`specs/00-06`) são a fonte da verdade do "porquê" e do schema; **não** as reescreva —
> referencie. Toda escolha concreta necessária para não deixar placeholders já está
> resolvida aqui (ver "Decisões resolvidas").
>
> **A Fase 1 é cortada em duas metades independentes (1A e 1B) e cada uma recebe seu
> próprio plano.** Gere primeiro o plano de **1A**; 1B só entra em plano depois de 1A
> fechada. As duas estão neste único documento porque as decisões se entrelaçam.

**Goal:** Sair de um sistema que só autentica para um sistema onde dá para cadastrar o
catálogo da fábrica e abrir Pedidos com Agrupamentos, via tela.

**Architecture:** Continuação da Clean Architecture simplificada da Fase 0 (`Domain` /
`Application` / `Infrastructure` / `Api`), Database First, front React + TypeScript (Vite).
Nenhum padrão novo é introduzido: 1A estabelece o molde de *vertical slice* CRUD e as
demais entidades o repetem.

---

## Global Constraints

Requisitos válidos para **todas** as tarefas dos planos de 1A e 1B:

- **Database First.** Schema nasce em `specs/02-modelo-de-dados.sql`. Nunca `Add-Migration`
  do zero. Mudança de schema segue a ordem do `CLAUDE.md`: `.sql` primeiro → mapeamento EF
  → `01-dominio-e-regras-de-negocio.md` se virar regra.
- **Nomenclatura.** Entidades de domínio em português espelhando o DDL (`Setor`, `Material`,
  `Componente`, `Pedido`, `Agrupamento`); padrões técnicos em inglês (`Repository`,
  `UseCase`, `Controller`, `Dto`). Siglas de 3+ letras em PascalCase (`Crud`, `Json`), como
  o repo já faz em `Jwt`, `Sha256`, `Json`.
- **Use cases de cadastro:** uma classe por entidade, nomeada `CadastroDe<Entidade>UseCase`,
  com métodos `Cadastrar` / `Editar` / `Listar` mais o que a natureza da tabela permitir —
  `DefinirAtivo` no catálogo, `Excluir` (guardado) só em `Agrupamento`, nada disso em
  `Pedido` (ver "Política de exclusão"). Use case com regra própria e
  densa ganha classe separada (ex.: `DefinirFilhosPadraoUseCase` em 1B). O nome usa
  "Cadastro" — a palavra do negócio (`06-roadmap-mvp.md`: "Cadastros básicos";
  `04-fluxos-de-usuario.md`: "Cadastro de Pedido") — e **não** `SetorUseCase`, que colidiria
  conceitualmente com use cases não-cadastrais da mesma entidade já previstos
  (`GET /setores/{id}/fila`, `05-api-endpoints.md:53`).
- **Autorização.** Escrita de catálogo: `[Authorize(Roles = "Administrador")]`. Escrita de
  Pedido/Agrupamento: `[Authorize(Roles = "PCP,Administrador")]` (Administrador é
  superusuário). Leitura: `[Authorize]` puro — Operador precisa ver Setor, Almoxarifado
  precisa ver Material, e nada aqui é sensível. Perfis conforme `00-visao-geral.md:28-33`;
  claim `role` = `PerfilNome`, já emitida pela Fase 0.
- **Datas em UTC, apresentadas em GMT-3** — regra da Fase 0, inalterada. `CriadoEm` persiste
  via `SYSUTCDATETIME()`; a serialização de borda já existe
  (`HorarioDeBrasiliaJsonConverter`).
- **Build em 0 warnings:** `dotnet build Rastreamento.slnx -warnaserror`.

---

## Corte da fase

### 1A — CRUD plano (`Setor`, `Material`, `Pedido`, `Agrupamento`)

Ordem interna: **`Setor` → `Material` → `Pedido` → `Agrupamento`**. `Setor` tem dois campos
e é onde o molde do vertical slice nasce; as três seguintes o repetem, cada uma somando um
elemento (unidade de medida, autoria, aninhamento).

**Por que este corte e não "catálogo primeiro":** 1A e 1B não têm dependência entre si —
nenhuma FK cruza os dois grupos. O corte é por dificuldade, não por domínio. Não existe
molde de CRUD no repositório (só auth), então a primeira entidade da fase inventa o slice
inteiro; isso deve acontecer na entidade mais simples, não junto com validação de ciclo. De
quebra, 1A fecha exatamente o critério de pronto declarado em `06-roadmap-mvp.md:23`.

### 1B — `Componente` + receita padrão

Cabeçalho de `Componente` mais os três sub-recursos de receita (`ComponenteFilhoPadrao`,
`ComponenteMaterialPadrao`, `ComponenteRoteiroPadrao`).

**Por que a receita é Fase 1 e não Fase 2:** as três tabelas de receita apontam só para
`Componente`, `Material` e `Setor` — todas da Fase 1. Quem depende delas é a Fase 2, que
*copia* a receita para `EstruturaItem`; o inverso não é verdade. `05-api-endpoints.md:25-27`
já as classifica em "Catálogo". E o critério de pronto da Fase 2 ("criar `EstruturaItem` a
partir de um `Componente` padrão — copiar receita") fica inverificável se o catálogo só
tiver cabeçalho. **O que fica na Fase 2 é a cópia, não a receita.**

**Semântica da receita (fecha ambiguidade para a Fase 2):** a cópia é *snapshot* no momento
em que a `EstruturaItem` é criada. Alterar a receita depois **não** propaga para estruturas
já criadas. Sem versionamento de receita, sem recálculo. O contexto de negócio é misto —
alguns componentes se repetem entre pedidos, boa parte da estrutura é ad-hoc — então a Fase
2 continua obrigada a suportar montagem do zero, e a receita não merece investimento além
disso.

### Fora da Fase 1 (explicitamente)

| Item | Onde vai |
|---|---|
| Cópia da receita para `EstruturaItem` | Fase 2 |
| Transição `Pedido.Status` → `EmProducao` | Fase 3 (primeiro apontamento de setor) |
| Regra de conclusão de Agrupamento/Pedido | Fase 5 |
| Pedido tipo `Retrabalho` e `MotivoRetrabalho` | Fase 5 |
| **Camada global de erro de API** (401/403/404/rede) | Depois de 1A — dívida rastreada |
| **Gating de navegação por perfil no front** | Depois de 1A — dívida rastreada |
| CRUD de `Usuario` / `Perfil` | Nenhuma fase o reivindica (ver Riscos) |

**Consequência aceita de adiar as duas dívidas do front:** a fronteira real de autorização é
o `[Authorize(Roles)]` no backend — o front não gatear é UX, não buraco de segurança. O
sintoma será um Operador vendo o link "Novo Pedido", clicando e tomando 403 que, sem a
camada global, aparece cru. Aceitável em protótipo executável; **não** é para ser esquecido.

---

## Mudança de schema (passo 1 do plano de 1A)

Motivação: a ameaça que justifica isto é **poluição de registros** — alguém criando Pedidos
espúrios. Política de exclusão não responde a ameaça de *criação*; atribuição de autoria
responde. Hoje não há como sequer identificar quais linhas seriam as espúrias. É barato
agora (as tabelas estão vazias) e caro depois (linhas existentes ficariam sem autor).

Aplicar em `specs/02-modelo-de-dados.sql` (fonte de verdade, script de criação):

- **`dbo.Pedido`**: adicionar `CriadoPorUsuarioId INT NOT NULL` + `CONSTRAINT
  FK_Pedido_CriadoPorUsuario FOREIGN KEY (CriadoPorUsuarioId) REFERENCES dbo.Usuario (Id)`.
  **Não** adicionar `CriadoEm`: `Pedido.DataAbertura DATETIME2 NOT NULL DEFAULT
  (SYSUTCDATETIME())` já é exatamente isso.
- **`dbo.Agrupamento`**: adicionar `CriadoPorUsuarioId INT NOT NULL` + FK análoga, **e**
  `CriadoEm DATETIME2 NOT NULL CONSTRAINT DF_Agrupamento_CriadoEm DEFAULT
  (SYSUTCDATETIME())` — esta tabela não tem timestamp nenhum hoje.

Para bancos de dev que já existem, o mesmo `ALTER` idempotente documentado no `CLAUDE.md`
(padrão das colunas de lockout). As duas tabelas estão vazias — nenhuma funcionalidade as
escreve ainda —, então `NOT NULL` sem default entra sem backfill.

O valor vem da claim de identidade do JWT da Fase 0, resolvida na borda (`Api`) e passada ao
use case como parâmetro explícito. **O use case não lê `HttpContext`** — `Application` não
conhece ASP.NET.

---

## Política de exclusão (vale para o schema inteiro, não só para a Fase 1)

Uma regra uniforme sobre 20 tabelas seria a forma errada, porque as tabelas têm três
naturezas distintas. Propagar exclusão lógica a todas custaria: quebrar 10 dos 14 índices
`UNIQUE` (inative "PED-001" e nunca mais se cria "PED-001"), tornar ambígua a conservação de
quantidade da Fase 3 (uma `Expedicao` inativa conta ou não no invariante? qualquer resposta
erra silenciosamente — o total só não fecha), e cobrar imposto de filtro em toda query das
Fases 2–6.

| Natureza | Tabelas | Política |
|---|---|---|
| **Catálogo** (definições reutilizáveis) | `Setor`, `Material`, `Componente` + receita | Inativação via `Ativo`, como o DDL já prevê. Nunca exclusão física |
| **Documentos** (cabeçalhos de trabalho) | `Pedido`, `Agrupamento`, `EstruturaItem` | Sem exclusão. **Exceção estreita:** hard delete de `Agrupamento` *vazio* (sem nenhum `EstruturaItem`) em Pedido com `Status = 'Aberto'` |
| **Eventos** (o que aconteceu) | `EstruturaSetorHistorico`, `Expedicao`, `Perda`, `MaterialSeparacao`, `RelatorioDimensionalAvaliacao` | Imutáveis. Não se apaga um evento — se estorna com evento compensatório |

Racional das duas linhas que a Fase 1 exerce: um `Agrupamento` vazio é `Codigo` +
`Quantidade` + `Tipo` e nada mais — não há histórico a preservar, e apagá-lo de verdade é o
que evita acumular ruído. Um `Pedido`, mesmo errado, tem `Numero` único que pode já ter sido
citado fora do sistema; corrige-se por edição, não some. E não expor exclusão de Pedido é
resposta mais forte que exclusão lógica contra remoção maliciosa: não se apaga o que não tem
rota de delete.

**Armadilha pré-existente, resolvida na tela e não no schema:** `UQ_Setor_Nome` e
`UQ_Material_Codigo` **não** são índices filtrados. Inativar o Setor "Solda" já hoje impede
criar "Solda" de novo. A saída não é filtrar o índice — isso permitiria dois "Solda" e
confundiria relatório —, é a API distinguir o caso e a tela oferecer **reativar o existente**.

---

## File Structure

Novos arquivos de **1A** (o molde; `Material`, `Pedido` e `Agrupamento` repetem a forma de
`Setor`):

```
src/Rastreamento.Domain/
  Entities/Setor.cs, Material.cs, Pedido.cs, Agrupamento.cs
  Abstractions/ISetorRepository.cs, IMaterialRepository.cs,
                IPedidoRepository.cs, IAgrupamentoRepository.cs
src/Rastreamento.Application/
  Cadastros/CadastroDeSetorUseCase.cs, CadastroDeMaterialUseCase.cs,
            CadastroDePedidoUseCase.cs, CadastroDeAgrupamentoUseCase.cs
  Cadastros/Dtos.cs
src/Rastreamento.Infrastructure/
  Persistence/Configurations/SetorConfiguration.cs, MaterialConfiguration.cs,
                             PedidoConfiguration.cs, AgrupamentoConfiguration.cs
  Persistence/SetorRepository.cs, MaterialRepository.cs,
              PedidoRepository.cs, AgrupamentoRepository.cs
src/Rastreamento.Api/
  Controllers/SetoresController.cs, MateriaisController.cs,
              PedidosController.cs, AgrupamentosController.cs
web/src/
  pages/SetoresPage.tsx, MateriaisPage.tsx, PedidosPage.tsx, PedidoDetalhePage.tsx
  api/cadastros.ts
```

Modificados: `RastreamentoDbContext.cs` (novos `DbSet`), `Program.cs` (DI dos repositórios e
use cases), `specs/02-modelo-de-dados.sql`, `specs/05-api-endpoints.md` (verbos novos),
`web/src/App.tsx` (rotas), `web/src/api/tipos.ts`.

Novos arquivos de **1B**: `Componente.cs` + as três entidades de receita, `IComponenteRepository`,
`CadastroDeComponenteUseCase`, `DefinirFilhosPadraoUseCase`, `DefinirMateriaisPadraoUseCase`,
`DefinirRoteiroPadraoUseCase`, configurations e repositório correspondentes,
`ComponentesController`, `web/src/pages/ComponentesPage.tsx` e `ComponenteDetalhePage.tsx`.

---

## Contratos / Interfaces

### Endpoints de 1A

`05-api-endpoints.md` lista hoje só `GET/POST` para estes recursos; os verbos abaixo o
estendem e **o arquivo deve ser atualizado** como parte do plano.

| Verbo e rota | Perfil | Observação |
|---|---|---|
| `GET /setores` | autenticado | `?incluirInativos=false` por padrão |
| `POST /setores` | Administrador | `{ nome }` |
| `PUT /setores/{id}` | Administrador | `{ nome }` |
| `PATCH /setores/{id}/ativo` | Administrador | `{ ativo }` — cobre inativar e reativar |
| `GET /materiais` | autenticado | `?incluirInativos=false` |
| `POST /materiais` | Administrador | `{ codigo, descricao, unidadeMedida }` |
| `PUT /materiais/{id}` | Administrador | idem |
| `PATCH /materiais/{id}/ativo` | Administrador | `{ ativo }` |
| `GET /pedidos` | autenticado | lista com contagem de Agrupamentos |
| `POST /pedidos` | PCP, Administrador | `{ numero, cliente }` — `Tipo` fixo em `Fabricacao`, `Status` default `Aberto` |
| `GET /pedidos/{id}` | autenticado | inclui os Agrupamentos |
| `PUT /pedidos/{id}` | PCP, Administrador | `{ numero, cliente }` |
| `GET /pedidos/{id}/agrupamentos` | autenticado | |
| `POST /pedidos/{id}/agrupamentos` | PCP, Administrador | `{ codigo, quantidade, tipo }` |
| `GET /agrupamentos/{id}` | autenticado | |
| `PUT /agrupamentos/{id}` | PCP, Administrador | `{ codigo, quantidade, tipo }` |
| `DELETE /agrupamentos/{id}` | PCP, Administrador | 204; **409** se tiver `EstruturaItem` ou se o Pedido não estiver `Aberto` |

Não existe `DELETE /pedidos/{id}`, `DELETE /setores/{id}` nem `DELETE /materiais/{id}` — por
decisão de política de exclusão, não por esquecimento.

### Contrato de erro

- **400** — validação de formato (DTO), `ValidationProblemDetails` padrão do ASP.NET.
- **403** — perfil sem permissão (do `[Authorize(Roles)]`).
- **404** — id inexistente.
- **409 duplicidade** — viola `UQ_Setor_Nome`, `UQ_Material_Codigo`, `UQ_Pedido_Numero` ou
  `UQ_Agrupamento_PedidoCodigo`. Corpo distingue o caso reativável:
  ```json
  { "erro": "ValorDuplicado", "campo": "nome", "existeInativo": true, "idExistente": 12 }
  ```
  `existeInativo: true` é o que permite a tela oferecer "reativar o existente" em vez de
  travar o usuário. Para `Pedido`/`Agrupamento` (sem `Ativo`) o campo é sempre `false`.
- **409 regra de negócio** — `DELETE /agrupamentos/{id}` com filhos ou Pedido não-Aberto:
  `{ "erro": "AgrupamentoNaoVazio" }` / `{ "erro": "PedidoNaoAberto" }`.

A duplicidade é verificada **no use case, antes do insert**, retornando `Result` de erro de
negócio — não se deixa a exceção do índice estourar até a API
(`03-arquitetura-tecnica.md:25-27`). A corrida entre a verificação e o insert continua
protegida pelo índice `UNIQUE`, e esse caso raro pode virar 409 pela via da exceção.

### Padrão do use case (molde)

```csharp
public sealed class CadastroDeSetorUseCase(ISetorRepository repositorio)
{
    public Task<Result<SetorDto>> Cadastrar(NovoSetorDto novo, CancellationToken ct);
    public Task<Result<SetorDto>> Editar(int id, NovoSetorDto alterado, CancellationToken ct);
    public Task<IReadOnlyList<SetorDto>> Listar(bool incluirInativos, CancellationToken ct);
    public Task<Result> DefinirAtivo(int id, bool ativo, CancellationToken ct);
}
```

A forma acima é a do **catálogo**. As variações:

- `CadastroDePedidoUseCase` — sem `DefinirAtivo` e sem `Excluir` (`Pedido` não tem `Ativo` e
  não expõe exclusão). `Cadastrar` recebe `int usuarioId` explícito (autoria).
- `CadastroDeAgrupamentoUseCase` — sem `DefinirAtivo`; tem
  `Task<Result> Excluir(int id, CancellationToken ct)`, que verifica *vazio* + Pedido
  `Aberto` antes de apagar. `Cadastrar` também recebe `int usuarioId`.

O `usuarioId` é resolvido **pelo controller** a partir da claim de identidade do JWT e
passado como parâmetro: `Application` não conhece `HttpContext`.

`Result` / `Result<T>` são os da Fase 0 (`Application/Common/Result.cs`) — não criar outro.

### Regras específicas de 1B

- **Ciclo em `ComponenteFilhoPadrao`.** O DDL barra só a auto-referência direta
  (`CK_ComponenteFilhoPadrao_NaoAutoReferencia`). Ciclo indireto (A→B→A, A→B→C→A) passa pelo
  banco e **tem que ser barrado no use case**: antes de inserir pai→filho, percorrer a
  descendência de `filho` e recusar se `pai` aparecer. Erro `409 { "erro": "CicloNaReceita" }`.
- **Reordenação do roteiro** sob `UQ_ComponenteRoteiroPadrao (ComponenteId, Ordem)`: trocar
  a ordem de dois setores colide no índice se feito linha a linha. A operação é
  **substituição da lista inteira em transação** (`PUT /componentes/{id}/roteiro-padrao` com
  o array completo), não edição incremental de `Ordem`.
- **Referência a item inativo:** recusar incluir `Material` ou `Setor` inativo numa receita;
  receitas existentes que já os referenciam continuam válidas (não se reescreve o passado).

---

## Acceptance Criteria (testáveis)

**1A:**

1. `POST /setores` com nome novo → 201; com nome já existente e ativo → 409
   `existeInativo:false`; com nome de um Setor inativo → 409 `existeInativo:true` + `idExistente`.
2. `PATCH /setores/{id}/ativo` com `{ativo:false}` → 204, e o Setor some de `GET /setores`
   mas aparece em `GET /setores?incluirInativos=true`.
3. `POST /setores` autenticado como `Operador` → 403. Como `Administrador` → 201.
4. `GET /setores` autenticado como `Operador` → 200 (leitura é livre para autenticado).
5. `POST /pedidos` grava `CriadoPorUsuarioId` igual ao `Id` do usuário da claim, e
   `DataAbertura` em UTC. `Status` nasce `Aberto`, `Tipo` nasce `Fabricacao`.
6. `POST /pedidos/{id}/agrupamentos` grava `CriadoPorUsuarioId` e `CriadoEm`.
7. `DELETE /agrupamentos/{id}` vazio, com Pedido `Aberto` → 204. Repetido → 404.
8. `POST /pedidos` como `Qualidade` → 403; como `PCP` → 201; como `Administrador` → 201.
9. **Critério de pronto da fase** (`06-roadmap-mvp.md:23`): pela tela, logado como
   Administrador cadastra um Setor e um Material; logado como PCP cadastra um Pedido e
   dois Agrupamentos vazios nele; ambos aparecem em `GET /pedidos/{id}`, com autor gravado.

**1B:**

10. `POST /componentes` com `Tipo` fora de `Bruto|Fabricado|Montagem` → 400 (validado na
    aplicação, não pela exceção do `CK_Componente_Tipo`).
11. Adicionar filho que fecha ciclo (A→B já existe; tentar B→A) → 409 `CicloNaReceita`. Idem
    para ciclo de 3 níveis.
12. `PUT /componentes/{id}/roteiro-padrao` com a ordem de dois setores invertida → 204, sem
    violar `UQ_ComponenteRoteiroPadrao`.
13. Incluir `Material` inativo numa receita → 409.
14. **Critério de pronto:** pela tela, cadastrar um `Componente` tipo `Montagem` com filhos,
    materiais e roteiro; e o sistema recusa o ciclo.

---

## Testing approach

Mantém a divisão que já existe no repositório (ver `CLAUDE.md`, "Pré-requisito externo dos
testes" — parte da suíte roda contra SQL Server real, via `docker compose up -d`):

- **`Application.Tests`** (fakes, sem banco): regras de cada `CadastroDe…UseCase` — duplicata
  detectada antes do insert, autoria preenchida a partir do parâmetro, guarda do delete de
  Agrupamento, e (1B) a detecção de ciclo, que é a lógica mais densa da fase e merece bateria
  própria incluindo ciclo de 3+ níveis.
- **`Infrastructure.Tests`** (SQL Server real): mapeamento EF de cada entidade nova contra o
  DDL — inclusive as colunas de autoria e os defaults de `CriadoEm`/`DataAbertura`.
- **`Api.Tests`** (ponta a ponta, SQL Server real): autorização por perfil (403 no perfil
  errado, 200/201 no certo), contrato de 409 com `existeInativo`, e o fluxo do critério de
  pronto.

TDD conforme `superpowers:test-driven-development`: teste falhando antes da implementação,
em cada passo do plano.

---

## Task outline (o `writing-plans` refina em passos bite-sized)

**Plano de 1A:**

1. Schema: colunas de autoria em `Pedido` e `Agrupamento` no `.sql` + `ALTER` idempotente
   documentado.
2. Vertical slice completo de `Setor` (Domain → Application → Infrastructure → Api → tela),
   **incluindo o contrato de 409 com `existeInativo`** — é aqui que o molde nasce.
3. `Material` repetindo o molde.
4. `Pedido`, somando autoria a partir da claim.
5. `Agrupamento`, somando aninhamento sob Pedido e a guarda do `DELETE`.
6. Atualizar `specs/05-api-endpoints.md` com os verbos novos.

**Plano de 1B** (só depois de 1A fechada): cabeçalho de `Componente` → materiais-padrão →
roteiro-padrão (substituição transacional da lista) → filhos-padrão (com detecção de ciclo,
o passo mais denso).

---

## Decisões resolvidas (não reabrir sem justificativa nova)

- **A receita é Fase 1, a cópia é Fase 2.** Receita = catálogo; depende só de entidades da
  Fase 1. Cópia = instância; é o que a Fase 2 constrói. A cópia é snapshot, sem propagação.
- **Corte 1A/1B por dificuldade, não por domínio.** O molde do slice nasce em `Setor`, e 1A
  fecha o critério de pronto do roadmap sozinha.
- **Autoria (`CriadoPorUsuarioId`) entra agora**, em `Pedido` e `Agrupamento` apenas — não no
  catálogo. Responde à ameaça de poluição de registros, que política de exclusão não responde.
- **Exclusão por natureza de tabela** (catálogo inativa, documento não exclui, evento é
  imutável) — não uma regra uniforme sobre o schema.
- **Uma classe de use case por entidade**, `CadastroDe<Entidade>UseCase`; classe própria só
  para operação com regra densa.
- **Camada global de erro e gating de navegação por perfil ficam fora de 1A** — protótipo
  executável primeiro. A fronteira de autorização real permanece no backend.
- **Não DDD-ificar.** O projeto é Clean Architecture com modelo anêmico e regras nos use
  cases (`03-arquitetura-tecnica.md:5`), e Database First é incompatível com o model-first do
  DDD. O único agregado que se pagaria neste modelo é a **Peça** (`EstruturaItem` de topo),
  por causa da conservação de quantidade da Fase 3 — quando lá chegar,
  `IEstruturaItemRepository` deve carregar a Peça com subárvore e movimentações, não oferecer
  acesso genérico linha a linha. Nada a fazer em 1A além de não criar precedente contrário.

---

## Riscos e pontos em aberto

- **CRUD de `Usuario`/`Perfil` não pertence a nenhuma fase.** Está em
  `05-api-endpoints.md:17-18` como rota de Administrador, mas o roadmap não o reivindica em
  fase alguma. Até que alguma o faça, criar usuário exige `db/seed.sql` ou SQL manual —
  buraco operacional real, registrado aqui sem ser resolvido.
- **`Agrupamento.Quantidade` é campo solto na Fase 1**, mas a partir da Fase 3 ele conversa
  com a conservação de quantidade. Editá-lo depois de existirem apontamentos deixa de ser
  inócuo; a guarda correspondente é da Fase 3, não desta.
- **Dívidas rastreadas de 1A:** camada global de erro de API; gating de navegação por perfil.
- **Herdadas da Fase 0, ainda abertas** (ver `CLAUDE.md`): tabela de auditoria persistente;
  limpeza de `RefreshToken` expirados; `SigningKey` como segredo de ambiente e
  `UseHttpsRedirection`; isenção do `/auth/refresh` no rate limit.

---

## Referências (specs de domínio — fonte da verdade)

- `specs/00-visao-geral.md:28-33` — perfis e suas responsabilidades
- `specs/01-dominio-e-regras-de-negocio.md:8-9,21-22,31-33` — Agrupamento, Componente como
  catálogo, roteiro customizável
- `specs/02-modelo-de-dados.sql` — schema (fonte de verdade)
- `specs/03-arquitetura-tecnica.md:5-36,39-53` — camadas, Database First, autorização
- `specs/04-fluxos-de-usuario.md:6-19` — fluxo "Cadastro de Pedido"
- `specs/05-api-endpoints.md:20-36,53` — rotas de catálogo e de pedido
- `specs/06-roadmap-mvp.md:19-30` — escopo declarado das Fases 1 e 2
