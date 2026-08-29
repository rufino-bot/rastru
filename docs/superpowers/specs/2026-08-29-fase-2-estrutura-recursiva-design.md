# Fase 2 — Estrutura recursiva (`EstruturaItem`)

Spec de design. Escrita em 2026-08-29, a partir do brainstorm da mesma data, logo depois de a Fase
1E fechar (PR #10 mesclado, `main` = `9ec40e6`).

**É a primeira fase de domínio do projeto.** Toda a Fase 1 foi cadastro, autenticação e interface;
aqui nasce a árvore que o sistema existe para rastrear.

**Pré-requisito:** a 1E mesclada. Esta fase começa limpa, em branch nova
(`fase-2-estrutura-recursiva`), a partir de `9ec40e6`.

---

## 0. O que já estava decidido antes deste brainstorm

Nada disto se re-decide aqui; a spec só aponta.

- **`EstruturaItem` é recursiva numa tabela só** — nó sem pai é **Peça**, nó com pai é **Item**.
  Não existem tabelas separadas (`01`, regra 3; decisão listada em `CLAUDE.md` como descartada).
- **Lote agregado, sem serial**, divisível por quantidades livres (`01`, regra 9).
- **Peça sempre referencia um `Componente`; só Item pode ser ad-hoc** — decidido em 2026-08-04,
  adiado de propósito para esta fase, com a motivação completa e as alternativas descartadas na
  regra 18 de `01`.
- **`EstruturaItem.Descricao` nomeia o nó**; NULL herda a descrição do `Componente` (`01`, regra 19).
- **A receita padrão pode conter ciclo no dado gravado** — a verificação da 1C é defesa em
  profundidade na escrita, **não** garantia sobre o grafo. A própria regra 20 de `01` diz, com
  todas as letras, que "uma travessia recursiva que dependa disso, como a cópia da Fase 2, precisa
  da própria guarda contra ciclo".
- **Setor repetido no roteiro é retorno ao mesmo setor, não duplicata** (`01`, regra 21).
- **Database First**: o schema nasce em `02-modelo-de-dados.sql`; nunca `Add-Migration`.

### O estado do disco que a fase encontra — medido em 2026-08-29, não lembrado

| Premissa | Veredito | Medição |
|---|---|---|
| `EstruturaItem` não tem entidade EF | **Confirmado, e é de propósito** | Nenhum `DbSet` em `RastreamentoDbContext.cs:10-20`; a decisão está escrita em `IAgrupamentoRepository.cs:22-28` |
| Existe um consumidor esperando o mapeamento | **Confirmado** | `AgrupamentoRepository.cs:42-50` usa `SqlQuery` cru contra `dbo.EstruturaItem`; o contrato já prevê virar LINQ sem mudar |
| A constraint `CK_EstruturaItem_PecaTemComponente` não existe | **Confirmado — e o grep engana** | `02-modelo-de-dados.sql:210-213` é **comentário**. `grep -c` devolve 1 e parece feita |
| O lado da estrutura espelha o do catálogo | **Confirmado** | `EstruturaItem`/`EstruturaMaterial`/`EstruturaRoteiro` ↔ `Componente`/`ComponenteMaterialPadrao`/`ComponenteRoteiroPadrao`. **A cópia recursiva não existe** — nasce aqui |
| Não há tela de Agrupamento | **Confirmado** | Ele vive dentro de `PedidoDetalhePage` (219 linhas). `GET /agrupamentos/{id}` existe na API **sem consumidor no front** |
| Criar Agrupamento não checa `Status` do Pedido | **Confirmado** | Método lido inteiro: `CadastroDeAgrupamentoUseCase.Cadastrar` não olha status. Só `Excluir` olha (`:125`, `PedidoNaoAberto`) |

### Baseline medida em 2026-08-29, nesta bancada

```
BACKEND : 464 aprovados, 0 falhas  (Application 201 · Infrastructure 58 · Api 205 · Domain 0, vazio)
          dotnet build Rastreamento.slnx -warnaserror → 0 Avisos, 0 Erros
FRONT   : 419 testes / 35 arquivos, build limpo
```

**Meça de novo no início do plano; não copie estes números.** Eles são de antes da primeira linha
de código da fase.

---

## 1. Escopo

### 1.1 O roadmap descreve três blocos; esta fase leva dois

O `06-roadmap-mvp.md` junta numa fase só: (1) o nascimento do `EstruturaItem`, (2) a árvore visual e
(3) upload e exibição de `Componente.ArquivoSolido`/`ArquivoFoto`.

**O bloco 3 sai, e vira Fase 2B.** `ArquivoSolido` é **caminho** (`NVARCHAR(260)`,
`02-modelo-de-dados.sql:98`), não `VARBINARY` — então upload é subsistema de storage: onde o arquivo
é gravado em disco, como é servido de volta, validação de tamanho e tipo, arquivo órfão quando o
`Componente` é inativado. Não compartilha tabela com os blocos 1 e 2, e o critério de pronto que o
próprio roadmap escreve — "dá para montar visualmente a árvore completa de uma Peça complexa" — não
o exige.

**`06-roadmap-mvp.md` é atualizado por esta fase** para registrar o corte e nomear a 2B (§5).

### 1.2 A segunda metade da regra 18 fica para a 2B, com gatilho escrito

A constraint garante que existe **onde** pendurar o sólido. Que ele esteja **preenchido** é
validação de aplicação, e ela **não entra aqui**: sem upload, ninguém preenche `ArquivoSolido` pela
interface.

**Custo aceito:** durante a Fase 2 dá para criar Peça a partir de Componente sem sólido, e nada
reclama. Fica registrado num comentário no caso de uso, no mesmo molde que a 1B usou em
`IAgrupamentoRepository` para o próprio `EstruturaItem` — o lugar onde a próxima pessoa a mexer
tropeça nele.

**Alternativa descartada, e por quê:** validar já bloquearia a verificação manual em navegador. Os
54 Componentes de `db/seed-demo.sql` **não têm sólido**, então nenhum serviria para criar Peça, e
destravar a demo exigiria gravar caminhos falsos por SQL — dado mentiroso no banco.

### 1.3 Edição: criar e customizar os nós; materiais e roteiro só visíveis

**Dentro:** copiar a receita, acrescentar sub-Item (de Componente ou ad-hoc), editar quantidade e
descrição de um nó, excluir nó com a subárvore. Entrega a promessa do glossário — `EstruturaItem` é
a árvore "copiada do catálogo **e customizada**" (`01:13`) — e o critério de pronto ("**montar**").

**Fora:** editar `EstruturaMaterial` e `EstruturaRoteiro` por nó. Eles vêm copiados e ficam
**visíveis**, não editáveis.

**A consequência, dita por inteiro:** a regra 7 ("o roteiro pode ser customizado por
Pedido/Agrupamento") sai desta fase **entregue pela metade** — copiável, não customizável. O corte
existe porque editar os três re-entregaria, dentro da árvore, as três telas que a 1C construiu para
o catálogo, e a 1C sozinha deu 47 commits. **Gatilho para retomar:** a primeira vez que um roteiro
real precisar divergir do padrão num pedido específico.

---

## 2. Domínio — as decisões deste brainstorm

### 2.1 `EstruturaItem.Quantidade` é **absoluta**; a cópia multiplica descendo

Peça de 10 cuja receita diz "4 por unidade" gera filho com **40**, não 4.

**Por quê:** a Fase 3 aponta entrada e saída de setor **por `EstruturaItem`**, e o operador
movimenta 40 suportes, não "4 por pai". A regra 9 fala em "quantidade total da Peça" — só fecha com
número absoluto guardado.

**Descartado:** guardar a razão e derivar o absoluto. Custo que matou: toda consulta da Fase 3 em
diante subiria a árvore para saber quanto "4" é de verdade, e a conservação de quantidade viraria
cálculo recursivo em cada apontamento.

### 2.2 Editar a quantidade de uma Peça **não** cascateia nos filhos

A cópia da receita é **pré-preenchimento, não automação** — a mesma filosofia que o roadmap já
escreveu para o import de BOM (`06-roadmap-mvp.md:246-250`). Depois de copiada, cada nó é dado que o
usuário mantém.

**A premissa que sustenta isso, e que precisa sobreviver a qualquer revisão futura:** com quantidade
absoluta **e** customização livre, **não existe invariante** dizendo filho = pai × razão. Um filho
pode legitimamente ser 45 para uma Peça de 10 (sobra de refugo). Não há violação a corrigir; o que
se decidiu foi o quanto o sistema se intromete.

**Custo nomeado:** dá para deixar a árvore desproporcional sem nada reclamar. Mitigação, já prevista
no layout: a árvore exibe a quantidade de **todo** nó, então a inconsistência fica à vista.

**Descartado:** cascata proporcional automática — apagaria em silêncio a customização manual que a
§1.3 existe para permitir.

### 2.3 Alteração de projeto com o Pedido em execução

**Informação de domínio trazida pelo usuário em 2026-08-29, e é comportamento padrão, não exceção:**
cliente grande pede alteração de projeto com o Pedido já rodando. **Acrescenta-se** Peça nova ao
Pedido em execução, e o que saiu do projeto original **só para de ser produzido** — não é apagado,
não é cancelado com registro formal; a produção cessa.

Três medições decorrem disso:

1. **Acrescentar já está liberado, e a assimetria já está no código.** `Cadastrar` não checa status;
   só `Excluir` checa. O código já faz o que o negócio descreve.
2. **Hoje nenhum Pedido sai de `Aberto`** — `04-fluxos-de-usuario.md:18`: o Pedido fica `Aberto` até
   o primeiro apontamento de setor, e apontamento é Fase 3. "Pedido em execução" só existe de fato a
   partir da 3.
3. **`EstruturaItem` não tem coluna de estado nenhuma.** "Parou de ser produzido" não tem onde ser
   gravado.

**O que esta fase faz:** o caso de uso de criar Peça **não** ganha guarda de status. Isso deixa de
ser omissão herdada e passa a ter motivo de negócio escrito.

**O que esta fase NÃO faz:** a descontinuação em si. Ver §5.2 — ela precisa de decisão de domínio que
ainda não foi tomada, e §6 registra o achado que a torna maior do que um sinalizador.

### 2.4 Excluir nó só enquanto o Pedido está `Aberto`

São duas operações diferentes, e ganham duas palavras diferentes:

- **Correção de montagem** (`DELETE`) — só existe enquanto nada foi produzido, isto é, Pedido
  `Aberto`. Apaga o nó e a subárvore.
- **Descarte** (parar de produzir) — é o que o cliente pede com o Pedido rodando, preserva a
  história, e **não é operação desta fase**.

**Por que a fronteira é `Aberto`:** é literalmente o precedente já em vigor para a exclusão de
Agrupamento (`PedidoNaoAberto`, `CadastroDeAgrupamentoUseCase.cs:125`). A fase não inventa regra —
estende uma.

**Descartado:** nascer já com a coluna de estado nesta fase. Seria coluna feita só para servir uma
guarda futura — o mesmíssimo argumento que o projeto usou para **não** mapear `EstruturaItem` na
Fase 1: *"um mapeamento feito só para servir de guarda envelheceria errado"*
(`IAgrupamentoRepository.cs:25`). Quem vai descobrir se o estado precisa de data, motivo, autor e o
que fazer com a quantidade já em setor é a Fase 3.

---

## 3. Backend

### 3.1 Schema — uma única mudança

`CK_EstruturaItem_PecaTemComponente` sai de comentário e vira DDL em `02-modelo-de-dados.sql`:

```sql
CONSTRAINT CK_EstruturaItem_PecaTemComponente
    CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL)
```

**O banco desta bancada precisa de `ALTER` de verdade** — e isto é diferente dos quatro blocos do
`CLAUDE.md`, que viraram no-op com a regeneração de 2026-08-04. Este banco foi regenerado *a partir
deste `.sql`*, e naquela data a constraint já era comentário: ela **não está lá**. O `ALTER`
idempotente entra no `CLAUDE.md` no mesmo padrão dos demais.

Nenhuma coluna nova. A §2.4 tirou a de estado da mesa.

### 3.2 Camadas

Três entidades EF novas em `Domain/Entities` — `EstruturaItem` (autorreferenciada),
`EstruturaMaterial`, `EstruturaRoteiro` — com `DbSet` no `RastreamentoDbContext` e mapeamento
Database First.

**A primeira coisa a tocar já tem teste.** `AgrupamentoRepository.TemEstruturaAsync` troca `SqlQuery`
cru por LINQ; o contrato previu isso por escrito e o teste existe desde a 1B. O mapeamento nasce com
verificação, sem teste novo.

### 3.3 A cópia recursiva

Criar Peça: `ComponenteId` (obrigatório pela constraint), `Quantidade`,
`RequerRelatorioDimensional`. A cópia desce por `ComponenteFilhoPadrao` e, em **cada** nó, grava
também `EstruturaMaterial` (de `ComponenteMaterialPadrao`) e `EstruturaRoteiro` (de
`ComponenteRoteiroPadrao`), preservando `Ordem`.

Quantidade multiplica descendo (§2.1). **Tudo numa transação: a árvore inteira ou nada.**

Três guardas:

**a) Ciclo — recusa, não poda.** Achou ciclo → 409 com código nomeado, operação inteira recusada.
Podar gravaria uma árvore silenciosamente incompleta, que é pior do que não gravar.

**b) Ciclo é por *caminho*, não por "já visto".** Se o mesmo `Componente` aparece em dois ramos
distintos, isso é **legítimo** e a cópia tem de aceitar. Só é ciclo se ele já está no caminho da raiz
até o nó atual.

