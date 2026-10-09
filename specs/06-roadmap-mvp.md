# 06 - Roadmap / MVP

Fases pensadas para serem executadas em sequência com Claude Code — cada fase pode virar
uma sessão de agent com escopo fechado, referenciando os arquivos desta pasta como
contexto. Recomenda-se não avançar de fase sem os "pontos em aberto" da fase anterior
resolvidos (ou conscientemente adiados).

**O schema pode mudar quando uma spec encontra a necessidade** (decisão do usuário de 2026-10-02).
Uma frase como "não muda o schema", escrita numa fase ou num registro de decisão, descreve o que se
sabia naquela data, não uma proibição: se o desenho de uma funcionalidade nova, ou a correção de algo
que já existe, mostrar que precisa de schema, a spec dela o propõe com o motivo, e a mudança segue o
caminho de sempre (`02-modelo-de-dados.sql` primeiro, depois o mapeamento, depois `01` se for regra).
O caso que motivou a regra: o import da estrutura a partir do CAD.

## Fase 0 — Setup do projeto

- Criar solution .NET (`Domain`, `Application`, `Infrastructure`, `Api`) conforme
  `03-arquitetura-tecnica.md`.
- Rodar `02-modelo-de-dados.sql` em um SQL Server local (Docker) e mapear entidades
  via EF Core (Database First), incluindo `Usuario`/`Perfil`.
- Implementar login (`POST /auth/login`) com emissão de JWT e claim de Perfil.
- Criar projeto React + TypeScript (Vite), estrutura de pastas inicial, tela de login,
  chamada de exemplo à API autenticada.
- Deploy manual (sem CI/CD por enquanto) — documentar passo a passo de publicação.

## Fase 1 — Cadastros básicos (CRUD)

- Setor, Material, Componente (catálogo) — CRUD simples.
- Pedido, Agrupamento — criação e listagem (sem regra de conclusão ainda).
- Critério de pronto: dá para cadastrar um Pedido com Agrupamentos vazios via tela.

> **1A concluída** (`Setor`, `Material`, `Pedido`, `Agrupamento` — CRUD pela tela, com
> autorização por perfil no backend).
>
> **1B concluída**: `Componente` (catálogo) — CRUD pela tela, escrita para Administrador e PCP,
> com **busca e paginação no servidor** (`?busca=`, `?pagina=`, `?tamanho=`, teto 100). Primeira
> listagem paginada do sistema; o contrato é o `PaginaDto<T>` genérico de
> `Application/Common`. `Setor` e `Material` **não** foram migrados para ele — dívida rastreada,
> e não item esquecido: eles não têm o volume que motivou a paginação.
>
> **1C concluída em 2026-08-28**: a receita padrão do Componente
> (`ComponenteFilhoPadrao`/`ComponenteMaterialPadrao`/`ComponenteRoteiroPadrao`) — backend
> (`ReceitaPadraoController`, três pares `GET/POST componentes/{id}/{filhos,materiais,roteiro}-padrao`,
> contrato em `05-api-endpoints.md`) e a tela de detalhe do Componente no front, com leitura e
> escrita das três seções e gating de perfil. **A Fase 2 passa a ter o que copiar** — e é só isso
> que muda: a lógica de cópia recursiva em si (`EstruturaItem` a partir da receita) ainda não
> existe, nasce na própria Fase 2.
>
> Dívida rastreada de 1A: **gating de NAVEGAÇÃO** por perfil — o link continua visível para todos.
> Segue aberta **por decisão**, não por esquecimento: o `CLAUDE.md` registra que o gating deste
> projeto vai na AÇÃO, não no link, e é a ação que a 1D fechou (ver abaixo). A outra dívida que
> vivia nesta linha — a camada global de erro de API no front — **foi fechada pela 1D**
> (`ErroDeApi` + `mensagemDeErro`), e por isso saiu daqui.

## Fase 1D — Identidade visual e UX

- Tokens de tema, primitivas de interface à mão sobre Tailwind e shell de navegação.
- Retrofit das 7 telas existentes para o padrão novo.
- Critério de pronto: mesma primitiva nas 7 telas, estados carregando/vazio/erro em toda tela que
  busca dados, navegação por teclado com foco visível, contraste AA medido por teste, e nenhuma
  tela rolando na horizontal em viewport de celular.

> **Esta fase NÃO tem aresta de dependência.** Ela não bloqueia nem é bloqueada pela 1C, e pode
> rodar antes ou depois dela. A letra é rótulo cronológico, não ordem obrigatória — sem esta frase,
> a sequência 1B → 1C → 1D se lê como dependência, e ela não é.
>
> **1D concluída em 2026-08-15.** Fecha três dívidas de UX que vinham da 1A e da 1B: camada global
> de erro de API (`ErroDeApi` + `mensagemDeErro`), gating de perfil, e botão desabilitado durante
> mutação. Fecha também o `useBuscaPaginada` (debounce, cancelamento, clamp, reset) e o W3
> (`setCarregando` sem prova).
>
> **O gating de perfil ficou na AÇÃO, não no link** — o link continua visível para todos porque a
> leitura de todos estes recursos é liberada a qualquer usuário autenticado no backend; o que some
> para quem não pode escrever é o formulário e os botões de (in)ativar. Esconder o link de Materiais
> do Almoxarifado tiraria dele uma leitura de que a **Fase 4** depende.
>
> **Corolário registrado:** se daqui a três fases o sistema precisar de outra passada de UI, isso não
> é uma fase planejada que faltou — é sinal de que o padrão não pegou. Não existe "Fase 1D parte 2".

## Fase 1E — Refinamento visual

- Tipografia própria: IBM Plex Sans/Mono auto-hospedadas, por troca dos tokens `--font-sans` e
  `--font-mono` — zero tela reescrita.
- `HomePage`: resumo pelos cinco status do Pedido no cartão de Pedidos, e seção "pedidos abertos
  há mais tempo".
- Critério de pronto: fonte aplicada e **carregada** (verificada no navegador, não só no token),
  os cinco status visíveis inclusive os zerados, e a seção nova com os três estados —
  carregando, vazio de verdade e erro — cada um com teste que morre se o estado sumir.

> **1E concluída em 2026-08-29**: tipografia IBM Plex auto-hospedada por troca de um token, o
> resumo pelos cinco status no cartão de Pedidos da Home e a seção "pedidos abertos há mais tempo".
> Saíram junto duas extrações que a fase provou necessárias — `statusDoPedido.ts` e a primitiva
> `LinhaDePedido`, que a `PedidosPage` adotou — e uma guarda executável para a dívida de
> `listarPedidos()` não paginado. Suíte do front em **419 testes / 35 arquivos**, medida depois do
> merge da `main`.

