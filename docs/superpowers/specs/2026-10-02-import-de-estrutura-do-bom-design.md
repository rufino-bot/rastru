# Import da estrutura a partir do BOM do CAD — desenho

Data: 2026-10-02. Branch: `claude/eloquent-ritchie-ubtsh8`, a partir da `main` em `c329d6b` (merge do
rufino-bot/rastru#30). Desenho aprovado pelo usuário em chat na mesma data, seção a seção (as cinco
seções da conversa viraram as seções 3 a 9 abaixo). Texto da spec aprovado pelo usuário na mesma
data ("pode seguir os 3 passos em ordem"), incluindo as decisões tomadas na escrita que a mensagem de
entrega destacou (o 409 da seção 3, o pendente apagado no reimport, o ciclo no catálogo como bloqueio,
as duas saídas do risco da seção 7).

**Ponto de partida, que esta spec não re-decide.** A seção "Fora das fases — importar a estrutura a
partir do CAD (decidido em 2026-08-04)" de `specs/06-roadmap-mvp.md` e o apêndice "viabilidade de
importar a estrutura do CAD" de `docs/superpowers/specs/2026-08-04-fase-1b-componente-design.md`
continuam valendo: o insumo é o **BOM indentado exportado** (CSV/XLSX), não o `.SLDASM`; o import gera
uma **proposta** que um humano confere e confirma, nunca gravação direta; é pré-preenchimento, não
automação; a conciliação part number ↔ `Componente.Codigo` é disciplina de cadastro, pré-requisito
declarado de uso. **Uma** coisa daquela seção é revista aqui, com o aval do usuário: o "não muda o
schema" (D7).

**Quando executa.** **Agora, antes da Fase 3B**, por decisão do usuário (D14): a 3B mexe em montagem e
Kit, e este desenho não depende dela. É exceção à ordem das fases, e o `CLAUDE.md` só admite exceção
declarada por escrito em `specs/06-roadmap-mvp.md` — o registro faz parte dos ajustes da seção 11, e
vem **antes** do plano.

## 1. O que se quer, e o que conta como pronto

**Problema.** Hoje a árvore de uma Peça é digitada à mão, item por item, e ela já existe pronta dentro
da montagem do CAD. Na fábrica, a árvore nova é cadastrada **na hora de criar o Pedido** — então o
import tem de alimentar o Agrupamento, não só o catálogo.

**Pronto é:**

- quem tem escrita na estrutura (hoje PCP e Administrador) envia o BOM de uma montagem a partir da
  página do Agrupamento e cai numa tela de conferência;
- a conferência mostra a árvore expandida, casada com o catálogo, com as pendências marcadas, e deixa
  corrigir o casamento, os dados de Componente novo, as quantidades, escolher a receita de cada
  Componente divergente e enviar o sólido de cada Componente;
- a conferência sobrevive a F5 e a fechar o navegador, e outro PCP pode continuá-la;
- confirmar, numa transação só, cria/reativa os Componentes, grava sólidos e receitas no catálogo e
  cria a Peça — **idêntica** à que o "Nova Peça" de hoje criaria a partir do catálogo resultante;
- descartar não deixa rastro nenhum no catálogo.

## 2. Decisões tomadas na conversa

| # | Decisão | Alternativas descartadas |
|---|---|---|
| D1 | O import alimenta **o Agrupamento e o catálogo**: gera a Peça e, no mesmo passo, casa ou cria cada Componente e grava a receita padrão em todos os níveis | só o catálogo (a Peça criada depois pelo "Nova Peça"); só o Agrupamento, sem tocar no catálogo |
| D2 | **Um arquivo = uma Peça.** A raiz é a montagem de topo; os itens de nível 1 do BOM são filhos dela. A raiz tem os mesmos campos do "Nova Peça": Componente (casado ou novo, com sólido), quantidade e Relatório Dimensional | cada linha de nível 1 vira uma Peça; as duas formas, escolhidas no import |
| D3 | **Todo Componente não-`Bruto` da árvore final precisa de sólido** para confirmar. Estende a regra 18 (hoje só a raiz) a toda a árvore, **no fluxo de import**. Upload e visualizador ficam na própria conferência | só a raiz, como hoje; todos sem exceção (contradiz o `Bruto` sem sólido de `01`) |
| D4 | Componente casado com **receita diferente** da do BOM é marcado com pílula de atenção e ganha um **comparativo de um nível** (filhos diretos: igual, quantidade muda, entra, sai) | subárvore inteira lado a lado (pesado, e o "antes" teria de ser montado do catálogo) |
| D5 | A divergência se resolve com uma **escolha por Componente, absoluta e sem padrão pré-marcado**: "manter a do catálogo" ou "usar a importada", inteira — nunca mistura linha a linha. Vale para **todas as ocorrências** do Componente na árvore. A escolha decide só os filhos diretos dele; quem confirma aprova a receita | escolha única para o import inteiro; escolha por ocorrência (o catálogo guarda uma receita por Componente); aceitar/recusar linha a linha (a receita final seria uma mistura que não existe nem no catálogo nem no CAD) |
| D6 | "Usar a importada" **substitui** a receita do catálogo. Pedidos existentes não mudam, porque a Peça é cópia da receita no momento da criação | manter o catálogo intacto e gravar receita só onde não havia |
| D7 | A proposta é um **rascunho salvo no servidor**, em tabelas próprias. Reabre o "não muda o schema" de `specs/06`, e o usuário pediu que a regra seja **reescrita**: o schema pode mudar quando a spec de uma funcionalidade nova ou de uma correção encontrar a necessidade | proposta só no navegador (F5 perde tudo, sólidos inclusive); criar os Componentes já no import (suja o catálogo, quebra o "nunca gravação direta") |
| D8 | A conferência **corrige na tela o que é do sistema** (casamento, dados do Componente novo, quantidade, `Tipo` `Bruto`) e **no CAD a forma da árvore** (acrescentar, remover, mover nó), por reimport | editor de árvore completo na tela; nada editável (o casamento, que é do sistema, ficaria sem onde corrigir) |
| D9 | Regras de casamento — seção 5.1 | — |
| D10 | Ciclo de vida do rascunho — seção 5.3 | — |
| D11 | **Leitura do arquivo no servidor e rascunho em tabelas normalizadas** | rascunho como documento JSON (perde as constraints, destoa do schema); leitura no navegador (duas implementações do formato, e o servidor sem o arquivo para reprocessar) |
| D12 | O rascunho guarda **uma receita por código**, não a árvore linha a linha; a árvore é expandida a partir da raiz. O mesmo filho repetido sob o mesmo pai **soma** as quantidades | linhas do BOM como estão (a quantidade editada por linha deixaria duas ocorrências do mesmo pai com receitas diferentes) |
| D13 | O mesmo código com filhos diferentes dentro do BOM, e ciclo entre códigos, são **erro do arquivo** | tratá-los como divergência |
| D14 | Executa **agora, antes da 3B** | depois da 3B; depois do HML; sem posição |
| D15 | Itens de biblioteca (Toolbox) são **Materiais**, não Componentes. O tratamento definitivo espera o BOM real (seção 10) | — |
| D16 | A tela de conferência é **de PC**: o BOM só existe no computador de quem cadastra. No celular ela não pode quebrar, mas não é otimizada | — |
| D17 | O BOM de referência é de uma montagem **pessoal do usuário** (o dos clientes é sigilo empresarial). Ele **não** entra no repositório sem decisão explícita do usuário; os testes usam arquivos sintéticos no mesmo formato | — |

