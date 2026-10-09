# 05 - Rascunho de Endpoints da API (REST)

Convenção: recursos no plural, verbos HTTP padrão. Ajustar nomes conforme convenção
de time, se houver uma já estabelecida.

## Prefixo: todas as rotas abaixo são servidas sob `/api`

Os caminhos deste documento são escritos **sem** o prefixo por legibilidade, mas a URL real é
`/api` + o caminho listado — `POST /pedidos` é `POST /api/pedidos`. O front nunca escreve o
prefixo à mão: quem o aplica é o `rota()` de `web/src/api/client.ts`, num lugar só.

**Por que existe:** as rotas do SPA (`/setores`, `/materiais`, `/pedidos`, `/pedidos/:id`) têm os
mesmos caminhos dos endpoints. Sem prefixo, dar F5 numa dessas telas faz o navegador pedir o
**documento** naquela URL e a API responde 401, porque navegação de documento não carrega
`Authorization: Bearer`. Isso aconteceu de verdade no e2e da Fase 1A. Mesma origem, aliás, não é
escolha livre: o cookie de refresh é `SameSite=Strict`, que bloqueia cross-site.

**Fechado em 2026-08-04.** O prefixo entra por `UsePathBase`, que retira `/api` quando presente e
deixaria passar quando ausente — sozinho ele faria a API responder também nos caminhos nus. Por
isso há uma guarda **logo antes** dele: requisição cujo `Request.Path` não começa por `/api`
(comparação ordinal) recebe **404**. A guarda testa `Path`, não `PathBase` — sob sub-application
do IIS o host já entrega `PathBase` preenchido em toda requisição, e testar `PathBase` deixaria
`/setores` passar sem prefixo nenhum; ler o `Path` antes do `UsePathBase` tirar o prefixo dele
cobre esse caso. Os caminhos nus (`/setores`, `/auth/login`, `/me`) **não respondem**, e é isso que
fecha a colisão com as rotas do SPA. Provado por `AuthEndpointsTests.PrefixoDeApi.cs`.

Restrição de ordem que decorre disso: se o SPA vier a ser servido como estáticos pela própria API
(`UseStaticFiles` / `MapFallbackToFile` — ver hospedagem, abaixo), o registro deles precisa vir
**antes** da guarda, senão ela devolve 404 para `index.html`, os assets e toda rota do SPA.

O `Path` do cookie de refresh é derivado do `PathBase` (`/api/auth`), e não literal: o cookie
precisa ser gravado sob o mesmo prefixo em que `/auth/refresh` atende, senão o navegador não o
reenvia e a sessão morre no primeiro refresh.

## Autenticação e Usuários

- `POST /auth/login` — usuário/senha → retorna JWT. Falha sempre em **401** genérico (usuário
  inexistente, inativo, senha errada e conta trancada são indistinguíveis). Excesso de tentativas
  do mesmo IP → **429** com `Retry-After`.
- `POST /auth/refresh` — troca o refresh token (cookie httpOnly) por um par novo de tokens.
  Deliberadamente **fora** do rate limit do login: é legítimo e frequente (a cada ~15 min por
  usuário, mais retries), e o refresh token é opaco de 256 bits, então força bruta nele é inviável
  (ver `CLAUDE.md`, seção de defesas de autenticação). Consequência que essa isenção carrega, ainda
  **em aberto e pendente de decisão** (não resolvida por este documento): é a premissa que
  permitiria a quem roubasse um refresh token válido rodar a rotação em loop sem limite de taxa.
- `GET/POST /usuarios` *(Administrador)*
- `GET/POST /perfis` *(Administrador)*

## Catálogo

- `GET /setores` — `?incluirInativos=false` por padrão *(qualquer perfil autenticado)*. Cada item
  ganha `atividade` (spec da Fase 3D, §2.3): substantivo que nomeia os botões da fila
  ("montagem" → "Iniciar montagem"/"Terminar montagem"); `null` quando o Setor não tem uma. Cada
  item ganha também `utilizaKit` (booleano, Fase 3B): marca o Setor onde o Kit é montado (regra 25).
- `POST /setores` *(Administrador)* — `{ nome, atividade?, utilizaKit? }`. `atividade` é opcional: ausente ou
  só espaços grava `null`; espaços são aparados, mas o texto não é forçado a minúscula (desvio D5
  do plano da Fase 3D — siglas como "CNC" são legítimas). `utilizaKit` é opcional e o padrão é falso
  (marca o Setor onde o Kit é montado, regra 25). A resposta ganha `atividade` e `utilizaKit`.
- `PUT /setores/{id}` *(Administrador)* — `{ nome, atividade?, utilizaKit? }`; substituição inteira — sem
  `atividade` no corpo, ela é limpa, e sem `utilizaKit` ele volta a falso. A resposta ganha `atividade` e
  `utilizaKit`.
- `PATCH /setores/{id}/ativo` *(Administrador)* — `{ ativo }`; cobre inativar **e** reativar.
  Não existe `DELETE`: catálogo se inativa, não se exclui (ver a política de exclusão na spec da
  Fase 1).
  **`ativo` é obrigatório nos três `PATCH /{recurso}/{id}/ativo`** (`Setor`, `Material`,
  `Componente` — os três compartilham o mesmo `DefinirAtivoDto`): corpo `{}` responde **400**, não
  204. Até 2026-08-05 o campo era `bool` não-anulável sem `[Required]`, então corpo sem `ativo`
  vinculava `false` e **inativava a linha em silêncio** — o oposto de "catálogo se inativa, não se
  exclui", que só faz sentido se a inativação for sempre pedida explicitamente
- `GET /materiais` — `?incluirInativos=false` por padrão *(qualquer perfil autenticado)*
- `POST /materiais` *(Administrador)* — `{ codigo, descricao, unidadeMedida }`
- `PUT /materiais/{id}` *(Administrador)* — idem
- `PATCH /materiais/{id}/ativo` *(Administrador)* — `{ ativo }`
- `GET /componentes` — `?busca=` (casa em código **ou** descrição), `?incluirInativos=false`,
  `?ordem=recentes|codigo|descricao`, `?pagina=1`, `?tamanho=20` (teto 100) *(qualquer perfil
  autenticado)*. Responde `{ itens, total, pagina, tamanho }`; `total` é contado com os mesmos
  filtros da página. Faixa fora do permitido responde 400; página além do fim responde 200 com
  `itens` vazio.
  - `ordem`: `recentes` (a padrão — também quando ausente, vazia ou só espaços) é `Id`
    decrescente; `codigo` é `Codigo` crescente (único); `descricao` é `Descricao` crescente e, no
    empate, `Id` decrescente. Toda opção termina em ordem total, o que faz `Skip/Take` não repetir
    nem pular linha entre páginas. A comparação é ordinal: `Codigo` (com maiúscula) é valor
    desconhecido e responde **400** `{ "erro": "Ordem 'Codigo' desconhecida. Aceitas: recentes,
    codigo, descricao." }` — a faixa inválida é validada antes e ganha da ordem inválida.
  - **A padrão é `recentes` desde a Fase 1F** (decisão 9 da spec da 1F; antes era `Codigo`
    crescente), e vale para **todo** consumidor da listagem, inclusive o seletor de catálogo
    (`SeletorComBusca`), que não manda `ordem`. Quem precisa da ordem por código a pede com
    `?ordem=codigo`.
  **Este documento não detalha os campos de cada item de `itens`** (nem detalhava antes da Fase
  2B) — só o envelope da paginação; por isso `temSolido`, que o item de listagem carrega desde a
  Fase 2B, não ganha bullet próprio aqui.
- `GET /componentes/{id}` *(qualquer perfil autenticado)* — detalhe do Componente:
  `{ id, codigo, descricao, tipo, ativo, temSolido, nomeDoSolido, tamanhoDoSolidoEmBytes }`
  (os três últimos entraram na Fase 2B; antes só havia os cinco primeiros). `nomeDoSolido` e
  `tamanhoDoSolidoEmBytes` são nulos **juntos** quando o Componente não tem sólido enviado, e
  preenchidos **juntos** quando tem — nunca um só. `404` se o Componente não existir.
- `POST /componentes` *(Administrador, PCP)* — `{ codigo, descricao, tipo }`, `tipo` em
  `Bruto | Fabricado | Montagem`
- `PUT /componentes/{id}` *(Administrador, PCP)* — idem
- `PATCH /componentes/{id}/ativo` *(Administrador, PCP)* — `{ ativo }`
  Não existe `DELETE`: catálogo se inativa, não se exclui.
