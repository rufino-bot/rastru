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

- `GET /setores` — `?incluirInativos=false` por padrão *(qualquer perfil autenticado)*
- `POST /setores` *(Administrador)* — `{ nome }`
- `PUT /setores/{id}` *(Administrador)* — `{ nome }`
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
  `?pagina=1`, `?tamanho=20` (teto 100) *(qualquer perfil autenticado)*. Responde
  `{ itens, total, pagina, tamanho }`; `total` é contado com os mesmos filtros da página.
  Faixa fora do permitido responde 400; página além do fim responde 200 com `itens` vazio.
- `POST /componentes` *(Administrador, PCP)* — `{ codigo, descricao, tipo }`, `tipo` em
  `Bruto | Fabricado | Montagem`
- `PUT /componentes/{id}` *(Administrador, PCP)* — idem
- `PATCH /componentes/{id}/ativo` *(Administrador, PCP)* — `{ ativo }`
  Não existe `DELETE`: catálogo se inativa, não se exclui.
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

- `GET /pedidos` *(qualquer perfil autenticado)*
- `POST /pedidos` *(PCP, Administrador)* — `{ numero, cliente }`. `Tipo` nasce `Fabricacao`,
  `Status` nasce `Aberto` e o autor vem da claim `sub` da sessão — nenhum dos três se aceita do
  cliente
- `GET /pedidos/{id}` — só o cabeçalho; os Agrupamentos saem pelo sub-recurso abaixo
- `PUT /pedidos/{id}` *(PCP, Administrador)* — `{ numero, cliente }`. Não existe `DELETE`:
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
  409 `{ "erro": "PedidoNaoAberto" }` se o Pedido não estiver `Aberto`.
  A ordem de verificação é **existe → Pedido `Aberto` → vazio**, então quando as duas recusas
  valem ao mesmo tempo a resposta é sempre `PedidoNaoAberto`. Um Agrupamento com estrutura num
  Pedido não `Aberto` **nunca** responde `AgrupamentoNaoVazio` — o cliente não pode assumir que
  recebe o código mais específico

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
  `{ "erro": "AgrupamentoNaoVazio" }` ou `{ "erro": "PedidoNaoAberto" }`.

A duplicidade é verificada **no use case, antes do insert**; o índice `UNIQUE` permanece como rede
de segurança para a corrida entre a verificação e a escrita.

## Estrutura

*(Fase 2. `EstruturaItem` é a árvore real de um Agrupamento — nó sem pai é Peça, nó com pai
é Item; ver `01-dominio-e-regras-de-negocio.md`. Perfis de escrita: `PCP, Administrador`, os mesmos
de Pedido/Agrupamento — quem monta o Pedido monta a árvore dele. Leitura é liberada a qualquer
perfil autenticado.)*

- `GET /agrupamentos/{id}/estrutura` *(qualquer perfil autenticado)* — árvore completa de
  `EstruturaItem` do Agrupamento. Cada nó já sai com `Materiais` e `Roteiro` resolvidos e
  `Descricao` já com o fallback do Componente aplicado — o front nunca recebe `Descricao` nula
- `POST /agrupamentos/{id}/estrutura` *(PCP, Administrador)* — cria a Peça (nó de topo), copiando a
  receita padrão a partir de um `Componente`. Body: `{ componenteId, quantidade,
  requerRelatorioDimensional }`. Sem opção de nó ad-hoc aqui: pela regra 18 toda Peça referencia um
  `Componente` — só um Item (nó com pai) pode ser ad-hoc
- `POST /estrutura/{id}/filhos` *(PCP, Administrador)* — acrescenta um Item filho ao nó `{id}`
  (Peça ou Item; os dois podem ganhar filho). Body: `{ componenteId?, descricao?, quantidade }`.
  Com `componenteId`: copia a receita do Componente, com as mesmas guardas do `POST` acima;
  `descricao`, se informada, sobrepõe a herdada (regra 19). Sem `componenteId` (ad-hoc):
  `descricao` é obrigatória
- `PUT /estrutura/{id}` *(PCP, Administrador)* — edita `Descricao` e `Quantidade` do nó `{id}`.
  Body: `{ descricao?, quantidade }`. Não cascateia a quantidade para os filhos — decisão de
  domínio, não lacuna (a cópia da receita é pré-preenchimento, não automação). `Descricao`
  vazia/só espaço grava `null`, que volta a herdar a do Componente — exceto num nó ad-hoc, que não
  tem de onde herdar e recusa a edição
- `DELETE /estrutura/{id}` *(PCP, Administrador)* — apaga o nó e a subárvore inteira dele
  (Material e Roteiro de cada nó, filhos antes de pais). Só permitido com o Pedido `Aberto` — ver
  o contrato de erro abaixo. **Sem** essa guarda nos dois `POST` acima, de propósito: acrescentar
  estrutura a um Pedido em execução é o comportamento padrão da fábrica (decisão do usuário,
  2026-08-29), não exceção — a assimetria entre criar e excluir é deliberada