## 3. Modelo de dados

Três tabelas novas em `specs/02-modelo-de-dados.sql`, levadas a banco anterior por um
`db/alter-importacao-bom.sql` idempotente (mesmo formato de `db/alter-fase-3d.sql`). `Componente`,
receita padrão e `EstruturaItem` **não mudam**.

```sql
CREATE TABLE dbo.ImportacaoDeEstrutura (
    Id                          INT IDENTITY(1,1) NOT NULL,
    AgrupamentoId               INT            NOT NULL,  -- FK dbo.Agrupamento
    NomeDoArquivo               NVARCHAR(260)  NOT NULL,
    RaizId                      INT            NULL,      -- FK ImportacaoDeEstruturaComponente; nulo só durante a criação
    QuantidadeDaPeca            DECIMAL(18,4)  NULL,      -- preenchida na conferência
    RequerRelatorioDimensional  BIT            NOT NULL DEFAULT (0),
    CriadoPorUsuarioId          INT            NOT NULL,  -- FK dbo.Usuario
    CriadoEm                    DATETIME2      NOT NULL DEFAULT (SYSUTCDATETIME()),
    AtualizadoEm                DATETIME2      NOT NULL DEFAULT (SYSUTCDATETIME()),  -- toda escrita no rascunho o atualiza (nota "AtualizadoEm")
    Versao                      ROWVERSION     NOT NULL
);

CREATE TABLE dbo.ImportacaoDeEstruturaComponente (   -- um registro por código distinto
    Id                          INT IDENTITY(1,1) NOT NULL,
    ImportacaoId                INT            NOT NULL,  -- FK, NO ACTION (nota "Sem ON DELETE CASCADE")
    CodigoLido                  NVARCHAR(50)   NULL,      -- nulo = linha sem part number
    DescricaoLida               NVARCHAR(200)  NOT NULL,
    ComponenteId                INT            NULL,      -- casado (FK dbo.Componente)
    CodigoNovo                  NVARCHAR(50)   NULL,      -- \
    DescricaoNova               NVARCHAR(200)  NULL,      --  > "criar novo"
    TipoNovo                    NVARCHAR(20)   NULL,      -- /  Bruto | Fabricado | Montagem
    EscolhaDeReceita            NVARCHAR(10)   NULL,      -- Catalogo | Importada | nulo = não decidido
    ImpressaoDaReceitaDoCatalogo BINARY(32)    NULL,      -- hash da receita de catálogo vista na escolha
    ArquivoSolidoPendenteId     INT            NULL       -- FK dbo.ArquivoDeComponente
);
-- UNIQUE (ImportacaoId, CodigoLido) WHERE CodigoLido IS NOT NULL   -- índice FILTRADO
-- CHECK: ComponenteId e os três campos "Novo" são mutuamente exclusivos
-- CHECK: EscolhaDeReceita IN ('Catalogo', 'Importada') ou nula; TipoNovo no mesmo domínio de Componente.Tipo

CREATE TABLE dbo.ImportacaoDeEstruturaFilho (        -- a receita lida, um nível
    Id                          INT IDENTITY(1,1) NOT NULL,
    PaiId                       INT            NOT NULL,  -- FK ImportacaoDeEstruturaComponente
    FilhoId                     INT            NOT NULL,  -- FK ImportacaoDeEstruturaComponente
    Ordem                       INT            NOT NULL,
    QuantidadeLida              DECIMAL(18,4)  NOT NULL,
    Quantidade                  DECIMAL(18,4)  NOT NULL   -- a corrigida; nasce igual à lida
);
-- UNIQUE (PaiId, FilhoId); CHECK (Quantidade > 0 AND QuantidadeLida > 0); CHECK (PaiId <> FilhoId)
```