- `POST /componentes/{id}/solido` *(Administrador, PCP)* — envia (ou **substitui**) o sólido 3D do
  Componente. `multipart/form-data`, campo `arquivo`. Resposta **204**, sem corpo — o front busca
  o detalhe de novo via `GET /componentes/{id}` para mostrar nome e tamanho. Falhas: `400`
  (arquivo inválido — extensão, tamanho acima de 16 MiB ou estrutura de STL inválida; ver §5.1 de
  `docs/superpowers/specs/2026-09-12-fase-2b-solido-3d-design.md`), `404` (Componente
  inexistente), `403` (perfil sem escrita). **Corpo acima do limite do endpoint** (16 MiB mais uma
  margem de 4 KiB para o overhead do multipart) **não tem uma resposta garantida**: o servidor
  recusa, mas o que chega depende do cliente — com `curl` chega um 400 (medido em 2026-09-13); com
  o `fetch` do Chromium e o `HttpClient` do .NET a conexão cai com o corpo ainda subindo e não
  chega resposta nenhuma (medido em 2026-09-18); Firefox, Safari e o navegador do Android não
  foram medidos. Por isso o front recusa o arquivo acima de 16 MiB antes de enviar — é essa
  checagem, e não a resposta do servidor, que explica o tamanho ao usuário.
- `GET /componentes/{id}/solido` *(qualquer perfil autenticado)* — `application/octet-stream`, com
  `Content-Disposition` carregando o nome original do arquivo enviado. Serve o download **e** o
  viewer 3D — um endpoint, dois consumidores. `404` quando o Componente não existe **ou** quando
  existe mas não tem sólido enviado.
- `GET /componentes/{id}/filhos-padrao` *(qualquer perfil autenticado)* — filhos padrão do
  Componente, com dados do Componente filho: `{ id, componenteFilhoId, codigo, descricao,
  quantidadePadrao }[]`
- `POST /componentes/{id}/filhos-padrao` *(Administrador, PCP)* — substitui a receita de filhos
  inteira. Body: `{ linhas: [{ componenteFilhoId, quantidadePadrao }] }`. `200`, não `201`: não
  cria um recurso novo endereçável, substitui o conteúdo de um sub-recurso que já tem endereço.
  `linhas: []` apaga a receita — é o comando explícito de remoção, não erro; campo `linhas`
  **ausente** responde `400` (é o `[Required]` do DTO que impede `POST {}` de limpar a receita em
  silêncio).
- `GET /componentes/{id}/materiais-padrao` *(qualquer perfil autenticado)* — `{ id, materialId,
  codigo, descricao, unidadeMedida, quantidadePadrao }[]`
- `POST /componentes/{id}/materiais-padrao` *(Administrador, PCP)* — Body: `{ linhas: [{
  materialId, quantidadePadrao }] }`. Mesmo contrato de `200`, lista vazia e `linhas` ausente do
  item acima.
- `GET /componentes/{id}/roteiro-padrao` *(qualquer perfil autenticado)* — `{ id, setorId, nome,
  ordem }[]`, ordenado por `ordem`
- `POST /componentes/{id}/roteiro-padrao` *(Administrador, PCP)* — Body: `{ linhas: [{ setorId
  }] }`, sem `ordem`: ela é atribuída pelo servidor, pela posição de cada linha no array — 1-based
  (a primeira linha vira `ordem = 1`). **O mesmo Setor pode repetir na lista** — significa retorno
  ao setor, não erro (ver regra 21 de `01-dominio-e-regras-de-negocio.md`). Mesmo contrato de `200`,
  lista vazia e `linhas` ausente dos dois itens acima.

Contrato de erro dos três sub-recursos acima: `{ erro: "..." }`, em `400` (validação — ex.:
referência inexistente ou inativa, nos três sub-recursos (material, setor ou componente filho); em
**materiais e filhos**, também quantidade inválida ou fora da escala e linha repetida no corpo; em
filhos, também auto-referência e ciclo na receita; e, nos três, Componente da rota **inativo**),
`404` (Componente da rota inexistente) e `409` (gravação concorrente derrubada pelo banco — refazer
o `POST` é o caminho). **No roteiro não há as validações de quantidade e de linha repetida que
materiais e filhos têm**: não existe campo de quantidade, e setor repetido é aceito, não erro — ver
o item acima e a regra 21; já a recusa por referência inexistente ou inativa (aqui, do setor) vale
igualmente no roteiro. **Não** é o mesmo formato da seção "Contrato de erro dos cadastros" (mais
abaixo, depois de Pedido/Agrupamento): lá o `409` é `{ erro: "ValorDuplicado", campo, existeInativo,
idExistente }`; aqui o `409` só existe por conflito de concorrência, e uma linha repetida dentro do
próprio corpo do `POST` responde `400`, não `409`, em materiais e filhos — os dois contratos usam o
mesmo status HTTP para coisas diferentes.

## Pedido / Agrupamento

- `GET /pedidos` *(qualquer perfil autenticado)* — **página** de Pedidos, com busca e filtro. Todos
  os parâmetros são opcionais: `?busca=`, `?status=Aberto,EmProducao`, `?material=3,5`,
  `?ordem=entrega|recentes|numero|cliente`, `?pagina=1`, `?tamanho=20` (teto 100). Responde `{ itens, total, pagina, tamanho }`, o mesmo envelope de
  `GET /componentes`; `total` é contado com os mesmos filtros da página. Cada item traz `pausa`:
  `null`, ou `{ desde, porUsuarioNome, motivo }` quando há pausa aberta (regra 31). Cada item traz
  também `dataEntrega` (`"aaaa-mm-dd"`, sem hora nem fuso) e `atrasado` (regra 33, calculado no
  servidor); o mesmo vale para `GET /pedidos/{id}` e para as respostas de `POST` e `PUT`.
  - `busca` acha o Pedido pelo **número**, pelo **cliente** ou pelo **código do Componente de
    qualquer nó dele** (a Peça ou um Item, inclusive filho). O texto é literal: `%`, `_` e `[` não
    são curinga de `LIKE`. Ignora caixa **e acento** ("metalurgica" acha "Metalúrgica"). O texto é
    aparado nas pontas: só espaços equivale a não mandar `busca`.
  - `status`: lista separada por vírgula de valores entre `Aberto`, `EmProducao`,
    `AguardandoExpedicao`, `Concluido` e `Cancelado`; o Pedido casa se tem **algum** deles (OU).
  - `material`: lista separada por vírgula de ids de Material; o Pedido casa se **algum nó** dele
    tem algum deles (OU). É o Material do **nó** (`EstruturaMaterial`, gravado na criação do nó),
    nunca o da receita do catálogo (`ComponenteMaterialPadrao`).
  - Entre `busca`, `status` e `material` vale **E**. Nas duas listas, pedaço vazio é ignorado e
    valor repetido colapsa (`material=3,,3` é `material=3`).
  - `ordem`: `entrega` (a padrão — também quando ausente, vazia ou só espaços) põe primeiro os
    Pedidos **não encerrados** (nem `Concluido` nem `Cancelado`) por `DataEntrega` crescente — o mais
    atrasado no topo — e, no empate, `DataAbertura` e `Id` crescentes; depois os **encerrados**, por
    `DataEntrega` decrescente e, no empate, `Id` decrescente (ordenar só pela data poria os concluídos
    antigos no topo). `recentes` é `DataAbertura` decrescente e, no empate, `Id` decrescente (a
    ordem que a listagem tinha antes de 2026-10-08, quando passou a ser a padrão `entrega`); `numero` é
    `Numero` crescente (único); `cliente` é `Cliente` crescente e, no empate, `Id` decrescente. O
    desempate por `Id` (e a unicidade de `Numero`) é o que faz `Skip/Take` não repetir nem pular
    linha entre páginas. A comparação é ordinal: `Numero` (com maiúscula) é valor desconhecido.
  - **400** `{ "erro": "..." }` para `pagina` menor que 1, `tamanho` menor que 1 ou maior que 100,
    `status` fora dos cinco (a frase nomeia o valor recusado), `material` que não seja lista de
    inteiros positivos e `ordem` desconhecida (`Ordem 'x' desconhecida. Aceitas: entrega, recentes, numero,
    cliente.`). A `ordem` é a última a ser validada: faixa, status e material inválidos ganham dela. **Página além do fim não é erro**: responde 200 com `itens` vazio.
- `GET /pedidos/resumo` *(qualquer perfil autenticado)* — sem parâmetros. Responde
  `{ porStatus, maisUrgentes }`. `porStatus` é `{ status, quantidade }[]`, contado no servidor
  sobre **todos** os Pedidos (nunca sobre uma página) e com **sempre os cinco status**, na ordem do
  `CK_Pedido_Status` (`Aberto`, `EmProducao`, `AguardandoExpedicao`, `Concluido`, `Cancelado`),
  zeros inclusive. `maisUrgentes` são até 5 Pedidos, no mesmo formato de `itens` de `GET /pedidos` (com
  `pausa`), fora de `Concluido` e `Cancelado`, por `DataEntrega` crescente (desempate `DataAbertura` e
  `Id`) — é o que a Home mostra em "Prazos de entrega". O campo se chamava `maisAntigosAbertos` até
  2026-10-08.
