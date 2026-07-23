# CLAUDE.md

Contexto para o Claude Code trabalhar neste repositório. Leia isto antes de qualquer
tarefa; para detalhes de domínio, arquitetura e roadmap, vá para a pasta `specs/`.

## Sobre o projeto

Sistema de rastreamento de peças dentro da fábrica: do cadastro do Pedido até a entrega
para a Expedição, passando pela estrutura recursiva de Peças/Itens, pela passagem por
Setores de produção, pela separação de Materiais e pelo Relatório Dimensional (com
possibilidade de abrir Retrabalho em caso de reprovação).

A fonte da verdade do domínio e das decisões já tomadas é a pasta `specs/`. Não
re-decida algo que já está resolvido lá sem perguntar antes.

## Stack

- **Backend**: .NET (C#), ASP.NET Core Web API
- **Frontend**: React + TypeScript (Vite), responsivo/mobile-first (uso em Android via navegador, sem PWA no MVP)
- **Banco**: SQL Server, on-premise
- **Auth**: login próprio (usuário/senha) + JWT, com perfis (Operador, Almoxarifado, PCP, Qualidade, Gestão, Administrador)
- **CI/CD**: nenhum ainda — deploy manual no MVP

## Mapa da pasta `specs/`

Leia o arquivo relevante para a tarefa em questão antes de codar — não é preciso carregar
todos de uma vez.

| Arquivo | Quando ler |
|---|---|
| `00-visao-geral.md` | Objetivo do sistema, escopo, perfis de usuário |
| `01-dominio-e-regras-de-negocio.md` | Antes de tocar em qualquer regra de negócio — glossário e regras numeradas |
| `02-modelo-de-dados.sql` | Fonte de verdade do schema do banco |
| `03-arquitetura-tecnica.md` | Estrutura de camadas, EF Core, autenticação, hospedagem |
| `04-fluxos-de-usuario.md` | Antes de implementar uma tela/fluxo — passo a passo por perfil |
| `05-api-endpoints.md` | Antes de criar/alterar um endpoint |
| `06-roadmap-mvp.md` | Para saber em que fase estamos e o que vem a seguir |

## Ordem de implementação

Seguir as fases de `06-roadmap-mvp.md` em sequência (Fase 0 → 6). Não implementar
funcionalidade de uma fase mais avançada antes da anterior estar concluída, mesmo que
pareça simples — a ordem existe para manter escopo fechado por etapa.

## Banco de dados — regra importante

`specs/02-modelo-de-dados.sql` é a **fonte de verdade** do schema. O mapeamento do EF
Core é *Database First*: o schema nasce no `.sql`, não em migrations do EF. Se uma
tarefa exigir mudança de schema:

1. Alterar `02-modelo-de-dados.sql` primeiro.
2. Atualizar o mapeamento EF Core a partir do novo schema.
3. Refletir a mudança em `01-dominio-e-regras-de-negocio.md` se for regra de negócio.

Nunca deixe o EF Core criar/alterar tabelas por conta própria via `Add-Migration` a
partir do zero.

## Convenções de nomenclatura

- **Nomes de domínio em português**, espelhando exatamente o DDL: `Pedido`, `Kit`,
  `EstruturaItem`, `Componente`, `Material`, `Setor`, `Usuario`, `Perfil`,
  `RelatorioDimensional`, etc. Não traduza entidades de negócio para inglês — isso cria
  divergência entre código, banco e as specs.
- **Nomes técnicos/padrões de projeto em inglês**: `Repository`, `UseCase`, `DTO`,
  `Controller` (ex.: `PedidoRepository`, `AbrirRetrabalhoUseCase`).
- Siga a estrutura de camadas descrita em `03-arquitetura-tecnica.md`
  (`Domain` / `Application` / `Infrastructure` / `Api`).

## Invariantes de negócio que não podem ser violadas

(resumo — ver `01-dominio-e-regras-de-negocio.md` para a lista completa)

- Um `EstruturaItem` (lote) nunca pode estar em dois Setores ao mesmo tempo — o lote é
  indivisível. Há um índice filtrado único no banco garantindo isso; não contorne essa
  regra na camada de aplicação.
- `EstruturaItem` é recursivo: nó sem pai = **Peça**, nó com pai = **Item**. Não crie
  tabelas separadas para Peça e Item.
- Reprovação no Relatório Dimensional **não** gera Retrabalho automaticamente — é uma
  ação separada e opcional do usuário (perfil Qualidade), com `MotivoRetrabalho`
  obrigatório quando aplicada.
- Um Pedido só é concluído quando o **último** Kit dele é concluído.
- Rastreamento é por **lote agregado**, nunca por unidade física individual (serial).

## O que evitar (decisões já descartadas — não reabrir sem justificativa nova)

- Windows Authentication (decidido: login próprio + JWT)
- PWA/offline no MVP (decidido: não necessário agora)
- Rastreamento por serial individual (decidido: lote agregado)
- Criar Peça e Item como tabelas separadas (decidido: tabela recursiva única)
- Roteiro de setores fixo por tipo de peça (decidido: pode variar por pedido/kit)

## Comandos

Ainda não definidos — este projeto está na Fase 0 (`06-roadmap-mvp.md`). Atualizar esta
seção com os comandos reais (`dotnet build`, `dotnet test`, `npm run dev`, etc.) assim
que a solution e o projeto frontend forem criados.