O esboço acima fixa a **forma**; os nomes de constraint e a ordem de criação (a FK circular `RaizId` ↔
`ImportacaoDeEstruturaComponente`, criada com `ALTER TABLE` depois das duas tabelas) estão em
`specs/02-modelo-de-dados.sql`, que é a fonte de verdade. Três pontos em que o schema construído **difere
do esboço**, e por quê:

- **`AtualizadoEm` no cabeçalho** (decisão P7 do plano do import). O `ROWVERSION` do cabeçalho só muda
  quando o **cabeçalho** é escrito, e a maior parte das escritas da conferência é num registro ou numa
  linha da receita. Toda escrita no rascunho passa a atualizar `AtualizadoEm`, e é isso que faz a `Versao`
  mudar — a versão otimista da seção 5.3 vale, assim, para o rascunho inteiro. A coluna também alimenta
  a lista de rascunhos do Agrupamento ("atualizado em").
- **Sem `ON DELETE CASCADE`** (decisão P6). Todas as FKs entre as três tabelas, a circular `RaizId`
  inclusive, são `NO ACTION`: o SQL Server recusa cascata quando há mais de um caminho até a mesma
  tabela, e há (`Filho` aponta para o registro por `PaiId` e por `FilhoId`). Quem apaga um rascunho é a
  aplicação, numa transação, na ordem **linhas da receita → `RaizId = NULL` → registros → cabeçalho →
  `ArquivoDeComponente` pendentes**.
- **`IX_ImportacaoDeEstruturaComponente_ImportacaoId`**, índice comum que o esboço não trazia. A exclusão
  do rascunho filtra os registros por `ImportacaoId`, e o índice filtrado de código não serve a esse
  filtro (só cobre as linhas com código). Sem ele, a exclusão — que roda **dentro** da transação
  `SERIALIZABLE` da confirmação (seção 7) — varreria e travaria os registros de **todos** os rascunhos
  até o commit.

**Por que o índice de código (`UX_ImportacaoDeEstruturaComponente_Codigo`) é filtrado:** um `UNIQUE`
comum do SQL Server aceita **um** nulo só, e várias linhas sem part number precisam coexistir no mesmo
rascunho — cada uma vira um registro próprio.

**Por que receita por código, e não linhas:** com a escolha por Componente (D5) e o "mesmo código com
filhos diferentes é erro" (D13), a árvore inteira é determinada pela raiz mais a receita de cada
código. Guardar linhas deixaria a quantidade ser corrigida numa ocorrência e não na outra do mesmo
pai, e o catálogo só guarda uma receita por Componente.

**O STL pendente vive em `dbo.ArquivoDeComponente`**, a mesma tabela do sólido de hoje, sem nenhum
`Componente` apontando para ele até a confirmação. Descartar o rascunho apaga esses arquivos, como
último passo da exclusão em ordem da nota "Sem `ON DELETE CASCADE`" — a FK vai do rascunho para o arquivo, então o arquivo
só pode sair depois do registro que o aponta.

**Exclusão de Agrupamento.** `DELETE /agrupamentos/{id}` passa a recusar com 409
`{ "erro": "AgrupamentoComImportacao" }` quando houver rascunho; quem quer excluir descarta antes. A
ordem de verificação existente (existe → Pedido `Aberto` → vazio) ganha o rascunho no fim. *(Decisão
tomada ao escrever a spec, não na conversa — fica destacada para a revisão do usuário.)*

## 4. Leitura do BOM

Em duas partes, para testar sem arquivo nem banco:

- **Leitor por formato, na `Infrastructure`** (CSV e XLSX). Só transforma o arquivo em linhas cruas:
  número da linha no arquivo, nível, código, descrição, quantidade. Biblioteca de XLSX escolhida no
  plano — candidatas `DocumentFormat.OpenXml` (Microsoft) e `ClosedXML`, ambas MIT.
- **Função pura, na `Application`**, que transforma as linhas cruas nas receitas por código (seção 3)
  ou numa lista de erros. É ela que concentra as regras e os testes.

**Erros de arquivo** — o import é recusado com **400 e a lista completa** (não só o primeiro erro),
cada um com o número da linha:

- coluna obrigatória ausente; arquivo vazio; arquivo acima do limite de tamanho (valor no plano);
- quantidade não numérica, ≤ 0, ou fora da faixa da coluna (`PlanejadorDeCopia.QuantidadeMinimaDaColuna`
  e `QuantidadeMaximaDaColuna`);
- nível que pula um degrau (`1` direto para `1.2.3`);
- o mesmo código com filhos diferentes (D13);
- ciclo entre códigos (D13);
- árvore acima de `PlanejadorDeCopia.ProfundidadeMaxima` ou `NosMaximos` — os mesmos limites da
  criação de Peça, para o import não aceitar o que a confirmação recusaria.

**Não é erro:** o mesmo filho repetido sob o mesmo pai **soma** (D12); linha sem part number vira
Componente novo com código em branco (regra 4 da seção 5.1).

As suposições sobre colunas, formato e quantidade estão na seção 10.

## 5. Conferência

### 5.1 Casamento (D9)

1. Casa por **`Codigo` exato**, sem espaços nas pontas; maiúscula e minúscula não diferenciam (o banco
   já compara assim). Sem casamento aproximado por descrição. Não casou → "criar novo"; quem confere
   pode trocar o casamento.
2. Casou com Componente **inativo**: casa, com pílula de atenção, e **é reativado na confirmação** —
   criar outro com o mesmo código é impossível (`UQ_Componente_Codigo`); mesmo comportamento do
   "Reativar o existente" das telas de cadastro.