- `GET /pedidos/materiais` *(qualquer perfil autenticado)* — sem parâmetros. `{ id, codigo,
  descricao }[]` dos Materiais que aparecem em **algum nó de algum Pedido** (`EstruturaMaterial`),
  ativos ou não, por descrição e, no empate, por código. São as opções do filtro de Material da
  tela de Pedidos, sem contagem por opção.
- `POST /pedidos` *(PCP, Administrador)* — `{ numero, cliente, dataEntrega }`, com `dataEntrega` em
  `"aaaa-mm-dd"`, obrigatória (ausente → 400) e de qualquer dia (regra 33). `Tipo` nasce `Fabricacao`,
  `Status` nasce `Aberto` e o autor vem da claim `sub` da sessão — nenhum dos três se aceita do
  cliente
- `GET /pedidos/{id}` — só o cabeçalho, com `pausa` como em `GET /pedidos`; os Agrupamentos saem
  pelo sub-recurso abaixo
- `PUT /pedidos/{id}` *(PCP, Administrador)* — `{ numero, cliente, dataEntrega }`, substituição inteira,
  em qualquer status. Não existe `DELETE`:
  Pedido é documento e se corrige por edição
- `POST /pedidos/{id}/retrabalhos` — cria um novo Pedido tipo Retrabalho vinculado.
  Body: `{ motivoRetrabalho: 'ReprovacaoDimensional' | 'ErroInterno' | 'SolicitacaoCliente' | 'Perda', relatorioDimensionalAvaliacaoId?: number, perdaId?: number }`
- `GET /pedidos/{id}/agrupamentos` *(qualquer perfil autenticado)*
- `POST /pedidos/{id}/agrupamentos` *(PCP, Administrador)* — `{ codigo, tipo }`,
  `tipo ∈ Kit | Avulso`
- `GET /agrupamentos/{id}`
- `PUT /agrupamentos/{id}` *(PCP, Administrador)* — `{ codigo, tipo }`
- `DELETE /agrupamentos/{id}` *(PCP, Administrador)* — 204. **A única exclusão física do
  sistema**, e é guardada: 409 `{ "erro": "AgrupamentoNaoVazio" }` se já houver `EstruturaItem`,
  409 `{ "erro": "PedidoNaoAberto" }` se o Pedido não estiver `Aberto`, e 409
  `{ "erro": "AgrupamentoComImportacao" }` se houver rascunho de importação do BOM esperando conferência
  (seção "Importação da estrutura"): quem quer excluir descarta o rascunho antes. O rascunho tem FK para o
  Agrupamento e não é estrutura — nenhum `EstruturaItem` existe até a confirmação —, então
  `AgrupamentoNaoVazio` não o enxerga.
  A ordem de verificação é **existe → Pedido `Aberto` → vazio → sem rascunho**, então quando duas recusas
  valem ao mesmo tempo a resposta é a primeira da ordem: com o Pedido não `Aberto`, sempre `PedidoNaoAberto`;
  com estrutura num Pedido `Aberto`, `AgrupamentoNaoVazio`. Um Agrupamento com estrutura num Pedido não
  `Aberto` **nunca** responde `AgrupamentoNaoVazio`, e um com rascunho **só** responde
  `AgrupamentoComImportacao` se passou pelas três verificações que o antecedem na ordem — o cliente não pode assumir que recebe o
  código mais específico

## Contrato de erro dos cadastros

*(Nasceu na Fase 1A com `Setor`, `Material`, `Pedido` e `Agrupamento`; a Fase 1B trouxe
`Componente` para o mesmo contrato, sem alterá-lo.)*

- **400** — validação de formato (`MaxLength` do DTO, campo obrigatório ausente — inclusive o
  `ativo` do `PATCH /{recurso}/{id}/ativo`) ou de regra simples (campo em branco, `tipo` fora do
  domínio). Formato do ASP.NET.
- **403** — perfil sem permissão, do `[Authorize(Roles)]`.
- **404** — id inexistente.
- **409 duplicidade** — viola `UQ_Setor_Nome`, `UQ_Material_Codigo`, `UQ_Componente_Codigo`,
  `UQ_Pedido_Numero` ou `UQ_Agrupamento_PedidoCodigo`:
  ```json
  { "erro": "ValorDuplicado", "campo": "nome", "existeInativo": true, "idExistente": 12 }
  ```
  `existeInativo: true` só acontece em catálogo (`Setor`, `Material`, `Componente`) e é o que
  permite a tela oferecer "reativar o existente" — os índices `UNIQUE` não são filtrados por
  `Ativo`, então um nome ocupado por linha inativa continua ocupado. Em `Pedido` e `Agrupamento` é
  sempre `false`.
