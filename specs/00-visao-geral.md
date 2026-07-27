# 00 - Visão Geral do Projeto

## Objetivo

Sistema de rastreamento de peças dentro da fábrica, cobrindo o ciclo completo desde o
cadastro do Pedido até a entrega para o setor de Expedição, incluindo:

- Cadastro de Pedidos, Agrupamentos e da estrutura recursiva de Peças/Itens
- Rastreamento da passagem de cada componente pelos Setores de produção
- Separação e entrega de Materiais de estoque para fabricação
- Relatório Dimensional opcional por Peça (quando o cliente exige), com aprovação/reprovação por quantidade
- Abertura de Pedidos de Retrabalho vinculados ao Pedido original, quando houver reprovação ou perda
- KPIs de tempo (tempo por setor, tempo total de fila x produção por pedido)

## Fora de escopo (por ora)

- Emissão de notas fiscais (feita pelo ERP do setor de Expedição, sistema externo)
- Controle do processo de pintura terceirizada (controlado por etiqueta no ERP das
  empresas de pintura, fora do nosso sistema)
- Rastreamento por unidade física individual (serial) — o rastreamento é por lote agregado
- O Relatório Dimensional é opcional (exigido pelo cliente em Peças específicas), não amostragem sobre 100%.

## Usuários / Perfis

Confirmado com o negócio: o MVP já precisa de perfis distintos, com telas restritas por
perfil (não é "todo usuário vê tudo").

- **Operador** de setor (registra entrada/saída de componentes no seu setor)
- **Almoxarifado** / Separação (registra separação de materiais)
- **PCP** / Planejamento (cadastra Pedidos, Agrupamentos, estrutura)
- **Qualidade** (preenche relatório dimensional, aprova/reprova, abre retrabalho)
- **Gestão** (consulta KPIs)
- **Administrador** (cadastros de catálogo, usuários e perfis)

> Autenticação: login próprio (usuário/senha) com JWT — ver `03-arquitetura-tecnica.md`.
> Modelo de dados: ver tabelas `Usuario` e `Perfil` em `02-modelo-de-dados.sql`.

## Stack definida

| Camada       | Tecnologia                                              |
|--------------|-----------------------------------------------------------|
| Backend      | .NET (C#), ASP.NET Core Web API                          |
| Frontend     | React + TypeScript, responsivo (mobile-first, uso em Android via navegador) |
| Banco        | SQL Server                                                |
| Hospedagem   | On-premise (servidor próprio da empresa)                  |

## Documentos desta pasta

1. `00-visao-geral.md` — este arquivo
2. `01-dominio-e-regras-de-negocio.md` — glossário e regras de negócio consolidadas
3. `02-modelo-de-dados.sql` — DDL completo (SQL Server)
4. `03-arquitetura-tecnica.md` — decisões técnicas de backend/frontend/infra
5. `04-fluxos-de-usuario.md` — fluxos operacionais principais
6. `05-api-endpoints.md` — rascunho dos endpoints REST
7. `06-roadmap-mvp.md` — fases de construção, para orientar os agents em ordem