> **Esta fase NÃO é a "Fase 1D parte 2" que o corolário acima descarta**, e vale dizer por quê em
> vez de fingir que a tensão não existe. O que aquele corolário advertia era uma reestilização
> ampla motivada por o padrão de primitivas não ter segurado. Aqui o padrão segurou: a troca de
> fonte é **o gancho que a própria 1D deixou pronto** ("trocar por uma fonte própria depois é mudar
> um token, não reescrever telas"), e o reforço da Home usa as primitivas existentes e o dado que a
> Home **já** buscava. As outras seis telas não são reabertas — a `PedidosPage` é tocada só para
> adotar a `LinhaDePedido` extraída do markup que ela mesma já tinha.
>
> **Fora de escopo, por decisão escrita:** densidade das outras telas; "prazo de entrega" e
> "pedidos em atraso" — o domínio não tem campo de data prevista, e criá-lo é mudança de schema
> **e** de formulário de cadastro, candidata a fase própria (§5 da spec da 1E); e qualquer KPI da
> Fase 6, que depende do rastreamento por Setor que só nasce na Fase 3.
>
> **Dívidas nomeadas por esta fase:** `listarPedidos()` não é paginado, e a Home deriva o resumo e
> a lista do array inteiro. Não é problema hoje, e **não** está só escrito: há guarda executável em
> `web/src/api/cadastros.test.ts` que fica vermelha se **o cliente** `listarPedidos()` passar a
> paginar — por truncar o array ou por trocar a assinatura por um envelope. Ela mede o cliente com
> `fetch` stubado, então **não cobre o lado do servidor**: se o backend passar a truncar
> `GET /api/pedidos` mantendo a forma de array, o cliente não muda, a guarda fica verde e a Home
> volta a mostrar "contagem das N primeiras" em silêncio. Fechar esse lado pede uma asserção de
> forma da resposta em `tests/Rastreamento.Api.Tests`, e ela não existe. A outra
> é cosmética — o rótulo de status aparece cru (`EmProducao`, `AguardandoExpedicao`) na Home e na
> `PedidosPage`; humanizá-lo é mexer nas duas telas de uma vez, fora do escopo desta fase. A
> terceira é estrutural: **as três guardas de tema não medem semântica de cor.**
> `semCorForaDaPaleta.test.ts` mede token fora da paleta, `contraste.test.ts` mede razão de
> contraste, `semModificadorDeOpacidadeEmCor.test.ts` mede opacidade — nenhuma verifica se uma cor
> reservada está sendo usada com o significado certo. Nesta fase, o resumo por status saiu com
> `Concluido 0` em verde e `Cancelado 0` em vermelho — violando "cor de estado nunca decora" — e
> passou por toda a suíte em verde; quem pegou foi a verificação no navegador, em 375px. O conserto
> desta instância foi pontual (tom neutro no resumo, mais um teste específico); a **classe** do
> problema continua sem guarda.

> **Emenda de 2026-09-29:** as duas primeiras «Dívidas nomeadas por esta fase» — `listarPedidos()` não paginado e o
> rótulo cru de status — foram pagas pelo Plano 1 de "Filtros da demanda e ação em lote" (seção
> «Filtros da demanda e ação em lote»): `GET /pedidos` é paginado, a Home lê `GET /pedidos/resumo`, e a guarda do
> cliente, a de `web/src/api/cadastros.test.ts`, foi trocada por duas, uma no backend e uma no front. O parágrafo «Dívidas nomeadas por esta fase»
> descreve a 1E como foi entregue. A terceira dívida, a classe "as guardas de tema não medem
> semântica de cor", **continua em aberto**.

## Fase 2 — Estrutura recursiva

> **2 concluída em 2026-09-11**, com o merge do PR #12: a tabela recursiva única
> (`EstruturaItem` sem pai é Peça, com pai é Item), o `PlanejadorDeCopia` puro que copia a receita
> do `Componente` com guardas de ciclo — **por caminho**, de modo que diamante vale e ciclo não —,
> profundidade, número de nós e faixa da coluna `DECIMAL(18,4)` nas duas direções; os cinco casos
> de uso em transação; os endpoints sob `/api` com o `Recurso` novo no espelho de perfis; e no
> front a primitiva `ArvoreDeEstrutura` mais a `AgrupamentoDetalhePage` com acrescentar filho,
> editar e excluir ligados. A constraint `CK_EstruturaItem_PecaTemComponente` entrou aqui e pegou
> um caso real de dado inválido no primeiro dia. Suítes medidas depois do merge: backend
> **529**, front **495 / 39 arquivos**, os dois builds limpos.

> **O que a fase custou, e onde:** zero Critical em todos os gates. O achado dominante não foi
> código — foi **prosa afirmando com confiança algo não medido**, em comentário, XML doc e spec,
> classe que nenhum teste pega. Daí saíram duas coisas que passam a valer para as fases seguintes:
> a seção "Convenção de citação em comentário e prosa" do `CLAUDE.md` (cite pelo **nome**, nunca
> por `arquivo.ext:NN` nem por distância relativa) e a prática de mandar o revisor **medir cada
> afirmação verificável** em vez de lê-la.

- Criar `EstruturaItem` a partir de um `Componente` padrão (copiar receita) ou do zero
  (customizado).
- Visualização em árvore da estrutura de um Agrupamento (Peça → Itens → sub-Itens).
- `EstruturaItem.Descricao` (regra 19): nome próprio do nó, com fallback para a descrição do
  `Componente` de origem quando NULL — serve ao nó ad-hoc, que sem ela chegaria anônimo à tela
  do operador.
- **Peça sempre referencia um `Componente`** — decidido em 2026-08-04, adiado de propósito
  para esta fase, que é onde o `EstruturaItem` nasce de fato. A constraint que fecha isso:
  ```sql
  CONSTRAINT CK_EstruturaItem_PecaTemComponente
      CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL)
  ```
  Só um **Item** (nó com pai) pode ser ad-hoc. Sem isso, o sólido — que mora em `Componente` —
  não tem onde ser pendurado numa Peça ad-hoc, e a regra 18 fica inexprimível para ela. A
  motivação completa e as alternativas descartadas estão na regra 18 de `01`; não re-decidir
  a partir do zero. A constraint garante o **gancho**; exigir o arquivo preenchido continua
  sendo validação de aplicação (um `CHECK` não alcança outra tabela) — é isso que a Fase 2B,
  abaixo, fecha.
- Como a coluna nasceu depois do banco de dev, aplicar o `ALTER` idempotente de `Descricao` ao
  iniciar a fase, no mesmo padrão dos demais em `CLAUDE.md`.
  **Não se aplica a um banco regenerado:** o banco de dev foi recriado em 2026-08-04 a partir
  deste `.sql`, então a coluna já veio no `CREATE`. Vale só para instalação anterior a essa data.
- Critério de pronto: dá para montar visualmente a árvore completa de uma Peça complexa.

## Fase 2B — Sólido 3D da Peça

> **2B concluída em 2026-09-18**, com o merge do PR #13: o sólido guardado em blob em
> `dbo.ArquivoDeComponente`, com tamanho e SHA-256 calculados pelo próprio banco (colunas
> `PERSISTED`); o formato STL, conferido pelo `ValidadorDeArquivoStl`; o envio — ou a substituição —
> e a leitura do sólido sob `/api`, com limite de 16 MiB cobrado também no cliente, e `TemSolido` nos
> DTOs de Componente. A regra 18 passou a ser cobrada de verdade: Peça criada a partir de um
> `Componente` sem sólido é recusada, e o seletor marca esses Componentes. No front, `UploadDeSolido`
> na tela do Componente e `VisualizadorDeSolido`: three.js carregado sob demanda, enquadramento pelo
> tamanho da peça e pela proporção do quadro, zoom, rotação e pan com "Recentralizar", acabamento
> metálico e liberação dos recursos de GPU ao desmontar. Suítes medidas depois do merge: backend
> **582**, front **557 / 42 arquivos**, os dois builds limpos.

> **O que a fase custou, e onde:** a verificação em navegador achou o que as suítes verdes não
> achavam — o viewer preso em "Carregando…" sob `<StrictMode>`, e uma câmera fixa que deixava peça
> pequena minúscula (e que, pela conta, ficaria **dentro** de uma peça grande). Cada achado virou
> task própria, com teste que morre sem a correção. E os recursos de GPU do viewer vazavam **um de
> cada vez**: cada review achava o membro seguinte da mesma família, até uma delas enumerar todos
> numa tabela. O que fica para as fases seguintes: diante de um vazamento, enumerar a família
> inteira antes de consertar a instância.

- Upload e exibição de `Componente.ArquivoSolidoId` (sólido 3D, guardado em blob na tabela
  `dbo.ArquivoDeComponente`) e a regra de negócio que o exige por Peça de Pedido — segunda metade
  da regra 18 de `01`: a Fase 2 fechou só o **gancho** (a constraint
  `CK_EstruturaItem_PecaTemComponente`, que garante que toda Peça tem onde pendurar o sólido);
  exigir o arquivo **preenchido** é validação de aplicação, cobrada só a partir desta fase, porque
  é aqui que nasce o upload que permite preenchê-lo — cobrar antes travaria a verificação manual (o
  `seed-demo` não tem sólido em nenhum dos Componentes).
- **Diferente das colunas de fases anteriores, o schema desta fase não espera "início de fase" para
  ser aplicado**: a tabela `dbo.ArquivoDeComponente` já existe, `Componente.ArquivoSolido` já deu
  lugar a `Componente.ArquivoSolidoId` em `02-modelo-de-dados.sql`, e os três `CREATE`/`ALTER`
  idempotentes correspondentes já foram aplicados no banco de dev — documentados no `CLAUDE.md`, e
  **não** no-op nesta máquina (o banco foi regenerado em 2026-08-04, antes de este schema existir).
- Critério de pronto: dá para fazer upload do sólido de um `Componente` pela tela, e a regra 18
  passa a ser cobrada de verdade — Peça sem sólido preenchido no `Componente` de origem é
  recusada.

## Fase 3 — Rastreamento de setor

- **Livro de movimentações** (`dbo.Movimentacao`): a quantidade de cada `EstruturaItem` repartida
  entre a iniciar, no Setor (por passo do Roteiro), aguardando coleta, aguardando montagem, no local
  de expedição e montado; conservação de quantidade (regra 9) por construção, validada na
  aplicação, não por índice filtrado.
- **Terminar e mover como ações separadas** (regra 22 de `01`): o operador inicia (primeira entrada,
  regra 28) e termina; o **Movimentador** entrega no próximo destino. Perfil novo `Movimentador` —
  linha em `dbo.Perfil`, na tabela `web/src/auth/permissoes.ts` e nos `[Authorize(Roles)]`; perfil
  novo exige código e deploy.
- **Montagem de todo nó com filhos** (regra 24): registro "montei N", destino "montado" e
  `EstruturaItem.QuantidadePorPai` (regra 26), sem a trava, que é da 3B.
- **Roteiro editável por nó** (regras 7 e 28), pelo PCP; passo alcançado não se edita.
- Fim do Roteiro (regra 29): Item vai à montagem do pai, num Setor que o Movimentador escolhe; Peça
  vai ao local de expedição.
- **Estorno** de registro errado, pelo autor ou pelo PCP.
- Tela de "fila do setor" para o operador: a iniciar, em trabalho, aguardando coleta, aguardando
  montagem com "dá para montar N; falta X de Y", e **sobra** (regra 30).
- Tela **Tarefas** do Movimentador com os **Itens prontos** (regra 23), calculada a partir do
  estado e atualizada periodicamente — sem tabela de aviso.
- Critério de pronto: dá para acompanhar, item por item, em qual posição cada peça está — inclusive
  se aguarda coleta —; o Movimentador vê o que tem a levar e registra a entrega; um Pedido percorre
  iniciar, terminar, entregar, montar e chegar ao local de expedição.

> **Ampliada em 2026-09-15** pela spec `2026-09-15-kit-montagem-e-movimentacao-design.md`: terminar ≠
> mover, "aguardando coleta", o perfil Movimentador e a tela Tarefas entraram aqui, e não na 3B,
> porque são como a movimentação funciona para **tudo**, Kit ou Avulso. Como "aguardando coleta" é
> representado no banco, com o lote divisível, é decisão da spec desta fase, junto das demais
> perguntas que a seção 9 daquela spec ("Deixado para a spec de cada fase") deixa para a Fase 3.
>
> **Ampliada de novo em 2026-09-24** pela spec `2026-09-24-fase-3-rastreamento-de-setor-design.md`,
> que respondeu essas perguntas: a montagem de todo nó e `QuantidadePorPai` vieram da 3B, e o
> Roteiro editável por nó, que o `05` listava sem fase, entrou aqui.

> **Emendada em 2026-09-28 pela Fase 3D (seção própria, neste arquivo):** montar deixou de ser ação — iniciar um nó com
> filhos consome os filhos —, o destino do filho pronto passou a ser o primeiro passo do pai, e
> entraram o estorno rápido na fila e a pausa de Pedido. Os bullets desta seção descrevem a Fase 3
> como foi entregue; onde divergem da 3D, vale a 3D.

**Estado em 2026-10-02:** **concluída em 2026-09-28**, com a verificação manual no celular da seção
9.5 da spec dela. A spec e o plano de documentação entraram na `main` pelo PR #17 (merge
`80e11a0`), e os planos de backend e de front pelo PR #18 (merge `695fbf8`), os dois em 2026-09-25.
O código entrou pelos PRs #20 (backend, merge `44b1d54`) e #21 (front, merge `0f4ae3c`), os dois em
2026-09-26, e por duas correções que a verificação pediu, mergeadas em 2026-09-28: o histórico do nó
numa lista só, do registro mais recente ao mais antigo (PR #22, merge `b31592f`), e a contagem de
Pedidos abertos com o contador de Tarefas recontado depois de cada ação (PR #23, merge `63361ff`).
Outros pontos que a verificação levantou viraram a Fase 3D, os filtros da demanda e o cartão
clicável da Fase 1F.

## Fase 3D — Ajustes pós-verificação da Fase 3

> **Executada antes da 3B**, por decisão de 2026-09-28 ("primeiro os 4 pontos, depois filtros, 1F e
> 3B"). Nasceu dos pontos que o usuário levantou na verificação manual da Fase 3 (seção 9.5 da spec
> dela). Spec: `docs/superpowers/specs/2026-09-28-fase-3d-ajustes-pos-verificacao-design.md`.

> **Ordem depois da 3D** (decisão do usuário de 2026-09-28, oficializada em 2026-09-29): primeiro os
> **filtros da demanda** (a fila do Setor, as Tarefas e a lista de Pedidos), depois a **Fase 1F —
> Cadastro sob demanda**, depois a **Fase 3B**. "Filtros" não é fase com letra deste roadmap, mas tem
> seção própria ("Filtros da demanda e ação em lote"), com spec e plano; o escopo que o
> brainstorm de 2026-09-29 decidiu é maior que o "filtrar a fila por Material e por Pedido" desta
> nota. A Fase 1F tem spec
> (`docs/superpowers/specs/2026-09-06-fase-1f-cadastro-sob-demanda-design.md`), mas em 2026-09-29 o
> arquivo vive só na branch `fase-1f-cadastro-sob-demanda` e não está na `main` — quem lê a `main`
> não o encontra na pasta de specs. Ela também ainda não tem seção neste arquivo: a entrada vem com
> a abertura da fase, como a própria spec prevê. A Fase 3B tem seção própria neste arquivo.
>
> **Emenda de 2026-10-01:** a Fase 1F abriu e ganhou a seção «Fase 1F — Cadastro sob demanda e
> ordenação das listas», neste arquivo, entre os filtros e a 3B. A spec dela continua só na branch
> `fase-1f-cadastro-sob-demanda` nessa data. O texto desta nota anterior a esta emenda descreve o
> arquivo em 2026-09-29.

- **Iniciar como verbo único** (regra 24 de `01`): iniciar um nó **com filhos** consome os filhos
  presentes no Setor e põe o pai em produção no primeiro passo, na mesma transação. "Montar" deixa
  de ser ação: `POST /estrutura/{id}/montagens` sai (404) e o pai tem um lugar só na fila, o card com
  os filhos e o botão Iniciar.
- **Atividade do Setor:** campo opcional `Setor.Atividade` (substantivo, até 40 caracteres) que dá
  nome aos botões da fila — "Iniciar montagem" / "Terminar montagem", "Iniciar solda" / "Terminar
  solda"; sem atividade, "Iniciar" / "Terminar".
- **Destino do filho pronto calculado** (regra 29): o primeiro passo do Roteiro do pai, sem escolha
  do Movimentador; `destinoSetorId` no corpo da entrega passa a ser recusado (`DestinoIndevido`).
- **Estorno rápido na fila:** as linhas de "Em trabalho", "Aguardando coleta" e a sobra do último
  passo trazem os registros que quem lê ainda pode estornar, e a tela abre uma lista curta deles (ou
  vai direto à confirmação quando há um só).
- **Pausa de Pedido** (regra 31): tabela própria `dbo.PedidoPausa`, só de inclusão, exceto o
  fecho do intervalo (`RetomadoEm` e `RetomadoPorUsuarioId`, gravados uma vez, no Retomar); pausam e
  retomam PCP, Gestão e Administrador (a primeira escrita da Gestão); a pausa recusa **só o Iniciar**
  — terminar, entregar e estornar continuam valendo.
- Critério de pronto: o da seção 9 da spec da Fase 3D, cujo último item é a **verificação manual no
  celular**, como na seção 9.5 da spec da Fase 3.

**Estado em 2026-10-02:** **concluída em 2026-09-29**. A verificação manual no celular foi feita
pelo usuário em 2026-09-29, com os seis passos do roteiro aprovados, e a fase entrou na `main` no
mesmo dia (PR #24, merge `7b3726a`). Antes do merge entraram duas mudanças que o usuário decidiu na
sessão da verificação: durante o roteiro, a ordem da fila do Setor, que passou a mostrar primeiro o
que está em trabalho e, no fim, o que aguarda coleta e a sobra; e, depois do roteiro, a pílula do
Pedido pausado no tom de atenção (âmbar), um tom reservado a estado.

## Filtros da demanda e ação em lote

> **Executada depois da 3D e antes da 1F e da 3B** (decisão do usuário de 2026-09-28, oficializada
> em 2026-09-29 — ver a nota "Ordem depois da 3D", na seção «Fase 3D — Ajustes pós-verificação da Fase 3»): **filtros → 1F → 3B**. A ordem
> não mudou; o que mudou é o tamanho do "filtros". Não é fase com letra. Spec:
> `docs/superpowers/specs/2026-09-29-filtros-e-lote-design.md`. **Sem mudança de schema**:
> `dbo.EstruturaMaterial` já existia e já é gravado na criação do nó, e a fase não traz
> `db/alter-*.sql`.

Nasceu de dois pedidos do usuário nas verificações manuais no celular — filtrar a fila por Material
e por Pedido, e agir em lote com "marcar todos" — mais a dívida da lista de Pedidos que a Fase 1E
nomeou. O escopo tem três blocos, **A + B + C**, executados em **dois planos**:

- **A — Filtros da demanda.** Um componente reutilizável, `FiltroDeDemanda`, aplicado à fila do Setor
  e às Tarefas, com as facetas Material (o do nó, não o do catálogo) e Pedido.
- **B — Ação em lote.** Seleção múltipla para Iniciar, Terminar e Iniciar o pai na fila, "Marcar
  todos" por seção, "Marcar todos" nas Tarefas, e duas rotas de lote no backend (`POST /inicios` e
  `POST /terminos`, tudo ou nada numa transação).
- **C — Pedidos.** `GET /pedidos` paginado, com busca e filtro por Status e Material; o resumo da
  Home; o rótulo do status em português.

**Plano 1 — Filtros** (`docs/superpowers/plans/2026-09-29-filtros-plano-1.md`): A e C, que são só
leitura e sem risco para o livro. Entregue, verificado no celular em 2026-09-30 e mergeado na `main`
(PR #25, merge `6d05c77`):

- `NoResumoDto` com `pedidoCliente` e `materiais` (uma consulta em lote, não uma por nó) — fila e
  Tarefas; `GET /pedidos` paginado com `busca`, `status`, `material`, `pagina` e `tamanho`;
  `GET /pedidos/resumo` e `GET /pedidos/materiais` (contrato em `05-api-endpoints.md`).
- No front, o `FiltroDeDemanda` e a função pura `casaComFiltro` (OU dentro da faceta, E entre
  facetas, faceta vazia não restringe), o `useSelecaoNaUrl`, e o módulo `filtroDaDemanda` que aplica
  o filtro à fila e às Tarefas; a `FilaDoSetorPage`, a `TarefasPage` e a `PedidosPage` filtram; a
  `HomePage` lê o resumo; `rotuloDoStatus` vale na Home, na lista, na `LinhaDePedido` e no filtro.
- **A guarda da 1E foi trocada, não apagada.** O teste do cliente que afirmava que `listarPedidos()`
  devolve o conjunto inteiro saiu; a mesma proteção passou a ser feita por
  `Resumo_conta_todos_os_Pedidos_alem_do_tamanho_de_pagina` (backend: o resumo conta além do tamanho
  de página) e por *"a Home le o resumo e nao uma lista de pedidos"* (front). A dívida do rótulo cru
  de status, também nomeada pela 1E, foi paga pelo `rotuloDoStatus`.

**Onde o código diverge do que a spec escreveu, e vale como está:**

- `GET /pedidos` desempata por `Id` decrescente depois de `DataAbertura` decrescente: sem ordem
  total, `Skip/Take` repete e pula linha entre páginas. A ordem que o usuário vê não muda.
- Na fila e nas Tarefas, a **contagem** de cada opção é sobre a seleção das *outras* facetas — a
  opção que daria lista vazia mostra 0 —, e a **lista** de opções é a da tela sem filtro nenhum, para
  não pular enquanto o operador marca. A spec dizia só "linhas visíveis que casariam".
- O filtro só muda o que se desenha. "Saiu da fila" e "saiu da lista" continuam calculados sobre a
  resposta inteira, e nas Tarefas o item marcado que o filtro esconde continua marcado e vai na
  entrega, com aviso de quantos estão ocultos.
- Valor marcado que a tela nunca viu (F5 com o Pedido já fora da fila) aparece como "Não está mais
  na lista", marcado, com 0 e removível. Na tela de Pedidos, valor da URL que a API recusaria
  (`?material=abc`, `?status=Qualquer`) **não é enviado** ao servidor, e o 400 fica como fronteira
  de quem chama a API direto.
- A URL **lê** a lista separada por vírgula, com ou sem codificação, mas **escreve** a vírgula
  codificada (`?material=3%2C5`).

**Plano 2 — Lote** (`docs/superpowers/plans/2026-09-30-filtros-plano-2.md`): o bloco B. **Escreve no
livro** sob SERIALIZABLE, com outro perfil de risco e de review que o Plano 1, e depende dele porque a
seleção convive com o filtro. Implementado na branch `filtros-e-lote-plano-2`:

- **Backend.** `POST /inicios` e `POST /terminos` (contrato em `05-api-endpoints.md`): `itens` com
  até 100 nós (`ApontamentoUseCase.TamanhoMaximoDoLote`), um 201 com um movimento por item na ordem
  do corpo, e os códigos novos `LoteVazio`, `LoteGrandeDemais` e `ItemRepetido`. **Uma regra, dois
  pontos de entrada:** a validação de iniciar e terminar mora num núcleo por item
  (`IniciarNoAsync`, `TerminarNoAsync`, privados do `ApontamentoUseCase`), e a rota de um nó e o lote
  passam por ele; o lote não recalcula saldo, Roteiro nem filhos. O lote só acrescenta ao livro as
  mesmas linhas que a ação individual acrescentaria; o plano não traz mudança de schema nem
  `db/alter-*.sql`.
- **Falha não commita (desvio D1 do plano).** `IExecucaoRepository.EmTransacaoAsync` ganhou uma
  sobrecarga com `confirmar`, que só commita se o resultado for de sucesso, e `Falhas.ExecutarAsync`
  passou a usá-la em todo caso de uso que a chama. Era necessário porque o lote grava o item 1 (inclusive
  um `SalvarAlteracoesAsync`, pela baixa dos filhos) antes de validar o item 2; antes, a transação
  commitava também um `Result` de falha, o que só era inofensivo enquanto todo caso de uso validava
  tudo antes da primeira escrita. O comportamento das rotas de um nó não muda.
- **Travas (desvio D3).** Antes do primeiro item, no Iniciar o lote trava, numa chamada só de
  `TravarNosAsync`, os itens e os filhos de cada um, e depois as linhas de Pedido por
  `TravarPedidosDosNosAsync`: todo nó antes de todo Pedido, a mesma ordem da rota de um nó, pela
  ordem fixa da seção 8.1 da spec da Fase 3. No Terminar, trava só os itens, também numa chamada de
  `TravarNosAsync`, e nenhum Pedido.
- **Front.** Na fila do Setor, a caixa de marcar, a trava de seção, o "Marcar todos" por seção, o campo
  de quantidade, a barra do lote e a recusa tudo ou nada (fluxo 2, passo 9 de
  `04-fluxos-de-usuario.md`), com a lógica pura em `web/src/execucao/loteDaFila.ts` e a barra em
  `BarraDoLote`; nas Tarefas, o "Marcar todos".
- **Onde o plano decidiu além da spec, e vale como está:** a recusa nomeia o item também quando a
  frase da rota individual não o nomeava (desvio D5); com um item marcado os botões individuais somem
  da fila inteira (D7; a spec da seção 5.6 os mantinha); a linha que fica bloqueada depois de marcada
  continua marcada, com o motivo, e a que some da resposta sai da seleção com aviso (D8); toda recusa
  do lote, e não só o 409, recarrega a fila (D11); o botão da barra usa o verbo puro, sem a atividade
  do Setor (D12).

**Estado:** Plano 1 implementado, verificado no celular em 2026-09-30 e mergeado na `main` (PR #25).
Plano 2 implementado, **fase não concluída**: falta a verificação manual no celular, pelo usuário,
**pendente em 2026-09-30** — este arquivo não registra o Plano 2 como verificado. A posição da fase
não mudou: **filtros → 1F → 3B**.

**Emenda de 2026-10-02:** o Plano 2 foi verificado no celular pelo usuário em 2026-10-01, sobre
`894fde5`, o mesmo commit que o PR #26 levou à `main` (merge `1594c23`, 2026-10-01): os sete passos
do roteiro passaram, sem achado no produto. Um registro de bancada, e não do produto: numa das
redes Wi-Fi usadas o celular não alcançou a aplicação, e a verificação foi feita em outra rede; a
causa não foi medida. Com os dois planos verificados no celular e na `main`, a verificação manual
que a spec pede ao fim de cada plano (seção 9 dela) está feita, e a seção está **concluída em
2026-10-01**.

O PR #26 entrou com uma intermitência da suíte de backend declarada, achada ao fechar a branch do
Plano 2 e presente também na `main` de antes dele: `dotnet test Rastreamento.slnx -m:1` falhava às
vezes em `Api.Tests`, por deadlock entre classes de teste paralelas do mesmo assembly, e uma das
falhas foi um 500 em `POST /estrutura/{id}/filhos`. O conserto não é escopo dos filtros e veio logo
depois, no PR #27 (merge `75df075`, 2026-10-01): a paralelização entre classes de `Api.Tests` foi
desligada, com um teste-guarda, e a gravação da árvore (criar Peça e acrescentar filho) passou a
rodar na transação da execução, com o retry de deadlock dela e o 409 `ConflitoDeConcorrencia` quando
as tentativas se esgotam. A medição está no parágrafo «O `-m:1` não basta *dentro* de um assembly»
do `CLAUDE.md`, o desenho em `docs/superpowers/specs/2026-10-01-deadlock-na-suite-de-api-design.md`,
e o contrato do 409 em `05-api-endpoints.md`.

## Fase 1F — Cadastro sob demanda e ordenação das listas

> **Executada depois de "Filtros da demanda e ação em lote" e antes da Fase 3B** (ver a nota "Ordem
> depois da 3D", na seção «Fase 3D — Ajustes pós-verificação da Fase 3»). O nome `1x` marca a
> família — refinamento de interface, herdeira da 1D, que criou a prop `acao` da `Pagina`, e da 1E —,
> e a posição neste arquivo marca a execução (decisão 6 da spec). Spec:
> `docs/superpowers/specs/2026-09-06-fase-1f-cadastro-sob-demanda-design.md`, escrita em 2026-09-06 e
> revista em 2026-10-01 contra a `main` de então (`75df075`). Plano:
> `docs/superpowers/plans/2026-10-01-fase-1f-cadastro-sob-demanda.md`. **Sem mudança de schema**:
> "Mais recentes" é o `Id` decrescente (o `Id` é `IDENTITY`) e, em Pedidos, a `DataAbertura` que já
> existia; a fase não traz `db/alter-*.sql`.

Nasceu de um defeito visto usando a aplicação com o `admin`: nas quatro telas de lista e na tela do
Agrupamento, o formulário de cadastro era o primeiro filho da `Pagina`, logo abaixo do título, na
mesma moldura da barra de filtros de `ComponentesPage` — e se lia como **filtro**. Lá e em `PedidosPage`
ele ficava **acima** da busca e do filtro de verdade. Quem só lê não via o defeito: o formulário já
estava sob `usePodeEscrever`. A revisão de 2026-10-01 acrescentou dois blocos: a **ordenação
escolhida pelo usuário**, que nasceu da pergunta "o item recém-criado aparece depois de salvar?" —
com busca, filtros e paginação no servidor, não aparecia —, e o **cartão de lista inteiro
clicável**, decisão de 2026-09-28 na verificação manual da Fase 3, posta nesta fase.

- **Cadastro sob demanda.** A primitiva `PainelDeEscrita` (`web/src/components/`), extraída do painel
  que a `AgrupamentoDetalhePage` escrevia à mão desde a Fase 2: moldura, `<h2>` que dá nome ao
  `<form>` por `aria-labelledby`, subtítulo opcional, `Cancelar` — desabilitado enquanto a escrita
  está em voo, pela prop `enviando` — e foco no primeiro campo ao abrir. Ao fechar, se o foco caiu
  no `<body>`, ele volta ao botão que abriu o painel (menos no painel do nó, na tela do
  Agrupamento), pelo hook `useDevolverFoco` (`web/src/hooks/`), que a tela chama. Não é modal — não
  fecha no `Escape` e não prende o foco —, por decisão da spec. As cinco telas —
  `SetoresPage`, `MateriaisPage`, `ComponentesPage`, `PedidosPage` e `AgrupamentoDetalhePage` — abrem
  em leitura, com o botão da ação ("Novo setor", "Novo material", "Novo componente", "Novo pedido",
  "Nova Peça") no `acao` da `Pagina`, e o painel abre como primeiro elemento abaixo do cabeçalho.
  Salvar com sucesso fecha o painel; conflito (409) e falha de rede o mantêm aberto, com o erro
  dentro dele. Na tela do Agrupamento convivem dois painéis — "Nova Peça" e o do nó (editar e
  acrescentar sub-Item) —, e abrir um fecha o outro. O estado vazio das cinco telas deixou de dizer
  "Use o formulário acima" e passou a nomear o botão.
- **Ordenação.** A primitiva `SeletorDeOrdem` ("Ordenar por", com a direção dentro da opção:
  "Código (A→Z)") nas quatro telas de lista, com **"Mais recentes" como padrão** nas quatro. Em
  Componentes e Pedidos quem ordena é o servidor: `GET /componentes?ordem=recentes|codigo|descricao`
  e `GET /pedidos?ordem=recentes|numero|cliente`, com `recentes` quando o parâmetro falta, vem vazio
  ou só com espaços, e 400 para valor desconhecido (contrato em `05-api-endpoints.md`). Em Setores e
  Materiais quem ordena é o cliente, pela função `ordenarCadastro` (`web/src/cadastros/`), e
  `GET /setores` e `GET /materiais` não mudaram, porque alimentam outros seletores. Só em Pedidos a ordem vai para a URL, junto de
  busca, filtros e página.
- **Salvar devolve a consulta ao padrão** (decisão 7 da spec), para o item novo aparecer no topo:
  ordem "Mais recentes" e, onde a tela os tem, busca vazia, filtros limpos, página 1 e sem inativos.
  Nas duas telas paginadas isso é o `voltarAoInicio()` do `useBuscaPaginada`, que recarrega mesmo
  quando a consulta já estava no padrão; em Pedidos, a URL é limpa na mesma transição, e a lista faz
  uma requisição só.
- **A padrão de `GET /componentes` mudou** de `Codigo` crescente para `recentes` para **todo**
  consumidor, inclusive o `SeletorComBusca` da tela do Agrupamento e da receita padrão — escolha
  explícita do usuário (decisão 9 da spec), preferida a um parâmetro que só a tela usasse.
- **Cartão inteiro clicável** em `FilaPage`, `PedidoDetalhePage` e `ComponentesPage`, com o
  pseudo-elemento que a `LinhaDePedido` já usava. O conserto da armadilha documentada em
  `ListaDeCadastro` — o overlay do link engolia o clique no botão do item — foi feito uma vez, na
  primitiva: o `ItemDeCadastro` põe a `acao` num wrapper posicionado, sem índice de empilhamento
  próprio, que fica acima do overlay pela ordem do DOM e abaixo da lista aberta do
  `SeletorComBusca`.

**Onde o plano decidiu além da spec, e vale como está** (decisões do plano, confirmadas pelo usuário
em 2026-10-01):

- O botão do cabeçalho some enquanto o painel dele está aberto (D5); na tela do Agrupamento, o "Nova
  Peça" some só com o painel de Peça aberto, e com o do nó aberto clicá-lo troca de painel.
- "Editar" setor usa o mesmo painel, com título "Editar setor" e o nome atual no subtítulo; salvar a
  **edição** recarrega mantendo a ordem e o "Mostrar inativos", porque o item editado já estava na
  tela (D6).
- "Reativar o existente" com sucesso é desfecho de sucesso como o salvar: fecha o painel e devolve a
  consulta ao padrão (D7). O item reativado tem `Id` antigo, então "Mais recentes" **não** o põe no
  topo — consequência aceita.
- Erro de escrita mora dentro do painel; erro de carga e de Inativar/Reativar do item, fora (D8) —
  com o painel fechado, um erro de Inativar não teria onde aparecer.

**Decidido pelo usuário em 2026-10-01, depois da revisão da branch**, além da spec e do plano:

- O `Cancelar` fica desabilitado enquanto o salvar está em voo: `Cancelar` não cancela a requisição,
  e a resposta que chegasse depois fecharia um painel reaberto e apagaria o digitado.
- Ao fechar, o foco volta ao botão que abriu o painel: o do cabeçalho nas cinco telas e, na edição
  de setor, o "Editar" daquele setor. O painel do nó da tela do Agrupamento, anterior a esta fase,
  fica como estava e não devolve foco.

A implementação estendeu as duas regras: o `Cancelar` fica desabilitado também durante o "Reativar
o existente", nas três telas que o têm; o foco só é devolvido se tiver caído no `<body>`; e, na
edição de setor, quando o "Editar" não volta com a recarga, o foco vai ao botão do cabeçalho.

**Critério de pronto** (seção "Critério de pronto" da spec): as cinco telas abrem em leitura, sem
bloco de campos abaixo do título; `npm test` verde e `npm run build` limpo; `dotnet build
Rastreamento.slnx -warnaserror` com 0 avisos e `dotnet test Rastreamento.slnx -m:1` verde; um perfil
sem escrita continua sem botão e sem painel; e a **verificação no navegador**, a 375px e em desktop —
as cinco telas em leitura, o painel abrindo, cancelando e salvando, os dois painéis do Agrupamento um
de cada vez, a ordenação nas quatro telas com o item recém-criado no topo depois de salvar, e o
cartão clicável nas três telas, onde clicar no **centro** do botão executa a ação e clicar no resto
do cartão navega. Este último item não tem prova na suíte: o jsdom não calcula layout.

**Fora de escopo** (seção "Fora de escopo" da spec): o painel sob demanda em `PedidoDetalhePage` e
`ComponenteDetalhePage`, onde o formulário já vive numa seção com `<h2>` (o cartão clicável de
`PedidoDetalhePage` está dentro do escopo); ordenação na Fila do Setor e nas Tarefas, onde a ordem
serve ao trabalho do chão de fábrica; busca e página de Componentes na URL; e uma primitiva de
mensagem de sucesso — o fechamento do painel é o sinal de sucesso.

**Estado em 2026-10-01:** implementada na branch `fase-1f-cadastro-sob-demanda`, **fase não
concluída** — falta a verificação no navegador do critério de pronto, que este arquivo **não**
registra como feita. Suítes na árvore de `6849190`: front **1109 testes / 67 arquivos**, verde
(`npm test -- --run`), contra 1011 / 64 na base da fase; backend **979** testes (Api 310 ·
Application 511 · Infrastructure 158), contra 945 na base — a contagem é de `dotnet test
--list-tests`, que descobre os testes sem executá-los, e a execução verde foi medida depois da task
de backend, em `49c2ce0`; `src/` e `tests/` não mudaram desde então.

**Estado em 2026-10-02, antes da verificação:** implementada na branch `fase-1f-cadastro-sob-demanda`,
em `e40f95d`; **não** mergeada na `main`, e sem PR aberto. A **verificação no navegador** do critério
de pronto continua **pendente**, e será feita numa sessão local; este arquivo não a registra como
feita, e a fase continua **não concluída**. Depois da medição de 2026-10-01, a branch recebeu um
conserto de front — o `Cancelar` desabilitado com a escrita em voo, o foco devolvido ao fechar o
painel e o wrapper da `acao` do `ItemDeCadastro` sem índice de empilhamento próprio — e a
atualização de documentação de `e40f95d`. Suítes: front **1139 testes / 68 arquivos**, verde
(`npm test -- --run`), medido em `562fad1`, depois do conserto, e de novo em 2026-10-02 na árvore
de `e40f95d`, que só muda documentação e um comentário de teste; backend **979** testes (Api 310 ·
Application 511 · Infrastructure 158), com execução verde de `dotnet test Rastreamento.slnx -m:1` em
`547a514`; `src/` e `tests/` não mudaram desde `49c2ce0`.

**Estado em 2026-10-02, depois da verificação:** o usuário conferiu, numa sessão local, sobre
`62efe30`, o checklist da **verificação no navegador** do critério de pronto e declarou a fase
validada, sem apontar defeito. `62efe30` é a passada de conserto de documentação que veio depois
de `e40f95d`, sem mudança de código: `src/`, `tests/` e `web/src/` não diferem entre os dois
commits. O checklist, pedido a 375px e em desktop, reunia os oito itens do plano e nove que a
revisão da branch acrescentou, entre eles: as cinco telas em leitura; o painel abrindo, cancelando
e salvando, com o foco devolvido a quem o abriu; a edição de setor no painel; o conflito com
"Reativar o existente" dentro do painel; os dois painéis do Agrupamento um de cada vez; a ordenação
nas quatro telas, com o item recém-criado no topo; a ordem de Pedidos preservada no F5 e no Voltar;
o cartão clicável nas três telas, com o centro do botão executando a ação sem navegar; o `Cancelar`
travado com a rede lenta; o perfil sem escrita sem botão nem painel; e, no detalhe do Componente, a
lista aberta do "Componente filho" por cima dos botões "Remover…". A resposta veio em bloco: este
arquivo registra o que o checklist cobria, não um resultado observado item por item. Abrir o "Nova
Peça" põe o foco no `SeletorComBusca`, que abre a lista sozinho; o usuário decidiu manter assim.
Com isso a fase está **concluída** na branch; o merge na `main` vem pelo PR. Suítes de novo em
2026-10-02, na árvore de `62efe30`: front **1139 / 68**, verde, e `npm run build` limpo;
`dotnet build Rastreamento.slnx -warnaserror` com 0 avisos e `dotnet test Rastreamento.slnx -m:1`
verde, **979** (Api 310 · Application 511 · Infrastructure 158).

**Estado em 2026-10-02, depois do merge:** **concluída e mesclada na `main`** pelo PR
rufino-bot/rastru#28 (merge `e5fdc81`), cuja árvore é idêntica à de `0d2d38f`, o último commit da
branch. A spec e o plano da fase estão na `main` desde esse merge, e a branch
`fase-1f-cadastro-sob-demanda` foi apagada.

## Data de entrega do Pedido (decidido em 2026-10-08; executa antes da 3B)

> **Executa antes da Fase 3B**, por decisão do usuário de 2026-10-08: não depende de Kit nem de
> montagem, e por isso é item próprio, em branch própria, e não emenda da 3B. Spec:
> `docs/superpowers/specs/2026-10-08-data-de-entrega-do-pedido-design.md`. É o candidato que a §5 da
> spec da Fase 1E registrou ("prazo de entrega" e "pedidos em atraso").

- `Pedido.DataEntrega` (`DATE NOT NULL`), informada no cadastro e editável em qualquer status, com
  qualquer data aceita.
- Edição de número, cliente e data de entrega na página do Pedido (até aqui o `PUT /pedidos/{id}` só
  existia no backend).
- Pílula **Atrasado** num tom próprio (roxo, o quarto estado reservado) na lista de Pedidos, na Home e
  na página do Pedido. O atraso é decidido no backend, com o "hoje" de Brasília.
- A lista de Pedidos abre ordenada por **prazo de entrega**, e a seção da Home vira **"Prazos de
  entrega"** (até cinco, os não encerrados mais urgentes).
- **Fica para um item seguinte:** a pílula na Fila do Setor e nas Tarefas. **Fica para uma spec
  própria:** a auditoria de edição de cadastro, que não existe hoje (seção 8 da spec).
- Critério de pronto: a seção 1 da spec.

**Estado em 2026-10-09, depois do merge:** **concluída e mesclada na `main`** pelo PR
rufino-bot/rastru#32 (merge `7074559`), cuja árvore é idêntica à de `bf0882d`, o último commit da
branch. Os itens deixados para depois continuam como a spec os registra, nas seções 8 e 10, entre
eles a pílula na Fila do Setor e nas Tarefas e a auditoria de edição de cadastro.

**Conferência no navegador e risco aceito:** o usuário conferiu a pílula, a ordem, a Home, a lista com
e sem o filtro de status, criar e editar (inclusive o número duplicado, o 409) e o perfil sem escrita.
O seletor de data nativo do Android **não foi conferido**: o celular não alcançou o PC, e na emulação de
celular do Chrome de PC o seletor não abriu com o toque emulado ligado e, desligado, abriu o do próprio
navegador. Que ele funciona no aparelho é inferência do usuário, e o risco foi aceito.

## Fase 3B — Kit e montagem

**Implementada na branch `fase-3b`**, ainda não mesclada. Spec:
`docs/superpowers/specs/2026-10-09-fase-3b-kit-e-montagem-design.md`.

> **Antes dela, o import da estrutura a partir do CAD** (decisão do usuário de 2026-10-02; ver a seção
> «Import da estrutura a partir do CAD») **e a data de entrega do Pedido** (decisão de 2026-10-08; ver
> a seção «Data de entrega do Pedido»). A ordem fica: filtros → 1F → **import do BOM** → **data de
> entrega** → 3B. O import foi concluído e mesclado em 2026-10-08, e a data de entrega em
> 2026-10-09; a 3B veio em seguida.

- **Título da página do Agrupamento com o Pedido** (pedido do usuário na conferência do import, em
  2026-10-08, emendado nesta fase por decisão dele, porque a 3B mexe no Agrupamento): o título passa a
  "Pedido X — {código do Agrupamento} — {Avulso|Kit}", com "Pedido X" como **link** para a página do
  Pedido. O número vem na resposta de `GET /agrupamentos/{id}` (campo novo, com
  `05-api-endpoints.md` atualizado), não numa segunda requisição do front. Motivo dele: os
  Agrupamentos devem ganhar códigos de lote interno da empresa, e é preciso ver qual Pedido está
  sendo alterado.

- `Setor.UtilizaKit` (regra 25 de `01`); a coluna entrou no schema no início desta fase
  (`db/alter-fase-3b.sql` para banco anterior). A montagem, o destino "montado" e `QuantidadePorPai`
  já existem desde a Fase 3, e o início de um nó com filhos consome os filhos desde a 3D.
- **A trava de montagem, em regra, já não é desta fase:** desde a **Fase 3D** ela é **estrutural**
  para todo nó, Kit ou Avulso, **que já tem filhos quando entra em produção** — o pai só entra em
  produção consumindo os filhos, então tudo o que ele termina, entrega ou leva à expedição já foi
  montado. **Fica de fora um caso, devolvido à 3B por decisão do usuário de 2026-09-29:** o filho
  acrescentado a um nó **já iniciado**, que era livre (seção 4.7 da spec da Fase 3). A 3B fica com
  `Setor.UtilizaKit`, o conjunto completo (regra 25), a tarefa Kit pronto (regra 23) e esse caso,
  que ela resolveu recusando: acrescentar filho a nó já iniciado devolve 409 `PaiJaIniciado` (D1 da
  spec da Fase 3B), e a saída limitada ao total montado vale sem exceção.
- Conjunto completo na entrada de Setor com `UtilizaKit`, sem passar do que o nó ainda precisa
  receber (regra 25).
- Tarefa **Kit pronto para montagem** na tela Tarefas (regra 23).
- A decidir na spec desta fase: as perguntas que a seção 9 da spec
  `2026-09-15-kit-montagem-e-movimentacao-design.md` ("Deixado para a spec de cada fase") deixa
  para a Fase 3B. Decididas na spec da Fase 3B; a errata de 2026-10-09 daquela spec fecha cada uma.
- Critério de pronto: um Kit de três níveis é montado de baixo para cima com montagem parcial; o
  sistema recusa conjunto incompleto, entrada além do que o nó precisa receber e o filho
  acrescentado a nó já iniciado; a tarefa Kit pronto aparece e some quando o Kit é levado.

## Fase 3C — Notificação push

> **Executada depois da Fase 5**, fora da ordem das letras: o fluxo ponta a ponta vem primeiro, e o
> push é reforço de uma lista que já funciona (a tela Tarefas). Fica numerada como 3C por tema. A
> posição em relação à **Fase 6** não está decidida.

- PWA **mínimo**: manifesto, ícones e service worker **sem cache de API** — não reabre a decisão
  "PWA/offline" de `03-arquitetura-tecnica.md`.
- Tabela de inscrição (usuário, endpoint, chaves do navegador), inscrever e cancelar, chaves VAPID
  como segredo de ambiente, envio e limpeza de inscrição morta.
- Disparo por evento no servidor: ao registrar "terminei", recalcular se formou conjunto completo
  que o nó ainda precisa receber (regras 23 e 25) e notificar os Movimentadores. Tocar na
  notificação abre a tela Tarefas, que continua sendo a fonte da verdade; entrega de push não é
  garantida.
- Verificação manual num Android real por HTTPS (service worker não roda no jsdom).
- Critério de pronto: um Movimentador com o celular bloqueado recebe o aviso de Kit pronto, e tocar
  nele abre a tela Tarefas.

## Fase 4 — Separação de materiais

- Registro de `MaterialSeparacao` vinculado a um `EstruturaItem`.
- **Onde cada material fica** — no almoxarifado, ou estocado num Setor (as chapas no Corte) —, e a
  **requisição de material** que o operador abre para o que falta e o Almoxarifado atende (decisão
  de 2026-09-24, no brainstorm da Fase 3).
- Critério de pronto: dá para saber quais materiais já foram separados/entregues para
  cada item em fabricação.

## Fase 5 — Dimensional e fechamento

- Registro opcional de `RelatorioDimensional` por Peça (perfil Qualidade), avaliado por
  quantidade (`RelatorioDimensionalAvaliacao`).
- Registro de `Expedicao` (remessas parciais), só de Peça — a expedição baixa do **local de
  expedição** (regra 29 de `01`) —, e de `Perda`, de Peça e — desde 2026-09-15 — também de Item; a
  perda ganha o motivo `Descarte` (regra 17 de `01`).
- O nó **pronto** do Retrabalho (regra 27) nasce aguardando coleta pelo tipo de movimento `Pronto`;
  `Expedido` e `Perdido` entram como posições do livro de movimentações; o `Descarte` da sobra é
  registrado pelo ator da perda (regra 30).
- **Descartar Peça ou Item em Pedido rodando** (decidido para esta fase em 2026-09-25): parar de
  produzir o que saiu do projeto do cliente, pelo mesmo ator e pelo mesmo destino do `Descarte` da
  sobra. Fecha o ponto em aberto "Descontinuar uma Peça trava o fechamento do Pedido", do `01`, e
  o filho acrescentado por engano que trava a montagem do pai (spec da Fase 3, seção 11).
  **Obrigatório antes do primeiro uso real.**
- Perda que impede montar (regra 27): a perda sobe até a Peça do topo, as partes não montadas daquela
  unidade saem junto, e, no Pedido de Retrabalho que o PCP cadastra para repor, o que já existe é
  marcado **pronto**. A decidir na spec desta fase: as perguntas que a seção 9 da spec
  `2026-09-15-kit-montagem-e-movimentacao-design.md` deixa para a Fase 5.
- **A perda nos tetos do Kit** (D5 da spec da Fase 3B): como a perda do próprio nó entra nos dois
  tetos, o que o nó ainda precisa receber (regras 23 e 25) e o que ainda falta montar (regra 24). A
  3B conta os conjuntos à espera pelo filho mais adiantado, e um conjunto que perde parte dentro da
  Solda continua contado. A **espera desalinhada** (a limitação conhecida da regra 25 de `01`; nota
  da D4 da mesma spec) é o mesmo mecanismo, e é tratada aqui junto: com ela, o teto do Kit pode
  ficar em zero, e o Kit, travado. Ela pode nascer, por exemplo, de trocar o Tipo do Agrupamento de
  Avulso para Kit, ou marcar `UtilizaKit` num Setor, com Kit em produção; de estornar uma linha só
  de uma entrega de Kit, porque o estorno é feito linha a linha e não confere o conjunto; e de
  editar a `QuantidadePorPai` de um filho que tem conjuntos à espera.
- **Estrutura alterada no meio da produção** (D1 da spec da Fase 3B): hoje acrescentar filho a nó
  já iniciado é recusado (`PaiJaIniciado`), e essa guarda é a resposta da 3B, não a definitiva. O
  usuário tem uma ideia de como tratar o caso, a revisitar aqui: estrutura errada descoberta no meio
  da produção é o mesmo assunto de alterar projeto ou descontinuar, o do item «Descartar Peça ou
  Item em Pedido rodando».
- Regra de fechamento de Agrupamento (todas as Peças concluídas — expedidas ou
  perdidas) e de Pedido (último Agrupamento concluído).
- Fluxo de abertura de Pedido de Retrabalho como ação **separada e opcional** a partir
  de uma reprovação **ou de uma perda** (regras 17 e 27 de `01`), com `MotivoRetrabalho` obrigatório
  (`ReprovacaoDimensional`/`ErroInterno`/`SolicitacaoCliente`/`Perda`).
- Critério de pronto: fluxo ponta a ponta funcionando — cadastro → produção →
  expedição/perda → aprovação/reprovação → (se aplicável, e só quando o usuário decidir)
  retrabalho.

## Fase 6 — KPIs

- Endpoint e tela de tempo médio por setor, calculado sobre o livro de movimentações
  (`dbo.Movimentacao`), pareando entradas e saídas de cada Setor por ordem de chegada. Separar fila
  de execução dentro do Setor exigiria uma posição a mais no livro — decidir nesta fase, se o
  negócio pedir.
- Endpoint e tela de tempo total/fila/produção por pedido.
- Perfil Gestão tem acesso a essas telas; demais perfis não.

## Fora das fases — decidir por spike: busca de peça por foto

**Problema real que motiva:** a fábrica não etiqueta peça. O operador tem a peça na mão e
não sabe qual é nem de que Pedido — e o `Codigo` é obrigatório só do lado do sistema.

**O que torna isso viável e não era verdade antes:** o sólido 3D é obrigatório (regra 18),
então a geometria de referência já existe, é exata e não precisa ser fotografada por
ninguém. A comparação é silhueta contra silhueta, não aprendizado de máquina — descritores
de forma clássicos (momentos de Hu/Zernike, descritores de Fourier), sem treino.

**Forma proposta:** o custo fica no cadastro (renderizar N silhuetas do sólido e guardar um
descritor por vista); na consulta é só comparar números. Nenhuma biblioteca de CAD em
produção.

**Escopo proposto — ordenador, não identificador.** A foto **reordena a lista curta do
setor do operador** (a consulta da Fase 3), não busca no catálogo inteiro. Isso troca um
problema de 1-em-milhares por um de 1-em-dez, e o Pedido vem da lista, não da foto.
Não exibir "% de certeza": o score é *ranking*, não confiança, e exibido como confiança
convida o operador a não conferir.

**Limites que sobrevivem, mesmo dando certo:**
- escala é ambígua entre variantes dimensionais proporcionais, sem referência de tamanho na foto;
- peça espelhada (suporte esquerdo/direito) tem a mesma silhueta;
- a peça muda de forma ao longo do processo (blank plano → dobrado → soldado);
- geometria **não** identifica o Pedido — essa informação não está na peça.

**Critério para virar fase:** um spike de 1–2 dias sobre ~20 peças reais com seus sólidos,
fotografadas na condição de captura pretendida (fundo claro, maior superfície de contato à
mostra), medindo a **taxa de acerto no top-3 dentro de uma lista de setor**. É resultado
empírico — nenhuma análise substitui esse número. Só entra no roadmap depois dele.

## Import da estrutura a partir do CAD (decidido em 2026-08-04; executa antes da 3B)

> **Executa antes da Fase 3B**, por decisão do usuário de 2026-10-02: a 3B mexe em montagem e Kit, e o
> import não depende dela. Spec: `docs/superpowers/specs/2026-10-02-import-de-estrutura-do-bom-design.md`,
> que detalha o fluxo (o BOM alimenta o Agrupamento **e** o catálogo, a conferência é um rascunho salvo no
> servidor). Até 2026-10-02 esta seção se chamava «Fora das fases — importar a estrutura a partir do
> CAD», e é por esse nome que planos e specs anteriores a citam. O texto abaixo é o de 2026-08-04, que
> continua valendo, menos o parágrafo do schema, revisto na mesma data.

**Problema que motiva:** hoje a árvore de uma Peça é digitada à mão, item por item — e ela já
existe, pronta e correta, dentro do arquivo de montagem do CAD.

**Decidido: o insumo é o BOM indentado exportado (CSV/XLSX), não o `.SLDASM`.** Palavras do
usuário: *"se o Solid consegue exportar o BOM, já nos atende"*.

Por que **não** ler `.SLDASM` direto: é formato proprietário e não documentado da Dassault (OLE
compound file com estruturas fechadas), sem biblioteca aberta confiável. As únicas saídas seriam a
**SolidWorks Document Manager API** (exige chave de licença da Dassault) ou a **API COM do
SolidWorks** (exige SolidWorks instalado e licenciado na máquina do servidor — inviável para uma API
web hospedada numa VPS, mais ainda do que seria num servidor on-premise: não há máquina servidora
da empresa para instalar o SolidWorks, e licenciar a ferramenta só para rodar numa VPS de terceiros
não se sustenta). É a mesma razão pela qual `Componente.ArquivoSolidoId` já é **STL, e só, não
`.SLDPRT`** (ver `02-modelo-de-dados.sql`); a regra vale um nível acima, para a montagem.

O BOM indentado carrega **nível de indentação, part number, descrição e quantidade** — que é
literalmente a forma de `ComponenteFilhoPadrao` (pai → filho + `QuantidadePadrao`) e de
`EstruturaItem` (árvore recursiva com quantidade). Parser trivial em C#, sem dependência
proprietária.

Alternativa descartada por agora, não por ser ruim: **STEP AP242/AP214 da montagem** — formato ISO
aberto, mas que com STL-only (ver glossário em `01`, `Componente.ArquivoSolidoId`) deixou de ser o
mesmo arquivo que já serve ao sólido; o parser também é bem mais pesado e o STEP costuma trazer
**nome de arquivo** em vez de part number, o que piora a conferência.

**Conciliação part number ↔ `Componente.Codigo`: resolvida como regra de negócio.** O sistema não
modela a numeração do cliente (ver glossário em `01`), então o código do CAD pode não ser o `Codigo`
do catálogo. **Quem importa confere as peças depois — código e quantidade.** O usuário assumiu
explicitamente que isso exige uma disciplina de cadastro que a empresa hoje não tem por padrão, e
que parte do esforço precisa partir do lado do cliente: é **pré-requisito declarado de uso da
ferramenta**, não um risco em aberto.

**Consequência que barateia a fase:** como a conferência humana é obrigatória de qualquer modo, o
import **não precisa acertar tudo**. Ele é pré-preenchimento, não automação — casamento parcial já
entrega valor, e a fase não carrega o requisito de resolver o caso ambíguo.

**Forma:** o import gera uma **proposta** que um humano confere e confirma; nunca gravação direta.

**Schema: o destino não muda, a área de trabalho é nova.** As tabelas de `Componente`/receita padrão
(Fases 1B e 1C) e `EstruturaItem` (Fase 2) continuam sendo o destino do import, sem mudança. O texto de
2026-08-04 dizia "não muda o schema"; o brainstorm de 2026-10-02 achou a necessidade de três tabelas
de rascunho (a conferência leva horas ou dias e precisa sobreviver a F5), e o usuário decidiu que isso
não é exceção a uma regra, e sim a regra geral do cabeçalho deste arquivo.

**Efeito único sobre a Fase 1C:** o caso de uso que grava a receita padrão deve aceitar **uma lista
de linhas de uma vez**, e não só uma linha por chamada. É quase de graça agora e evita reescrever o
caso de uso quando o import chegar; a tela continua digitando linha a linha.

**Estado em 2026-10-08, depois do merge:** **concluído e mesclado na `main`** pelo PR
rufino-bot/rastru#31 (merge `b555d92`), cuja árvore é idêntica à de `ebbf5d9`, o último commit da
branch. O usuário conferiu o fluxo inteiro no navegador numa sessão local, em 2026-10-08, e pediu
cinco ajustes de tela, que entraram na própria branch antes do merge. Suítes medidas na mesma data:
front **1330 / 76** em `ebbf5d9`, verde, e `npm run build` limpo; backend em `b43e8c6` (sem mudança
de backend depois dele), `dotnet build Rastreamento.slnx -warnaserror` com 0 avisos e
`dotnet test Rastreamento.slnx -m:1` verde, **1241** (Api 345 · Application 685 · Infrastructure 211).
O BOM de uma montagem real **ainda não foi testado**: as suposições sobre o formato do arquivo
continuam as da seção 10 da spec, com as emendas que a implementação fez nela (seção 13).

## Fora das fases — dívida: CRUD de Usuário e permissão por Perfil (registrada em 2026-09-15)

`GET/POST /usuarios` consta em `05-api-endpoints.md`, mas não tem implementação, e nenhuma fase do
roadmap o implementa: hoje um usuário só nasce por SQL. São **duas dívidas de custo diferente**, sem
fase e sem data, por decisão do usuário:

- **CRUD de Usuário** (criar conta, ativar, atribuir perfil existente) — **barata**. Fica mais urgente
  com a Fase 3: até existir, cada conta de Movimentador nasce por SQL na VPS.
- **Permissão por Perfil fora do código** (o mapeamento perfil → ação sair dos `[Authorize(Roles)]`
  literais) — **cara**; é ela que faz perfil novo exigir código e deploy.
