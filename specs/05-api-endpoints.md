# 05 - Rascunho de Endpoints da API (REST)

Convenção: recursos no plural, verbos HTTP padrão. Ajustar nomes conforme convenção
de time, se houver uma já estabelecida.

## Autenticação e Usuários

- `POST /auth/login` — usuário/senha → retorna JWT
- `GET/POST /usuarios` *(Administrador)*
- `GET/POST /perfis` *(Administrador)*

## Catálogo

- `GET/POST /setores`
- `GET/POST /materiais`
- `GET/POST /componentes`
- `GET/POST /componentes/{id}/filhos-padrao`
- `GET/POST /componentes/{id}/materiais-padrao`
- `GET/POST /componentes/{id}/roteiro-padrao`

## Pedido / Agrupamento

- `GET/POST /pedidos`
- `GET /pedidos/{id}`
- `POST /pedidos/{id}/retrabalhos` — cria um novo Pedido tipo Retrabalho vinculado.
  Body: `{ motivoRetrabalho: 'ReprovacaoDimensional' | 'ErroInterno' | 'SolicitacaoCliente' | 'Perda', relatorioDimensionalAvaliacaoId?: number, perdaId?: number }`
- `GET/POST /pedidos/{id}/agrupamentos`
- `GET /agrupamentos/{id}`

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
