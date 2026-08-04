# Rastru

Sistema de **rastreamento de peças dentro da fábrica** — do cadastro do Pedido até a
entrega para a Expedição, passando pela estrutura recursiva de Peças/Itens, pela passagem
por Setores de produção, pela separação de Materiais e pelo Relatório Dimensional (com
possibilidade de abrir Retrabalho em caso de reprovação).

Projeto de TCC. O rastreamento é por **lote agregado** (não por unidade física individual),
e um lote é indivisível — nunca está em dois Setores ao mesmo tempo.

## Stack

- **Backend:** .NET (C#), ASP.NET Core Web API, Clean Architecture (Domain / Application / Infrastructure / Api)
- **Banco:** SQL Server (on-premise), EF Core em modo **Database First**
- **Frontend:** React + TypeScript (Vite) + Tailwind CSS, mobile-first
- **Auth:** login próprio (usuário/senha) + JWT, com perfis (Operador, Almoxarifado, PCP, Qualidade, Gestão, Administrador)

## Estrutura do repositório

| Pasta | Conteúdo |
|---|---|
| `src/` | Backend .NET — 4 projetos (`Domain`, `Application`, `Infrastructure`, `Api`) |
| `tests/` | Suíte de testes (xUnit) — um projeto de teste por camada |
| `web/` | Frontend React + TypeScript (Vite) |
| `specs/` | Fonte da verdade do domínio, regras de negócio, modelo de dados e roadmap |
| `db/` | Scripts de banco (seed de perfis + usuário admin) |
| `docs/` | Documentação de processo (specs de design e planos de implementação) |

> `specs/02-modelo-de-dados.sql` é a **fonte da verdade do schema**. O EF Core mapeia a
> partir dele (Database First) — o schema nunca nasce de migrations do EF.

## Pré-requisitos

- .NET SDK (ver `Rastreamento.slnx`)
- Docker (para o SQL Server local)
- Node.js LTS + npm (para o frontend)

## Como rodar

### 1. Banco de dados (Docker)

```bash
docker compose up -d
```

Aplicar uma vez no banco `Rastreamento` de `localhost:1433`:

- `specs/02-modelo-de-dados.sql` — schema (fonte da verdade)
- `db/seed.sql` — perfis + usuário administrador

### 2. Backend (API)

```bash
dotnet build Rastreamento.slnx        # build tem que ficar em 0 warnings
dotnet run --project src/Rastreamento.Api   # perfil http → http://localhost:5169
```

Testes:

```bash
dotnet test Rastreamento.slnx
```

> Parte da suíte roda contra o SQL Server real (mapeamento EF, atomicidade da rotação de
> refresh token). Sem o container no ar, esses testes falham por erro de conexão.

### 3. Frontend

```bash
cd web
npm install
npm run dev          # http://localhost:5173, com proxy de /api para a API
npm run test         # testes Vitest da lógica de auth
```

Em desenvolvimento, front e API ficam na **mesma origem** via proxy do Vite — necessário
para o cookie `SameSite=Strict` do refresh token funcionar. O access token vive só em
memória; o refresh token só em cookie httpOnly + Secure + SameSite=Strict.

### Credenciais de desenvolvimento

`admin` / `Admin@123` (usuário do seed) e a senha do SA do SQL Server local são **apenas
para desenvolvimento**. A `SigningKey` de exemplo commitada precisa virar um segredo de
ambiente real antes de qualquer deploy.

## Roadmap

O desenvolvimento segue as fases de `specs/06-roadmap-mvp.md` em sequência (Fase 0 → 6).

- **Fase 0 — concluída:** autenticação ponta a ponta (backend JWT com access + refresh
  token rotacionado e revogável; frontend React com login, sessão sustentada por refresh e
  tela protegida).
- **Fase 1 em diante:** funcionalidades de domínio (Pedidos, Kits, Estrutura de Peças/Itens,
  Setores, Materiais, Relatório Dimensional, Retrabalho).

Para entender o domínio, as regras e as decisões já tomadas, comece por `specs/`
(`00-visao-geral.md` → `06-roadmap-mvp.md`).
