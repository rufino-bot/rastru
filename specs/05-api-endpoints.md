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
- `GET/POST /componentes/{id}/filhos-padrao` — **Fase 1C**, não implementado
- `GET/POST /componentes/{id}/materiais-padrao` — **Fase 1C**, não implementado
- `GET/POST /componentes/{id}/roteiro-padrao` — **Fase 1C**, não implementado

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

- `GET /agrupamentos/{id}/estrutura` — árvore completa de `EstruturaItem` do Agrupamento
- `POST /agrupamentos/{id}/estrutura` — cria Peça (nó de topo), com opção de copiar de um
  `Componente` padrão
- `POST /estrutura-itens/{id}/itens` — adiciona Item filho a um `EstruturaItem`
- `GET/POST /estrutura-itens/{id}/materiais`
- `GET/POST /estrutura-itens/{id}/roteiro`

## Execução / Rastreamento

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