3. Descrição do BOM diferente da do catálogo num casado: vale **a do catálogo**; a do BOM aparece ao
   lado, só para conferência. Sem pílula — o código manda.
4. Linha **sem part number**: "criar novo" com **código em branco e obrigatório** — bloqueia a
   confirmação até ser preenchido ou casado à mão.
5. Componente casado que **já tem sólido**: usa o existente; o painel fixo deixa **substituir**, como
   o cadastro de Componente.
6. `Tipo` do Componente novo: `Montagem` se tem filhos no BOM, `Fabricado` se é folha; quem confere
   pode trocar uma folha para `Bruto`, o que tira a exigência de sólido. O `Tipo` de um **casado** não
   muda.

### 5.2 Receita divergente (D4, D5, D6)

Um código **diverge** quando está casado e a receita de catálogo dele (filhos diretos e quantidades)
é diferente da lida do BOM — incluindo o caso em que o BOM não traz filhos e o catálogo traz. Componente
novo, casado **sem receita no catálogo** (traga o BOM filhos ou não) ou casado com receita igual **não** diverge, e recebe a do BOM na confirmação sem
escolha nenhuma.

Escolher **"catálogo"** num código X decide só os filhos diretos de X: os filhos presentes **só no
BOM** saem da árvore (com o que estiver abaixo deles), os presentes **só no catálogo** entram (expandidos
pelas receitas de catálogo deles), e os presentes nos dois mantêm as próprias escolhas. A tela mostra
esse efeito antes de salvar ("retira N nós do BOM, traz M do catálogo"). Os Componentes que entram pelo
catálogo também caem na exigência de sólido (D3).

Escolher **"importada"** grava a receita lida no catálogo na confirmação, substituindo a que existe.

Ao escolher, o servidor guarda a **impressão** (SHA-256) da receita de catálogo que estava sendo
comparada. Ela é o que detecta, na confirmação, que alguém mudou o catálogo no meio (seção 7).

### 5.3 Ciclo de vida do rascunho (D10)

1. Aparece na página do Agrupamento, na seção **"Importações em conferência"**, com "Continuar" e
   "Descartar". Um Agrupamento pode ter **vários** rascunhos, um por arquivo.
2. Acessa quem tem escrita em `estrutura`. O rascunho **não** é de quem o abriu: outro PCP continua.
   Autor e data ficam registrados.
3. **Nada sai do rascunho antes de confirmar** — inclusive o sólido de um Componente **casado**: o STL
   enviado na conferência só substitui o do catálogo na confirmação.
4. **Descartar** apaga o rascunho e os STLs dele. **Confirmar** grava tudo numa transação e apaga o
   rascunho; não há histórico de rascunho confirmado.
5. Edição simultânea: versão otimista (`Versao`); escrita com versão velha recebe 409 e a tela
   recarrega.
6. **Sem expiração automática.** Rascunho abandonado fica até alguém descartar. STL órfão acumulado
   vira dívida medida se aparecer, não regra antecipada — e conta para o limite de 10 GB do SQL Server
   Express registrado na seção 8.5 da spec do HML.

### 5.4 Bloqueios da confirmação

Calculados a cada leitura contra o catálogo atual, mostrados na tela e conferidos de novo no servidor:

- Componente não-`Bruto` sem sólido (casado sem sólido e sem pendente, ou novo sem pendente), em toda
  a **árvore final** — incluindo o que entrou pela receita de catálogo;
- código divergente sem escolha;
- Componente novo com código vazio, ou com código que já existe no catálogo;
- quantidade da Peça não informada;
- a receita resultante criaria **ciclo no catálogo** — a combinação das receitas importadas com as de
  catálogo mantidas pode fechar um ciclo que nenhuma das duas tinha sozinha; a mensagem nomeia o
  caminho, como a de `ReceitaPadraoUseCase`.

## 6. API

As rotas de **escrita** são *(PCP, Administrador)* — o recurso `estrutura` de `web/src/auth/permissoes.ts`
—, e as de **leitura** (`GET`), de qualquer perfil autenticado, como no `EstruturaController`. Toda escrita
leva a `versao` do rascunho e responde **409** se ela estiver velha. O contrato fechado — corpos, códigos de
erro e o formato do arquivo — está em `specs/05-api-endpoints.md`, seção "Importação da estrutura".

| Rota | O que faz |
|---|---|
| `POST /agrupamentos/{id}/importacoes` | Multipart com o arquivo. Lê, casa e cria o rascunho → **201** com o estado. **400** com a lista de erros do arquivo |
| `GET /agrupamentos/{id}/importacoes` | Lista os rascunhos do Agrupamento (arquivo, autor, data) |
| `GET /importacoes/{id}` | Estado completo: árvore expandida da raiz; por código, casamento, situação (casado, novo, inativo, divergente), comparativo de um nível, sólido (existente ou pendente); **lista de bloqueios** |
| `PUT /importacoes/{id}` | Quantidade da Peça e Relatório Dimensional |
| `PUT /importacoes/{id}/componentes/{cid}` | Casamento manual, dados do Componente novo, escolha de receita |
| `PUT /importacoes/{id}/filhos/{fid}` | Corrige a quantidade de um filho |
| `POST /importacoes/{id}/componentes/{cid}/solido` | Envia o STL pendente — mesmas validações de `POST /componentes/{id}/solido` (16 MiB, estrutura de STL) |
| `GET /importacoes/{id}/componentes/{cid}/solido` | Lê o STL pendente, para o visualizador |
| `POST /importacoes/{id}/arquivo` | **Reimporta.** Substitui as receitas lidas; preserva, por código que continua no arquivo, o sólido pendente, o casamento manual e os dados do novo; **zera a escolha** do código cuja receita lida mudou. Pendente de código que saiu do arquivo é apagado |
| `DELETE /importacoes/{id}` | Descarta (seção 5.3) |
| `POST /importacoes/{id}/confirmacao` | Confirma (seção 7) → **201** com a Peça criada |

