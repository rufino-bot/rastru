# 06 - Roadmap / MVP

Fases pensadas para serem executadas em sequência com Claude Code — cada fase pode virar
uma sessão de agent com escopo fechado, referenciando os arquivos desta pasta como
contexto. Recomenda-se não avançar de fase sem os "pontos em aberto" da fase anterior
resolvidos (ou conscientemente adiados).

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

## Fase 2 — Estrutura recursiva

- Criar `EstruturaItem` a partir de um `Componente` padrão (copiar receita) ou do zero
  (customizado).
- Visualização em árvore da estrutura de um Agrupamento (Peça → Itens → sub-Itens).
- Upload e exibição de `Componente.ArquivoSolido` (sólido 3D) e da regra de negócio que o
  exige por Peça de Pedido — regra 18 de `01`. Inclui `EstruturaItem.Descricao` (regra 19).
- **Peça sempre referencia um `Componente`** — decidido em 2026-08-04, adiado de propósito
  para esta fase, que é onde o `EstruturaItem` nasce de fato. Acrescentar ao DDL:
  ```sql
  CONSTRAINT CK_EstruturaItem_PecaTemComponente
      CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL)
  ```
  Só um **Item** (nó com pai) pode ser ad-hoc. Sem isso, o sólido — que mora em `Componente` —
  não tem onde ser pendurado numa Peça ad-hoc, e a regra 18 fica inexprimível para ela. A
  motivação completa e as alternativas descartadas estão na regra 18 de `01`; não re-decidir
  a partir do zero. A constraint garante o **gancho**; exigir o arquivo preenchido continua
  sendo validação de aplicação (um `CHECK` não alcança outra tabela).
- Como as colunas nasceram depois do banco de dev, aplicar os `ALTER` idempotentes de
  `ArquivoSolido`/`ArquivoFoto`/`Descricao` ao iniciar a fase, no mesmo padrão dos demais
  em `CLAUDE.md`.
  **Não se aplica a um banco regenerado:** o banco de dev foi recriado em 2026-08-04 a partir
  deste `.sql`, então as colunas já vieram no `CREATE`. Vale só para instalação anterior a essa
  data.
- Critério de pronto: dá para montar visualmente a árvore completa de uma Peça complexa.

## Fase 3 — Rastreamento de setor

- Apontamento de entrada/saída de `EstruturaItem` em `Setor`.
- Validação de conservação de quantidade (soma em setores + expedido + perdido = total
  da Peça; na aplicação, não por índice filtrado).
- Tela de "fila do setor" para o operador.
- Critério de pronto: dá para acompanhar, item por item, em qual setor cada peça está.

## Fase 4 — Separação de materiais

- Registro de `MaterialSeparacao` vinculado a um `EstruturaItem`.
- Critério de pronto: dá para saber quais materiais já foram separados/entregues para
  cada item em fabricação.

## Fase 5 — Dimensional e fechamento

- Registro opcional de `RelatorioDimensional` por Peça (perfil Qualidade), avaliado por
  quantidade (`RelatorioDimensionalAvaliacao`).
- Registro de `Expedicao` (remessas parciais) e de `Perda` por Peça.
- Regra de fechamento de Agrupamento (todas as Peças concluídas — expedidas ou
  perdidas) e de Pedido (último Agrupamento concluído).
- Fluxo de abertura de Pedido de Retrabalho como ação **separada e opcional** a partir
  de uma reprovação, com `MotivoRetrabalho` obrigatório
  (`ReprovacaoDimensional`/`ErroInterno`/`SolicitacaoCliente`/`Perda`).
- Critério de pronto: fluxo ponta a ponta funcionando — cadastro → produção →
  expedição/perda → aprovação/reprovação → (se aplicável, e só quando o usuário decidir)
  retrabalho.

## Fase 6 — KPIs

- Endpoint e tela de tempo médio por setor, calculado a partir de `DataEntrada`
  (chegada) até `DataSaida`. `DataInicioExecucao` já existe no schema e pode ser
  adotado depois, sem migração, caso o negócio queira decompor fila x execução.
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

## Fora das fases — importar a estrutura a partir do CAD (decidido em 2026-08-04)

**Problema que motiva:** hoje a árvore de uma Peça é digitada à mão, item por item — e ela já
existe, pronta e correta, dentro do arquivo de montagem do CAD.

**Decidido: o insumo é o BOM indentado exportado (CSV/XLSX), não o `.SLDASM`.** Palavras do
usuário: *"se o Solid consegue exportar o BOM, já nos atende"*.

Por que **não** ler `.SLDASM` direto: é formato proprietário e não documentado da Dassault (OLE
compound file com estruturas fechadas), sem biblioteca aberta confiável. As únicas saídas seriam a
**SolidWorks Document Manager API** (exige chave de licença da Dassault) ou a **API COM do
SolidWorks** (exige SolidWorks instalado e licenciado na máquina do servidor — inviável para uma API
web on-premise). É a mesma razão pela qual `Componente.ArquivoSolido` já é **STEP ou STL, não
`.SLDPRT`** (ver `02-modelo-de-dados.sql`); a regra vale um nível acima, para a montagem.

O BOM indentado carrega **nível de indentação, part number, descrição e quantidade** — que é
literalmente a forma de `ComponenteFilhoPadrao` (pai → filho + `QuantidadePadrao`) e de
`EstruturaItem` (árvore recursiva com quantidade). Parser trivial em C#, sem dependência
proprietária.

Alternativa descartada por agora, não por ser ruim: **STEP AP242/AP214 da montagem** — formato ISO
aberto, seria o mesmo arquivo que já serve ao sólido, mas o parser é bem mais pesado e o STEP
costuma trazer **nome de arquivo** em vez de part number, o que piora a conferência.

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

**Não muda o schema.** As tabelas de `Componente`/receita padrão (Fases 1B e 1C) e `EstruturaItem`
(Fase 2) já são o destino do import.

**Efeito único sobre a Fase 1C:** o caso de uso que grava a receita padrão deve aceitar **uma lista
de linhas de uma vez**, e não só uma linha por chamada. É quase de graça agora e evita reescrever o
caso de uso quando o import chegar; a tela continua digitando linha a linha.