> **Isto é medição, não hipótese.** No `seed-demo` gravado, **1 Componente aparece sob mais de um
> pai**. Uma guarda de "já visto em qualquer lugar" recusaria a receita `MT-1000` que está no banco
> agora. Diamante vale; ciclo não.

**c) Teto de segurança — profundidade e número de nós.** Não é regra de negócio: é pára-quedas
contra transação desgovernada, e por isso **não entra em `01-dominio-e-regras-de-negocio.md`**.

> **Por que o número não sai do demo.** Medido em 2026-08-29: a receita gravada desce **2 níveis
> abaixo da raiz** — isto é, **3 níveis contando a Peça** — e a maior expansão dá **15 nós**
> (`MT-1000`; os outros dois pais dão 5 e 4), com **zero** arestas fechando ciclo. Mas o `seed-demo`
> é massa que *nós* escrevemos — ele mede o demo, não a fábrica, e uma montagem real de cliente
> grande pode ser muito mais funda. A medição entrega um **piso** (qualquer teto fica acima de 3
> níveis contados e de 15 nós), não o teto.
>
> O teto fica **alto o bastante para nunca recusar estrutura plausível** (ordem de 20 níveis e ~500
> nós, com erro nomeado). Ele pode **descer** quando houver dado real; subir um teto que já recusou
> trabalho de cliente é o erro caro. `[[escolher-o-caso-de-teste-e-escolher-o-cego]]`
>
> **Consequência para o teste:** o demo **não exercita** árvore funda nem ciclo. As duas guardas
> precisam de receita sintética, montada de propósito no teste.

