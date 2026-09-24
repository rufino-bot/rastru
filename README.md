# Rastru

Sistema de **rastreamento de peças dentro da fábrica** — do cadastro do Pedido até a
entrega para a Expedição, passando pela estrutura recursiva de Peças/Itens, pela passagem
por Setores de produção, pela separação de Materiais e pelo Relatório Dimensional (com
possibilidade de abrir Retrabalho em caso de reprovação).

Projeto de TCC. O rastreamento é por **lote agregado** (não por unidade física individual),
e o lote é **divisível por quantidades livres**: parte dele pode estar num Setor e parte em
outro ao mesmo tempo. Não há identidade de sub-lote (sem etiqueta/serial) — controla-se
apenas *quanto* está *onde*, sob o invariante de **conservação de quantidade**: soma das
unidades em todos os Setores + expedido + perdido = quantidade total da Peça.

## Stack

- **Backend:** .NET (C#), ASP.NET Core Web API, Clean Architecture (Domain / Application / Infrastructure / Api)
- **Banco:** SQL Server numa VPS paga com domínio próprio, EF Core em modo **Database First**
- **Frontend:** React + TypeScript (Vite) + Tailwind CSS, mobile-first, com three.js (carregado sob demanda) no visualizador de sólido 3D
- **Auth:** login próprio (usuário/senha) + JWT, com perfis (Operador, Almoxarifado, Movimentador, PCP, Qualidade, Gestão, Administrador — o Movimentador passa a existir na Fase 3)

## Estrutura do repositório

| Pasta | Conteúdo |
|---|---|
| `src/` | Backend .NET — 4 projetos (`Domain`, `Application`, `Infrastructure`, `Api`) |
| `tests/` | Suíte de testes (xUnit) — um projeto de teste por camada |
| `web/` | Frontend React + TypeScript (Vite) |
| `specs/` | Fonte da verdade do domínio, regras de negócio, modelo de dados e roadmap |
| `db/` | Scripts de banco — `seed.sql` (perfis + usuários de desenvolvimento) e `seed-demo.sql` (massa de demonstração, opcional) |
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
- `db/seed.sql` — perfis + os dois usuários de desenvolvimento
- `db/seed-demo.sql` — **opcional**: catálogo de demonstração (Setores, Materiais e Componentes,
  com receitas padrão). Nenhum teste depende dele — é a suíte que cria a própria massa, e é isso
  que a torna determinística numa máquina qualquer. Sem ele, porém, as telas de catálogo ficam
  vazias e parecem quebradas. Carregue-o com `-f 65001`: os nomes são `NVARCHAR` acentuados e, se
  a codepage se perder na carga, os dados entram corrompidos sem o banco acusar nada.

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
npm run test         # suíte Vitest: telas, primitivas de interface, camada de API e guardas de tema
npm run build        # tsc -b + vite build
npm run lint         # oxlint
```

> `npm run build` faz parte do ciclo, não só `npm test`: o Vitest não faz typecheck, então um erro
> de tipo num arquivo `.test.tsx` quebra o build sem quebrar a suíte.

Em desenvolvimento, front e API ficam na **mesma origem** via proxy do Vite — necessário
para o cookie `SameSite=Strict` do refresh token funcionar. O access token vive só em
memória; o refresh token só em cookie httpOnly + Secure + SameSite=Strict.

### Credenciais de desenvolvimento

Os dois usuários do seed — `admin` / `Admin@123` (perfil Administrador) e `pcp` / `Pcp@123`
(perfil PCP) — e a senha do SA do SQL Server local são **apenas para desenvolvimento**.

A `SigningKey` de exemplo commitada precisa virar um segredo de ambiente real antes de qualquer
deploy. Isso é procedimento de deploy, não dívida de código: o `JwtOptionsValidator` recusa o
valor de exemplo já no startup (e exige no mínimo 32 bytes), então esquecer de trocá-la derruba a
aplicação ao subir, em vez de deixar passar uma chave fraca em silêncio.

## Roadmap

O desenvolvimento segue as fases de `specs/06-roadmap-mvp.md` em sequência, da Fase 0 à Fase 6. O
roadmap desdobra parte delas: 1A a 1C dentro da Fase 1, e 1D, 1E, 2B, 3B e 3C como fases próprias.
A sequência admite as exceções que aquele arquivo declara por escrito; a mais recente é a **Fase 3C**
(notificação push), executada depois da Fase 5 — a posição dela em relação à Fase 6 não está decidida.

Concluídas até aqui:

- **Fase 0:** autenticação ponta a ponta (backend JWT com access + refresh token rotacionado e
  revogável; frontend React com login, sessão sustentada por refresh e tela protegida).
- **Fases 1A, 1B e 1C:** cadastros básicos — `Setor`, `Material`, `Pedido` e `Agrupamento`; o
  catálogo de `Componente`, com busca e paginação no servidor; e a receita padrão do Componente
  (filhos, materiais e roteiro).
- **Fases 1D e 1E:** identidade visual e UX — tokens de tema, primitivas de interface próprias e
  shell de navegação; depois, tipografia auto-hospedada e o resumo de Pedidos na Home.
- **Fase 2:** a estrutura recursiva — `EstruturaItem` sem pai é Peça, com pai é Item —, com a
  cópia da receita do Componente e a árvore editável na tela do Agrupamento.
- **Fase 2B:** o sólido 3D da Peça — arquivo STL guardado em blob, enviado e lido sob `/api`, com
  upload e visualizador no navegador.

A seguir vêm a **Fase 3** (rastreamento de setor: livro de movimentações, terminar e mover como
ações separadas — com o perfil **Movimentador** e a tela de tarefas dele —, montagem de todo nó com
filhos, Roteiro editável por nó, fila do setor para o operador e chegada ao local de expedição) e a
**Fase 3B** (Kit: trava de montagem por nó, com conjunto completo na entrada do Setor marcado como
de montagem — hoje, a Solda). Depois, as Fases 4 a 6 — separação de materiais; Relatório Dimensional, expedição,
perda e fechamento, com o retrabalho como ação separada e opcional; e os KPIs de tempo por setor
e por pedido.

Para entender o domínio, as regras e as decisões já tomadas, comece por `specs/`
(`00-visao-geral.md` → `06-roadmap-mvp.md`).