**Planejado, ainda sem rota decidida (Fase 3/4):** as cinco rotas acima cobrem criar, ler, editar
e excluir o nó — nenhuma cobre **editar** o Roteiro ou os Materiais de um nó já existente depois de
copiados da receita. Isso não é lacuna esquecida: é o outro lado da regra 7 de
`01-dominio-e-regras-de-negocio.md` ("o roteiro pode ser copiado de um padrão do catálogo, mas
pode ser customizado por Pedido/Agrupamento — não é fixo") e do verbete **Roteiro** do glossário
("pode ser específico daquele Pedido/Agrupamento"), que hoje não tem endpoint nenhum para exercer
a customização depois da cópia inicial. As duas linhas abaixo são o registro dessa decisão de
domínio, **não** um contrato implementado — `grep -rn "estrutura-itens" src/ --include=*.cs`
devolve vazio:

- `GET/POST /estrutura-itens/{id}/materiais` *(planejado — Fase 4)*
- `GET/POST /estrutura-itens/{id}/roteiro` *(planejado — Fase 3)*

Note o prefixo: `estrutura/{id}` (sem "itens") é o real, implementado na Fase 2 e usado nas cinco
rotas acima; `estrutura-itens/{id}` é o prefixo do rascunho ainda não implementado — o mesmo que a
seção "Execução / Rastreamento", abaixo, já usa para as rotas dela, também todas planejadas
(Fase 3/4). Se as duas rotas deste bloco saírem do papel, decidir ali o prefixo definitivo é
decidir para as duas seções de uma vez.

### Contrato de erro da Estrutura

- **400** — corpo `{ "erro": "<mensagem em português>" }`, só nas três rotas com corpo. **Não** é o
  formato da seção "Contrato de erro dos cadastros", que descreve 400 como "formato do
  ASP.NET": aqui o mesmo campo `erro` que no 409 carrega um código estável (`CicloNaReceita` etc.)
  carrega uma frase pronta para o operador ler. Quatro causas:
  - quantidade abaixo do piso da coluna (`0,0001`);
  - na cópia da receita, um nó calculado passa do teto da coluna (`DECIMAL(18,4)`) —
    `QuantidadeExcedeColunaException`, com a frase nomeando o componente e o valor;
  - na cópia da receita, a multiplicação **estoura o tipo `decimal` do próprio .NET** antes de
    chegar a comparar com o teto da coluna — `OverflowException`, caso mais raro e mais extremo que
    o anterior, com mensagem genérica ("a quantidade informada, multiplicada pela receita,
    ultrapassa o que o sistema suporta", sem números); **não** é o mesmo caso do teto da coluna, e
    as duas frases não devem ser lidas como sinônimas;
  - nó ad-hoc sem `Descricao`.
- **403** — perfil sem permissão, do `[Authorize(Roles = "PCP,Administrador")]` nas quatro rotas de
  escrita (os dois `POST`, o `PUT` e o `DELETE`) — mesmo formato do "Contrato de erro dos
  cadastros".
- **404** — Agrupamento inexistente (`GET`/`POST /agrupamentos/{id}/estrutura`) ou nó inexistente
  (`POST /estrutura/{id}/filhos`, `PUT`, `DELETE`).
- **409** — quatro códigos, no mesmo formato do 409 de regra de negócio já usado em
  `DELETE /agrupamentos/{id}`: corpo `{ "erro": "<código>" }`. Os três códigos do
  `PlanejadorDeCopia` (as três primeiras linhas da tabela) levam `mensagem` junto do `erro`;
  `PedidoNaoAberto` não — mesmo precedente do `DELETE /agrupamentos/{id}`.

  | Código | Onde | Motivo |
  |---|---|---|
  | `CicloNaReceita` | `POST /agrupamentos/{id}/estrutura`, `POST /estrutura/{id}/filhos` | a receita copiada do Componente tem ciclo; `mensagem` nomeia o caminho do ciclo |
  | `EstruturaProfundaDemais` | idem | a cópia recursiva passaria de 20 níveis de profundidade |
  | `EstruturaGrandeDemais` | idem | a cópia recursiva geraria mais de 500 nós |
  | `PedidoNaoAberto` | `DELETE /estrutura/{id}` | o Pedido do Agrupamento não está `Aberto` |

  `EstruturaProfundaDemais` e `EstruturaGrandeDemais` não são regra de negócio — são para-quedas
  contra receita corrompida ou cópia recursiva desgovernada, por isso não entram em
  `01-dominio-e-regras-de-negocio.md` (ver o comentário do planejador da cópia, no código, se o
  contrato mudar).

## Execução / Rastreamento

*(Planejado — Fase 3/4, ainda sem controller. Prefixo `estrutura-itens/{id}`, o mesmo do bloco
"Planejado" da seção Estrutura acima, e diferente do `estrutura/{id}` que a Fase 2 já implementou.)*

- `POST /estrutura-itens/{id}/entradas-setor` — registra entrada no setor atual
- `POST /estrutura-itens/{id}/saidas-setor` — registra saída do setor atual
- `GET /estrutura-itens/{id}/historico-setor`
- `POST /estrutura-itens/{id}/separacoes-material`
- `GET /setores/{id}/fila` — itens aguardando/em execução naquele setor

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

- `POST /pecas/{estruturaItemId}/perdas` — registra uma Perda.
  Body: `{ quantidade, motivoPerda: 'PerdaArmazem' | 'MortaEmProcesso', setorId?, observacao?, responsavel }`
- `GET /pecas/{estruturaItemId}/perdas`

## KPIs

- `GET /kpis/tempo-por-setor?de=&ate=`
- `GET /kpis/tempo-por-pedido?de=&ate=`

> Este rascunho é ponto de partida para a Fase 0/1 do roadmap — refinar contratos
> (request/response DTOs) já dentro do Claude Code, caso a caso, conforme cada
> caso de uso for implementado.