### 3.4 Contrato

```
GET    /agrupamentos/{id}/estrutura     a árvore inteira do Agrupamento
POST   /agrupamentos/{id}/estrutura     nova Peça  → dispara a cópia recursiva
POST   /estrutura/{id}/filhos           sub-Item: de Componente (copia) ou ad-hoc
PUT    /estrutura/{id}                  quantidade e descrição
DELETE /estrutura/{id}                  o nó e a subárvore — só com Pedido `Aberto`
```

Molde do `AgrupamentosController`: aninhado para criar e listar, topo para operar no item, cada ação
declarando a própria rota (sem `[Route]` de classe). Nenhuma rota escreve `/api` — quem aplica o
prefixo é o `rota()` de `web/src/api/client.ts`.

A resposta do `GET` vai **aninhada** (filhos dentro do pai): é o que o layout da §4.1 renderiza
recursivamente, sem o cliente remontar a árvore a partir de `EstruturaPaiId`.

**Escrita em `PCP,Administrador`**, leitura para qualquer autenticado — igual a Agrupamento, e
`web/src/auth/permissoes.ts` espelha isso.

### 3.5 Erros

Cada recusa viaja como **código** no corpo, no padrão que a 1A fixou e a spec de endpoints define —
o controller repassa e não deriva comportamento da string:

| Situação | Resposta |
|---|---|
| Peça sem `ComponenteId` | 400, validação |
| Ciclo na receita durante a cópia | 409 `CicloNaReceita` |
| Teto de profundidade ou de nós estourado | 409, código próprio |
| `DELETE` com Pedido fora de `Aberto` | 409 `PedidoNaoAberto` — mesmo código já em uso |
| Nó inexistente | 404 |

---

## 4. Frontend

### 4.1 A tela: rota nova e lista indentada

**Rota `/agrupamentos/:id` → `AgrupamentoDetalhePage`**, seguindo o precedente de `/componentes/:id`
da 1C. O link nasce na lista de Agrupamentos da `PedidoDetalhePage`, que hoje não leva a lugar
nenhum. Não se enfia árvore recursiva na `PedidoDetalhePage`, já com 219 linhas.

**Layout: lista indentada, uma coluna** — escolhido pelo usuário em 2026-08-29, contra árvore com
painel de detalhe e contra cartões aninhados. Um nó por linha; a indentação carrega a hierarquia;
materiais e roteiro abrem **embaixo da própria linha**. Reaproveita a forma de lista que as seis
telas existentes já usam, e é a única das três que **não precisa de layout alternativo no celular** —
as outras duas exigiam segunda tela ou quebravam com três níveis.

