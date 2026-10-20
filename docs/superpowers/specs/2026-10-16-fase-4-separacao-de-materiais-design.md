# Fase 4 — Separação de materiais — desenho

Data: 2026-10-16. Branch: `claude/nice-babbage-wsvl9v`, a partir da `main` em `17890b2`. Desenho aprovado
pelo usuário em chat, numa sessão de nuvem iniciada em 2026-10-15: dez perguntas de produto, a abordagem
técnica e as quatro seções do desenho (schema e regras, API, telas, testes e escopo), cada uma aprovada em
separado.

**Ponto de partida.** A seção "Fase 4 — Separação de materiais" de `specs/06-roadmap-mvp.md` pede o registro
de `MaterialSeparacao` por `EstruturaItem`, "onde cada material fica" e a requisição de material que o
operador abre e o Almoxarifado atende (decisão da seção 2.3 da spec da Fase 3,
`docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md`). O critério de pronto do roadmap
é: dá para saber quais materiais já foram separados/entregues para cada item em fabricação.

**Quando executa.** Na ordem do roadmap: a Fase 3B foi mesclada em 2026-10-14 (rufino-bot/rastru#33), e a
Fase 4 é a seguinte. Não é exceção à ordem das fases.

**O que o código tinha antes desta spec** (lido em `17890b2`):

- `dbo.MaterialSeparacao` existe **só no schema**: sem entidade, sem mapeamento, sem rota, sem tela. Nenhum
  código a escreveu.
- O perfil `Almoxarifado` existe no seed e não autoriza nenhuma rota (nenhum `[Authorize(Roles)]` o cita).
- `EstruturaMaterial` (o planejado do nó) é copiado da receita padrão pelo `PlanejadorDeCopia`, já
  multiplicado pela quantidade do nó. Não há rota para editá-lo depois da cópia: o
  `05-api-endpoints.md` deixava `GET/POST /estrutura-itens/{id}/materiais` "sem fase atribuída".
- A receita padrão (`ReceitaPadraoUseCase`) **já aceita** materiais num Componente que tem filhos: nada
  restringe material à folha, ao contrário do que diz a regra 4 do `01`.

## 1. O que se quer, e o que conta como pronto

**Como a separação acontece na fábrica** (palavras do usuário, resumidas). O material do almoxarifado é
separado **por antecipação**, olhando o que vai ser fabricado, e principalmente em dois casos:

- **o Kit da Fase 3B**, montado na Solda: o Separador leva o material até lá;
- **a montagem final**, que acontece sempre na **Expedição**: quem está na expedição tem a árvore de
  montagem e busca o material.

Nos dois casos o material pertence a um **nó com filhos** (o Kit, ou a Peça) e é usado no Setor do
**primeiro passo do Roteiro** desse nó, onde o nó inicia consumindo os filhos (regra 24). A Peça da
montagem final tem um Roteiro de **um passo só**, a Expedição, e a montagem é o início dela ali. **A
execução não muda.** A chapa, ao contrário, fica estocada no Corte, e o operador a pega ali mesmo.

**Pronto é:**

- o Separador tem uma lista do que falta levar para cada nó, só do material que fica no almoxarifado,
  ordenada pelo prazo do Pedido, e registra pelo celular o que levou;
- o PCP edita os materiais planejados de qualquer nó, inclusive o que nasceu sem receita;
- o painel do nó mostra, para cada material, planejado, separado e pendência, com o histórico;
- a Fila do Setor mostra, na linha do nó que inicia ali, se o material dele está completo;
- o cadastro de Material diz onde cada material fica.

## 2. Decisões tomadas na conversa

| # | Decisão | Alternativas descartadas |
|---|---|---|
| D1 | O Separador **se antecipa**: trabalha pelo planejado, sem esperar pedido de ninguém | só separar sob requisição do operador; depender do material (parte antecipada, parte sob pedido) |
| D2 | A montagem final é o **início da Peça** num Roteiro de um passo só, a Expedição. O material de um nó com filhos é usado no Setor do primeiro passo dele. **A execução não muda**, e a **regra 4 do `01` é corrigida**: qualquer nó pode ter Materiais | montagem final depois do Roteiro, no local de expedição, fora do livro (pediria uma montagem nova na regra 29 e na Fase 5); Peça com passos antes da montagem e consumo no último (contraria a regra 24 da 3D) |
| D3 | O "Kit" da separação é **o Agrupamento Kit da 3B**, na Solda | um "kit de material" (saco de parafusos) como forma física da separação |
| D4 | **Um evento só, registrado por quem tira o material**: o Separador, que leva à Solda e busca para a Expedição. "Separado" e "entregue" são a mesma coisa | um evento só, registrado pelo almoxarifado no balcão; dois eventos, "separado" e "recebido" |
| D5 | O perfil **`Almoxarifado` é renomeado para `Separador`**. Continuam sendo sete perfis. Revê o descarte da seção 2.2 da spec da Fase 3 (seção 9 desta spec) | manter o nome `Almoxarifado`; perfil novo, ao lado do Almoxarifado |
| D6 | O **Material ganha "Fica em"**: Almoxarifado (o padrão) ou um Setor. Só o material do almoxarifado entra na lista do Separador | marca no Setor ("recebe material do Separador"), que não separa a chapa do parafuso usados no mesmo Setor; não guardar, com a lista poluída de chapas |
| D7 | **Sem requisição de material** nesta fase. A lista calculada (planejado − separado) já responde "o que falta". A requisição volta se a fábrica sentir falta dela (seção 10) | requisição para o que está além do planejado ou fora dele; requisição como "preciso agora"; as duas |
| D8 | A fase **inclui editar os materiais do nó** (`GET`/`PUT /estrutura/{id}/materiais`), pelo PCP. Atribui fase ao que o `05` deixava sem fase | planejado só pela receita; editar com fase decidida para depois |
| D9 | **Só se separa material do planejado do nó.** Acima do planejado, a tela avisa e aceita (o parafuso que caiu). Fora do planejado, recusa: é erro de plano, e o PCP o corrige pelo D8 | qualquer material, com aviso; só até o planejado |
| D10 | A correção é **por estorno**, só de inclusão, como no livro de movimentações. **Sem saldo de estoque e sem devolução** | excluir a linha; estorno mais "devolução" como conceito próprio |
| D11 | A lista do Separador traz o nó **desde que ele existe**, ordenada pela data de entrega do Pedido, com filtros por Setor de destino, Pedido e Material. Pedido pausado fica na lista; nó sem Roteiro aparece como "sem Roteiro" | só depois de o Pedido entrar em produção; só quando o nó está perto de montar |
| D12 | O separado aparece no **painel do nó** (detalhe e histórico) e na **Fila do Setor** (resumo neutro na linha do nó que inicia ali). O início **não** é bloqueado nem pede confirmação | só no painel do nó; aviso com confirmação no início |
| D13 | A separação é um **livro próprio, só de inclusão** (`dbo.MaterialSeparacao` reescrita), e o separado é sempre **calculado** | um `Tipo` novo em `dbo.Movimentacao` (mistura material com a conservação de quantidade dos nós, regra 9); coluna `QuantidadeSeparada` em `EstruturaMaterial` (estado duplicado, sobrescrito sem rastro, contradiz o D10) |
| D14 | Editar os materiais do nó é **livre a qualquer momento**, inclusive depois de separar. O plano decide o que pode ser separado daqui em diante; o que já foi separado é fato e fica no histórico | recusar tirar do plano um material com separação até ela ser estornada |
| D15 | O resumo da Fila conta **materiais completos, não quantidades** ("Material: 2 de 3 completos") | soma de quantidades (8 parafusos mais 2 kg de eletrodo não é um número) |
| D16 | O Separador **pode** registrar um material que fica num Setor, se ele estiver no planejado do nó. Ele só não aparece na lista | recusar material de Setor |
| D17 | O painel "Separar" abre **preenchido com a pendência**: o caso comum é um toque só | campos vazios |

## 3. Modelo de dados

### 3.1 `dbo.Material` — onde o material fica

Em `specs/02-modelo-de-dados.sql`, primeiro (Database First):

```sql
-- Onde o material fica (Fase 4, D6). NULL = almoxarifado: entra na lista do Separador.
-- Um Setor = estocado ali (a chapa no Corte): o operador pega no proprio Setor.
SetorDeEstoqueId    INT                 NULL,
CONSTRAINT FK_Material_SetorDeEstoque FOREIGN KEY (SetorDeEstoqueId) REFERENCES dbo.Setor (Id),
```

`dbo.Setor` é a primeira tabela do `.sql`, antes de `dbo.Material`, então a FK cabe no próprio `CREATE
TABLE` do Material.

### 3.2 `dbo.MaterialSeparacao` — reescrita como livro

```sql
CREATE TABLE dbo.MaterialSeparacao (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId     INT                 NOT NULL,
    MaterialId          INT                 NOT NULL,
    Tipo                NVARCHAR(20)        NOT NULL, -- Separacao | Estorno
    Quantidade          DECIMAL(18,4)       NOT NULL, -- sempre positiva; o Estorno subtrai pelo Tipo
    EstornoDeId         INT                 NULL,     -- so no Estorno: a separacao que ele desfaz
    DataHora            DATETIME2           NOT NULL CONSTRAINT DF_MaterialSeparacao_DataHora DEFAULT (SYSUTCDATETIME()),
    UsuarioId           INT                 NOT NULL, -- autor
    CONSTRAINT PK_MaterialSeparacao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_MaterialSeparacao_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_MaterialSeparacao_Material FOREIGN KEY (MaterialId) REFERENCES dbo.Material (Id),
    CONSTRAINT FK_MaterialSeparacao_EstornoDe FOREIGN KEY (EstornoDeId) REFERENCES dbo.MaterialSeparacao (Id),
    CONSTRAINT FK_MaterialSeparacao_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_MaterialSeparacao_QuantidadePositiva CHECK (Quantidade > 0),
    CONSTRAINT CK_MaterialSeparacao_Tipo CHECK (Tipo IN ('Separacao', 'Estorno')),
    CONSTRAINT CK_MaterialSeparacao_EstornoApontaOriginal
        CHECK ((Tipo = 'Estorno' AND EstornoDeId IS NOT NULL)
            OR (Tipo = 'Separacao' AND EstornoDeId IS NULL))
);

CREATE UNIQUE INDEX UX_MaterialSeparacao_EstornoDe ON dbo.MaterialSeparacao (EstornoDeId) WHERE EstornoDeId IS NOT NULL;
CREATE INDEX IX_MaterialSeparacao_EstruturaItem ON dbo.MaterialSeparacao (EstruturaItemId);
```

- Saem `Responsavel` (texto livre) e `DataSeparacao`. O autor passa a ser `UsuarioId`, como em
  `Movimentacao`, `Pedido` e `Agrupamento`.
- A separação aponta `(EstruturaItemId, MaterialId)`, e **não** a linha de `EstruturaMaterial`: pelo D14, o
  PCP pode tirar do plano um material que já tem separação, e uma FK para o planejado impediria isso.
- O estorno repete nó, material e quantidade da linha original. A igualdade é validada na aplicação (o
  `CHECK` não alcança outra linha), como no livro de movimentações.
- **Só de inclusão**, pelo mesmo invariante do livro de movimentações: nunca `UPDATE` nem `DELETE` numa linha.

### 3.3 Perfil

- `db/seed.sql`: `'Almoxarifado'` vira `'Separador'` na lista de perfis, e o seed ganha o usuário
  **`separador`**, com senha e hash próprios, gerados com o mesmo `BCryptPasswordHasher` de produção, como o
  `operador` e o `movimentador` (decisão do usuário de 2026-10-13, que vale para todo usuário de perfil).
- O comentário de `dbo.Perfil.Nome` em `02-modelo-de-dados.sql` passa a listar `Separador`.

### 3.4 Migração de banco anterior — `db/alter-fase-4.sql`

Idempotente, `-b -f 65001`, no formato dos anteriores, com o bloco correspondente no `CLAUDE.md`:

1. `Material.SetorDeEstoqueId` e a FK, se a coluna não existir.
2. `dbo.MaterialSeparacao`: se a tabela tiver a forma antiga (a coluna `Responsavel` existe) **e estiver
   vazia**, é apagada e recriada na forma nova. Se tiver a forma antiga **e alguma linha**, o script **aborta
   com erro**, em vez de descartar dado em silêncio. Se já tiver a forma nova, não faz nada.
3. `UPDATE dbo.Perfil SET Nome = 'Separador' WHERE Nome = 'Almoxarifado'`, e o `INSERT` do perfil se nenhum
   dos dois existir.

O usuário `separador` entra por `db/seed.sql`, que é idempotente e reaplicável. Não é preciso regenerar o
banco.

### 3.5 O que não muda

`EstruturaMaterial`, `ComponenteMaterialPadrao`, `dbo.Movimentacao` e as tabelas da execução. O `PUT` dos
materiais do nó só passa a escrever em `EstruturaMaterial`.

## 4. Regras de domínio (`01`)

- **Regra 4, reescrita.** Qualquer `EstruturaItem`, folha ou com filhos, pode ter Materiais (o planejado,
  `EstruturaMaterial`). O material de um nó é usado no Setor do **primeiro passo** do Roteiro dele: numa
  folha, onde ela é fabricada; num nó com filhos, onde ele inicia consumindo os filhos (regra 24).
- **Regra 5, reescrita.** O material que fica no almoxarifado é separado pelo **Separador** e levado, ou
  buscado, até o Setor do primeiro passo do nó. O que fica num Setor, o operador pega ali. **Separar nunca
  condiciona o início**: a regra 28 não muda, e a falta de material é informação, não bloqueio.
- **Regra 34, nova: a separação é um livro só de inclusão.**
  - O **separado** de um material num nó é a soma das separações menos a soma dos estornos dele.
  - A **pendência** é o planejado menos o separado, quando positiva, e só existe para o material que fica no
    almoxarifado.
  - Só se separa material que está no planejado do nó. Acima do planejado, o sistema aceita e avisa.
  - A correção é um **estorno**, da linha inteira, no máximo um por linha, pelo autor da separação, pelo PCP
    ou pelo Administrador. Não há saldo de estoque nem devolução.
  - Editar o planejado é livre a qualquer momento. O que já foi separado continua no histórico, e um
    material separado que saiu do plano aparece como "fora do planejado".
- **Glossário.** "Separação" e "Separador" entram; a linha de "Usuário / Perfil" troca Almoxarifado por
  Separador.

## 5. Backend

### 5.1 Domínio e mapeamento

- `Material` ganha `SetorDeEstoqueId` (nulo = almoxarifado).
- Entidade nova `MaterialSeparacao`, com `Tipo` (`Separacao` | `Estorno`), mapeada em
  `Configurations/MaterialSeparacaoConfiguration.cs`.
- O cálculo de separado, pendência e "acima do planejado" vive **num lugar só** na Application (a mesma
  função serve a lista do Separador, o painel do nó e o resumo da Fila), para a regra não ser reescrita em
  três consultas.

### 5.2 Rotas

Todas sob `/api`, com as rotas de nó sob `estrutura/{id}` (o `05` pede que as rotas planejadas sob
`estrutura-itens/{id}` passem para esse prefixo).

**Materiais do nó (D8):**

- **`GET /estrutura/{id}/materiais`** — qualquer autenticado. Para cada material: `materialId`, código,
  descrição, unidade, `ficaEm` (`{ setorId, nome }` ou `null` = almoxarifado), `planejado`, `separado`,
  `pendencia` e `noPlanejado`. A lista inclui o material que saiu do plano e tem separação, com
  `noPlanejado: false` e `planejado: 0`.
  - **Emenda de 2026-10-19, decisão do usuário na conferência da Fase 4:** o material que saiu do plano só
    vem **enquanto o separado líquido for maior que zero**. Fora do planejado e com toda separação estornada,
    ele sai da lista: não resta nada dele no nó, e as linhas continuam no `GET /estrutura/{id}/separacoes`
    (o histórico não muda). O material do planejado vem sempre, mesmo com separado zero e a pendência
    inteira. O filtro mora no backend, porque é o contrato da rota; o `PUT` devolve a lista pela mesma regra.
- **`PUT /estrutura/{id}/materiais`** — PCP e Administrador. Corpo `[{ materialId, quantidade }]`, que
  **substitui** a lista inteira, no molde do `PUT /estrutura/{id}/roteiro`. Valida com as mesmas regras
  dos materiais da receita padrão (`ReceitaPadraoUseCase.SubstituirMateriais`): material repetido, material
  inativo, quantidade não positiva ou fora da escala `DECIMAL(18,4)` → `400`; nó inexistente → `404`.
  - **Emenda de 2026-10-20, decisão do usuário na review de branch da Fase 4 ("aceitar só no nó"):** a regra
    do material inativo **não é a da receita padrão**. O `PUT` aceita o material inativo que **já está no
    planejado atual do nó** e continua recusando (`400 MaterialInvalido`) o inativo que não está. O motivo é
    que o `PUT` é a lista inteira: recusar o inativo já planejado travava toda edição dos outros materiais do
    nó (nem a quantidade de outro material salvava) até o PCP tirá-lo do plano, o que apaga uma necessidade
    real da peça e, se ele já tinha separação, o transforma em "fora do planejado". É o critério que o Roteiro
    do nó e o "Fica em" do Material já têm para o Setor inativo já apontado (o teste `Editar_mantendo_o_setor_ja_inativado_e_aceito`, do cadastro de Material). A receita padrão
    (`ReceitaPadraoUseCase.SubstituirMateriais`) **não muda**: segue recusando o inativo, e fica como dívida
    registrada.

**Separação:**

- **`POST /estrutura/{id}/separacoes`** — Separador e Administrador. Corpo `{ linhas: [{ materialId,
  quantidade }] }`, **tudo ou nada**: valida tudo e grava as linhas numa escrita só, **sem** a transação da
  execução (decisão P1 do plano de backend), diferente do lote da Fila.
  - `201` com as linhas gravadas e o resumo novo de cada material tocado; a linha que passou do planejado
    volta com `acimaDoPlanejado: true` (a tela já avisou antes do envio).
  - `409 MaterialForaDoPlanejado` quando algum material não está no planejado do nó, nomeando-o;
    `400` para quantidade inválida, lista vazia ou material repetido no corpo; `404` para nó inexistente.
  - Material que fica num Setor e está no planejado é aceito (D16).
- **`POST /separacoes`** (**emenda de 2026-10-19, decisão do usuário na conferência da Fase 4**) — Separador e
  Administrador, os mesmos perfis da rota de um nó. Separar em vários nós de uma vez, **tudo ou nada**. Corpo
  `{ nos: [{ estruturaItemId, linhas: [{ materialId, quantidade }] }] }`.
  - Antes de olhar os nós, as recusas de entrada do lote da execução, nesta ordem: lista de nós vazia ou
    ausente (`400 LoteVazio`, "Marque pelo menos um item."), mais nós que o teto do lote da execução
    (`ApontamentoUseCase.TamanhoMaximoDoLote`, `400 LoteGrandeDemais`) e nó repetido no corpo
    (`400 ItemRepetido`).
  - Depois, cada nó pelas **mesmas regras e na mesma ordem** da rota de um nó (404, linhas vazias,
    quantidade, material repetido, fora do planejado), na ordem do corpo, parando no primeiro recusado. É o
    mesmo núcleo de validação, não uma cópia. A recusa de um nó leva o nome dele na frente da frase
    (`"{nome}: {frase}"`), como o lote da Fila; não levam o prefixo o `404`, que continua sem corpo, e o
    `409 MaterialForaDoPlanejado`, cuja frase já nomeia o nó.
  - Sem a transação da execução, como a rota de um nó (decisão P1 do plano de backend): valida todos os nós e
    só então grava as linhas de todos numa escrita só.
  - `201` com uma entrada por nó, na ordem do corpo: o `estruturaItemId` e o mesmo conteúdo da resposta da
    rota de um nó (as linhas gravadas e o resumo de cada material tocado).
- **`GET /estrutura/{id}/separacoes`** — qualquer autenticado. O histórico do nó, mais recente primeiro:
  `id`, `tipo`, material, `quantidade`, `dataHora`, autor, `estornoDeId` e `estornavel` (calculado para quem
  pede, como os `estornaveis` da Fase 3D).
- **`POST /separacoes/{id}/estorno`** — Separador, PCP e Administrador. O Separador só estorna a linha de
  que é autor: fora disso, `403 Proibido`. `409 JaEstornada` para a linha já estornada (inclusive na corrida:
  dois estornos concorrentes da mesma linha dão um `201` e um `409`, pelo índice único, e nunca `500`);
  `409` também para tentar estornar um estorno; `404` para linha inexistente.

**A lista do Separador (D11):**

- **`GET /separacoes/pendencias`** — qualquer autenticado. Todo nó com pendência maior que zero em material
  do almoxarifado, de Pedido **aberto** (status diferente de `Concluido` e `Cancelado`). Por item: o nó (id, código, descrição, Agrupamento), o Pedido
  (id, número, cliente, `dataEntrega`, `atrasado`, `pausa`), o **destino** (`{ setorId, nome }` do primeiro
  passo do Roteiro, ou `null` = "sem Roteiro") e os materiais pendentes (`materialId`, código, descrição,
  unidade, `planejado`, `separado`, `pendencia`). Ordem: data de entrega, depois Pedido, depois nó. Chega
  **inteira**, e a tela filtra no cliente, como a Fila e as Tarefas.

**Fila do Setor (D12, D15):**

- `GET /setores/{id}/fila`: a linha do nó que **inicia** naquele Setor e tem material do almoxarifado no
  planejado ganha `material: { completos, total }`, onde `total` é o número de materiais do almoxarifado no
  planejado e `completos` os de pendência zero. Os outros nós vêm com `material: null`.

**Cadastro de Material (D6):**

- `POST` e `PUT /materiais` aceitam `setorDeEstoqueId` (nulo = almoxarifado); Setor inexistente ou inativo →
  `400`. `GET /materiais` devolve o `ficaEm`.

### 5.3 Perfis

| Ação | Perfis |
|---|---|
| Separar (um nó ou em lote) | Separador, Administrador |
| Estornar uma separação | Separador (só a própria), PCP, Administrador |
| Editar os materiais do nó | PCP, Administrador |
| "Fica em" do Material | Administrador (como o resto do cadastro de Material) |
| Ler tudo o que está acima | qualquer autenticado |

### 5.4 Contrato de erro

O `05` ganha a tabela **"Contrato de erro da Separação"**, no formato `{ erro, mensagem }` do contrato da
Execução: `MaterialForaDoPlanejado`, `JaEstornada`, `EstornoDeEstorno` e `Proibido`, com o status de cada
um. As mensagens nomeiam o material e o nó. **Nomear o nó segue a regra 19** (emenda de 2026-10-20, decisão da
review de branch da Fase 4): a descrição própria do nó e, sem ela, o rótulo do Componente, o mesmo que a tela
mostra e que a lista do Separador já resolve ("do nó Quadro de comando"). O nó copiado do catálogo, que é o
caso comum, não tem descrição própria, e antes desta emenda a frase saía com o Id ("do item 42"), que a tela
não mostra em lugar nenhum. O mesmo nome vai na frente da frase do lote (`"{nome}: {frase}"`).

**Emenda de 2026-10-19, decisão do usuário na conferência da Fase 4:** o `POST /separacoes` (seção 5.2) reusa
os códigos do lote da execução, `LoteVazio`, `LoteGrandeDemais` e `ItemRepetido` (os três `400`, com a mesma
frase do lote da Fila nos dois primeiros), e devolve, para o nó recusado, o código e o status da rota de um
nó, com o nome do nó na frente da frase. O `404` continua sem corpo, e o `409 MaterialForaDoPlanejado` passa
sem o prefixo, porque a frase dele já nomeia o nó.

## 6. Front

### 6.1 Tela nova "Separação" (`/separacao`)

- Item **"Separação"** no menu do `AppShell`, visível a todos (leitura é livre; o gating vai na ação).
  **Emenda de 2026-10-19, decisão do usuário na conferência da Fase 4:** com o oitavo link, o cabeçalho quebrou
  em duas linhas em 1024px; o item "Início" saiu do menu (barra e gaveta), e a marca "Rastru" do cabeçalho virou
  o link para a Home, com o nome acessível "Rastru — Início".
- `<Pagina titulo="Separação">`, pensada para o **celular**: um cartão por nó, com o código e a descrição do
  nó; o Pedido com cliente e data de entrega; a pílula "Atrasado" e a de pausa, quando se aplicam (são
  estado, e usam os tons já reservados); o destino ("→ Solda", "→ Expedição" ou "sem Roteiro"); e uma
  linha por material pendente ("PAR-M8 Parafuso M8 · falta 2 UN (8 de 10)").
- Filtros pelo `FiltroDeDemanda`, com a seleção na URL por `useSelecaoNaUrl` e a regra por `casaComFiltro`:
  facetas **Setor de destino**, **Pedido** e **Material**.
- O botão **"Separar"** de cada cartão, sob `usePodeEscrever('separacao')`, abre um `PainelDeEscrita` com um
  `Campo` por material pendente, preenchido com a pendência (D17). Campo vazio ou zero não vai. Um campo
  acima da pendência mostra, embaixo, "acima do planejado em 2 UN", como texto neutro, não como erro. O
  envio é um `POST` só; no sucesso, o painel fecha, a lista recarrega e o cartão sem pendência some. O erro
  de escrita mora dentro do painel. O painel recebe `enviando` e devolve o foco por `useDevolverFoco`, como
  os demais.
- Os três estados, cada um com teste que morre se o estado sumir: carregando; vazio, distinguindo "Nada a
  separar" de "Nenhum item com esses filtros"; e erro (`mensagemDeErro`).
- **Emenda de 2026-10-19, decisão do usuário na conferência da Fase 4 — separar em lote**, no molde da Fila e
  das Tarefas (desvios D7 a D12 do plano 2 dos filtros), com a lógica pura em `web/src/separacao/loteDaSeparacao.ts`:
  - Uma caixa por cartão, só para quem separa, com nome acessível que distingue o nó **e** o Pedido ("Marcar
    QD-01 — Quadro (PED-1)"): o mesmo código de Componente aparece em Pedidos diferentes. A caixa marca o
    **cartão inteiro**, com todos os materiais pendentes dele.
  - O cartão marcado mostra um campo por material pendente, preenchido com a pendência e lido como o do
    painel individual, com o mesmo aviso neutro "acima do planejado em …". O nome acessível de cada campo
    acrescenta o cartão ao rótulo do material, para ser único na tela. A **quantidade é editável no lote**.
  - Campo vazio ou zero não vai. Cartão marcado com todos os campos vazios ou zero, ou com algum texto que não
    é número, deixa o lote inválido, e o botão não envia.
  - A `BarraDoLote`, fixa no rodapé enquanto há seleção: "Separar 2 itens" ou "Separar 1 item", contando
    cartões; "Registrando…" durante o envio; "Limpar seleção"; os marcados que o filtro esconde vão no lote,
    com o aviso da barra. "Marcar todos" age sobre os cartões visíveis.
  - Marcar ou desmarcar fecha o painel individual aberto; o "Separar" de cada cartão continua.
  - Um `POST /separacoes` leva todos os marcados. Sucesso limpa a seleção e recarrega. Recusa recarrega,
    mantém a seleção e mostra a frase do servidor na barra. A recarga que tira da lista um cartão marcado o
    tira da seleção, com aviso, e o texto digitado nos cartões que ficam não é reescrito (o campo que
    ninguém tocou mostra a pendência atual, como no painel individual). Dois toques enviam uma vez.

### 6.2 Painel do nó (`AgrupamentoDetalhePage`, `PainelDoNo`)

- Seção **"Materiais"**: cada material com planejado, separado, pendência e "Fica em"; o que saiu do
  planejado e tem histórico, marcado "fora do planejado".
  - **Exceção, decidida na conferência de 2026-10-19:** o material que fica num Setor **não mostra
    pendência** (regra 34: o operador o pega ali); a linha dele traz só planejado, separado e o "Fica em".
  - **Emenda de 2026-10-19, decisão do usuário na conferência da Fase 4:** "tem histórico" quer dizer separado
    líquido maior que zero. O material fora do planejado com toda separação estornada sai da seção (a regra
    mora no `GET /estrutura/{id}/materiais`, seção 5.2); as linhas dele continuam em "Separações".
  - **Emenda de 2026-10-19, decisão do usuário na conferência da Fase 4:** no editor, o "Remover" de cada
    linha fica na linha da caixa do campo, da altura dela, e a dica (descrição e unidade) embaixo; a linha de
    adicionar tem os rótulos na mesma linha, e o `<select>`, o campo e o "Adicionar" alinhados. O arranjo é o
    `aoLado` do `Campo`.
- **"Editar materiais"**, sob `usePodeEscrever('materiaisDoNo')`, abre um editor no molde do
  `EditorDeRoteiroDoNo`, com o mesmo seletor de material que a receita padrão usa na
  `ComponenteDetalhePage`.
- **"Separações"**: o histórico, mais recente primeiro, com o botão de estornar nas linhas `estornavel`, no
  padrão da `ListaDeEstornaveis`.

### 6.3 Fila do Setor

A linha do nó com `material` não nulo ganha o texto **neutro** "Material: 2 de 3 completos", ou "Material
completo" quando `completos = total`. Sem cor de estado: a falta é informação (regra "cor de estado nunca
decora"). O início não muda.

### 6.4 Materiais (cadastro)

O painel de novo e de editar Material ganha o `Campo` **"Fica em"**: um `<select>` com "Almoxarifado" e os
Setores ativos. A lista mostra "Fica em: Corte a Laser" quando não é o almoxarifado.

### 6.5 Permissões (`web/src/auth/permissoes.ts`)

Três recursos novos, espelhando o backend e cobertos pela guarda de espelhamento (um `Recurso` por conjunto
de perfis, como os da Fase 3):

| Recurso | Perfis |
|---|---|
| `separacao` | Separador, Administrador |
| `estornoDeSeparacao` | Separador, PCP, Administrador (o botão ainda compara o autor; quem decide é o `403`) |
| `materiaisDoNo` | PCP, Administrador |

Onde o front escreve "Almoxarifado", passa a escrever "Separador" (hoje, em `permissoes.ts`, em lugar
nenhum).

## 7. Testes

### 7.1 Backend

- **Application (fakes):** separado, pendência e "acima do planejado"; pendência só do almoxarifado; recusa
  de material fora do planejado; tudo ou nada do lote; estorno pelo autor, pelo PCP, `Proibido` para outro
  Separador, `JaEstornada`, estorno de estorno; `PUT` dos materiais (repetido, inativo, escala, substituição
  inteira); o material que sai do plano continua no histórico com `noPlanejado: false`; a lista do
  Separador (ordem pelo prazo, destino pelo primeiro passo, "sem Roteiro", Pedido pausado presente, nó sem
  pendência ausente); o resumo `{ completos, total }` só no nó que inicia no Setor da Fila.
- **Infrastructure (banco real):** mapeamento da tabela reescrita e do `SetorDeEstoqueId`; o `CHECK` de
  `Tipo`/`EstornoDeId`; o índice único do estorno. Teste novo que escreve numa tabela compartilhada segue
  as regras de `[Collection]` e de asserção escopada do `CLAUDE.md`.
- **Api (ponta a ponta):** cada rota com os perfis certos e os errados (`403`); o contrato de erro; dois
  estornos concorrentes da mesma linha (um `201`, um `409`); o usuário `separador` seedado entra em
  `Usuario_seedado_valida_a_propria_senha_no_proprio_perfil`.

### 7.2 Front (Vitest + jsdom)

A tela Separação (três estados, filtros na URL, painel pré-preenchido, aviso de "acima", gating do
botão); o painel do nó (materiais, editor, histórico, estorno); o texto da Fila; o "Fica em" do cadastro; a
guarda de espelhamento das permissões. `npm run build` faz parte do ciclo.

### 7.3 Mutações que a review de cada task deve tentar

Lição registrada da 3B (a review de branch achou, por mutação, pontos da regra 25 sem teste que morresse).
Cada mutação abaixo tem de deixar ao menos um teste vermelho:

- tirar a recusa de material fora do planejado;
- contar o material que fica num Setor na pendência;
- somar o estorno em vez de subtrair;
- permitir que um Separador estorne a linha de outro;
- mostrar o resumo da Fila em nó que não inicia naquele Setor;
- tirar o filtro de Pedido aberto da lista do Separador (enquanto a conclusão não existe, o teste monta
  um Pedido com `Status = 'Concluido'` direto no fake ou no banco).

### 7.4 Conferência no navegador, pelo usuário

Pela regra de 2026-10-07 do `CLAUDE.md`, a review de branch só é despachada depois desta conferência, que é
do usuário. A massa traz um Kit com parafuso na receita e Roteiro na Solda, uma Peça com Roteiro de um passo
na Expedição e material da montagem final, e uma chapa com "Fica em: Corte". Os usuários são `separador`,
`pcp` e `operador`. A captura por agente prepara a conferência e não a substitui.

## 8. Riscos conhecidos, aceitos

- **`PUT` dos materiais concorrente com uma separação.** A separação pode passar por um material que acabou
  de sair do plano. Ela fica no histórico como "fora do planejado" e é estornável. Não se serializa o
  `PUT` com a separação por isso.
- **Lista inteira de pendências.** Tem o mesmo risco do limite de 2100 parâmetros do SQL Server já
  registrado para o `Contains` sobre os nós de Pedidos abertos (dívida com gatilho "antes do primeiro
  deploy"). Medir junto.
- **Token com o perfil antigo.** Um access token emitido com "Almoxarifado" vale no máximo
  `Jwt:AccessTokenMinutes` (15 min) depois da migração; nenhuma rota autoriza esse nome.

## 9. Ajustes de documentação (na mesma branch)

- `00-visao-geral.md`: o perfil passa a ser **Separador** (separa e leva, ou busca, o material do
  almoxarifado para os Setores que precisam dele).
- `01-dominio-e-regras-de-negocio.md`: regras 4 e 5 reescritas, regra 34 nova, glossário (seção 4).
- `02-modelo-de-dados.sql` e `db/alter-fase-4.sql` (seção 3), com o bloco do `alter` no `CLAUDE.md`.
- `04-fluxos-de-usuario.md`: o fluxo "3. Separação de Material" reescrito com o perfil Separador, a lista,
  o "Separar" e o estorno; a frase "o sistema pode alertar (não necessariamente bloquear)" vira a decisão
  D12.
- `05-api-endpoints.md`: as rotas da seção 5.2; o bloco "Roteiro e Materiais do nó depois da cópia" passa a
  dizer que os Materiais do nó são da Fase 4; a nota "Fase 4, ainda planejada" vira as rotas reais; a tabela
  de contrato de erro.
- `06-roadmap-mvp.md`, seção da Fase 4: a requisição de material sai, registrada como não feita e por quê
  (D7); a edição dos materiais do nó entra; o critério de pronto ganha os itens da seção 1.
- `CLAUDE.md`: o nome do perfil na seção Stack e onde mais aparecer; as contagens de primitiva que mudarem
  (`FiltroDeDemanda`, `PainelDeEscrita`, `useDevolverFoco`), **remedidas** com o comando de cada uma e a
  data.
- Spec da Fase 3, seção 2.2: uma nota dizendo que o descarte do perfil "Separador" foi revisto aqui. O
  motivo daquele descarte era acoplar a primeira entrada à separação; aqui a separação não dispara nada, e
  o Separador é o Almoxarifado renomeado, não um perfil a mais.

## 10. Fora do escopo

- **Requisição de material** (D7). Volta se a fábrica sentir falta, com uma spec própria.
- **Saldo de estoque e devolução** (D10): o sistema registra o que saiu para cada nó, não quanto há no
  almoxarifado.
- **Bloquear ou avisar no início** (D12).
- **Notificação ao Separador**: é tema da Fase 3C.
- **Casar itens do Toolbox com Material no import do BOM**: continua esperando o BOM real (D15 da spec do
  import).
- **Separação em Pedido concluído**: a conclusão só nasce na Fase 5, que herda a guarda. A lista do
  Separador já exclui Pedido concluído ou cancelado (seção 5.2).

## 11. Planos

Dois, como na Fase 3: um de **backend** (schema, `alter`, regras, mapeamento, rotas e a documentação de
`00`/`01`/`02`/`04`/`05`/`06`) e outro de **front** (as telas da seção 6, as permissões e as contagens do
`CLAUDE.md`). O de front depende do contrato JSON do de backend, que o plano de backend fixa numa seção
"Contrato JSON novo", como nas fases anteriores.