Sem guarda de status do Pedido, pelo mesmo motivo do `POST /agrupamentos/{id}/estrutura`: acrescentar
estrutura a Pedido em execução é o comportamento padrão da fábrica. Contrato de erro no formato da
seção "Contrato de erro da Estrutura" de `specs/05-api-endpoints.md`; os códigos novos estão na seção
"Importação da estrutura" do mesmo arquivo.

## 7. Confirmação

A gravação é numa transação só (passos 2 a 6); o passo 1 roda antes dela, sobre o catálogo lido fora (ver a
seção 13):

1. Recalcula os bloqueios (seção 5.4). Sobrou algum → **400** (a lista não vai no corpo: a tela a relê pelo
   `GET`).
2. Recalcula a impressão da receita de catálogo de cada código com escolha. Mudou → **409** ("a receita
   de X mudou no catálogo, confira de novo"), e a escolha daquele código é zerada.
3. Cria os Componentes novos, reativa os inativos, grava os sólidos pendentes (substituindo o de um
   casado, quando for o caso).
4. Grava a receita lida em cada código com escolha "importada" e em cada código que não divergia.
5. Cria a Peça **pelo caminho que já existe** — o planejamento de cópia do catálogo de `CriarPeca` —,
   que agora reflete as escolhas.
6. Apaga o rascunho.

**Risco da leitura dentro da transação — resolvido pela segunda saída** (decisão P5 do plano do import;
os detalhes estão na seção 13). Hoje `CriarPeca` lê o catálogo **fora** da transação, de propósito:
`LerReceitaCompletaAsync` sob SERIALIZABLE travaria as tabelas da receita por faixa e abriria um ciclo de
deadlock com `ReceitaPadraoRepository` (seção 3.2 da spec `2026-10-01-deadlock-na-suite-de-api-design.md`).
A confirmação do import faz o mesmo: lê o catálogo, calcula os bloqueios (passo 1) e planeja a Peça **em
memória** (o plano que o passo 5 grava), com a avaliação que já serve ao `GET`, **antes** de abrir a
transação. **Dentro** dela confere de novo a versão do rascunho — igual, o rascunho é o que foi avaliado —,
relê só o que é estreito (passo 2) e grava na ordem dos passos 3 a 6; o passo 5 grava o plano feito fora, e
não faz uma releitura do que os passos 3 e 4 gravaram.

**A medição do risco, em 2026-10-03.** Com a confirmação implementada, `dotnet test tests/Rastreamento.Api.Tests`
rodou **10 vezes seguidas** e passou 345 de 345 em todas; depois das correções que a revisão pediu, rodou
mais **5** e mais **3** vezes, com o mesmo resultado, sem nenhuma falha. Isso não prova ausência de
deadlock — a suíte de Api roda sem paralelismo entre classes, então nenhum teste confirma dois rascunhos ao
mesmo tempo —, e por isso houve também uma medição das travas. Sob SERIALIZABLE, a exclusão do rascunho
filtrava os registros por `ImportacaoId`, coluna sem índice próprio (o índice único filtrado não serve a um
`WHERE ImportacaoId = x`): com outro rascunho de 2.000 registros e 2.000 linhas de receita no banco, a
transação da confirmação segurou **2.002** travas de faixa na chave primária dos registros, dos rascunhos
de todo mundo. Com `IX_ImportacaoDeEstruturaComponente_ImportacaoId` (seção 3) ficaram **0** nela, e 1 em
`UQ_ImportacaoDeEstruturaFilho`, que serve à exclusão das linhas da receita com ou sem o índice novo. A
medição é do `ExcluirAsync` real do repositório, com parâmetro, que é o plano que a aplicação executa; uma
medição equivalente no `sqlcmd` com variável local deu uma varredura mesmo com o índice, porque o
otimizador usa a densidade média em vez do valor.

## 8. Telas

Segue a seção "Interface" do `CLAUDE.md`: `<Pagina>`, primitivas de `web/src/components/`, cores só
pelos tokens.

**Na `AgrupamentoDetalhePage`:**

- botão **"Importar BOM"** no `acao`, ao lado do "Nova Peça", sob o mesmo `usePodeEscrever('estrutura')`;
  abre um `PainelDeEscrita` com o campo de arquivo (e o erro de arquivo, quando houver). Sucesso →
  navega para a conferência;
- seção **"Importações em conferência"**, só quando houver rascunho: arquivo, autor, data,
  "Continuar" e "Descartar" (este pela `Confirmacao`).

**Tela de conferência, `/importacoes/:id`**, de cima para baixo:

1. **Painel fixo (sticky) do Componente selecionado** — fixo só de `md` para cima (D16); no celular é um
   bloco comum no topo, que rola com a tela. À esquerda, `VisualizadorDeSolido` com
   `UploadDeSolido`. À direita: código e descrição (a do BOM ao lado da do catálogo quando difere); o
   casamento — `SeletorComBusca` para trocar, ou os campos do Componente novo com o `Tipo`; e, se o
   código diverge, a **tabela de comparativo** de um nível e a escolha "manter a do catálogo / usar a
   importada", sem padrão marcado, com o efeito ("retira N, traz M") mostrado antes de salvar.
2. **Faixa da Peça**: quantidade e Relatório Dimensional; resumo das pendências em pílulas ("3
   divergências", "5 sem sólido"), cada uma levando ao primeiro nó com aquela pendência; **Confirmar**,
   desabilitado enquanto houver bloqueio, com a lista do que falta; Descartar.
3. **Árvore expandida.** Cada nó: código, descrição, quantidade (editável na linha) e pílulas de
   situação — *novo*, *inativo*, *receita divergente*, *sem sólido*. Clicar ou tocar seleciona o nó no
   painel. As ocorrências do mesmo código ficam destacadas juntas quando uma está selecionada, para
   deixar visível que a decisão vale para todas.

**Cores.** *Divergente*, *sem sólido* e *inativo* usam **`atencao`** — são estado que pede atenção,
o que cumpre "cor de estado nunca decora". *Novo* é neutra. A pílula de atenção no início da árvore,
pedida pelo usuário para a receita divergente, é parte do resumo de pendências; o texto exato fica
para depois, por decisão dele.

**Reuso.** A `ArvoreDeEstrutura` recebe o DTO da árvore real. Se o contrato dela não comportar o
rascunho sem virar outra coisa, a conferência ganha uma árvore própria em `web/src/components/`, com
teste — decisão do plano, depois de ler o código.

Os três estados (carregando, vazio, erro) valem para a tela de conferência e para a seção da
`AgrupamentoDetalhePage`.

## 9. Testes

- **Application** (fakes, sem banco): a função pura do BOM — cada erro de arquivo, soma do filho
  repetido, ciclo, nível pulado, limites; os casos de uso — cada bloqueio, divergência e não
  divergência, o efeito de "catálogo" na árvore final (inclusive a exigência de sólido que ele traz), a
  escolha zerada quando a impressão muda, o reimport preservando sólido e casamento por código, a
  confirmação recusando com bloqueio pendente e com ciclo no catálogo.
- **Infrastructure**: leitores de CSV (`;` e `,`; UTF-8 com BOM e Windows-1252) e de XLSX, contra
  arquivos **sintéticos** (D17); mapeamento EF das três tabelas; o índice filtrado (dois nulos
  coexistem, dois códigos iguais não); o descarte apagando os `ArquivoDeComponente` pendentes. Teste
  novo que escreve em `Componente` entra em `ColecaoQueEscreveEmComponente`.
- **Api** (ponta a ponta): import → conferência → confirmação gera Peça **idêntica** à que
  `POST /agrupamentos/{id}/estrutura` geraria a partir do catálogo resultante; 403 para perfil sem
  escrita; 409 por versão velha e por catálogo alterado; `DELETE /agrupamentos/{id}` recusado com
  rascunho.
- **Front**: três estados; clique no nó troca o painel; Confirmar desabilitado com a lista de
  bloqueios; pílulas de pendência em `atencao`; o efeito "retira N / traz M"; `npm run build` no ciclo.
- **Medição obrigatória** do risco da seção 7: a suíte de Api repetida (o número de execuções, no
  plano), registrada em relatório.

## 10. Pendências do BOM real — suposições provisórias

*(Implementadas em 2026-10-03; o que a implementação fixou além da tabela — a codificação do CSV sem BOM, o
nível numérico do XLSX, os apelidos de cabeçalho — está na seção 13.)*

O usuário vai fornecer o BOM de uma montagem pessoal (D17). Cada linha abaixo tem uma suposição para o
plano não travar; o arquivo confirma ou derruba, e o que cair vira emenda curta nesta spec.

| Ponto | Suposição provisória |
|---|---|
| Formato | aceitar **CSV e XLSX** |
| Colunas | "Nº do item" hierárquico (`1.2.3`), part number, descrição, quantidade; os nomes exatos vêm do arquivo |
| Quantidade | **por pai** — a forma da receita |
| Código da raiz | em branco e obrigatório na conferência, se o arquivo não o trouxer |
| CSV | separador `;` ou `,`, detectado; codificação UTF-8 com BOM ou Windows-1252 (padrão do Excel e do SolidWorks em pt-BR) |
| Itens do Toolbox (Materiais, D15) | entram como Componente novo, e quem confere marca `Bruto`; a forma definitiva (casar com `Material`, gerar `ComponenteMaterialPadrao`) espera o arquivo |

## 11. Ajustes de documentação

**Antes do plano** (oficializam a exceção de ordem, D14) — **feitos em 2026-10-02**, no commit
seguinte ao da spec; a seção de `06` passou a se chamar «Import da estrutura a partir do CAD (decidido
em 2026-08-04; executa antes da 3B)», e a regra do schema foi para o cabeçalho de `06`:

- `specs/06-roadmap-mvp.md`: a seção "Fora das fases — importar a estrutura a partir do CAD" deixa de
  ser "fora das fases" e passa a apontar esta spec, com a posição **antes da 3B**; a frase "Não muda o
  schema" é substituída pela regra geral pedida pelo usuário (D7) — o schema pode mudar quando a spec
  de uma funcionalidade nova ou de uma correção encontrar a necessidade —, com este caso como motivo;
- `CLAUDE.md`, seção "Ordem de implementação": a exceção nova, ao lado da 3D e da 3C.

**Junto com a implementação:**

- `specs/02-modelo-de-dados.sql` e `db/alter-importacao-bom.sql` (seção 3);
- `specs/05-api-endpoints.md` (seção 6, e o 409 novo do `DELETE /agrupamentos/{id}`);
- `specs/01-dominio-e-regras-de-negocio.md`: no import, sólido exigido em toda a árvore exceto
  `Bruto` (D3); a escolha de receita por Componente, e que quem confirma aprova a receita (D5, D6);
- `CLAUDE.md`: o comando do `alter`, e a primitiva nova da seção "Interface", se houver;
- o apêndice da spec da 1B passa a apontar esta spec.

## 12. Fora do escopo

- Casamento aproximado (por descrição, por semelhança de código).
- Ler `.SLDASM`, STEP ou qualquer coisa que não seja o BOM exportado.
- Materiais e roteiro vindos do BOM — continuam preenchidos à mão depois da confirmação; Materiais do
  Toolbox esperam o BOM real (seção 10).
- Histórico de importações confirmadas.
- Expiração automática de rascunho.
- Conferência otimizada para celular.

## 13. Emendas da implementação

Registro de 2026-10-03, do que a implementação fixou onde esta spec deixou a escolha ou estreitou uma
suposição. O que a seção 3 muda no schema está lá; o contrato HTTP fechado, em `specs/05-api-endpoints.md`
(seção "Importação da estrutura"); a regra de domínio, na regra 32 de
`specs/01-dominio-e-regras-de-negocio.md`.

**Leitura do arquivo (seções 4 e 10).**

- **Cabeçalhos por tabela de apelidos** (decisão P9 do plano do import). As quatro colunas são achadas
  pelo cabeçalho, normalizado (maiúsculas, sem acento nem pontuação, sem o sinal ordinal de "Nº", espaços
  colapsados), numa tabela de apelidos que cobre o SolidWorks em português e em inglês. Quando o BOM real
  chegar, a emenda mexe só nessa tabela (`ColunasDoBom`). As quatro colunas têm de estar no
  **cabeçalho**; o código só pode faltar no **conteúdo** da linha.
- **Codificação do CSV sem BOM.** UTF-8 com BOM é UTF-8. Sem BOM, o arquivo é tentado como **UTF-8
  estrito** e, se tiver byte inválido, lido como **Windows-1252**. A suposição da seção 10 ("UTF-8 com BOM ou
  Windows-1252") ficou estreita demais: ler um UTF-8 sem BOM como Windows-1252 estraga os acentos **em
  silêncio**, e o arquivo parece ter sido aceito.
- **Nível numérico no XLSX.** Uma célula de nível numérica e **inteira** (`2`, `10`) é aceita. Numérica e
  **não inteira** (`1.1`, `1.10`) é recusada, com a linha dita: `1.10` e `1.1` são o mesmo número no Excel
  e dois itens diferentes no BOM, então o nível só é confiável como texto. A quantidade, ao contrário, vem
  como número e é formatada sem notação científica e sem o ruído do ponto flutuante.
- **Quantidade** (decisão P10): `1,5` e `1.5`, no máximo um separador, sem milhar, de
  `QuantidadeMinimaDaColuna` a `QuantidadeMaximaDaColuna`, com no máximo 4 casas (zeros à direita não
  contam). A soma do mesmo filho repetido sob o mesmo pai também tem de caber na faixa.
- **Limites** (decisão P11): arquivo de até **5 MiB**, só `.csv` e `.xlsx`; o XLSX é lido só na **primeira**
  planilha, e um XLSX que descompacta para mais de 64 MiB é recusado. Uma linha sem nada nas quatro colunas
  lidas é ignorada. Os textos que estourariam a coluna (código acima de 50 caracteres, descrição vazia ou
  acima de 200, nome do arquivo acima de 260) viram erro **do arquivo**, com a linha, e não um 500.
- **Código da raiz.** O BOM não tem linha para a montagem de topo, então o arquivo nunca traz o código
  dela: a raiz nasce com a descrição igual ao nome do arquivo sem extensão e "criar novo" com código em
  branco, que a conferência exige preencher ou casar à mão (decisão P8). No reimport, a raiz é **sempre** o
  mesmo registro.

**Bloqueios (seção 5.4).**

- Além dos listados, existe `QuantidadeForaDaFaixa`: a quantidade da Peça multiplicada pela receita sai da
  faixa da coluna (ou estoura o `decimal`). Com ciclo, profundidade e tamanho, é uma das quatro recusas da
  expansão; quando ela é recusada, `raiz` vem nula e o motivo está na lista de bloqueios, que continua
  calculada para o resto.
- `CodigoJaExiste` vale também para **dois Componentes novos com o mesmo código no mesmo rascunho**, que
  esbarrariam em `UQ_Componente_Codigo` na confirmação.
- A mensagem do ciclo nomeia o caminho **por código** (o Componente novo sem código, pela descrição entre
  aspas), e não pelo Id interno.
- Os bloqueios por registro (código, divergência, sólido) valem **só para a árvore final** (decisão P3): um
  código que saiu por uma escolha "catálogo" acima dele não vira Componente e não trava nada. Um nó que
  entra só pela receita do catálogo exige sólido (D3) mas não tem registro: o bloqueio aponta o
  Componente, e o sólido se envia no cadastro dele, não na conferência.

**Casamento e escolha (seções 5.1 e 5.2).**

- Dois registros do mesmo rascunho **não** casam com o mesmo Componente — o catálogo tem uma receita por
  Componente, e as duas se pisariam. O casamento automático nunca produz isso (o código é único no
  rascunho); a troca manual é recusada com 400, e o reimport deixa "criar novo" o registro que casaria com
  um Componente já tomado.
- **Trocar o casamento e escolher a receita na mesma escrita é recusado.** Trocar o casamento é trocar a
  receita de catálogo em comparação, e uma escolha enviada junto seria sobre uma receita que o usuário não
  viu; a escolha se faz numa escrita seguinte, já sobre o casamento novo. Trocar o casamento zera a
  escolha e a impressão. Escolher onde não há divergência também é recusado.
- **Escolha num código que deixou de divergir é inerte** (por exemplo, a quantidade corrigida na tela fez a
  receita lida igualar a do catálogo): nenhum bloqueio a cobra, a confirmação a ignora, e a receita do
  código é a lida (decisão P2).
- A receita **efetiva** de cada código, a que entra na árvore final (decisão P2): a lida, se o código é
  novo, se casa e não diverge, ou se diverge com escolha "importada"; a do catálogo, se diverge com escolha
  "catálogo"; a lida, provisoriamente, se diverge sem escolha (a árvore mostrada é a do BOM, e o bloqueio
  impede a confirmação). Um Id alcançado só pelo catálogo usa a receita do catálogo. A árvore final é
  calculada, nunca gravada (decisão P4): uma função pura serve ao `GET` e à confirmação.
- Id provisório (decisão P1): na sobreposição do rascunho sobre o catálogo, o registro casado usa o
  `ComponenteId` e o "criar novo" usa o Id do registro **negativo**. Materiais e roteiro de um Id negativo
  saem vazios, como os de um Componente sem receita.

**Reimport (seção 6).**

- Por código, aparado e sem diferenciar caixa: o registro de um código que **continua** no arquivo é mantido
  (mesmo Id), com o casamento manual, os dados do "criar novo" e o sólido pendente. O de código **novo**
  nasce como no `POST` de criação; o de código que **saiu** é removido, e o pendente dele é apagado. Linha
  sem código (fora a raiz) não tem identidade, e sai e entra de novo a cada reimport.
- A escolha de receita **só segue** se a receita lida do código (os códigos dos filhos e as quantidades
  lidas) é a mesma de antes; senão é zerada. Se algum filho não tem código, a receita nunca é "a mesma".
- As quantidades corrigidas na tela **voltam ao que o arquivo diz**: o arquivo é a forma da árvore (D8).
- O `TipoNovo` de um "criar novo" mantido **acompanha o arquivo novo** — `Montagem` se passou a ter filhos,
  `Fabricado` se passou a ser folha. `Bruto` é escolha do usuário e fica. O de um casado não muda.
- Um arquivo recusado deixa o rascunho como estava. **A tela de conferência oferece o reimport** no botão
  "Reimportar" da faixa da Peça, que abre um painel com o campo de arquivo (o mesmo do "Importar BOM" do
  Agrupamento, `PainelDoArquivoDoBom`). No sucesso, a resposta vira o estado e o painel fecha. Com o arquivo
  recusado (`BomInvalido`) ou outra falha, a mensagem e a lista de linhas ficam no painel, que continua
  aberto com o arquivo descartado. No 409 de versão velha, a tela relê o rascunho e o aviso aparece no
  painel, e o envio seguinte já sai com a versão relida.

**Confirmação (seção 7).**

- Só o que está na árvore final é criado, reativado e gravado (decisão P3). Em particular, um Componente
  **inativo que entra só pela receita do catálogo** (sem registro no rascunho) **não é reativado**: é o
  que a "Nova Peça" faz hoje com um filho inativo da receita, que o copia sem reativá-lo. O inativo que tem
  registro (casado à mão ou por código) é reativado.
- O catálogo é lido e a Peça planejada **fora** da transação (decisão P5). Dentro dela se relê a versão do
  rascunho — a leitura segura a linha do cabeçalho até o commit, e toda escrita do rascunho passa por ela —
  e a receita de catálogo de cada pai **com escolha**, para conferir a impressão (faixa estreita:
  `UQ_ComponenteFilhoPadrao` começa por `ComponentePaiId`). Impressão diferente desfaz a transação, responde
  409 `ReceitaDoCatalogoMudou` e zera a escolha daquele código numa escrita à parte, de melhor esforço.
- **Resíduos aceitos**, os mesmos de `CriarPeca` mais um: uma mudança concorrente no catálogo dos Ids
  alcançados só pela receita do catálogo, entre a leitura e o commit, não é vista; e um código novo criado
  no catálogo por outra requisição nesse intervalo esbarra em `UQ_Componente_Codigo` no `INSERT`, que sobe
  como erro não tratado — o `GET` seguinte mostra o bloqueio `CodigoJaExiste`.
- Sem bloqueio, a resposta é 201 com a Peça. Com bloqueio, 400 `ImportacaoComBloqueios` **sem a lista**
  (decisão P15), que a tela relê pelo `GET`.
- Descartar não pede a versão: é a saída de um rascunho que ninguém mais quer.

**Telas (seção 8).**

- A conferência usa árvore **própria**, `ArvoreDaImportacao`: o contrato da `ArvoreDeEstrutura` é o nó real,
  com ações, posições e Roteiro que o rascunho não tem. Cada linha tem um `<button>` como seletor do nó
  (exceção escrita em `CLAUDE.md`, seção "Interface") e a quantidade por pai editável na linha.
- `UploadDeSolido` e `VisualizadorDeSolido` passaram a receber o **caminho** do binário (e, o upload, a função
  `enviar`), para servir tanto ao sólido do Componente quanto ao pendente do rascunho.
- A seção "Importações em conferência" só aparece para quem escreve em `estrutura` e depois de a árvore do
  Agrupamento carregar. Quem não escreve abre a tela de conferência por link em modo leitura: sem Confirmar nem
  Descartar, e com os campos travados.
- `DELETE /agrupamentos/{id}` recusado com rascunho: a tela de Pedido explica o 409 com "Descarte-as antes de
  excluir".
