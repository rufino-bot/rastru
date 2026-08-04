# Fase 1B — CRUD de `Componente` (catálogo)

Design validado em 2026-08-04. Branch: `fase-1b-componente-receita-padrao`, saindo de `a417baf`
(merge do PR #4, prefixo `/api` fechado).

## 1. Recorte da fase, e o que ele custa

A Fase 1B do roadmap era `Componente` **mais** a receita padrão (`ComponenteFilhoPadrao`,
`ComponenteMaterialPadrao`, `ComponenteRoteiroPadrao`). O usuário optou por partir em duas:

- **1B (esta fase):** CRUD de `Componente`, com busca e paginação no servidor.
- **1C (próxima):** os três sub-recursos da receita padrão.

Razão dada: o particionamento da 1A se provou, e fatiar reduz a janela de erro complexo e de lacuna
no modelo.

**Consequência, nomeada em vez de descoberta depois: a Fase 2 fica bloqueada até a 1C existir.** A
Fase 2 é "criar `EstruturaItem` a partir de um `Componente` padrão (copiar receita)" — sem receita
cadastrada não há o que copiar. A 1C não é opcional nem adiável indefinidamente; é pré-requisito.

## 2. Decisões tomadas nesta sessão

| Decisão | Escolha | Quem decide |
|---|---|---|
| Perfis com permissão de escrita | **Administrador + PCP** | usuário |
| Busca | **no servidor** (`?busca=`), não no cliente | usuário |
| Paginação | **entra junto** com a busca | usuário |
| Tamanho de página na tela | **seletor 20 / 50 / 100** | usuário |
| `@testing-library/react` | **adotar nesta fase** | usuário |
| Contrato paginado | `PaginaDto<T>` genérico; `Setor`/`Material` **não** migram agora | usuário |

`GET` continua aberto a qualquer perfil autenticado, como em `Setor` e `Material`.

`/componentes` é a primeira entidade de **catálogo** com dois perfis de escrita: na 1A, catálogo era
só Administrador, e PCP só aparecia em `Pedido`/`Agrupamento`.

### Por que `Setor`/`Material` não migram para paginação agora

O ledger registra a lição *"quando uma convenção nova nasce, varrer o código anterior contra ela é
passo obrigatório"* — ela nasceu de `criarSetor`/`criarMaterial` terem ficado 5 tasks sem prova de
URL. Aquela era uma convenção de **prova**: o código antigo estava desprotegido. Esta é uma
convenção de **forma**, e `Setor` (dezenas de linhas) e `Material` (uma centena) não têm o problema
de volume que motivou a paginação — a listagem não-paginada deles não está errada, está adequada ao
tamanho.

O que a fase **não** pode fazer é deixar a paginação nascer com formato ad-hoc: por isso o
`PaginaDto<T>` entra genérico já na 1B, mesmo com um consumidor só. Migrar `Setor`/`Material` depois
vira preencher, não redesenhar. Fica como **dívida rastreada**, não como item esquecido.

## 3. Modelo de dados — nada muda

`dbo.Componente` já existe em `specs/02-modelo-de-dados.sql` (fonte de verdade) e no banco de dev.
A 1B **não altera schema**.

```
Id            INT IDENTITY  PK
Codigo        NVARCHAR(50)  NOT NULL  UQ_Componente_Codigo
Descricao     NVARCHAR(200) NOT NULL
Tipo          NVARCHAR(20)  NOT NULL  CK_Componente_Tipo IN ('Bruto','Fabricado','Montagem')
ArquivoSolido NVARCHAR(260) NULL      -- Fase 2
ArquivoFoto   NVARCHAR(260) NULL      -- Fase 2
Ativo         BIT           NOT NULL  DEFAULT 1
```

**`ArquivoSolido` e `ArquivoFoto` não são mapeadas na 1B.** O upload e a regra 18 (sólido obrigatório
por Peça de Pedido) são trabalho da Fase 2. A entidade da 1B tem 5 propriedades: `Id`, `Codigo`,
`Descricao`, `Tipo`, `Ativo`.

> **Correção registrada:** durante o design, a justificativa dada para não mapeá-las foi que o banco
> de dev não tinha essas colunas e qualquer `SELECT` do EF quebraria. Isso **deixou de valer** — o
> banco foi regenerado em 2026-08-04 e as colunas existem. A decisão de não mapear permanece, mas
> agora por **escopo** (é trabalho da Fase 2), não por restrição do banco.

### Banco de dev regenerado em 2026-08-04

O banco estava em desacordo com a fonte de verdade: `dbo.Componente` tinha 5 colunas,
sem `ArquivoSolido`/`ArquivoFoto` (que nasceram no schema depois de o banco ter sido criado) — a
lição transversal nº 7 do ledger, *"mudança de schema na fonte de verdade não chega sozinha ao banco
de dev"*, materializada.

Regenerado com `DROP DATABASE` + `specs/02-modelo-de-dados.sql` + `db/seed.sql`. Conferido depois:
20 tabelas, 6 perfis, 2 usuários (`admin`, `pcp`), `Componente` com 7 colunas. **Baseline conferida
de novo em seguida, toda verde: 263 backend (101 Application + 27 Infrastructure + 135 Api) · 45
front · `dotnet build -warnaserror` em 0 warnings.**

Efeito colateral a saber: os quatro blocos de `ALTER` idempotente documentados em `CLAUDE.md` (para
bancos pré-existentes) viraram no-op nesta máquina, e a instrução de `specs/06` de aplicar os
`ALTER` de `ArquivoSolido`/`ArquivoFoto`/`Descricao` ao iniciar a Fase 2 **não se aplica mais aqui**
— as colunas já vieram no `CREATE`. Os blocos continuam corretos para quem tiver banco antigo.

## 4. Contrato de API

```
GET    /componentes?busca=&incluirInativos=false&pagina=1&tamanho=20
       200 → { itens: ComponenteDto[], total, pagina, tamanho }        (qualquer autenticado)

POST   /componentes                { codigo, descricao, tipo }         (Administrador, PCP)
PUT    /componentes/{id}           { codigo, descricao, tipo }         (Administrador, PCP)
PATCH  /componentes/{id}/ativo     { ativo }                           (Administrador, PCP)
```

`ComponenteDto` = `(int Id, string Codigo, string Descricao, string Tipo, bool Ativo)`.

**Sem `DELETE`.** Política de exclusão por natureza de tabela, decidida na 1A: catálogo **inativa**
(`Ativo`), documento não exclui, evento é imutável. `specs/05` já registra a ausência de `DELETE`
para `/componentes`.

O front nunca escreve o prefixo `/api` à mão — quem o aplica é o `rota()` de
`web/src/api/client.ts`, e o call site passa o caminho **sem** prefixo (`/componentes`).

### Regras do contrato, com o motivo de cada uma

1. **`Tipo` é validado no caso de uso**, não por `[RegularExpression]` no DTO nem pelo
   `CK_Componente_Tipo`. Precedente direto: `NovoAgrupamentoDto` (`Kit`/`Avulso`). Exceção de `CHECK`
   sobe como `SqlException` e vira **500**; o cliente merece **400**. Forma:
   `private static readonly string[] TiposValidos = ["Bruto", "Fabricado", "Montagem"];`

2. **`MaxLength` espelha o DDL — 50 / 200 / 20 — e vai SEM `[property:]`**, no parâmetro do
   construtor primário do record. Com `[property:]` o MVC lança `InvalidOperationException`
   ("validation metadata ... that will be ignored") e a requisição vira 500 em vez de 400. Convenção
   já custeada na 1A (`NovoSetorDto`, `NovoMaterialDto`, `NovoAgrupamentoDto`).

3. **Ordenação por `Codigo`, sempre.** `UQ_Componente_Codigo` garante ordem **total**; sem ordem
   total a paginação repete e pula linhas entre páginas — defeito que só aparece com dados reais.

4. **`total` é contado com os mesmos filtros da página** (`busca` + `incluirInativos`), em consulta
   separada. `total` sem filtro faz o front calcular um número de páginas que não existe.

5. **Faixa: `pagina` começa em 1; `tamanho` default 20, teto 100.** Fora da faixa (`pagina < 1`,
   `tamanho < 1`, `tamanho > 100`) → **400**, não clamp silencioso: o estilo do projeto é erro
   explícito. O teto existe para `?tamanho=100000` não virar negação de serviço trivial. O seletor da
   tela (20/50/100) fica dentro do teto de propósito.

6. **Página além do fim não é erro:** 200 com `itens: []` e o `total` verdadeiro. É fim de lista, não
   pedido inválido.

7. **Busca casa em `Codigo` OU `Descricao`**, por `Contains`. Case-insensitive vem da **collation**
   do SQL Server (padrão CI), não de `ToLower()` na consulta — `ToLower()` impediria uso de índice.
   `busca` vazia ou só espaços = sem filtro. Mesmo `Trim` que o resto do projeto aplica a toda
   entrada de texto.

## 5. Camadas e arquivos

Mesmo caminho de `Material`; o único arquivo sem análogo é o `PaginaDto<T>`.

| Arquivo | Papel |
|---|---|
| `Domain/Entities/Componente.cs` | `Id`, `Codigo`, `Descricao`, `Tipo`, `Ativo` |
| `Domain/Abstractions/IComponenteRepository.cs` | contrato + `FiltroDeComponente` |
| `Infrastructure/Persistence/Configurations/ComponenteConfiguration.cs` | mapeamento explícito das 5 colunas |
| `Infrastructure/Persistence/ComponenteRepository.cs` | filtro, `OrderBy`, `Skip/Take`, `CountAsync` |
| `Application/Common/PaginaDto.cs` | **novo**, genérico, ao lado de `Result.cs` |
| `Application/Cadastros/Dtos.cs` | `ComponenteDto`, `NovoComponenteDto` (acrescentar) |
| `Application/Cadastros/CadastroDeComponenteUseCase.cs` | validação, duplicidade, faixa |
| `Api/Controllers/ComponentesController.cs` | herda `CadastroControllerBase` |
| `web/src/api/cadastros.ts` | `listarComponentes`, `criarComponente`, `definirAtivoComponente` |
| `web/src/pages/ComponentesPage.tsx` | tela |
| `web/src/App.tsx` (rota) + navegação | entrada da tela |

Formas propostas:

```csharp
public sealed record PaginaDto<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho);

public sealed record FiltroDeComponente(string? Busca, bool IncluirInativos, int Pagina, int Tamanho);

Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(FiltroDeComponente f, CancellationToken ct);
```

O repositório vive em `Domain` e por isso devolve **tupla de entidades**, não `PaginaDto<T>` — DTO é
camada de `Application`. Quem monta o `PaginaDto` é o caso de uso.

**Sem `editarComponente` no front.** O `PUT /componentes/{id}` existe e fica provado no backend, mas
a tela não tem UI de edição (mesmo molde de `MateriaisPage`). Exportar a função sem chamador seria
código morto — é a mesma decisão, com o mesmo motivo, que `cadastros.ts` já registra para
`editarPedido`. Ela nasce junto com a tela que a usar.

A validação da faixa fica **no caso de uso**, não no controller: é regra, e regra mora em
`Application` neste projeto.

`ObterPorIdAsync` e `ObterPorCodigoAsync` seguem **sem `AsNoTracking`**, como em
`MaterialRepository` — `Editar` e `DefinirAtivo` mutam a entidade devolvida e dependem do change
tracking. Só a listagem usa `AsNoTracking`.

## 6. Front

`ComponentesPage` no molde de `MateriaisPage`, mais:

- input de busca;
- controles de página (anterior / próxima, "página X de Y");
- seletor de tamanho de página (20 / 50 / 100);
- `<select>` de `Tipo` no formulário, com as três opções do `CHECK`.

**Regra que parece detalhe e não é: mudar a busca (ou o tamanho de página) reseta para a página 1.**
Sem isso, buscar algo que cabe em 2 páginas estando na página 7 mostra lista vazia, com cara de bug.

**A montagem da URL com os query params mora em `cadastros.ts`, não no componente** — é o que permite
prová-la em `cadastros.test.ts`. Essa é exatamente a convenção de "prova de URL" que faltou em
`criarSetor`/`criarMaterial` por 5 tasks na 1A.

### `@testing-library/react` entra nesta fase

Era dívida marcada no ledger como *decisão de fase, com gatilho novo a cada rodada*. A 1B é o
gatilho: pela primeira vez o componente carrega **comportamento** (reset-de-página, estado de
paginação, seletor de tamanho), não só marcação — e sem a lib isso nasceria sem prova possível.

Escopo do que a lib cobre nesta fase: o reset-de-página e os controles de paginação da
`ComponentesPage`. Cobrir a dívida antiga da `PedidoDetalhePage` (caminho "Cancelar" do modal,
`autoFocus`, `required`) **não** é obrigação da 1B — fica destravado e vira item de backlog com
caminho aberto.

## 7. Tratamento de erro

Tudo reaproveitando `CadastroControllerBase`; nenhuma tradução nova.

| Situação | Resposta |
|---|---|
| `codigo` já existe (inclusive em linha inativa) | 409 `{ erro: "ValorDuplicado", campo: "codigo", existeInativo, idExistente }` |
| campo vazio ou só espaços; `tipo` fora da lista; faixa de paginação inválida | 400 `{ erro }` |
| `PUT` / `PATCH` em id inexistente | 404 |
| sem token / perfil sem permissão | 401 / 403 |

O 409 carrega `existeInativo` porque `UQ_Componente_Codigo` **não** é filtrado por `Ativo`: código de
linha inativa continua ocupado, e é isso que permite a tela oferecer "reativar o existente" em vez de
travar quem cadastra.

A checagem de duplicidade acontece **antes** do insert, para dar erro de negócio claro em vez de
deixar a violação de índice vazar como 500; o índice `UNIQUE` permanece como rede de segurança para a
corrida entre a checagem e a escrita. Em `Editar`, só é conflito se o código pertencer a **outra**
linha — manter o próprio código é no-op.

## 8. Testes e critério de pronto

**O critério de pronto não é "os testes passam"** — é *"antes a mutação não matava nada, depois mata,
e mata só o esperado"*. Mutações que esta fase precisa matar:

| Mutação | Teste que tem de ficar vermelho |
|---|---|
| apagar `OrderBy(c => c.Codigo)` | paginação com mais de uma página e códigos inseridos fora de ordem alfabética |
| contar `total` sem os filtros | busca que reduz o conjunto; conferir `total`, não só `itens` |
| remover o teto de `tamanho` | `?tamanho=101` → 400 |
| remover `Roles = "Administrador,PCP"` do `POST` | Operador recebe 403 **e nada entra no banco** |
| `Contains` só em `Codigo` | busca por texto que existe apenas na `Descricao` |
| trocar `pagina` 1-based por 0-based no `Skip` | página 2 não pode repetir linha da página 1 |
| aceitar `Tipo` fora da lista | `POST` com `tipo: "Qualquer"` → 400, não 500 |
| não resetar página ao mudar a busca | teste de componente: buscar estando na página 2 volta para a 1 |

Sobre a mutação de `Roles`: achado **B10** da 1A — mutar `[Authorize(Roles)]` em verbo de escrita
**mexe no banco**, porque sobra o `[Authorize]` de classe, o request passa e a escrita acontece. O
teste tem de **conferir o banco**, não deduzir do status.

Distribuição por projeto:

- **`Application.Tests`** (fakes, sem banco): campos obrigatórios; `Tipo` inválido e os três válidos;
  duplicidade em `Cadastrar` e em `Editar` (incluindo "manter o próprio código é no-op"); faixa de
  paginação (`pagina < 1`, `tamanho < 1`, `tamanho > 100`, defaults); tradução de `busca` vazia para
  "sem filtro".
- **`Infrastructure.Tests`** (SQL Server real): mapeamento das 5 colunas; `UQ_Componente_Codigo`;
  `CK_Componente_Tipo`; e a **paginação/ordenação/contagem acontecendo no SQL** — é aqui que a
  paginação se prova de verdade.
- **`Api.Tests`** (ponta a ponta): 401 sem token; 403 para Operador (com conferência do banco); 200
  para Administrador **e** para PCP; 409 / 404 / 400; paginação e busca pela URL; caminho sob o
  prefixo `/api`.
- **`web`**: `cadastros.test.ts` para a URL montada com os query params; testes de componente
  (`@testing-library/react`) para reset-de-página e controles.

## 9. Fora de escopo — nomeado, não esquecido

| Item | Destino |
|---|---|
| `ComponenteFilhoPadrao`, `ComponenteMaterialPadrao`, `ComponenteRoteiroPadrao` | **1C** — e a Fase 2 depende dela |
| Upload de `ArquivoSolido` / `ArquivoFoto` e a regra 18 | Fase 2 |
| `CK_EstruturaItem_PecaTemComponente` | Fase 2 (já registrado em `specs/06`) |
| Import de estrutura a partir do CAD | fase futura — ver apêndice |
| Migrar `Setor`/`Material` para o contrato paginado | dívida rastreada |
| Cobrir `PedidoDetalhePage` com testes de componente | backlog (destravado por esta fase) |
| Camada global de erro de API; gating de navegação por perfil | backlog de Fase 1 (corte do usuário na 1A) |

## 10. Atualizações de spec que a fase deve fazer

- **`specs/05-api-endpoints.md`**: `/componentes` ganha perfis, query params e o contrato paginado;
  registrar que os três sub-recursos ficam para a 1C.
- **`specs/06-roadmap-mvp.md`**: dividir a Fase 1B em 1B e 1C; registrar explicitamente que a Fase 2
  fica bloqueada até a 1C; anotar que os `ALTER` de início da Fase 2 não se aplicam a um banco
  regenerado.
- **`CLAUDE.md`**: registrar a regeneração do banco e que os blocos de `ALTER` viraram no-op nesta
  máquina (continuam válidos para banco antigo).

## Apêndice — viabilidade de importar a estrutura do CAD

Levantado pelo usuário nesta sessão, respondido e **decidido** aqui. **Não é trabalho da 1B nem da
1C.** A decisão está registrada em `specs/06-roadmap-mvp.md`, que é a fonte de verdade do roadmap;
este apêndice guarda o raciocínio que levou a ela.

A pergunta era se dá para ler o **arquivo de montagem** (`.SLDASM`) e gerar automaticamente os
componentes, quantidades e `EstruturaItem`.

**Ler `.SLDASM` diretamente: não é viável aqui.** É formato proprietário e não documentado da
Dassault (OLE compound file com estruturas fechadas). Não existe biblioteca aberta e confiável que
leia a árvore de montagem. As saídas reais são a **SolidWorks Document Manager API** (lê sem abrir o
SolidWorks, mas exige chave de licença da Dassault) ou a **API COM do SolidWorks** (exige SolidWorks
instalado e licenciado na máquina do servidor — inviável para API web on-premise).

Isso é coerente com decisão já tomada: `specs/02` define `ArquivoSolido` como **STEP ou STL, não
`.SLDPRT`**, justamente por ser proprietário. A mesma lógica vale um nível acima, para a montagem.

**O que é viável, em ordem de custo:**

1. **BOM indentado exportado (CSV/XLSX)** — recomendado. O SolidWorks exporta a *Bill of Materials*
   com nível de indentação, part number, descrição e quantidade: literalmente a forma de
   `ComponenteFilhoPadrao` (pai → filho + `QuantidadePadrao`) e de `EstruturaItem`. Parser trivial em
   C#, sem dependência proprietária.
2. **STEP AP242 / AP214 da montagem** — formato ISO aberto, carrega a árvore de produtos com
   ocorrências; quantidade sai de contar ocorrências. Vantagem: é o mesmo arquivo que serve ao
   sólido/silhueta. Desvantagem: parser bem mais pesado, e o STEP costuma trazer **nome de arquivo**,
   não part number — a conciliação piora.
3. **Document Manager API** — só se a empresa já tiver a licença. Não depender disso.

**Caminho escolhido pelo usuário em 2026-08-04: o BOM indentado (opção 1).** "Se o Solid consegue
exportar o BOM, já nos atende."

**A conciliação part number ↔ `Componente.Codigo` foi resolvida como decisão de negócio, não de
código.** Eu havia levantado que, se o part number do CAD não for o mesmo `Codigo` do catálogo, o
import não casa nada e vira "criar tudo de novo a cada montagem". O usuário fechou a questão: **quem
importa confere depois as peças, código e quantidade** — e assumiu explicitamente que isso exige
disciplina de cadastro que a empresa hoje não tem por padrão, com parte do esforço partindo do lado
do cliente. É pré-requisito de uso da ferramenta, declarado, não um risco pendente.

**Consequência que essa decisão traz, e que vale a favor:** se um humano confere código e quantidade
de qualquer modo, **o import não precisa acertar tudo**. Ele é pré-preenchimento, não automação — um
casamento parcial já entrega valor, e a fase não carrega o requisito de resolver o caso ambíguo.
Isso baixa a barra técnica de forma significativa e é o que torna a opção 1 suficiente.

**Forma quando virar fase:** o import gera uma **proposta** que um humano confere e confirma, nunca
gravação direta — "importar do CAD e conferir". **Nada disso muda o schema**: as tabelas da 1B e da
1C já são o destino do import. Registrado em `specs/06-roadmap-mvp.md`.

**Efeito único sobre a 1C:** o caso de uso que grava a receita deve aceitar **uma lista de linhas de
uma vez**, não só uma linha por chamada. É quase de graça e evita reescrever o caso de uso depois; a
tela continua digitando linha a linha.

Sobre o sólido/foto: confirmado pelo usuário que serve ao operador reconhecer a peça no apontamento —
exatamente o que já está registrado (regra 18, `ArquivoFoto` opcional, busca por foto como
**ordenador da lista do setor**, nunca identificador global, e só depois de um spike medir acerto no
top-3). Continua fora da 1B.
