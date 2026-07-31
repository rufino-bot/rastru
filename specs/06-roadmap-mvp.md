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
> autorização por perfil no backend). Falta **1B**: `Componente` + receita padrão
> (`ComponenteFilhoPadrao`, `ComponenteMaterialPadrao`, `ComponenteRoteiroPadrao`), que recebe
> plano próprio. Dívidas rastreadas de 1A: camada global de erro de API no front e gating de
> navegação por perfil.

## Fase 2 — Estrutura recursiva

- Criar `EstruturaItem` a partir de um `Componente` padrão (copiar receita) ou do zero
  (customizado).
- Visualização em árvore da estrutura de um Agrupamento (Peça → Itens → sub-Itens).
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