### 4.2 Primitivas

- A árvore é **primitiva nova em `web/src/components/`, com teste próprio** — não embutida na tela.
- Escolher o Componente usa **`SeletorComBusca`**. O gatilho que o `CLAUDE.md` nomeia é "catálogo
  paginado", e este vira o **segundo** consumidor da primitiva — a confirmação de que ela estava
  certa, e não mais um caso único.
- Nada de campo, botão, banner ou estado escrito à mão.

### 4.3 Estados e gating

Os três estados obrigatórios — carregando (`EstadoCarregando`), vazio (com texto que distingue "não
achei" de "não há nada") e erro (`mensagemDeErro`) — **cada um com teste que morre se o estado
sumir**. Busca paginada, se houver, por `useBuscaPaginada`.

`usePodeEscrever` esconde formulário e ações de escrita; o link continua visível. O `try/catch` do
403 continua obrigatório: esconder botão não é segurança.

### 4.4 Cor

Só tokens de `web/src/index.css`. E a regra que a 1E cobrou caro: **cor de identidade não significa
estado, cor de estado não decora**. Nó ad-hoc, nó de catálogo e nó folha se distinguem por forma,
peso ou rótulo — **nunca** por verde ou vermelho, que são de aprovado/reprovado.

### 4.5 Um caminho morto que esta fase liga

Hoje o `409 AgrupamentoNaoVazio` é praticamente inalcançável: nada cria estrutura, então
`TemEstruturaAsync` sempre devolve falso. **A Fase 2 o liga pela primeira vez.** É um teste de
integração que passa a valer de verdade e uma tela (`PedidoDetalhePage`) que passa a poder receber
esse 409 na prática — o plano trata isso como verificação, não como suposição.

---

## 5. O que esta fase atualiza, e o que fica de fora

### 5.1 Documentos que esta fase atualiza

1. **`specs/02-modelo-de-dados.sql`** — a constraint sai de comentário (§3.1). **Primeiro**, antes do
   EF.
2. **`specs/05-api-endpoints.md`** — os cinco endpoints da §3.4, com os códigos de erro da §3.5.
3. **`specs/06-roadmap-mvp.md`** — registra o corte da §1.1 e nomeia a **Fase 2B** (upload e exibição
   de `ArquivoSolido`/`ArquivoFoto`), herdando dela a segunda metade da regra 18.
4. **`specs/01-dominio-e-regras-de-negocio.md`** — a seção "Pontos ainda em aberto" ganha o achado da
   §6. **O teto da §3.3 não entra aqui**: é limite técnico, não regra de negócio.
5. **`CLAUDE.md`** — o `ALTER` idempotente da constraint, e a seção de Interface se a primitiva de
   árvore virar padrão para tela nova.

### 5.2 Fora de escopo, cada um com o gatilho escrito

| Fora | Gatilho para voltar |
|---|---|
| Upload e exibição do sólido/foto | **Fase 2B**, imediatamente após esta |
| Cobrança de `ArquivoSolido` preenchido (regra 18, 2ª metade) | Junto com a 2B — antes não há como preencher |
| **Descartar** Peça em Pedido rodando (§2.3) | **Fase 3**, onde "Pedido em execução" passa a existir |
| Editar materiais e roteiro por nó (regra 7) | O primeiro roteiro real que precise divergir do padrão |
| Import de BOM indentado | Já registrado em `06-roadmap-mvp.md`, fora das fases |

---

## 6. O achado que esta fase levanta e **não** resolve

Medido durante o brainstorm, e hoje não está escrito em `specs/` nenhuma:

**Descontinuar uma Peça trava o fechamento do Pedido.** Pela regra 13, uma Peça conclui quando
**toda** a quantidade virou expedido **ou** perdido; o Agrupamento conclui quando todas as Peças
concluem; o Pedido, quando o último Agrupamento conclui. Uma Peça descartada com 4 unidades que
nunca entraram em produção: essas 4 não são expedidas nem perdidas. A Peça **nunca conclui**, o
Agrupamento nunca conclui, e **o Pedido nunca fecha**.

Ou seja: descontinuar não é um sinalizador. Precisa de um **bucket terminal** próprio, ou de uma
exceção explícita na regra 13 — e isso é decisão de domínio, com efeito na **Fase 5** (fechamento),
não só na 3.

**Esta fase o registra em `01`, seção "Pontos ainda em aberto", e não o decide.** Decidi-lo aqui
seria decidir o desenho da Fase 5 sem o contexto dela.

---

## 7. Prova

### 7.1 Por camada

- **`Application.Tests`** (fakes, sem banco): a cópia recursiva — multiplicação de quantidade,
  materiais e roteiro copiados em cada nó com `Ordem` preservada, ciclo recusado, diamante aceito,
  teto estourado, `DELETE` recusado fora de `Aberto`, criação permitida com Pedido fora de `Aberto`.
- **`Infrastructure.Tests`**: o mapeamento das três entidades contra o SQL Server real, incluindo a
  autorreferência e a constraint nova. **Teste novo que escreve em tabela já escrita por outra
  classe entra na `[Collection]` daquela tabela** — e asserção sobre contagem global de tabela
  compartilhada é flaky por construção, independentemente de `[Collection]`: escope por prefixo.
- **`Api.Tests`**: os cinco endpoints ponta a ponta, os códigos de erro da §3.5, e o gating de perfil
  (o 403 para quem não é `PCP`/`Administrador`).
- **Front**: a primitiva de árvore com teste próprio; a tela com os três estados; o gating.

### 7.2 O que o `seed-demo` **não** cobre

Já medido (§3.3): profundidade 2, 15 nós, zero ciclos. Portanto **nem a guarda de ciclo nem o teto
são exercitados pela massa de demonstração**. Ambos precisam de receita sintética construída no
próprio teste. Um plano que confie no demo aqui passa verde sem provar nada.

### 7.3 As mutações que a review vai ter de derrubar

Escritas aqui porque o autor da guarda testa a forma que ela já pega; quem acha buraco é quem foi
mandado burlar. `[[mutacao-do-autor-confirma-o-desenho]]`

- Trocar a guarda de ciclo de "caminho" para "já visto" — **tem de quebrar**, e o teste que quebra é
  o do diamante, não o do ciclo.
- Remover a multiplicação de quantidade na descida (gravar a razão) — tem de quebrar.
- Remover a transação e gravar nó a nó — tem de quebrar com ciclo no meio da árvore.
- Trocar o `DELETE` para aceitar Pedido fora de `Aberto` — tem de quebrar.
- Acrescentar guarda de status na **criação** — tem de quebrar (§2.3).
- Não copiar `Ordem` do roteiro, ou copiá-la reindexada — tem de quebrar (regra 21: setor repetido é
  retorno, e a unicidade é da posição).

### 7.4 O que se **mede** no início do plano, em vez de herdar

A baseline da §0 é de 2026-08-29, antes da primeira linha da fase. **Remeça** front e backend na
abertura, com o SQL Server no ar, e propague o número medido para as tasks — contagem absoluta em
plano é dívida composta. `[[propagar-baseline-medida-para-o-plano]]`