- **409 regra de negócio** — só no `DELETE /agrupamentos/{id}`:
  `{ "erro": "AgrupamentoNaoVazio" }`, `{ "erro": "PedidoNaoAberto" }` ou
  `{ "erro": "AgrupamentoComImportacao" }` (rascunho de importação do BOM pendente; ver "Importação da
  estrutura").

A duplicidade é verificada **no use case, antes do insert**; o índice `UNIQUE` permanece como rede
de segurança para a corrida entre a verificação e a escrita.

## Estrutura

*(Fase 2. `EstruturaItem` é a árvore real de um Agrupamento — nó sem pai é Peça, nó com pai
é Item; ver `01-dominio-e-regras-de-negocio.md`. Perfis de escrita: `PCP, Administrador`, os mesmos
de Pedido/Agrupamento — quem monta o Pedido monta a árvore dele. Leitura é liberada a qualquer
perfil autenticado.)*

- `GET /agrupamentos/{id}/estrutura` *(qualquer perfil autenticado)* — árvore completa de
  `EstruturaItem` do Agrupamento. Cada nó já sai com `Materiais` e `Roteiro` resolvidos e
  `Descricao` já com o fallback do Componente aplicado — o front nunca recebe `Descricao` nula.
  Desde a Fase 3, cada nó traz também `quantidadePorPai` (nula na Peça) e `semRoteiro` (a pendência
  da regra 28)
- `POST /agrupamentos/{id}/estrutura` *(PCP, Administrador)* — cria a Peça (nó de topo), copiando a
  receita padrão a partir de um `Componente`. Body: `{ componenteId, quantidade,
  requerRelatorioDimensional }`. Sem opção de nó ad-hoc aqui: pela regra 18 toda Peça referencia um
  `Componente` — só um Item (nó com pai) pode ser ad-hoc. Regra 18, segunda metade (cobrada desde
  a Fase 2B): o `Componente` de origem precisa ter sólido 3D (`ArquivoSolidoId` preenchido) — sem
  ele, 400; se o `Componente` não existir, 404 (ver "Contrato de erro da Estrutura")
- `POST /estrutura/{id}/filhos` *(PCP, Administrador)* — acrescenta um Item filho ao nó `{id}`
  (Peça ou Item; os dois podem ganhar filho). Body: `{ componenteId?, descricao?, quantidade,
  quantidadePorPai }` — `quantidadePorPai` obrigatória e maior que zero desde a Fase 3 (regra 26);
  os filhos copiados da receita abaixo dele a recebem de `ComponenteFilhoPadrao.QuantidadePadrao`.
  Com `componenteId`: copia a receita do Componente, com as mesmas guardas do `POST` acima;
  `descricao`, se informada, sobrepõe a herdada (regra 19). Sem `componenteId` (ad-hoc):
  `descricao` é obrigatória
- `PUT /estrutura/{id}` *(PCP, Administrador)* — edita `Descricao`, `Quantidade` e, num Item,
  `QuantidadePorPai` do nó `{id}`. Body: `{ descricao?, quantidade, quantidadePorPai? }`
  (`quantidadePorPai` obrigatória no Item, proibida na Peça). Desde a Fase 3, a quantidade não desce
  abaixo do que já saiu de "a iniciar" nem, num nó com filhos, abaixo do total montado; editar a
  razão é livre, porque a baixa já gravada não muda. Não cascateia a quantidade para os filhos — decisão de
  domínio, não lacuna (a cópia da receita é pré-preenchimento, não automação). `Descricao`
  vazia/só espaço grava `null`, que volta a herdar a do Componente — exceto num nó ad-hoc, que não
  tem de onde herdar e recusa a edição
- `DELETE /estrutura/{id}` *(PCP, Administrador)* — apaga o nó e a subárvore inteira dele
  (Material e Roteiro de cada nó, filhos antes de pais). Só permitido com o Pedido `Aberto` — ver
  "Contrato de erro da Estrutura". **Sem** essa guarda nos dois `POST` acima, de propósito:
  acrescentar estrutura a um Pedido em execução é o comportamento padrão da fábrica (decisão do usuário,
  2026-08-29), não exceção — a assimetria entre criar e excluir é deliberada. Esta guarda também
  impede apagar nó que já tem movimento no livro (Fase 3): o primeiro `Inicio` de qualquer nó põe o
  Pedido em `EmProducao`, e o status não volta, nem com o estorno (regra 28 e spec da Fase 3, seção
  4.1)

**Roteiro e Materiais do nó depois da cópia.** As cinco rotas de `EstruturaController` cobrem criar,
ler, editar e excluir o nó — nenhuma cobre **editar** o Roteiro ou os Materiais de um nó já
existente depois de copiados da receita. É o outro lado da regra 7 de
`01-dominio-e-regras-de-negocio.md` — o roteiro de Setores pode ser copiado de um padrão do
catálogo (`Componente`), mas pode ser customizado por Pedido/Agrupamento, não é fixo.

- **Roteiro — Fase 3.** `GET` e `PUT /estrutura/{id}/roteiro`, na seção "Execução / Rastreamento":
  sem ele, um Item ad-hoc nasce sem Roteiro e nunca pode ter a primeira entrada (regra 28).
- **Materiais — sem fase atribuída.** `GET/POST /estrutura-itens/{id}/materiais` *(planejado)*.
  `06-roadmap-mvp.md` não tem bullet para customizar os Materiais **por nó**; a Fase 4 (Separação de
  materiais) é o registro de `MaterialSeparacao` e a requisição de material, e separar material não
  é editar a lista de materiais do nó. Quem for atribuir a fase decide primeiro no roadmap, e só
  depois aqui.

Note o prefixo: `estrutura/{id}` (sem "itens") é o real, implementado na Fase 2 e usado também pelas
rotas de nó da Fase 3. `estrutura-itens/{id}` sobra só nas duas rotas ainda planejadas — os
Materiais do nó (sem fase) e `separacoes-material` (Fase 4) —, e quem as implementar deve passá-las para
`estrutura/{id}`.

### Contrato de erro da Estrutura

- **400** — vem de duas origens distintas nestas rotas, e o cliente precisa saber ler as duas.

  **Do caso de uso**, só nas três rotas com corpo: `{ "erro": "<mensagem em português>" }` — aqui o
  mesmo campo `erro` que no 409 carrega um código estável (`CicloNaReceita` etc.) carrega uma frase
  pronta para o operador ler. São seis as causas de negócio:
  - `quantidadePorPai` ausente ou ≤ 0 num Item, ou informada numa Peça (regra 26, desde a Fase 3);
  - quantidade **digitada** abaixo do piso da coluna (`0,0001`);
  - na cópia da receita, uma quantidade calculada sai da faixa da coluna (`DECIMAL(18,4)`) —
    `QuantidadeForaDaColunaException`, com a frase nomeando o alvo e o valor. A mesma checagem corre
    nas duas direções — **teto e piso** — e sobre os dois sujeitos — **nó e material** —, então são
    quatro combinações sob esta causa única. O piso durante a descida não é redundante com o da
    quantidade digitada — fatores fracionários encolhem o produto nó a nó, e um valor que a coluna
    arredondaria para `0,0000` seria uma Peça (ou um material) de quantidade zero gravada sem erro
    nenhum. A quantidade de material sai do mesmo `DECIMAL(18,4)`
    (`dbo.EstruturaMaterial.Quantidade`), e por isso passa pela mesma guarda;
  - na cópia da receita, a multiplicação **estoura o tipo `decimal` do próprio .NET** antes de
    chegar a comparar com o teto da coluna — `OverflowException`, caso mais raro e mais extremo que
    o anterior, com mensagem genérica ("a quantidade informada, multiplicada pela receita,
    ultrapassa o que o sistema suporta", sem números); **não** é o mesmo caso do teto da coluna, e
    as duas frases não devem ser lidas como sinônimas;
  - nó ad-hoc sem `Descricao`;
  - regra 18, segunda metade (cobrada desde a Fase 2B), só no
    `POST /agrupamentos/{id}/estrutura`: o `Componente` de origem da Peça não tem sólido 3D. Corpo
    `{ "erro": "Este Componente nao tem solido 3D (regra 18). Envie o arquivo STL no cadastro do
    Componente antes de criar a Peca." }`
    — sem `mensagem`, porque a falha não tem `Detalhe` (ver `Recusar` em `EstruturaController`).

  **Da validação de formato e de binding** (corpo malformado, `"quantidade": "abc"`), que é
  recusada antes de chegar ao caso de uso: o formato do ASP.NET, o mesmo que a seção "Contrato de
  erro dos cadastros" descreve — `EstruturaController` é `[ApiController]` e o `Program.cs` da API
  registra `AddControllers()` sem substituir a resposta padrão de model state. Um cliente que só
  souber ler `erro` quebra no primeiro corpo malformado.
- **403** — perfil sem permissão, do `[Authorize(Roles = "PCP,Administrador")]` nas quatro rotas de
  escrita (os dois `POST`, o `PUT` e o `DELETE`) — mesmo formato do "Contrato de erro dos
  cadastros".
- **404** — Agrupamento inexistente (`GET`/`POST /agrupamentos/{id}/estrutura`), `Componente`
  inexistente (`POST /agrupamentos/{id}/estrutura` — checado depois do Agrupamento, por convenção:
  o recurso da rota antes do que o corpo referencia; a ordem não protege sigilo, porque Agrupamento
  e catálogo de Componentes são legíveis por qualquer perfil autenticado) ou nó inexistente
  (`POST /estrutura/{id}/filhos`, `PUT`, `DELETE`).
- **409** — seis códigos, no mesmo formato do 409 de regra de negócio já usado em
  `DELETE /agrupamentos/{id}`: corpo `{ "erro": "<código>" }`. Os três códigos do
  `PlanejadorDeCopia` — `CicloNaReceita`, `EstruturaProfundaDemais` e `EstruturaGrandeDemais` —
  levam `mensagem` junto do `erro`, e o `ConflitoDeConcorrencia` também (a frase de
  `CodigosDaExecucao.MensagemDeConflito`); `PedidoNaoAberto` não — mesmo precedente do
  `DELETE /agrupamentos/{id}`.

  | Código | Onde | Motivo |
  |---|---|---|
  | `CicloNaReceita` | `POST /agrupamentos/{id}/estrutura`, `POST /estrutura/{id}/filhos` | a receita copiada do Componente tem ciclo; `mensagem` nomeia o caminho do ciclo |
  | `EstruturaProfundaDemais` | idem | a cópia recursiva passaria de 20 níveis de profundidade |
  | `EstruturaGrandeDemais` | idem | a cópia recursiva geraria mais de 500 nós |
  | `PedidoNaoAberto` | `DELETE /estrutura/{id}` | o Pedido do Agrupamento não está `Aberto` |
  | `QuantidadeAbaixoDoMovimentado` | `PUT /estrutura/{id}` | a quantidade nova é menor do que já saiu de "a iniciar" ou, num nó com filhos, do que o total montado (Fase 3); leva `mensagem` com os números |
  | `ConflitoDeConcorrencia` | `POST /agrupamentos/{id}/estrutura`, `POST /estrutura/{id}/filhos`, `PUT /estrutura/{id}`, `DELETE /estrutura/{id}` (e `PUT /estrutura/{id}/roteiro`, na seção "Execução / Rastreamento") | deadlock repetido até o esgotamento das 3 tentativas, ou lock timeout, na escrita; nada foi gravado, e tentar de novo é seguro. Leva `mensagem`; o `PUT` e o `DELETE` já podiam emitir desde a Fase 3, e os dois `POST` desde 2026-10-01 |

  `EstruturaProfundaDemais` e `EstruturaGrandeDemais` não são regra de negócio — são para-quedas
  contra receita corrompida ou cópia recursiva desgovernada, por isso não entram em
  `01-dominio-e-regras-de-negocio.md` (ver o comentário do planejador da cópia, no código, se o
  contrato mudar).

## Importação da estrutura

*(Spec: `docs/superpowers/specs/2026-10-02-import-de-estrutura-do-bom-design.md`. O BOM exportado do CAD
vira um **rascunho** salvo no servidor, que um humano confere e confirma; só a confirmação grava
catálogo e cria a Peça. Perfis de escrita: `PCP, Administrador`, o mesmo valor da constante
`PerfisDeEscrita` do `EstruturaController`, declarada à parte no `ImportacaoController` — quem monta a árvore
importa o BOM dela. Leitura: qualquer perfil autenticado. As
rotas são de `ImportacaoController`, que declara a própria rota em cada ação: as de criar e listar são
aninhadas sob Agrupamento, as de rascunho são de topo.)*

- `POST /agrupamentos/{id}/importacoes` *(PCP, Administrador)* — `multipart/form-data` com o campo
  `arquivo` (`.csv` ou `.xlsx`, até **5 MiB**). Lê, casa com o catálogo e cria o rascunho → **201** com o
  `ImportacaoDto`. **400** `BomInvalido` com a lista de erros do arquivo (ver "Contrato de erro da Importação");
  **404** se o Agrupamento não existe. Sem guarda de status do Pedido, pelo mesmo motivo dos `POST` da
  Estrutura. O autor vem da claim `sub`
- `GET /agrupamentos/{id}/importacoes` — os rascunhos do Agrupamento, do mais novo para o mais antigo:
  `ResumoDeImportacaoDto[]`. 404 se o Agrupamento não existe
- `GET /importacoes/{id}` — o estado completo do rascunho (`ImportacaoDto`), **calculado a cada leitura**
  contra o catálogo de agora e nunca gravado. 404
- `PUT /importacoes/{id}` *(PCP, Administrador)* — a Peça: `{ versao, quantidadeDaPeca,
  requerRelatorioDimensional }`. `quantidadeDaPeca` nula a limpa (e é o bloqueio `QuantidadeDaPecaAusente`).
  → 200 `ImportacaoDto`
- `PUT /importacoes/{id}/componentes/{cid}` *(PCP, Administrador)* — um registro do rascunho (`{cid}` é o
  `registroId` do DTO; um por **código** distinto): `{ versao, componenteId, codigoNovo, descricaoNova,
  tipoNovo, escolhaDeReceita }`. `componenteId` preenchido **casa** o registro com aquele Componente e
  descarta os dados do "criar novo"; nulo o deixa "criar novo", e `codigoNovo`, `descricaoNova` e `tipoNovo`
  (`Bruto`, `Fabricado` ou `Montagem`), quando preenchidos, são os dados dele (nulo mantém o que está).
  `escolhaDeReceita` é o **estado inteiro** da escolha — `Catalogo`, `Importada` ou nulo, que a limpa. Quatro
  recusas de regra, todas 400 (ver "Contrato de erro da Importação"): casar com um Componente que outro registro do rascunho
  já tem; mandar `escolhaDeReceita` **na mesma escrita** em que o casamento muda; mandar `escolhaDeReceita`
  num registro que não diverge; e passar a `Bruto` um "criar novo" que tem filhos no BOM (só uma folha passa a
  `Bruto`; o `Bruto` que o reimport manteve num código que ganhou filhos continua aceito). → 200 `ImportacaoDto`
- `PUT /importacoes/{id}/filhos/{fid}` *(PCP, Administrador)* — corrige a quantidade por pai de uma linha da
  receita lida (`{fid}` é o `filhoId` do nó): `{ versao, quantidade }`. A quantidade vale de `0,0001` ao teto
  da coluna, com no máximo 4 casas. → 200 `ImportacaoDto`
- `POST /importacoes/{id}/componentes/{cid}/solido` *(PCP, Administrador)* — envia o STL **pendente** do
  registro: `multipart/form-data` com `arquivo` e o campo de formulário `versao`. As mesmas validações de
  `POST /componentes/{id}/solido` (16 MiB, estrutura de STL, mesmas frases). O arquivo vive em
  `dbo.ArquivoDeComponente` sem nenhum Componente apontando para ele até a confirmação; um pendente anterior
  do mesmo registro é apagado depois que o novo estiver ligado. → 200 `ImportacaoDto`
- `GET /importacoes/{id}/componentes/{cid}/solido` — o STL pendente (`application/octet-stream`), para o
  visualizador e o download. **404** sem corpo para rascunho, registro ou pendente que não existe — o cliente
  não distingue os três
- `POST /importacoes/{id}/arquivo` *(PCP, Administrador)* — **reimporta**: `multipart/form-data` com `arquivo`
  e `versao`. Lê o arquivo exatamente como a criação (mesmos erros, mesmo limite) e substitui as receitas
  lidas; preserva, **por código** que continua no arquivo, o sólido pendente, o casamento manual e os dados do
  "criar novo"; zera a escolha de receita do código cuja receita lida mudou; apaga o pendente de código que
  saiu. Arquivo recusado deixa o rascunho como estava. → 200 `ImportacaoDto`; 400 `BomInvalido`
- `DELETE /importacoes/{id}` *(PCP, Administrador)* — descarta: apaga o rascunho, os registros, as linhas da
  receita e os sólidos pendentes, numa transação. **Não pede a `versao`** — é a saída de um rascunho que
  ninguém mais quer. → 204; 404
- `POST /importacoes/{id}/confirmacao` *(PCP, Administrador)* — `{ versao }`. Confirma: numa transação,
  cria os Componentes novos, reativa os inativos que têm registro, grava os sólidos pendentes e as receitas,
  cria a Peça pelo mesmo planejamento de cópia do `POST /agrupamentos/{id}/estrutura` e apaga o rascunho. →
  **201** com o `EstruturaItemDto` da Peça (a mesma forma do `POST` da Estrutura). A Peça é **idêntica** à que
  o `POST` da Estrutura criaria a partir do catálogo resultante

### Formato do arquivo

O BOM indentado exportado do CAD, CSV ou XLSX, com uma linha de cabeçalho e as quatro colunas da tabela a seguir. Os
**apelidos de cabeçalho são provisórios** (cobrem o SolidWorks em português e em inglês; o BOM real de cada
CAD confirma ou derruba, e a emenda mexe só na tabela `ColunasDoBom`). O cabeçalho é normalizado — maiúsculas,
sem acento nem pontuação, sem o sinal de "Nº" — antes de comparar.

| Coluna | Apelidos aceitos | Conteúdo |
|---|---|---|
| Nível | `Nº do item`, `Item no`, `Item`, `Número do item` | `1`, `1.2`, `1.2.3`: inteiros positivos separados por ponto; `01` e `1` são o mesmo. Um item de nível `1` é filho da raiz |
| Código | `Nº da peça`, `Part number`, `Número da peça` | o part number; **pode ficar em branco na linha** (vira Componente novo com código em branco), mas a coluna tem de estar no cabeçalho |
| Descrição | `Descrição`, `Description` | texto, até 200 caracteres |
| Quantidade | `Qtd`, `Qty`, `Quantidade` | por pai: `1,5` ou `1.5`, sem separador de milhar, de `0,0001` ao teto da coluna (`DECIMAL(18,4)`), até 4 casas |

- **Um arquivo é uma Peça.** A montagem de topo é a **raiz**, sem linha no arquivo: ela nasce com a descrição
  igual ao nome do arquivo sem extensão e o código em branco, que a conferência exige preencher ou casar.
- **CSV:** separador `;` ou `,`, decidido pela linha de cabeçalho (empate fica com `;`); aspas no padrão
  RFC 4180. Codificação: UTF-8 com BOM; **sem BOM, tenta UTF-8 estrito e, se houver byte inválido, lê como
  Windows-1252** — ler UTF-8 sem BOM como Windows-1252 estragaria os acentos em silêncio.
- **XLSX:** só a **primeira** planilha; o cabeçalho é a primeira linha com conteúdo. A quantidade pode ser
  numérica. O nível numérico **inteiro** (`2`) é aceito; o **não inteiro** (`1.1`, `1.10`) é recusado — são o
  mesmo número no Excel e itens diferentes no BOM —, e a saída é salvar a coluna como texto.
- O mesmo filho repetido sob o mesmo pai **soma** as quantidades. O pai de uma linha é a última linha lida
  cujo nível é o prefixo do dela.
- **Erros do arquivo** (cada um vira uma linha da lista do 400): arquivo vazio, acima de 5 MiB ou de extensão
  que não seja `.csv`/`.xlsx`; XLSX que descompactado passa de 64 MiB (o limite de 5 MiB é do arquivo
  compactado); nome do arquivo acima de 260 caracteres ou, sem a extensão, acima de 200 (a raiz usa esse nome
  como descrição); coluna ausente do cabeçalho; XLSX corrompido; aspas não fechadas; nível
  inválido ou que pula um degrau (`1` direto para `1.2.3`); quantidade não numérica, fora da faixa ou com
  mais de 4 casas; o **mesmo código com filhos diferentes**; **ciclo entre códigos**; árvore acima de
  `PlanejadorDeCopia.ProfundidadeMaxima` (20 níveis) ou `NosMaximos` (500); código acima de 50 caracteres,
  descrição vazia ou acima de 200. Os erros **se acumulam por etapa**, não no arquivo todo: a leitura
  (formato, colunas, linhas), a montagem da árvore e a conferência de tamanhos rodam em sequência, e a etapa
  seguinte só roda se a anterior não achou nenhum erro. Dentro de uma etapa vêm todos, não só o primeiro;
  corrigidos esses, o arquivo pode ainda trazer os da etapa seguinte.

### Estado do rascunho (`ImportacaoDto`)

JSON em `camelCase`. A árvore e as situações são **calculadas** a cada leitura; só o que o usuário decidiu
está gravado.

```text
ImportacaoDto { id, agrupamentoId, nomeDoArquivo, criadoPor, criadoEm, atualizadoEm, versao,
                quantidadeDaPeca: number|null, requerRelatorioDimensional,
                raiz: NoDaImportacaoDto|null, componentes: SituacaoDoComponenteDto[], bloqueios: BloqueioDto[] }
NoDaImportacaoDto { registroId: number|null, componenteId: number|null, filhoId: number|null,
                    codigo, descricao, quantidadePorPai: number|null, origem: "Bom"|"Catalogo",
                    pendencias: ("Novo"|"Inativo"|"Divergente"|"SemSolido")[], filhos: NoDaImportacaoDto[] }
SituacaoDoComponenteDto { registroId, codigoLido, descricaoLida, componenteId, codigoDoCatalogo,
                    descricaoDoCatalogo, tipo, ativo, temSolido, temSolidoPendente,
                    nomeDoSolido, tamanhoDoSolidoEmBytes,
                    codigoNovo, descricaoNova, tipoNovo, divergente, escolhaDeReceita: "Catalogo"|"Importada"|null,
                    comparativo: { codigo, descricao, noCatalogo: number|null, noBom: number|null,
                                   situacao: "Igual"|"QuantidadeMuda"|"Entra"|"Sai" }[],
                    efeitoDeManterCatalogo: { retira, traz } | null, naArvoreFinal }
BloqueioDto { tipo, registroId: number|null, componenteId: number|null, mensagem }
ResumoDeImportacaoDto { id, nomeDoArquivo, criadoPor, criadoEm, atualizadoEm }
```

- **`versao`** é o `ROWVERSION` do cabeçalho, em **base64** (o `byte[]` serializado pelo `System.Text.Json`).
  Toda escrita a manda no corpo (`versao`) ou, no multipart, no campo de formulário `versao`. Versão diferente
  da do banco → 409 `ImportacaoDesatualizada`. **Toda escrita no rascunho** — do cabeçalho, de um registro ou
  de uma linha — troca a versão, e por isso `atualizadoEm` existe no cabeçalho: sem ele, uma escrita num
  registro não mudaria o `ROWVERSION`.
- **`raiz`** é nula quando a expansão é recusada (ciclo, profundidade, tamanho ou quantidade fora da coluna):
  o motivo está em `bloqueios`. `registroId` é nulo no nó que veio **só do catálogo**; `componenteId`, no
  registro "criar novo"; `filhoId`, na raiz e na aresta que vem do catálogo.
- **`componentes`** tem um item por registro do rascunho, **inclusive os que saíram da árvore final** por uma
  escolha `Catalogo` acima deles (`naArvoreFinal: false`). `divergente` é verdadeiro quando o código está
  casado, o Componente tem receita no catálogo e ela (filhos diretos e quantidades) difere da lida — inclusive
  quando o BOM não traz filhos e o catálogo traz; casado **sem receita no catálogo** não diverge, traga o BOM
  filhos ou não; é aí que `comparativo`
  (um nível) e `efeitoDeManterCatalogo` (quantos nós saem e quantos entram se a escolha for `Catalogo`)
  vêm preenchidos. Descrição de casado vale a **do catálogo**; a lida fica ao lado.
- **`bloqueios[].tipo`**, em português com acento na `mensagem`, que é texto de tela: `QuantidadeDaPecaAusente`,
  `SemSolido`, `DivergenciaSemEscolha`, `CodigoVazio`, `CodigoJaExiste` (código do "criar novo" que já existe
  no catálogo **ou** que se repete em outro "criar novo" do mesmo rascunho), `QuantidadeForaDaFaixa` (a
  quantidade da Peça, multiplicada pela receita, sai da faixa da coluna) e os três do `PlanejadorDeCopia` —
  `CicloNaReceita`, `EstruturaProfundaDemais`, `EstruturaGrandeDemais`. Os de registro (código, divergência,
  sólido) valem **só para a árvore final**. Confirmar é possível com a lista vazia.

### Contrato de erro da Importação

- **400** — mesma dupla origem da Estrutura: o formato do ASP.NET para corpo malformado e campo obrigatório
  ausente (o `arquivo` do multipart, por exemplo), e `{ "erro": ..., "mensagem"?: ... }` do caso de uso.
  - **`BomInvalido`**, em `POST /agrupamentos/{id}/importacoes` e `POST /importacoes/{id}/arquivo`:
    `erro` é o **código** e `mensagem` traz **uma linha por erro do arquivo, separadas por `\n`** (`"Linha 7:
    quantidade inválida '0': ...\nLinha 9: ..."`; sem o prefixo "Linha N:" quando o erro é do arquivo
    inteiro). As linhas são texto de tela, em português com acento; a tela escreve o título e lista as linhas
    como vêm. As **demais** frases de erro da Importação (abaixo) seguem sem acento, e a tela não as mostra.
  - **`ImportacaoComBloqueios`**, em `POST /importacoes/{id}/confirmacao`: o rascunho ainda tem bloqueio. **Sem
    a lista** — a tela relê o `GET`, que a traz, e a lista não existe em dois contratos.
  - `versao` **ausente ou vazia** (corpo JSON ou campo do multipart) não chega ao caso de uso: o parâmetro
    é `string` não anulável, e o MVC o recusa antes com o 400 no formato do ASP.NET.
  - Nas demais, `erro` é uma **frase** (como nos 400 da Estrutura): `versao` que não é base64
    («Versao do rascunho invalida.»); quantidade fora da faixa ou com mais de 4 casas; `tipoNovo` fora de `Bruto`/`Fabricado`/`Montagem`;
    `escolhaDeReceita` fora de `Catalogo`/`Importada`; código acima de 50 caracteres ou descrição acima de 200;
    Componente já casado com outro registro do rascunho; escolha de receita enviada junto de uma troca de
    casamento («Escolha a receita depois de conferir o novo casamento.»); escolha de receita num registro que
    não diverge; `Bruto` num "criar novo" com filhos no BOM («So um Componente sem filhos no BOM pode passar a
    Bruto.»); e o que `POST /componentes/{id}/solido` já recusa no STL.
- **401** — além do token ausente ou inválido, em `POST /agrupamentos/{id}/importacoes` e
  `POST /importacoes/{id}/componentes/{cid}/solido`, o **token assinado por nós mas sem a claim `sub`**: essas
  duas rotas gravam o autor (do rascunho e do arquivo) e leem o usuário da claim, então sem ela respondem
  401 sem corpo e não 500. As demais rotas de escrita não leem a claim.
- **403** — perfil sem permissão, do `[Authorize(Roles = "PCP,Administrador")]` nas rotas de escrita.
- **404** — Agrupamento, rascunho, registro, linha da receita ou Componente-alvo inexistentes, **sem corpo**.
- **409** — três códigos, com `erro` estável:

  | Código | Onde | Motivo |
  |---|---|---|
  | `ImportacaoDesatualizada` | toda escrita com `versao`, e a confirmação | a `versao` do corpo não é a do banco, ou outra escrita chegou entre a leitura e o salvamento. Nada foi gravado; a tela relê. Sem `mensagem` |
  | `ReceitaDoCatalogoMudou` | `POST /importacoes/{id}/confirmacao` | a receita de catálogo de um Componente com escolha mudou depois da escolha (a impressão SHA-256 que o servidor guardou não bate). A transação é desfeita, e a escolha daquele código é zerada **em melhor esforço**, fora dela: a limpeza é pulada se a versão do rascunho mudou nesse meio-tempo, e qualquer falha dela é engolida, porque a resposta já é o 409 — o pior desfecho é a escolha velha continuar, e a confirmação seguinte devolve o mesmo 409. Leva `mensagem` nomeando os códigos |
  | `ConflitoDeConcorrencia` | `POST /importacoes/{id}/confirmacao` | deadlock repetido até o esgotamento das 3 tentativas, ou lock timeout, na transação da confirmação; nada foi gravado, e tentar de novo é seguro. Leva `mensagem`, a mesma frase das demais escritas de estrutura |

  O `DELETE /agrupamentos/{id}` ganha o `AgrupamentoComImportacao`, descrito na seção "Pedido / Agrupamento".

## Execução / Rastreamento

*(Fase 3, redesenhada pela spec `docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md`
sobre as regras 22 a 30 de `01-dominio-e-regras-de-negocio.md`. Contrato da fase; a implementação é
o plano 2 da Fase 3. Rotas de nó no prefixo `estrutura/{id}`, o da Fase 2. Leitura liberada a
qualquer perfil autenticado; cada rota de escrita declara os perfis, sempre com `Administrador`.)*

**Escrita**

- `POST /estrutura/{id}/inicios` *(Operador)* — primeira entrada (regra 28). Body:
  `{ setorId, quantidade }`. Exige Roteiro, o primeiro passo em `setorId` e saldo a iniciar; põe o
  Pedido em `EmProducao` se ele estava `Aberto`. **Aceita nó com filhos** (spec da Fase 3D, §2.1):
  iniciar N do pai consome `N × QuantidadePorPai` de cada filho direto que aguarda montagem naquele
  Setor (regra 24), e exige que eles estejam lá (`FilhosInsuficientes`). A resposta é o movimento de
  Início, com `montagemId` preenchido quando houve consumo.
- `POST /estrutura/{id}/terminos` *(Operador)* — terminar. Body: `{ setorId, ordem, quantidade }`; a
  quantidade passa a aguardar coleta no mesmo Setor e passo.
- `POST /inicios` *(Operador)* — iniciar **vários nós** no mesmo Setor, tudo ou nada (spec
  `docs/superpowers/specs/2026-09-29-filtros-e-lote-design.md`, seção 6). Body:
  `{ setorId, itens: [{ estruturaItemId, quantidade }] }`. Serve à seção "A iniciar aqui" e ao "Iniciar
  o pai" da fila do Setor: o início de um nó com filhos consome os filhos, como em
  `POST /estrutura/{id}/inicios`. A validação é **a mesma** da rota de um nó, porque as duas passam
  pelo mesmo núcleo por item (regra de saldo, de Roteiro, de filhos e de Pedido pausado): o lote não
  tem regra própria, só aplica o item 1, o 2 e assim por diante, cada um descontando do que os
  anteriores já gravaram, numa transação só. Gera as mesmas linhas de livro que a rota individual
  geraria para cada item. 201 com a lista de movimentos, **um por item, na ordem em que vieram**; no
  pai, é o movimento de Início dele (com `montagemId`), o mesmo que a rota individual devolve, e as
  baixas dos filhos ficam no livro.
- `POST /terminos` *(Operador)* — terminar vários nós, tudo ou nada, como `POST /inicios`. Body:
  `{ setorId, itens: [{ estruturaItemId, ordem, quantidade }] }`; cada quantidade passa a aguardar
  coleta no mesmo Setor e passo. 201 com um movimento de Término por item, na ordem dos itens.
- **Lote — recusa na entrada** (`POST /inicios` e `POST /terminos`), antes de abrir a transação e nesta ordem: `itens` ausente ou vazio → 400
  `LoteVazio`; mais de **100** itens (`ApontamentoUseCase.TamanhoMaximoDoLote`, o mesmo teto de página
  de `GET /pedidos`) → 400 `LoteGrandeDemais`; quantidade de algum item fora da regra → 400
  `QuantidadeInvalida`, com a frase da rota individual; o mesmo nó mais de uma vez no Iniciar, ou o
  mesmo par nó + `ordem` no Terminar → 400 `ItemRepetido`, e a `mensagem` diz qual ("O nó 7 aparece
  mais de uma vez no lote." / "O nó 7 no passo 2 aparece mais de uma vez no lote."); por fim, Setor
  inexistente → 404 sem corpo.
- **Lote — a primeira recusa de negócio aborta tudo** (`POST /inicios` e `POST /terminos`). Os itens são avaliados na ordem do corpo; a primeira
  recusa devolve **o código e o status que a rota individual daria** — 409 `SaldoInsuficiente`,
  `SemRoteiro`, `NaoEhOPrimeiroPasso`, `FilhosInsuficientes`, `PedidoPausado`, `PedidoFechado`; 404
  sem corpo para nó inexistente; 400 `QuantidadeInvalida` para o produto por filho que não cabe na
  coluna — e **nada é gravado**: o que os itens anteriores escreveram é desfeito junto com a
  transação. A `mensagem` **nomeia o item** recusado. `SemRoteiro`, `NaoEhOPrimeiroPasso` e
  `SaldoInsuficiente` já trazem o nó na frase da rota individual e passam iguais; toda outra recusa
  do núcleo ganha o prefixo "«nome do nó»: " (a descrição da regra 19), como `Calço: O Pedido
  PED-01 está pausado.`. O `ConflitoDeConcorrencia` continua com a frase genérica, porque é do lote
  inteiro.
- **Lote — perfis e travas** (`POST /inicios` e `POST /terminos`). Os mesmos das rotas de um nó (`Operador` e `Administrador`). Antes do primeiro
  item o lote trava, na ordem fixa da spec da Fase 3 (seção 8.1): no Iniciar, os itens e os filhos
  de cada um, em ordem crescente de Id, e depois os Pedidos deles; no Terminar, só os itens, e nenhum
  Pedido, como a rota de um nó.
- `POST /entregas` *(Movimentador)* — entrega uma lista, tudo ou nada. Body:
  `{ itens: [{ estruturaItemId, origem: { posicao, setorId, ordem }, quantidade }] }`.
  **O destino é sempre calculado** (spec da Fase 3D, §2.2): o próximo passo, o local de expedição
  para Peça no fim do Roteiro, ou, para Item no último passo, o **primeiro passo do Roteiro do pai**
  (regra 29). `destinoSetorId` **não se manda**: o campo sobrevive no contrato só para ser recusado
  — preenchido, dá 400 `DestinoIndevido`, mesmo que aponte o Setor certo. Redirecionar o que aguarda
  montagem fora do primeiro passo do pai é uma entrega com origem `AguardandoMontagem`, e leva ao
  primeiro passo de agora; se já está nele, 409 `RedirecionamentoSemEfeito`.
- `POST /movimentacoes/{id}/estorno` *(Operador, Movimentador, PCP)* e
  `POST /montagens/{id}/estorno` *(Operador, PCP)* — desfazem um registro com o movimento inverso,
  enquanto a quantidade não tiver andado. Estornar uma montagem desfaz também o Início do pai que
  ela gravou (Fase 3D), desde que o pai ainda esteja onde o início o pôs; o Início de um pai que
  consumiu filhos não se estorna por `/movimentacoes/{id}/estorno` (`EstornoImpossivel`). Só o
  autor, ou PCP ou Administrador (403 para os demais, decidido no caso de uso). As duas rotas declaram os mesmos perfis no `[Authorize]` — Operador,
  Movimentador e PCP —, porque vivem no mesmo controller; na de montagem, o Movimentador, que nunca é
  autor de uma, recebe o 403 do caso de uso.
- `PUT /estrutura/{id}/roteiro` *(PCP)* — troca os passos do Roteiro do nó. Body:
  `{ passos: [setorId, …] }`, em ordem. Passo já alcançado não muda.
- `POST /pedidos/{id}/pausas` *(PCP, Gestão)* — pausa o Pedido (regra 31; spec da Fase 3D, §2.5).
  Body: `{ motivo? }` — opcional, aparado, no máximo 200 caracteres (`MotivoLongoDemais`; texto em
  branco vira `null`). 201 com a pausa:
  `{ id, pedidoId, pausadoEm, pausadoPorUsuarioId, pausadoPorNome, motivo, retomadoEm, retomadoPorUsuarioId, retomadoPorNome }`.
  409 `PedidoJaPausado` se já há pausa aberta; 409 `PedidoFechado` se o Pedido está `Concluido` ou
  `Cancelado`. A pausa recusa só o Iniciar (409 `PedidoPausado` em `POST /estrutura/{id}/inicios`,
  também para nó com filhos); terminar, entregar e estornar continuam valendo.
- `POST /pedidos/{id}/retomada` *(PCP, Gestão)* — sem corpo; fecha a pausa aberta e responde 200 com
  a mesma forma, agora com `retomadoEm`, `retomadoPorUsuarioId` e `retomadoPorNome`. 409
  `PedidoNaoPausado` se não há pausa aberta. Retomar um Pedido que fechou com a pausa aberta é
  permitido: só fecha o intervalo.

**Leitura**

- `GET /setores/{id}/fila` — a iniciar aqui, em trabalho, aguardando coleta, aguardando montagem
  (por pai, com "Dá para iniciar N; falta iniciar X") e sobra. "A iniciar aqui" **não lista nó com
  filhos** (Fase 3D): o pai tem um lugar só, o grupo de montagem, que ganha `iniciaAqui` (este Setor
  é o primeiro passo do pai) e `primeiroPassoDoPai` (`{ id, nome }`, `null` se o pai não tem
  Roteiro). A resposta ganha `setorAtividade` (spec da Fase 3D, §2.3): a `atividade` do Setor da
  fila, que a tela usa para nomear os botões "Iniciar montagem"/"Terminar montagem" — `null` quando
  o Setor não tem uma, e os botões ficam só "Iniciar"/"Terminar". As linhas de "em trabalho",
  "aguardando coleta" e "sobra" trazem `estornaveis` (spec da Fase 3D, §2.4): os registros por trás
  da linha que ainda dá para estornar — `{ tipo, id, quantidade, usuarioId, usuarioNome, dataHora }`,
  do mais recente ao mais antigo. `tipo` é `Inicio` ou `Termino` (estornam-se por
  `POST /movimentacoes/{id}/estorno`) ou `Montagem` (o início de um pai, que consumiu os filhos —
  estorna-se por `POST /montagens/{id}/estorno`). A lista é filtrada por quem lê (o autor vê os
  seus; PCP e Administrador, todos) e cortada pelo saldo da posição (só entra o registro cuja
  quantidade ainda cabe no que está ali). Onde a mesma posição de coleta aparece em duas seções
  (tarefa e sobra do último passo), os `Termino` vão só para "aguardando coleta"; a "sobra" os
  recebe apenas quando não há tarefa (desvio D4 do plano da Fase 3D). Em "a iniciar" o campo vem
  sempre vazio; "aguardando montagem" não o tem. Todo nó resumido (`no`, `pai`, `filhos`) ganha
  `pausa`: `null`, ou `{ desde, porUsuarioNome, motivo }` quando o Pedido dele está pausado (regra
  31). Em "a iniciar", o que é de Pedido pausado vem **depois** do resto, para a tela agrupá-lo em
  "Pausados"; dentro de cada grupo a ordem é a de antes. Todo nó resumido ganha também, para os
  filtros da tela, `pedidoCliente` (o cliente do Pedido do nó, ao lado de `pedidoNumero`) e
  `materiais`: `{ id, codigo, descricao }[]`, os Materiais **do próprio nó** (`EstruturaMaterial`,
  não os do catálogo do Componente), por código; lista vazia — nunca nula — quando o nó não tem
  nenhum, como o Item ad-hoc. A fila e as Tarefas **não têm parâmetro de filtro**: chegam inteiras e
  a tela filtra no cliente, por Material e por Pedido.
- `GET /tarefas` — os Itens prontos (cada nó com `pausa`, `pedidoCliente` e `materiais`, como na fila), com destino calculado, agrupados pelo Setor de origem. Na
  montagem, `destino.setorId`/`setorNome` são o primeiro passo do pai (sem `ordem`); `destino` não
  traz mais `sugestaoSetorId` nem `setoresPossiveis` (Fase 3D). Pai sem Roteiro: `paiSemRoteiro` e
  `setorId` nulo.
- `GET /tarefas/contagem` — só o número, para o contador do menu.
- `GET /agrupamentos/{id}/posicoes` — saldo por posição de todos os nós do Agrupamento, e o total
  montado dos nós com filhos.
- `GET /estrutura/{id}/movimentacoes` — o livro do nó, com autor e estorno.
- `GET /estrutura/{id}/roteiro` — o Roteiro do nó, com os passos já alcançados marcados.

O formato exato de cada corpo e de cada resposta está na seção "Contrato JSON" do plano 2 da Fase 3
(`docs/superpowers/plans/2026-09-25-fase-3-backend.md`) **e** na seção "Contrato JSON novo" do plano da
Fase 3D (`docs/superpowers/plans/2026-09-28-fase-3d-ajustes-pos-verificacao.md`), que é o que o front
consome. O segundo traz só o que a 3D mudou (`atividade`, `estornaveis`, `pausa`, `iniciaAqui`,
`primeiroPassoDoPai`, o destino calculado e as rotas de pausa); o resto continua no primeiro. Os
campos `pedidoCliente` e `materiais` do nó resumido e as três rotas `GET /pedidos`, `GET /pedidos/resumo` e `GET /pedidos/materiais` estão na
seção "Contrato JSON novo" do plano 1 dos filtros da demanda
(`docs/superpowers/plans/2026-09-29-filtros-plano-1.md`). O corpo e as respostas de `POST /inicios` e
`POST /terminos` estão na seção "Contrato JSON novo" do plano 2 (`docs/superpowers/plans/2026-09-30-filtros-plano-2.md`).

**Fase 4, ainda planejada:** `POST /estrutura-itens/{id}/separacoes-material` (ver o bloco "Roteiro
e Materiais do nó depois da cópia", na seção Estrutura, sobre o prefixo).

### Contrato de erro da Execução

Formato `{ erro, mensagem }`: `erro` é o código estável, `mensagem` a frase para o operador, com nó,
Setor e números quando ajudam.

| Status | Código | Quando |
|---|---|---|
| 400 | `QuantidadeInvalida` | quantidade ≤ 0 ou fora da coluna (no lote, a de qualquer item, na entrada, com a mesma frase da rota individual; a do produto por filho, já dentro da transação, ganha o prefixo "{nome}: ") |
| 400 | `LoteVazio` | `POST /inicios` ou `POST /terminos` sem `itens`, ou com a lista vazia |
| 400 | `LoteGrandeDemais` | mais de 100 itens no lote (`ApontamentoUseCase.TamanhoMaximoDoLote`) |
| 400 | `ItemRepetido` | o mesmo nó em `POST /inicios`, ou o mesmo nó e passo em `POST /terminos`; a `mensagem` diz qual |
| 400 | `DestinoIndevido` | `destinoSetorId` mandado — o destino da entrega é sempre calculado |
| 400 | `EntregaVazia` | lista de entrega vazia |
| 400 | `RoteiroInvalido` | Setor inexistente ou inativo entrando no Roteiro |
| 400 | `OrigemInvalida` | origem da entrega fora de `AguardandoColeta`/`AguardandoMontagem`, ou com Setor e passo que não combinam com a posição |
| 400 | `MotivoLongoDemais` | motivo da pausa com mais de 200 caracteres (o `[MaxLength]` do corpo já dá 400 antes; o caso de uso repete a guarda) |
| 403 | `Proibido` | estorno de registro alheio sem ser PCP nem Administrador |
| 404 | — | nó, Setor, movimento ou montagem inexistente (no lote, o Setor ou o nó de algum item, se for a primeira recusa) |
| 409 | `SemRoteiro` | iniciar nó sem Roteiro |
| 409 | `NaoEhOPrimeiroPasso` | iniciar num Setor que não é o do primeiro passo |
| 409 | `SaldoInsuficiente` | a origem não tem a quantidade |
| 409 | `FilhosInsuficientes` | algum filho não tem, no Setor, o que N unidades pedem; a `mensagem` nomeia o filho |
| 409 | `RedirecionamentoSemEfeito` | redirecionar para a montagem o que já aguarda no primeiro passo do pai |
| 409 | `PaiSemRoteiro` | entrega para a montagem de pai sem Roteiro |
| 409 | `PassoJaAlcancado` | editar, remover ou inserir antes de passo que já é histórico |
| 409 | `QuantidadeAbaixoDoMovimentado` | reduzir a `Quantidade` do nó (`PUT /estrutura/{id}`) abaixo do que já saiu de "a iniciar" ou, num nó com filhos, do total já montado — mesmo código da seção "Estrutura", listado aqui também porque a spec da Fase 3 (§8.2) o inclui no catálogo de erros da Execução |
| 409 | `EstornoImpossivel` | a quantidade já andou; estorno de estorno; baixa de montagem, ou Início de pai que consumiu filhos, estornados sozinhos; estorno de montagem cujo pai já andou |
| 409 | `JaEstornado` | o registro já foi estornado |
| 409 | `PedidoFechado` | movimentar nó de Pedido `Concluido` ou `Cancelado`, ou pausá-lo |
| 409 | `PedidoPausado` | iniciar nó (ou pai) de Pedido pausado — só o Iniciar é recusado |
| 409 | `PedidoJaPausado` | pausar Pedido que já tem pausa aberta |
| 409 | `PedidoNaoPausado` | retomar Pedido sem pausa aberta |
| 409 | `ConflitoDeConcorrencia` | outra pessoa registrou no mesmo item ao mesmo tempo |

## Expedição

*(Rotas em nível de Peça, `estruturaItemId`; nomes definitivos podem ser afinados na
fase de implementação da API.)*

- `POST /pecas/{estruturaItemId}/expedicoes` — registra uma remessa parcial.
  Body: `{ quantidade, responsavel }`
- `GET /pecas/{estruturaItemId}/expedicoes`

## Dimensional

*(Modelo header + detalhe por quantidade. Rotas em nível de Peça, `estruturaItemId`;
nomes definitivos podem ser afinados na fase de implementação da API.)*

- `GET /pecas/{estruturaItemId}/relatorio-dimensional` — header do `RelatorioDimensional`
  da Peça, com as `RelatorioDimensionalAvaliacao` (uma por remessa avaliada)
- `POST /pecas/{estruturaItemId}/relatorio-dimensional/avaliacoes` — registra uma
  avaliação por quantidade.
  Body: `{ quantidadeAvaliada, quantidadeAprovada, quantidadeReprovada, medidas?, informadoPor }`

## Perdas

*(Rotas em nível de Peça, `estruturaItemId`; nomes definitivos podem ser afinados na
fase de implementação da API.)*

> **Nota (2026-09-15).** O `motivoPerda` ganha `'Descarte'`, e a perda passa a valer para Item, não
> só Peça — o prefixo `/pecas/` deixa de descrever o recurso (regras 17 e 27 de
> `01-dominio-e-regras-de-negocio.md`). Contrato redesenhado na spec da Fase 5.

- `POST /pecas/{estruturaItemId}/perdas` — registra uma Perda.
  Body: `{ quantidade, motivoPerda: 'PerdaArmazem' | 'MortaEmProcesso', setorId?, observacao?, responsavel }`
- `GET /pecas/{estruturaItemId}/perdas`

## KPIs

- `GET /kpis/tempo-por-setor?de=&ate=`
- `GET /kpis/tempo-por-pedido?de=&ate=`

> Este rascunho é ponto de partida para a Fase 0/1 do roadmap — refinar contratos
> (request/response DTOs) já dentro do Claude Code, caso a caso, conforme cada
> caso de uso for implementado.
