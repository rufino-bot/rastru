# Rastru

Sistema de **rastreamento de peças dentro da fábrica** — do cadastro do Pedido até a
entrega para a Expedição, passando pela estrutura recursiva de Peças/Itens, pela passagem
por Setores de produção, pela separação de Materiais e pelo Relatório Dimensional (com
possibilidade de abrir Retrabalho em caso de reprovação).

Projeto de TCC. O rastreamento é por **lote agregado** (não por unidade física individual),
e o lote é **divisível por quantidades livres**: parte dele pode estar num Setor e parte em
outro ao mesmo tempo. Não há identidade de sub-lote (sem etiqueta/serial) — controla-se
apenas *quanto* está *onde*, sob o invariante de **conservação de quantidade**, para toda Peça e
todo Item: o que está em produção (a iniciar, nos Setores, aguardando coleta ou montagem, ou no
local de expedição) + o que já foi montado dentro do pai + expedido + perdido = quantidade total.

## Stack

- **Backend:** .NET (C#), ASP.NET Core Web API, Clean Architecture (Domain / Application / Infrastructure / Api)
- **Banco:** SQL Server numa VPS paga com domínio próprio, EF Core em modo **Database First**
- **Frontend:** React + TypeScript (Vite) + Tailwind CSS, mobile-first, com three.js (carregado sob demanda) no visualizador de sólido 3D
- **Auth:** login próprio (usuário/senha) + JWT, com perfis (Operador, Almoxarifado, Movimentador, PCP, Qualidade, Gestão, Administrador — o Movimentador existe desde a Fase 3)

## Estrutura do repositório

| Pasta | Conteúdo |
|---|---|
| `src/` | Backend .NET — 4 projetos (`Domain`, `Application`, `Infrastructure`, `Api`) |
| `tests/` | Suíte de testes (xUnit) — um projeto de teste por camada |
| `web/` | Frontend React + TypeScript (Vite) |
| `specs/` | Fonte da verdade do domínio, regras de negócio, modelo de dados e roadmap |
| `db/` | Scripts de banco — `seed.sql` (perfis + usuários de desenvolvimento) e `seed-demo.sql` (massa de demonstração, opcional); `alter-fase-3.sql` e `alter-fase-3d.sql`, que levam um banco criado antes dessas fases até o schema atual |
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
dotnet test Rastreamento.slnx -m:1
```

> Parte da suíte roda contra o SQL Server real (mapeamento EF, atomicidade da rotação de
> refresh token). Sem o container no ar, esses testes falham por erro de conexão. O `-m:1` roda um
> projeto de teste por vez: dois processos escrevendo no livro de movimentações ao mesmo tempo
> deadlockam no banco compartilhado, e a suíte fica intermitente sem ele.

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
roadmap desdobra parte delas: 1A a 1C dentro da Fase 1, e 1D, 1E, 1F, 2B, 3B, 3C e 3D como fases próprias.
A sequência admite as exceções que aquele arquivo declara por escrito: a **Fase 3D** rodou antes da 3B,
e a **Fase 3C** (notificação push) roda depois da Fase 5 — a posição dela em relação à Fase 6 não está
decidida.

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
- **Fase 3:** rastreamento de setor — livro de movimentações, terminar e mover como ações
  separadas (com o perfil **Movimentador** e a tela de tarefas dele), montagem de todo nó com
  filhos, Roteiro editável por nó, fila do setor para o operador e chegada ao local de expedição.
  O código entrou na `main` pelos PRs #20 (backend) e #21 (front), em 2026-09-26, e por duas
  correções em 2026-09-28: o histórico do nó do registro mais recente ao mais antigo (PR #22) e a
  contagem de Pedidos abertos com o contador de Tarefas (PR #23).
- **Fase 3D:** ajustes pós-verificação da Fase 3, verificados manualmente no celular — iniciar como
  verbo único (iniciar um nó com filhos consome os filhos, e montar deixa de ser ação), atividade do
  Setor nos botões, destino do filho pronto calculado, estorno rápido na fila e pausa de Pedido.
  A fila do Setor passou a mostrar o que está em trabalho primeiro e, no fim, o que aguarda coleta
  e a sobra, e o Pedido pausado ganhou a pílula de tom de atenção (âmbar), um tom que só significa
  estado.
- **Filtros da demanda e ação em lote** (seção própria do roadmap, sem letra de fase): filtro por
  Material e por Pedido na fila do Setor e nas Tarefas; iniciar, terminar e iniciar o pai em lote
  na fila, e "Marcar todos" na fila e nas Tarefas; a lista de Pedidos paginada no servidor, com
  busca e filtro por Status e Material. Em dois planos, cada um verificado no celular: o primeiro
  entrou na `main` pelo PR #25, em 2026-09-30, e o segundo pelo PR #26, em 2026-10-01.
- **Fase 1F:** cadastro sob demanda e ordenação das listas — o formulário de criar e editar vira um
  painel que abre por botão (Setores, Materiais, Componentes, Pedidos e a Peça do Agrupamento), a
  ordem das listas é escolhida pelo usuário e o cartão inteiro é clicável. Verificada no navegador
  em 2026-10-02.

A seguir vem a **Fase 3B** (Kit: conjunto completo na entrada do Setor marcado como
de montagem — hoje, a Solda — e a tarefa Kit pronto, mais o caso do nó que ganha filho depois de
iniciado; a trava de montagem, para o nó que já tem filhos ao entrar em produção, já é estrutural
desde a 3D). Depois, as Fases 4 a 6 — separação de materiais; Relatório Dimensional, expedição,
perda e fechamento, com o retrabalho como ação separada e opcional; e os KPIs de tempo por setor
e por pedido. A Fase 3C (notificação push) fica depois da Fase 5, como o roadmap declara.

Para entender o domínio, as regras e as decisões já tomadas, comece por `specs/`
(`00-visao-geral.md` → `06-roadmap-mvp.md`).
