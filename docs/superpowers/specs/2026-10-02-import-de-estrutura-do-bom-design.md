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
    Versao                      ROWVERSION     NOT NULL
);

CREATE TABLE dbo.ImportacaoDeEstruturaComponente (   -- um registro por código distinto
    Id                          INT IDENTITY(1,1) NOT NULL,
    ImportacaoId                INT            NOT NULL,  -- FK, ON DELETE CASCADE
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

O esboço acima fixa a **forma**; nomes de constraint, `ON DELETE` exatos e a ordem de criação (a FK
circular `RaizId` ↔ `ImportacaoDeEstruturaComponente`) ficam para o plano, que os mede contra o banco.

**Por que o índice é filtrado:** um `UNIQUE` comum do SQL Server aceita **um** nulo só, e várias
linhas sem part number precisam coexistir no mesmo rascunho — cada uma vira um registro próprio.

**Por que receita por código, e não linhas:** com a escolha por Componente (D5) e o "mesmo código com
filhos diferentes é erro" (D13), a árvore inteira é determinada pela raiz mais a receita de cada
código. Guardar linhas deixaria a quantidade ser corrigida numa ocorrência e não na outra do mesmo
pai, e o catálogo só guarda uma receita por Componente.

**O STL pendente vive em `dbo.ArquivoDeComponente`**, a mesma tabela do sólido de hoje, sem nenhum
`Componente` apontando para ele até a confirmação. Descartar o rascunho apaga esses arquivos — pela
aplicação, já que a FK vai do rascunho para o arquivo e o `CASCADE` não os alcança.

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
é diferente da lida do BOM — incluindo o caso em que um dos lados não tem filhos. Componente novo,
casado sem receita ou casado com receita igual **não** diverge, e recebe a do BOM na confirmação sem
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

Todas as rotas são *(PCP, Administrador)* — o recurso `estrutura` de `web/src/auth/permissoes.ts`. Toda
escrita leva a `versao` do rascunho e responde **409** se ela estiver velha.

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
seção "Contrato de erro da Estrutura" de `specs/05-api-endpoints.md`; os códigos novos são definidos no
plano.

## 7. Confirmação

Numa transação só:

1. Recalcula os bloqueios (seção 5.4). Sobrou algum → **400** com a lista.
2. Recalcula a impressão da receita de catálogo de cada código com escolha. Mudou → **409** ("a receita
   de X mudou no catálogo, confira de novo"), e a escolha daquele código é zerada.
3. Cria os Componentes novos, reativa os inativos, grava os sólidos pendentes (substituindo o de um
   casado, quando for o caso).
4. Grava a receita lida em cada código com escolha "importada" e em cada código que não divergia.
5. Cria a Peça **pelo caminho que já existe** — o planejamento de cópia do catálogo de `CriarPeca` —,
   que agora reflete as escolhas.
6. Apaga o rascunho.

**Risco registrado, a medir — não resolvido.** Hoje `CriarPeca` lê o catálogo **fora** da transação,
de propósito: `LerReceitaCompletaAsync` sob SERIALIZABLE travaria as tabelas da receita por faixa e
abriria um ciclo de deadlock com `ReceitaPadraoRepository` (seção 3.2 da spec
`2026-10-01-deadlock-na-suite-de-api-design.md`). Aqui o passo 5 precisa ler **dentro** da transação,
para enxergar o que os passos 3 e 4 gravaram. O plano escolhe o desenho (ler dentro; ou planejar a
cópia a partir do estado em memória do rascunho, sem reler o catálogo) e **mede** com a suíte de Api
repetida, como foi feito no conserto do deadlock.

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

1. **Painel fixo (sticky) do Componente selecionado.** À esquerda, `VisualizadorDeSolido` com
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
