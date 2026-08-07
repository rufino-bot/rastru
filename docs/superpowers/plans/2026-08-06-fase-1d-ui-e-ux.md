# Fase 1D — Identidade visual e UX — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar ao sistema um padrão visual e de interação único — tokens, primitivas e shell de navegação — e aplicá-lo às 7 telas existentes, de modo que toda tela futura nasça dentro do padrão em vez de fora dele.

**Architecture:** Fase de **front puro**. Nada em `src/` (backend) muda. Três camadas novas em `web/src/`: (1) tokens de tema em `index.css` via `@theme` do Tailwind v4, com prova automatizada de contraste; (2) primitivas de UI à mão em `web/src/components/`, sem biblioteca de terceiros; (3) um shell de aplicação (`AppShell`) como rota de layout do React Router. As 7 telas passam a consumir as três. Antes de qualquer reescrita de markup entra a **rede**: teste de fumaça por tela e as dívidas de comportamento (`useBuscaPaginada`, `ErroDeApi`).

**Tech Stack:** React 19 + TypeScript (Vite 8), React Router 7, Tailwind CSS v4 (já instalado), Vitest 4 + `@testing-library/react` 16 (já instalados), oxlint. **Zero dependência nova** — nem de UI, nem de teste.

## Global Constraints

Valem para **todas** as tasks. Em conflito entre este plano e o adendo `docs/superpowers/plans/2026-07-28-fase-1a-convencoes-obrigatorias.md`, **o adendo ganha**.

- **Spec de origem:** `docs/superpowers/specs/2026-08-06-fase-1d-ui-e-ux-design.md`. **Leia inteira antes da sua task** — ela tem o *porquê* de decisões que aqui aparecem só como valor.
- **Adendo obrigatório:** `docs/superpowers/plans/2026-07-28-fase-1a-convencoes-obrigatorias.md`. Desta fase interessam **F1–F6** (front); B1–B14 são de backend e não se aplicam, porque **nenhuma task toca em `src/`**.
- **Baseline MEDIDA em 2026-08-06, no HEAD desta branch** (`90dde7c`, rebase sobre `main` = `0513224`, que já contém a 1B):
  - `cd web && npm test` → **96 testes / 6 arquivos**, todos verdes.
  - `cd web && npm run build` → limpo (`dist/assets/index-*.js` ≈ 256,8 kB; `index-*.css` ≈ 9,9 kB).
  - `cd web && npm run lint` → **1 warning, e ele é alheio**: `src/auth/AuthContext.tsx:48 react(only-export-components)`. Não conserte — não é desta fase.
  - Backend: **não re-medido de propósito**. Esta fase não toca em `src/` nem em `tests/`; `dotnet test` não faz parte do ciclo de nenhuma task aqui. Se você mudou algo em `src/`, você saiu do escopo.
  - **Se a sua contagem inicial divergir de 96, pare e reporte.** Contagem de brief desatualizada já mordeu três tasks neste projeto.
- **Os totais absolutos de teste que aparecem daqui para baixo são ESTIMATIVAS, calculadas sem executar nada — e a Task 1 já provou que erram.** O plano previa 111 ao fim dela; o real é **110**, porque a aritmética somou 4 testes de `LoginPage` onde o código do próprio plano define 3. O que **vincula** é: (a) a baseline que você mede no início da sua task tem de bater com o número que a task anterior **reportou** — não com o número escrito aqui; e (b) o **delta** que a sua task acrescenta. Se o total divergir da estimativa e o delta estiver certo, **reporte o número real e siga** — não invente teste nem apague teste para fechar a conta. Números conhecidos e medidos: **início da fase 96 · fim da Task 1 110**.
- **`dotnet test` NÃO roda o front.** A suíte desta fase é `cd web && npm test` (= `vitest run`, não é watch).
- **Rode `npm run build` além de `npm test`.** Erro de tipo em `.test.tsx` quebra o build **sem** quebrar o `npm test` — o Vitest não faz typecheck e o `tsconfig.app.json` inclui `src` inteiro. Uma task não está pronta com build vermelho.
- **Ordem obrigatória da fase (spec §10): rede antes do markup.** As Tasks 1–3 são rede e comportamento; a primeira linha de markup novo só aparece na Task 4. Não antecipe.
- **Critério de pronto nunca é "os testes passam"** — é *"antes a mutação não matava nada, depois mata, e mata só o esperado"*. Cada task lista as mutações que precisa matar; **meça-as você mesmo** e reporte o número de mortes por mutação.
- **Ao verificar uma mutação, afirme que o texto ANTIGO SUMIU** do arquivo (`grep -c` caindo), nunca só que o novo apareceu. Deu **três falsos zeros** na fase passada.
- **Reverter mutação com edição inversa, nunca `git checkout`** — a árvore tem sujeira alheia permanente.
- **`git status` tem sujeira alheia e permanente:** `.claude/settings.local.json` modificado e `.claude/settings.json` untracked. **Não commite nenhuma das duas.**
- **Não edite fonte com `Set-Content` do PowerShell 5.1** — ele corrompe UTF-8 (acentuação) e a suíte fica verde mesmo assim. Use as ferramentas de edição de arquivo.
- **O repositório é PÚBLICO** (`github.com/rufino-bot/rastru`). Varredura de segredo antes de qualquer push é passo obrigatório.
- **Texto de interface em português, com acentuação correta.** Nomes de domínio em português (`Componente`, `Agrupamento`); nomes técnicos em inglês só onde já é a convenção do repo (`Repository`, `UseCase`, `DTO`). Nomes de primitiva e de hook: **português** (`Pagina`, `Botao`, `useBuscaPaginada`) — é a convenção que o front já segue (`TelaCarregando`, `estadoDaSessao`, `apiFetch`).

### Decisões desta fase que **não** se re-decidem

Estão fechadas na spec ou foram decididas pelo usuário durante o planejamento. Se você discorda, **reporte — não altere**.

- **Paleta (spec §3):** chrome `#134E4A`, marca `#5EEAD4`, ação `#3E6E68`, fundo de pílula `#E8F0EF`, positivo `#16A34A`, negativo `#DC2626`. A ação ser monocromática com o chrome foi escolha explícita do usuário, contra a recomendação de dois matizes. **Verde e vermelho são reservados a estado; cor de identidade nunca significa estado.**
- **Pilha de fonte do sistema** (`ui-sans-serif` / `ui-monospace`). Zero asset para baixar. Código de peça e material em monoespaçada — decisão funcional (alinha na coluna, facilita conferir na bancada), não decorativa.
- **Sem biblioteca de componentes de terceiros.** Primitivas à mão sobre Tailwind v4.
- **Sem tema escuro.** Fora de escopo declarado — dobraria os estados a provar em cada primitiva.
- **Gating de perfil vai na AÇÃO, não no link** — decisão do usuário em 2026-08-06, e ela **diverge da letra da spec §6**. Motivo medido: os controllers liberam **leitura** para qualquer usuário autenticado; só escrita tem `Roles`. Esconder o link de Materiais do Almoxarifado tiraria dele uma leitura que ele pode (e vai precisar) fazer na Fase 4. Então: o link aparece para todos; o que some para quem não pode escrever é o **formulário de cadastro** e os **botões Inativar/Reativar/Excluir**. O `try/catch` do F2 **continua existindo** — o 403 do backend segue sendo a fronteira real de segurança, e o gating de UI não substitui nada.
- **`useBuscaPaginada` nasce sem consumidor (Task 3) e é adotado na Task 9.** A spec §9 manda o hook entrar "antes do re-layout das telas"; construí-lo já acoplado à `ComponentesPage` obrigaria a reescrever `ComponentesPage.test.tsx` (987 linhas, 33 testes) **duas** vezes — uma pelo hook, outra pelo re-layout. Existir antes e ser adotado no re-layout satisfaz a ordem da spec com um terço do churn.
- **"Ficou bonito" não é critério de aceite.** Foi barrado de propósito. Os critérios verificáveis estão na spec §11 e viram passos de teste/grep neste plano.

### Valores fixos desta fase (copie exatamente)

- **Largura de página:** `max-w-3xl` (768px). O `max-w-md` (448px) de hoje é o que a spec §7 diz não caber na tela de Componentes.
- **Debounce da busca:** **300 ms**, parametrizável (`atrasoDoDebounce`), default no hook.
- **Tamanhos de página:** `[20, 50, 100]`. O teto 100 é do backend — acima vira 400.
- **Perfis de escrita, espelhando os `[Authorize(Roles)]` do backend** (conferidos no disco em 2026-08-06):
  - `setores` → `Administrador`
  - `materiais` → `Administrador`
  - `componentes` → `Administrador`, `PCP`
  - `pedidos` → `PCP`, `Administrador`
  - `agrupamentos` → `PCP`, `Administrador`
- **Perfis existentes** (`db/seed.sql`): `Operador`, `Almoxarifado`, `PCP`, `Qualidade`, `Gestao`, `Administrador`. Repare: **`Gestao` sem acento** — é o valor do banco.
- **Status de `Pedido`** (`CK` do DDL): `Aberto`, `EmProducao`, `AguardandoExpedicao`, `Concluido`, `Cancelado`.
- **Breakpoint da gaveta:** `md` do Tailwind (768px), como a spec §6 pede ("abaixo de ~768px").

---

## File Structure

Mapa do que a fase cria e modifica. Cada arquivo tem uma responsabilidade só.

**Criados — rede e comportamento (Tasks 1–3)**

| Arquivo | Responsabilidade |
|---|---|
| `web/src/testes/api.ts` | Kit de teste de tela: `respostaJson`, `fetchPorRota`. Sem ele, cada arquivo de teste reinventa o mock de `fetch` roteado. |
| `web/src/pages/HomePage.test.tsx` | Fumaça da Home |
| `web/src/pages/PedidosPage.test.tsx` | Fumaça de Pedidos |
| `web/src/pages/PedidoDetalhePage.test.tsx` | Fumaça do detalhe do Pedido |
| `web/src/pages/LoginPage.test.tsx` | Fumaça do Login (harness de auth, diferente das demais) |
| `web/src/api/erros.ts` | `ErroDeApi` (carrega o `status`) + `mensagemDeErro` (401/403/404/5xx/rede → texto amigável) |
| `web/src/api/erros.test.ts` | Prova de cada ramo de `mensagemDeErro` |
| `web/src/hooks/useBuscaPaginada.ts` | Busca com debounce, cancelamento por sequência, clamp de página e reset de filtro |
| `web/src/hooks/useBuscaPaginada.test.tsx` | Prova do hook, incluindo o W3 |

**Criados — tema e primitivas (Tasks 4–6)**

| Arquivo | Responsabilidade |
|---|---|
| `web/src/tema/contraste.ts` | `luminanciaRelativa` / `razaoDeContraste` — funções puras WCAG |
| `web/src/tema/contraste.test.ts` | Lê o `@theme` do `index.css` e **mede** cada par declarado. Token novo sem par declarado **reprova**. |
| `web/src/components/Pagina.tsx` | Container de página: largura, respiro, cabeçalho com título e ação |
| `web/src/components/Botao.tsx` | `primario` / `secundario` / `perigo`, com estado `carregando` que desabilita |
| `web/src/components/Campo.tsx` | Label + controle, com `id` ligado por `useId` |
| `web/src/components/BannerDeErro.tsx` | Mensagem de erro com `role="alert"` |
| `web/src/components/ListaDeCadastro.tsx` | `ListaDeCadastro` (o `<ul>`) e `ItemDeCadastro` (o `<li>`, com inativo distinguível e slot de ação) |
| `web/src/components/Pilula.tsx` | Rótulo de categoria/estado |
| `web/src/components/EstadoVazio.tsx` | Estado vazio explícito, com título e descrição próprios |
| `web/src/components/ControlesDePaginacao.tsx` | Anterior / posição / Próxima |
| `web/src/components/*.test.tsx` | Um arquivo de teste por primitiva |

**Criados — shell e permissões (Task 7)**

| Arquivo | Responsabilidade |
|---|---|
| `web/src/auth/permissoes.ts` | Tabela declarativa `recurso → perfis de escrita`, espelhando o backend |
| `web/src/auth/permissoes.test.ts` | Prova da tabela |
| `web/src/auth/usePermissao.ts` | `usePodeEscrever(recurso)` — lê o perfil da sessão |
| `web/src/components/AppShell.tsx` | Barra superior + gaveta abaixo de 768px + identidade + logout + `<Outlet/>` |
| `web/src/components/AppShell.test.tsx` | Prova do shell |

**Modificados**

| Arquivo | O que muda | Task |
|---|---|---|
| `web/src/index.css` | Ganha o bloco `@theme` com os tokens | 4 |
| `web/src/api/cadastros.ts` | Os `throw new Error(...)` viram `throw new ErroDeApi(status, ...)` | 2 |
| `web/src/App.tsx` | As 6 rotas protegidas passam a ser filhas de uma rota de layout com `AppShell` | 7 |
| `web/src/pages/SetoresPage.tsx` · `MateriaisPage.tsx` · `PedidosPage.tsx` | Retrofit: primitivas, estados, gating de ação | 8 |
| `web/src/pages/ComponentesPage.tsx` | Re-layout + adoção do `useBuscaPaginada` | 9 |
| `web/src/pages/PedidoDetalhePage.tsx` | Re-layout + modal com as primitivas | 10 |
| `web/src/pages/HomePage.tsx` | Muda de papel: cartões de contagem reais | 11 |
| `web/src/pages/LoginPage.tsx` | Identidade aplicada | 12 |
| `web/src/components/TelaCarregando.tsx` | Passa a usar os tokens | 4 |
| `specs/06-roadmap-mvp.md` | Registra a Fase 1D e a ausência de aresta de dependência | 12 |

**Não muda nada em `src/`, `tests/`, `specs/02-modelo-de-dados.sql`, `db/`.**

---

### Task 1: Rede de fumaça — kit de teste e as 4 telas sem prova

**Files:**
- Create: `web/src/testes/api.ts`
- Create: `web/src/pages/HomePage.test.tsx`
- Create: `web/src/pages/PedidosPage.test.tsx`
- Create: `web/src/pages/PedidoDetalhePage.test.tsx`
- Create: `web/src/pages/LoginPage.test.tsx`

**Interfaces:**
- Consumes: `apiFetch`, `inicializar`, `_resetParaTeste` de `web/src/api/client.ts`; as telas como estão hoje.
- Produces: `respostaJson(corpo: unknown, status?: number): Response` e `fetchPorRota(mapa: Record<string, () => Response | Promise<Response>>): Mock` em `web/src/testes/api.ts`. **Todas as tasks seguintes usam as duas.**

**Por que esta task existe primeiro:** hoje só 3 das 7 telas têm teste. As Tasks 8–12 reescrevem markup em massa. Regressão de markup não quebra build nem tipo — aparece para o usuário. Esta é a rede que torna as reescritas seguras.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Test Files  6 passed (6)` · `Tests  96 passed (96)`. Se divergir, **pare e reporte**.

- [ ] **Step 2: Escrever o kit de teste**

`web/src/testes/api.ts`:

```ts
import { vi } from 'vitest'

/**
 * Resposta JSON pronta para `vi.stubGlobal('fetch', ...)`.
 *
 * O corpo NUNCA é `''`, mesmo em resposta de erro: um corpo vazio faz `.json()` lançar sozinho, e
 * um `rejects.toThrow()` sem argumento passa pelo parse falho em vez de passar pela guarda que se
 * queria provar (adendo F6 — custou três mutações vivas na Fase 1A).
 */
export function respostaJson(corpo: unknown, status = 200): Response {
  return new Response(JSON.stringify(corpo), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/**
 * Mock de `fetch` roteado por caminho. A chave é o caminho COM o prefixo `/api` — é isso que o
 * `rota()` de `client.ts` monta, e escrever a chave sem o prefixo é o erro que faz o teste falhar
 * com "fetch não esperado" em vez de com a asserção.
 *
 * A query string é descartada na comparação: o teste declara `/api/componentes`, não
 * `/api/componentes?busca=&pagina=1&tamanho=20`. Quando a URL completa importa (prova de filtro),
 * asserte sobre `fetchMock.mock.calls[n][0]`, que guarda a URL inteira.
 *
 * Rota não declarada REJEITA com mensagem nomeando a URL, em vez de devolver `undefined` — sem
 * isso o teste morre dez linhas adiante em "cannot read property 'ok' of undefined", e o que se
 * lê no relatório não tem relação com a causa.
 */
export function fetchPorRota(mapa: Record<string, () => Response | Promise<Response>>) {
  return vi.fn((url: string | URL) => {
    const caminho = String(url).split('?')[0]
    const entrada = mapa[caminho]
    if (!entrada) return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    return Promise.resolve(entrada())
  })
}
```

- [ ] **Step 3: Rodar a suíte para confirmar que o kit não quebrou nada**

```bash
cd web && npm test
```

Expected: ainda `96 passed`. O kit ainda não tem consumidor.

- [ ] **Step 4: Escrever o teste de fumaça da `PedidosPage`**

`web/src/pages/PedidosPage.test.tsx`:

```tsx
// @vitest-environment jsdom
//
// Ambiente por ARQUIVO, e não em `vite.config.ts`: os testes de `api/` rodam em ambiente `node` e
// usam `new Response(...)`; trocar o ambiente global arriscaria mexer nos globals deles sem ganho.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { PedidosPage } from './PedidosPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

// O auto-cleanup do RTL depende de um `afterEach` GLOBAL, que só existe com `globals: true` no
// Vitest — e este projeto importa `describe`/`it`/`expect` explicitamente, ou seja, globals off.
// Sem esta linha o segundo teste renderiza por cima do primeiro e os `getBy*` falham com
// "found multiple elements".
afterEach(cleanup)

const PEDIDO = {
  id: 7,
  numero: 'PED-001',
  cliente: 'Fábrica Alfa',
  tipo: 'Normal',
  status: 'Aberto',
  dataAbertura: '2026-08-06T09:30:00-03:00',
  criadoPorUsuarioId: 1,
}

describe('PedidosPage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra os pedidos que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson([PEDIDO]),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText(/PED-001/)).toBeTruthy()
    expect(screen.getByText(/Fábrica Alfa/)).toBeTruthy()
  })

  it('mostra a data de abertura no fuso que a API mandou, sem reconverter', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson([PEDIDO]),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    // 06/08/2026 09:30 — se alguém trocar `formatarDataHora` por `new Date(...).toLocaleString()`,
    // o horário anda com o fuso da máquina e este teste morre. É exatamente o defeito que a função
    // existe para impedir.
    expect(await screen.findByText(/06\/08\/2026 09:30/)).toBeTruthy()
  })

  it('mostra erro quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson({ erro: 'Falhou' }, 500),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText('Não foi possível carregar os pedidos.')).toBeTruthy()
  })

  it('limpa o formulário e recarrega a lista depois de abrir um pedido', async () => {
    let listagens = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => {
        listagens += 1
        return respostaJson(listagens === 1 ? [] : [PEDIDO])
      },
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)
    await screen.findByPlaceholderText('Código do pedido')

    fireEvent.change(screen.getByPlaceholderText('Código do pedido'), { target: { value: 'PED-001' } })
    fireEvent.change(screen.getByPlaceholderText('Cliente'), { target: { value: 'Fábrica Alfa' } })
    fireEvent.click(screen.getByText('Abrir pedido'))

    // O POST cai na MESMA rota da listagem: `fetchPorRota` casa por caminho, e o mock devolve a
    // lista nova. O que se prova aqui é o ramo de SUCESSO — campo limpo e lista recarregada —,
    // que é a metade que nenhum teste do projeto cobria antes desta task.
    expect(await screen.findByText(/PED-001/)).toBeTruthy()
    expect((screen.getByPlaceholderText('Código do pedido') as HTMLInputElement).value).toBe('')
  })
})
```

- [ ] **Step 5: Rodar e confirmar que os 4 testes novos passam**

```bash
cd web && npm test -- PedidosPage
```

Expected: `Tests  4 passed (4)`.

- [ ] **Step 6: Provar que a rede morde — mutação na `PedidosPage`**

Aplique, uma de cada vez, revertendo por edição inversa:

| # | Mutação em `web/src/pages/PedidosPage.tsx` | Mortes esperadas |
|---|---|---|
| M1 | remover `setForm(FORMULARIO_VAZIO)` do ramo de sucesso (linha 42) | ≥ 1 |
| M2 | remover `await carregar()` do ramo de sucesso (linha 43) | ≥ 1 |
| M3 | trocar `{p.numero} — {p.cliente}` por `{p.cliente} — {p.numero}` (linha 79) | ≥ 1 |
| M4 | trocar `formatarDataHora(p.dataAbertura)` por `p.dataAbertura` (linha 81) | ≥ 1 |
| M5 | remover a linha `setErro('Não foi possível carregar os pedidos.')` (linha 21) | ≥ 1 |

Confirme cada mutação com `grep -c` do texto **antigo** caindo a 0, e reverta antes da próxima.
**Se alguma matar 0, o teste correspondente é decorativo — conserte o teste, não a contagem.**

- [ ] **Step 7: Commit**

```bash
git add web/src/testes/api.ts web/src/pages/PedidosPage.test.tsx
git commit -m "test(web): kit de teste de tela e rede de fumaca da PedidosPage"
```

- [ ] **Step 8: Escrever o teste de fumaça do `PedidoDetalhePage`**

`web/src/pages/PedidoDetalhePage.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { PedidoDetalhePage } from './PedidoDetalhePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

const PEDIDO = {
  id: 7,
  numero: 'PED-001',
  cliente: 'Fábrica Alfa',
  tipo: 'Normal',
  status: 'Aberto',
  dataAbertura: '2026-08-06T09:30:00-03:00',
  criadoPorUsuarioId: 1,
}

const AGRUPAMENTO = {
  id: 21,
  pedidoId: 7,
  codigo: 'AGR-01',
  tipo: 'Kit',
  criadoEm: '2026-08-06T10:00:00-03:00',
  criadoPorUsuarioId: 1,
}

// A tela lê `:id` da rota, então ela precisa nascer DENTRO de uma rota casada — renderizar o
// componente solto deixaria `useParams()` vazio e `pedidoId` viraria NaN.
function renderizarDetalhe() {
  return render(
    <MemoryRouter initialEntries={['/pedidos/7']}>
      <Routes>
        <Route path="/pedidos/:id" element={<PedidoDetalhePage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('PedidoDetalhePage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra o cabeçalho do pedido e os agrupamentos dele', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    }))

    renderizarDetalhe()

    expect(await screen.findByText('PED-001')).toBeTruthy()
    expect(screen.getByText('Fábrica Alfa')).toBeTruthy()
    expect(await screen.findByText('AGR-01')).toBeTruthy()
  })

  it('pede confirmação antes de excluir e só exclui depois do "Excluir" do diálogo', async () => {
    const fetchMock = fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => new Response(null, { status: 204 }),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))

    // O diálogo apareceu e NADA foi excluído ainda: a pausa deliberada é a propriedade sob teste.
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/agrupamentos/21'))).toBe(false)

    // Dois botões escritos "Excluir" na tela agora (o do item e o do diálogo): pegar pelo texto
    // dentro do diálogo é o que impede o teste de clicar no errado e passar por acidente.
    const dialogo = screen.getByRole('dialog')
    const confirmar = Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!
    fireEvent.click(confirmar)

    await screen.findByText('AGR-01')
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/agrupamentos/21'))).toBe(true)
  })

  it('cancelar fecha o diálogo sem excluir', async () => {
    const fetchMock = fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    fireEvent.click(screen.getByText('Cancelar'))

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/agrupamentos/21'))).toBe(false)
  })

  it('explica o motivo quando a exclusão é recusada por agrupamento não vazio', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'AgrupamentoNaoVazio' }, 409),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    expect(
      await screen.findByText('Este agrupamento já tem estrutura e não pode mais ser excluído.'),
    ).toBeTruthy()
  })
})
```

- [ ] **Step 9: Rodar e confirmar**

```bash
cd web && npm test -- PedidoDetalhePage
```

Expected: `Tests  4 passed (4)`.

- [ ] **Step 10: Provar que a rede morde — mutação no `PedidoDetalhePage`**

| # | Mutação em `web/src/pages/PedidoDetalhePage.tsx` | Mortes esperadas |
|---|---|---|
| M6 | trocar o `onClick` do botão "Excluir" do item por `() => excluir(a.id)` (pula a confirmação, linha 142) | ≥ 1 |
| M7 | trocar `setPendenteExclusao(null)` do "Cancelar" por `confirmarExclusao` (linha 178) | ≥ 1 |
| M8 | trocar `MOTIVO_DA_RECUSA.AgrupamentoNaoVazio` pelo texto de `PedidoNaoAberto` (linha 15) | ≥ 1 |
| M9 | remover `<h1 …>{pedido.numero}</h1>` (linha 104) | ≥ 1 |

- [ ] **Step 11: Commit**

```bash
git add web/src/pages/PedidoDetalhePage.test.tsx
git commit -m "test(web): rede de fumaca do PedidoDetalhePage"
```

- [ ] **Step 12: Escrever o teste de fumaça da `HomePage`**

`web/src/pages/HomePage.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// A `HomePage` usa `useAuth()` só para o `logout`. Como o `AuthProvider` de verdade dispara um
// init-refresh no mount (rede), aqui ele é substituído por um provider mínimo — o que se testa é a
// tela, não a sessão. O `useAuth` real tem sua prova em `estadoDaSessao.test.ts` e no LoginPage.
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: { status: 'anonimo' }, login: async () => {}, logout: async () => {} }),
}))

const USUARIO = { id: 1, nomeUsuario: 'admin', nomeCompleto: 'Administrador do Sistema', perfil: 'Administrador' }

describe('HomePage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra quem está logado depois de carregar /me', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/me': () => respostaJson(USUARIO),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('Administrador do Sistema')).toBeTruthy()
    expect(screen.getByText('Administrador')).toBeTruthy()
  })

  it('mostra "Carregando…" antes da resposta e o esconde depois', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/me': () => respostaJson(USUARIO),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.getByText('Carregando…')).toBeTruthy()
    await screen.findByText('Administrador do Sistema')
    expect(screen.queryByText('Carregando…')).toBeNull()
  })

  it('mostra erro quando /me falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/me': () => respostaJson({ erro: 'Falhou' }, 500),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('Não foi possível carregar seus dados.')).toBeTruthy()
  })
})
```

**Aviso ao implementador da Task 11:** este arquivo será reescrito quando a Home mudar de papel. Isso é esperado, não regressão — a rede existe para pegar mudança **não intencional** de markup; a Task 11 muda a tela de propósito e atualiza o teste junto.

- [ ] **Step 13: Rodar e confirmar**

```bash
cd web && npm test -- HomePage
```

Expected: `Tests  3 passed (3)`.

- [ ] **Step 14: Escrever o teste de fumaça da `LoginPage`**

`web/src/pages/LoginPage.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { LoginPage } from './LoginPage'
import { AuthProvider } from '../auth/AuthContext'
import { _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// Harness diferente das outras telas, de propósito: a `LoginPage` só existe dentro do
// `AuthProvider` de VERDADE — é ele que chama `client.inicializar` e o init-refresh. Por isso
// aqui NÃO se chama `inicializar()` à mão (o provider chama) e o mock precisa responder
// `/api/auth/refresh`, que dispara no mount antes de qualquer interação.
describe('LoginPage', () => {
  beforeEach(() => { _resetParaTeste() })
  afterEach(() => { vi.unstubAllGlobals() })

  function renderizarLogin() {
    return render(
      <MemoryRouter><AuthProvider><LoginPage /></AuthProvider></MemoryRouter>,
    )
  }

  it('mostra os dois campos e o botão de entrar', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/auth/refresh': () => respostaJson({ erro: 'sem sessão' }, 401),
    }))

    renderizarLogin()

    expect(await screen.findByLabelText('Usuário')).toBeTruthy()
    expect(screen.getByLabelText('Senha')).toBeTruthy()
    expect(screen.getByText('Entrar')).toBeTruthy()
  })

  it('mostra a mensagem genérica quando o login é recusado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/auth/refresh': () => respostaJson({ erro: 'sem sessão' }, 401),
      '/api/auth/login': () => respostaJson({ erro: 'nao importa' }, 401),
    }))

    renderizarLogin()
    fireEvent.change(await screen.findByLabelText('Usuário'), { target: { value: 'admin' } })
    fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'errada' } })
    fireEvent.click(screen.getByText('Entrar'))

    // Texto ÚNICO e genérico: honra o não-oráculo do backend, que responde o mesmo 401 para
    // usuário inexistente, conta trancada e senha errada. Variar a mensagem por caso aqui
    // desfaria no front a defesa que o backend paga BCrypt para manter.
    expect(await screen.findByText('Usuário ou senha inválidos.')).toBeTruthy()
  })

  it('avisa que a sessão expirou quando a volta ao login veio de sessão perdida', async () => {
    // O init-refresh responde 200 (sessão restaurada), e a primeira chamada autenticada devolve
    // 401 duas vezes seguidas -> `onSessionLost` -> estado anônimo com motivo 'sessao-expirada'.
    // É o único caminho que acende este aviso, e nenhum teste o cobria.
    let refreshes = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/auth/refresh': () => {
        refreshes += 1
        return refreshes === 1
          ? respostaJson({
              accessToken: 't',
              accessTokenExpiraEm: '2026-08-06T10:00:00-03:00',
              usuario: { id: 1, nomeUsuario: 'admin', nomeCompleto: 'Admin', perfil: 'Administrador' },
            })
          : respostaJson({ erro: 'expirou' }, 401)
      },
      '/api/me': () => respostaJson({ erro: 'nao autorizado' }, 401),
    }))

    const { apiFetch } = await import('../api/client')
    renderizarLogin()
    // Espera o provider terminar o init-refresh antes de forçar a perda de sessão.
    await screen.findByText('Entrar')
    await apiFetch('/me')

    expect(await screen.findByText('Sessão expirada. Entre novamente.')).toBeTruthy()
  })
})
```

**Atenção:** os `<label>` da `LoginPage` hoje envolvem o `<input>` sem `htmlFor`/`id`. `getByLabelText` funciona com label envolvente, então o teste passa **como está** — não mexa na tela nesta task. A ligação explícita por `id` entra na Task 5, junto do `Campo`.

- [ ] **Step 15: Rodar a suíte inteira**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: **110 testes / 10 arquivos**, todos verdes · build limpo · lint com **só** o warning alheio de `AuthContext.tsx:48`.
*(96 + 14: PedidosPage 4, PedidoDetalhePage 4, HomePage 3, LoginPage 3. Medido em 2026-08-06 — a estimativa original dizia 111 e estava errada por um teste de LoginPage que a aritmética contou e o código não define.)*

- [ ] **Step 16: Provar que a rede morde — mutação na `HomePage` e na `LoginPage`**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M10 | `HomePage.tsx:26` — remover `setCarregando(false)` do `finally` | ≥ 1 |
| M11 | `HomePage.tsx:43` — trocar `{usuario.perfil}` por `{usuario.nomeUsuario}` | ≥ 1 |
| M12 | `LoginPage.tsx:26` — trocar a mensagem por `'Senha inválida.'` | ≥ 1 |
| M13 | `LoginPage.tsx:16` — trocar `estado.motivo === 'sessao-expirada'` por `false` | ≥ 1 |

- [ ] **Step 17: Commit**

```bash
git add web/src/pages/HomePage.test.tsx web/src/pages/LoginPage.test.tsx
git commit -m "test(web): rede de fumaca da HomePage e da LoginPage"
```

**Definition of done da Task 1:** as **7** telas têm arquivo de teste; suíte em **110** (medido); build e lint limpos; as 13 mutações medidas, cada uma com ≥ 1 morte, e o número reportado.

---

### Task 2: `ErroDeApi` e mensagens amigáveis de API

**Files:**
- Create: `web/src/api/erros.ts`
- Create: `web/src/api/erros.test.ts`
- Modify: `web/src/api/cadastros.ts` (os 12 `throw new Error(...)`)
- Modify: `web/src/api/cadastros.test.ts` (acrescentar um `describe` no fim)

**Interfaces:**
- Consumes: nada de tasks anteriores.
- Produces: `class ErroDeApi extends Error { readonly status: number }` e `mensagemDeErro(e: unknown, fallback: string): string`, ambos de `web/src/api/erros.ts`. **As Tasks 8–11 chamam `mensagemDeErro` em todo `catch` de tela.**

**A dívida que esta task fecha** (spec §9, rastreada desde a Fase 1A): hoje toda falha vira o mesmo texto — `'Não foi possível carregar os setores.'` — não importa se foi 403 de perfil, 404 de registro sumido, sessão expirada ou wifi caído. O usuário lê "não foi possível" e não sabe se tenta de novo, se chama o administrador, ou se o registro não existe mais.

**O que NÃO muda:** a assinatura pública de `cadastros.ts` (tudo continua lançando `Error`, já que `ErroDeApi extends Error`), os textos das mensagens existentes, e o `try/catch` do F2 nas telas. Os **16** `rejects.toThrow()` de `cadastros.test.ts` seguem válidos — foram conferidos no disco: **nenhum passa argumento de mensagem**.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Tests  110 passed (110)` — número **medido** ao fim da Task 1, não estimado. Se divergir, pare e reporte.

- [ ] **Step 2: Escrever o teste de `mensagemDeErro`, que ainda não existe**

`web/src/api/erros.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { ErroDeApi, mensagemDeErro } from './erros'

const PADRAO = 'Não foi possível carregar os setores.'

describe('mensagemDeErro', () => {
  it('explica sessão expirada no 401', () => {
    expect(mensagemDeErro(new ErroDeApi(401, 'x'), PADRAO)).toBe('Sua sessão expirou. Entre novamente.')
  })

  it('explica falta de permissão no 403', () => {
    // O caso mais frequente na fábrica: Operador clicando em ação de Administrador. Hoje ele lê
    // "Não foi possível alterar o setor", tenta de novo, e recebe o mesmo 403.
    expect(mensagemDeErro(new ErroDeApi(403, 'x'), PADRAO))
      .toBe('Seu perfil não tem permissão para esta ação.')
  })

  it('explica registro inexistente no 404', () => {
    expect(mensagemDeErro(new ErroDeApi(404, 'x'), PADRAO)).toBe('Este registro não existe mais.')
  })

  it('explica falha do servidor no 500', () => {
    expect(mensagemDeErro(new ErroDeApi(500, 'x'), PADRAO))
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
  })

  it('explica falha do servidor no 503 também, não só no 500', () => {
    // A guarda é `>= 500`, não `=== 500`: hardcodar o 500 deixaria 502/503/504 — os status que um
    // proxy ou uma API fora do ar realmente devolvem — caindo no texto genérico.
    expect(mensagemDeErro(new ErroDeApi(503, 'x'), PADRAO))
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
  })

  it('cai no texto da tela para status que não têm explicação própria', () => {
    // 400 é falha de validação: quem sabe o que dizer sobre ela é a tela, não esta função.
    expect(mensagemDeErro(new ErroDeApi(400, 'x'), PADRAO)).toBe(PADRAO)
  })

  it('explica queda de rede', () => {
    // `fetch` rejeita com TypeError quando a requisição nem sai — o sintoma do wifi de fábrica.
    expect(mensagemDeErro(new TypeError('Failed to fetch'), PADRAO))
      .toBe('Sem conexão com o servidor. Verifique a rede e tente de novo.')
  })

  it('cai no texto da tela para erro que não é de API nem de rede', () => {
    expect(mensagemDeErro(new Error('qualquer outra coisa'), PADRAO)).toBe(PADRAO)
    expect(mensagemDeErro('nem erro é', PADRAO)).toBe(PADRAO)
  })
})

describe('ErroDeApi', () => {
  it('carrega o status e continua sendo um Error', () => {
    // O `instanceof Error` é o que mantém os 16 `rejects.toThrow()` de cadastros.test.ts válidos.
    const e = new ErroDeApi(409, 'Falha na requisição (409).')
    expect(e).toBeInstanceOf(Error)
    expect(e.status).toBe(409)
    expect(e.message).toBe('Falha na requisição (409).')
    expect(e.name).toBe('ErroDeApi')
  })
})
```

- [ ] **Step 3: Rodar para ver falhar**

```bash
cd web && npm test -- erros
```

Expected: FAIL — `Failed to resolve import "./erros"`.

- [ ] **Step 4: Escrever `erros.ts`**

`web/src/api/erros.ts`:

```ts
/**
 * Erro de API que CARREGA o status. Antes desta classe o status vivia só dentro da string da
 * mensagem (`Falha ao listar setores (403).`), então a tela não tinha como distinguir "seu perfil
 * não pode" de "o servidor caiu" sem fazer parse de texto.
 *
 * Continua sendo `Error`: as funções de `cadastros.ts` não mudam de contrato, e todo
 * `rejects.toThrow()` que já existia segue valendo.
 */
export class ErroDeApi extends Error {
  constructor(readonly status: number, mensagem: string) {
    super(mensagem)
    this.name = 'ErroDeApi'
  }
}

/**
 * Traduz o que caiu no `catch` para uma frase que diga ao usuário o que fazer a seguir.
 *
 * `fallback` é o texto específico da tela ("Não foi possível carregar os setores.") e continua
 * sendo o destino de tudo que esta função não sabe explicar melhor — status de validação (400),
 * erro de programação, valor que nem erro é. A função nunca INVENTA explicação: ou reconhece o
 * caso, ou devolve o que a tela já diria.
 *
 * O 401 aqui é informativo, não corretivo: quem devolve o usuário ao login é o `onSessionLost` do
 * `client.ts`, depois de o refresh falhar. Esta mensagem cobre a janela em que a tela ainda está
 * montada.
 */
export function mensagemDeErro(e: unknown, fallback: string): string {
  if (e instanceof ErroDeApi) {
    if (e.status === 401) return 'Sua sessão expirou. Entre novamente.'
    if (e.status === 403) return 'Seu perfil não tem permissão para esta ação.'
    if (e.status === 404) return 'Este registro não existe mais.'
    if (e.status >= 500) return 'O servidor não respondeu como esperado. Tente de novo em instantes.'
    return fallback
  }
  // `fetch` rejeita com TypeError quando a requisição nem sai (DNS, rede, CORS). É o único erro
  // de rede que chega aqui — o resto do caminho já virou Response.
  if (e instanceof TypeError) return 'Sem conexão com o servidor. Verifique a rede e tente de novo.'
  return fallback
}
```

- [ ] **Step 5: Rodar para ver passar**

```bash
cd web && npm test -- erros
```

Expected: `Tests  9 passed (9)`.

- [ ] **Step 6: Trocar os 12 `throw` de `cadastros.ts`**

Acrescente o import no topo de `web/src/api/cadastros.ts`:

```ts
import { apiFetch } from './client'
import { ErroDeApi } from './erros'
```

Troque **cada** `throw new Error(...)` mantendo a mensagem **caractere por caractere**. Lista completa, na ordem do arquivo (linhas de hoje):

| Linha | Mensagem (inalterada) | Status a passar |
|---|---|---|
| 32 | `Falha na requisição (409): formato de conflito inesperado.` | `409` |
| 34 | `Falha na requisição (${resp.status}).` | `resp.status` |
| 40 | `Falha ao listar setores (${resp.status}).` | `resp.status` |
| 58 | `Falha ao alterar o setor (${resp.status}).` | `resp.status` |
| 81 | `Falha ao listar materiais (${resp.status}).` | `resp.status` |
| 100 | `Falha ao alterar o material (${resp.status}).` | `resp.status` |
| 132 | `Falha ao listar pedidos (${resp.status}).` | `resp.status` |
| 138 | `Falha ao carregar o pedido (${resp.status}).` | `resp.status` |
| 174 | `Falha ao listar agrupamentos (${resp.status}).` | `resp.status` |
| 204 | `Falha ao excluir o agrupamento (${resp.status}).` | `resp.status` |
| 254 | `Falha ao listar componentes (${resp.status}).` | `resp.status` |
| 275 | `Falha ao alterar o componente (${resp.status}).` | `resp.status` |

Forma de cada troca:

```ts
// antes
if (!resp.ok) throw new Error(`Falha ao listar setores (${resp.status}).`)
// depois
if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar setores (${resp.status}).`)
```

Confirme que não sobrou nenhum:

```bash
cd web && grep -c "throw new Error" src/api/cadastros.ts
```

Expected: `0`.

- [ ] **Step 7: Rodar a suíte inteira — nenhum teste antigo pode quebrar**

```bash
cd web && npm test
```

Expected: `Tests  119 passed (119)` (110 + 9). **Se algum teste de `cadastros.test.ts` quebrar, você mudou uma mensagem.** Confira com `git diff` e restaure o texto exato.

- [ ] **Step 8: Acrescentar a prova de que o status viaja**

No **fim** de `web/src/api/cadastros.test.ts`, depois do último `describe` existente:

```ts
describe('status nas falhas de API', () => {
  // Sem estes testes, trocar `resp.status` por um literal (`403`, `500`) em qualquer um dos 12
  // `throw` passaria verde: os `rejects.toThrow()` que já existem não olham o status, só o fato de
  // ter lançado. E é o status que a tela usa para escolher a mensagem.
  it('listarSetores propaga o status da resposta', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'x' }), { status: 403 }),
    ))

    await expect(listarSetores(false)).rejects.toMatchObject({ name: 'ErroDeApi', status: 403 })
  })

  it('lerOuFalhar propaga o status da resposta', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'x' }), { status: 500 }),
    ))

    await expect(criarSetor('Solda')).rejects.toMatchObject({ name: 'ErroDeApi', status: 500 })
  })

  it('definirAtivoComponente propaga o status da resposta', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })))

    await expect(definirAtivoComponente(4, false)).rejects.toMatchObject({ name: 'ErroDeApi', status: 403 })
  })
})
```

**Confira os imports do topo do arquivo:** `listarSetores`, `criarSetor` e `definirAtivoComponente` já são importados lá. Não duplique.

- [ ] **Step 9: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  123 passed (123)` · build limpo · lint só com o warning alheio de `AuthContext.tsx:48`.

- [ ] **Step 10: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `erros.ts` — `e.status >= 500` → `e.status === 500` | ≥ 1 (o teste do 503) |
| M2 | `erros.ts` — remover o ramo `if (e.status === 403)` | ≥ 1 |
| M3 | `erros.ts` — `e instanceof TypeError` → `false` | ≥ 1 |
| M4 | `erros.ts` — remover o `return fallback` de dentro do bloco `ErroDeApi` | ≥ 1 |
| M5 | `cadastros.ts:40` — `resp.status` → `500` no `ErroDeApi` de `listarSetores` | ≥ 1 |
| M6 | `cadastros.ts:34` — `resp.status` → `400` no `lerOuFalhar` | ≥ 1 |

- [ ] **Step 11: Commit**

```bash
git add web/src/api/erros.ts web/src/api/erros.test.ts web/src/api/cadastros.ts web/src/api/cadastros.test.ts
git commit -m "feat(web): ErroDeApi com status e mensagens amigaveis de falha"
```

**Definition of done da Task 2:** suíte em **123**; `grep -c "throw new Error" src/api/cadastros.ts` = 0; as 6 mutações medidas com ≥ 1 morte; **nenhuma tela modificada** — as telas passam a chamar `mensagemDeErro` nas Tasks 8–11.

---

### Task 3: `useBuscaPaginada` — debounce, cancelamento, clamp e o W3

**Files:**
- Create: `web/src/hooks/useBuscaPaginada.ts`
- Create: `web/src/hooks/useBuscaPaginada.test.tsx`

**Interfaces:**
- Consumes: nada.
- Produces (a Task 9 é quem consome):

```ts
export interface PaginaDeBusca<T> { itens: T[]; total: number }
export interface FiltroDeBusca { busca: string; incluirInativos: boolean; pagina: number; tamanho: number }
export interface OpcoesDeBuscaPaginada<T> {
  buscar: (filtro: FiltroDeBusca) => Promise<PaginaDeBusca<T>>
  tamanhoInicial?: number
  atrasoDoDebounce?: number
}
export interface BuscaPaginada<T> {
  itens: T[]; total: number; totalDePaginas: number
  textoDaBusca: string; busca: string
  incluirInativos: boolean; pagina: number; tamanho: number
  carregando: boolean; erro: unknown
  mudarBusca(valor: string): void
  mudarInativos(valor: boolean): void
  mudarTamanho(valor: number): void
  irParaPagina(valor: number): void
  recarregar(): Promise<void>
}
export function useBuscaPaginada<T>(opcoes: OpcoesDeBuscaPaginada<T>): BuscaPaginada<T>
```

**As quatro dívidas que ele fecha, e por que juntas:** debounce, cancelamento, clamp de página e reset de filtro são a **mesma** propriedade vista de quatro ângulos — *o que está na tela corresponde ao filtro que o usuário vê*. Resolver três e deixar uma produz a cobertura desigual que a spec §9 nomeia.

**O hook nasce sem consumidor.** Decisão registrada nas Global Constraints: acoplá-lo à `ComponentesPage` aqui obrigaria a reescrever `ComponentesPage.test.tsx` (987 linhas, 33 testes) duas vezes — uma pelo hook, outra pelo re-layout da Task 9.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Tests  123 passed (123)`.

- [ ] **Step 2: Escrever o teste do hook, que ainda não existe**

`web/src/hooks/useBuscaPaginada.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, act } from '@testing-library/react'
import { useBuscaPaginada, type FiltroDeBusca, type PaginaDeBusca } from './useBuscaPaginada'

afterEach(cleanup)

interface Item { id: number; nome: string }

// Componente-hospedeiro: um hook não renderiza nada, então a prova passa por uma tela mínima que
// expõe cada saída num nó com texto asserível. Deliberadamente burro — se ele tiver lógica, o
// teste passa a provar o hospedeiro, não o hook.
function Hospedeiro({ buscar, atraso = 300 }: {
  buscar: (f: FiltroDeBusca) => Promise<PaginaDeBusca<Item>>
  atraso?: number
}) {
  const b = useBuscaPaginada<Item>({ buscar, atrasoDoDebounce: atraso })
  return (
    <div>
      <input aria-label="busca" value={b.textoDaBusca} onChange={(e) => b.mudarBusca(e.target.value)} />
      <input aria-label="inativos" type="checkbox" checked={b.incluirInativos}
             onChange={(e) => b.mudarInativos(e.target.checked)} />
      <select aria-label="tamanho" value={b.tamanho} onChange={(e) => b.mudarTamanho(Number(e.target.value))}>
        <option value={20}>20</option>
        <option value={50}>50</option>
      </select>
      <button onClick={() => b.irParaPagina(b.pagina + 1)}>Próxima</button>
      <p>pagina:{b.pagina}</p>
      <p>total:{b.total}</p>
      <p>paginas:{b.totalDePaginas}</p>
      {b.carregando && <p>carregando</p>}
      {b.erro !== null && <p>erro</p>}
      <ul>{b.itens.map((i) => <li key={i.id}>{i.nome}</li>)}</ul>
    </div>
  )
}

function pagina(itens: Item[], total = itens.length): PaginaDeBusca<Item> {
  return { itens, total }
}

// Timers falsos com `advanceTimersByTimeAsync`, que avança o relógio E drena as microtasks — sem
// ele o `await` da resposta ficaria pendurado e o teste leria um estado intermediário.
// `findBy*` NÃO é usado neste arquivo, de propósito: ele depende do `waitFor`, que tem seu próprio
// relacionamento com timers falsos. Avançar explicitamente e ler com `getBy*` é determinístico.
async function avancar(ms: number) {
  await act(async () => { await vi.advanceTimersByTimeAsync(ms) })
}

describe('useBuscaPaginada', () => {
  beforeEach(() => { vi.useFakeTimers() })
  afterEach(() => { vi.useRealTimers() })

  it('carrega na montagem e mostra os itens', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([{ id: 1, nome: 'Corte' }]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.getByText('Corte')).toBeTruthy()
    expect(buscar).toHaveBeenCalledTimes(1)
    expect(buscar).toHaveBeenCalledWith({ busca: '', incluirInativos: false, pagina: 1, tamanho: 20 })
  })

  it('faz UMA requisição para três teclas digitadas em sequência', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(buscar).toHaveBeenCalledTimes(1) // só a carga inicial

    const campo = screen.getByLabelText('busca')
    fireEvent.change(campo, { target: { value: 'S' } })
    fireEvent.change(campo, { target: { value: 'SU' } })
    fireEvent.change(campo, { target: { value: 'SUP' } })

    // Antes de o debounce vencer, nada saiu. Hoje, sem o hook, isto são TRÊS requisições — foi
    // medido na review da Task 6 da 1B.
    await avancar(299)
    expect(buscar).toHaveBeenCalledTimes(1)

    await avancar(1)
    expect(buscar).toHaveBeenCalledTimes(2)
    expect(buscar).toHaveBeenLastCalledWith({ busca: 'SUP', incluirInativos: false, pagina: 1, tamanho: 20 })
  })

  it('mostra o texto digitado no campo imediatamente, sem esperar o debounce', async () => {
    // O debounce atrasa a REQUISIÇÃO, nunca o campo. Um campo que só atualiza depois de 300 ms é
    // percebido como teclado travado.
    const buscar = vi.fn().mockResolvedValue(pagina([]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.change(screen.getByLabelText('busca'), { target: { value: 'SUP' } })

    expect((screen.getByLabelText('busca') as HTMLInputElement).value).toBe('SUP')
  })

  it('ignora a resposta de uma busca que já foi superada por outra', async () => {
    // A corrida real: "SU" demora 200 ms e "SUP" responde na hora. Sem guarda de sequência, a
    // resposta de "SU" chega DEPOIS e sobrescreve a lista — campo mostrando SUP, lista mostrando o
    // resultado de SU. Foi provado empiricamente na review da Task 6.
    const buscar = vi.fn((f: FiltroDeBusca) => {
      if (f.busca === 'SU') {
        return new Promise<PaginaDeBusca<Item>>((resolve) => {
          setTimeout(() => resolve(pagina([{ id: 1, nome: 'RESULTADO DE SU' }])), 200)
        })
      }
      return Promise.resolve(pagina([{ id: 2, nome: 'RESULTADO DE SUP' }]))
    })

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    const campo = screen.getByLabelText('busca')
    fireEvent.change(campo, { target: { value: 'SU' } })
    await avancar(300)   // dispara "SU", que só responde em +200 ms
    fireEvent.change(campo, { target: { value: 'SUP' } })
    await avancar(300)   // dispara "SUP", que responde na hora
    await avancar(500)   // deixa a resposta atrasada de "SU" chegar

    expect(screen.getByText('RESULTADO DE SUP')).toBeTruthy()
    expect(screen.queryByText('RESULTADO DE SU')).toBeNull()
  })

  it('volta para a página 1 quando a busca muda', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([], 100))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)
    expect(screen.getByText('pagina:2')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('busca'), { target: { value: 'X' } })
    await avancar(300)

    expect(screen.getByText('pagina:1')).toBeTruthy()
  })

  it('volta para a página 1 quando o filtro de inativos muda', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([], 100))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)

    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)

    expect(screen.getByText('pagina:1')).toBeTruthy()
    expect(buscar).toHaveBeenLastCalledWith({ busca: '', incluirInativos: true, pagina: 1, tamanho: 20 })
  })

  it('volta para a página 1 quando o tamanho de página muda', async () => {
    // Os TRÊS resets têm teste próprio, e cada um exige uma montagem diferente. Confundir as
    // montagens foi o erro que deixou o terceiro reset sem prova na Fase 1B.
    const buscar = vi.fn().mockResolvedValue(pagina([], 100))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)

    fireEvent.change(screen.getByLabelText('tamanho'), { target: { value: '50' } })
    await avancar(0)

    expect(screen.getByText('pagina:1')).toBeTruthy()
    expect(buscar).toHaveBeenLastCalledWith({ busca: '', incluirInativos: false, pagina: 1, tamanho: 50 })
  })

  it('recua a página quando o total encolhe e a página atual deixa de existir', async () => {
    // Clamp: 100 itens = 5 páginas, o usuário vai para a 3, alguém inativa 90 itens, agora são 10
    // itens = 1 página. Sem clamp a tela fica numa página vazia, com cara de bug.
    let total = 100
    const buscar = vi.fn((f: FiltroDeBusca) =>
      Promise.resolve(pagina(f.pagina === 1 ? [{ id: 1, nome: 'Sobrou' }] : [], total)),
    )

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)
    expect(screen.getByText('pagina:3')).toBeTruthy()

    total = 10
    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)

    expect(screen.getByText('paginas:1')).toBeTruthy()
    expect(screen.getByText('pagina:1')).toBeTruthy()
    expect(screen.getByText('Sobrou')).toBeTruthy()
  })

  it('mostra "carregando" em TODA recarga, não só na primeira', async () => {
    // Este é o W3, deferido da Fase 1B com justificativa: `setCarregando(true)` no início de cada
    // carga não tinha prova nenhuma. Sem ele, depois da primeira carga o usuário nunca mais vê
    // sinal de atividade — a lista antiga fica parada na tela até a resposta nova trocar tudo de
    // repente. No wifi da fábrica são segundos olhando para dado errado sem saber.
    let liberar: (p: PaginaDeBusca<Item>) => void = () => {}
    const buscar = vi.fn()
      .mockResolvedValueOnce(pagina([{ id: 1, nome: 'Antigo' }]))
      .mockImplementationOnce(() => new Promise<PaginaDeBusca<Item>>((r) => { liberar = r }))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(screen.queryByText('carregando')).toBeNull()

    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)
    expect(screen.getByText('carregando')).toBeTruthy()
    expect(screen.getByText('Antigo')).toBeTruthy()  // a lista antiga segue visível, mas AVISADA

    await act(async () => { liberar(pagina([{ id: 2, nome: 'Novo' }])) })
    expect(screen.queryByText('carregando')).toBeNull()
    expect(screen.getByText('Novo')).toBeTruthy()
  })

  it('expõe o erro e o limpa quando uma recarga posterior tem sucesso', async () => {
    const buscar = vi.fn()
      .mockRejectedValueOnce(new Error('rede caiu'))
      .mockResolvedValueOnce(pagina([{ id: 1, nome: 'Corte' }]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(screen.getByText('erro')).toBeTruthy()

    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)

    expect(screen.queryByText('erro')).toBeNull()
    expect(screen.getByText('Corte')).toBeTruthy()
  })

  it('sai do estado de carregando mesmo quando a busca falha', async () => {
    const buscar = vi.fn().mockRejectedValue(new Error('rede caiu'))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.queryByText('carregando')).toBeNull()
  })

  it('calcula totalDePaginas pelo total do servidor, não pelo tamanho da página recebida', async () => {
    // `total` é o total SOB O FILTRO, não `itens.length`. Confundir os dois faz a paginação sumir
    // exatamente quando ela é necessária.
    const buscar = vi.fn().mockResolvedValue(pagina([{ id: 1, nome: 'Corte' }], 41))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.getByText('paginas:3')).toBeTruthy()  // ceil(41 / 20)
  })

  it('mantém pelo menos uma página quando não há nada', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([], 0))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.getByText('paginas:1')).toBeTruthy()
    expect(screen.getByText('pagina:1')).toBeTruthy()
  })
})
```

- [ ] **Step 3: Rodar para ver falhar**

```bash
cd web && npm test -- useBuscaPaginada
```

Expected: FAIL — `Failed to resolve import "./useBuscaPaginada"`.

- [ ] **Step 4: Escrever o hook**

`web/src/hooks/useBuscaPaginada.ts`:

```ts
import { useCallback, useEffect, useRef, useState } from 'react'

export interface PaginaDeBusca<T> {
  itens: T[]
  total: number
}

export interface FiltroDeBusca {
  busca: string
  incluirInativos: boolean
  pagina: number
  tamanho: number
}

export interface OpcoesDeBuscaPaginada<T> {
  buscar: (filtro: FiltroDeBusca) => Promise<PaginaDeBusca<T>>
  tamanhoInicial?: number
  atrasoDoDebounce?: number
}

export interface BuscaPaginada<T> {
  itens: T[]
  total: number
  totalDePaginas: number
  /** O que está no campo AGORA. Atualiza a cada tecla. */
  textoDaBusca: string
  /** O que foi realmente consultado. Atrasa `atrasoDoDebounce` em relação ao campo. */
  busca: string
  incluirInativos: boolean
  pagina: number
  tamanho: number
  carregando: boolean
  /** O erro cru. Quem traduz para texto de tela é `mensagemDeErro` — o hook não conhece domínio. */
  erro: unknown
  mudarBusca(valor: string): void
  mudarInativos(valor: boolean): void
  mudarTamanho(valor: number): void
  irParaPagina(valor: number): void
  recarregar(): Promise<void>
}

/**
 * Busca paginada com as quatro propriedades que a Fase 1B resolveu à mão na `ComponentesPage` — e,
 * segundo a review da Task 6, resolveu errado ou sem prova:
 *
 * 1. **Debounce** — digitar "SUP" faz UMA requisição, não três.
 * 2. **Cancelamento por sequência** — vence a última requisição ENVIADA, não a última a RESPONDER.
 *    Sem isto o campo mostra "SUP" e a lista mostra o resultado de "SU".
 * 3. **Clamp de página** — se o total encolhe e a página atual deixa de existir, recua em vez de
 *    mostrar lista vazia com cara de bug.
 * 4. **Reset de filtro** — mudar busca, tamanho ou inativos volta para a página 1.
 *
 * Não usa `AbortController`: abortar exigiria que cada função de listagem aceitasse um
 * `AbortSignal`, ou seja, mudar a assinatura pública de `cadastros.ts` e o arquivo de teste de 679
 * linhas dele. A guarda de sequência entrega a mesma propriedade observável — o efeito da resposta
 * obsoleta é descartado — ao custo de a requisição obsoleta ainda trafegar. Já adjudicado duas
 * vezes neste projeto; não reabrir sem medição nova.
 */
export function useBuscaPaginada<T>({
  buscar,
  tamanhoInicial = 20,
  atrasoDoDebounce = 300,
}: OpcoesDeBuscaPaginada<T>): BuscaPaginada<T> {
  const [textoDaBusca, setTextoDaBusca] = useState('')
  const [busca, setBusca] = useState('')
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [pagina, setPagina] = useState(1)
  const [tamanho, setTamanho] = useState(tamanhoInicial)
  const [itens, setItens] = useState<T[]>([])
  const [total, setTotal] = useState(0)
  // `true` na montagem: a primeira carga já está a caminho quando o primeiro render acontece.
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<unknown>(null)

  const sequenciaRef = useRef(0)

  // A função de busca vive num ref para que um chamador que passe uma lambda nova a cada render
  // não vire laço infinito de requisições: com ela nas dependências de `carregar`, uma lambda
  // inline dispararia carga -> render -> lambda nova -> carga. O ref quebra o ciclo, e o
  // exhaustive-deps não cobra refs.
  const buscarRef = useRef(buscar)
  useEffect(() => { buscarRef.current = buscar })

  // Debounce: o campo (`textoDaBusca`) anda na hora; a consulta (`busca`) espera o silêncio.
  // O reset de página vive AQUI, e não em `mudarBusca`, para acontecer junto com a consulta nova —
  // resetar a cada tecla dispararia uma carga por tecla, que é o que o debounce impede.
  useEffect(() => {
    if (textoDaBusca === busca) return
    const timer = setTimeout(() => {
      setPagina(1)
      setBusca(textoDaBusca)
    }, atrasoDoDebounce)
    return () => clearTimeout(timer)
  }, [textoDaBusca, busca, atrasoDoDebounce])

  const carregar = useCallback(async () => {
    const minhaSequencia = ++sequenciaRef.current
    setCarregando(true)
    try {
      const resposta = await buscarRef.current({ busca, incluirInativos, pagina, tamanho })
      if (minhaSequencia !== sequenciaRef.current) return
      setItens(resposta.itens)
      setTotal(resposta.total)
      setErro(null)
    } catch (e) {
      if (minhaSequencia !== sequenciaRef.current) return
      setErro(e)
    } finally {
      // A guarda cobre os QUATRO efeitos pós-`await`, não só os dois óbvios: sem ela a resposta
      // obsoleta apagaria o "carregando" da requisição que ainda está em voo.
      if (minhaSequencia === sequenciaRef.current) setCarregando(false)
    }
  }, [busca, incluirInativos, pagina, tamanho])

  useEffect(() => { carregar() }, [carregar])

  const totalDePaginas = Math.max(1, Math.ceil(total / tamanho))

  // Clamp: só depois de a carga assentar, para não recuar a página no meio de uma requisição cujo
  // `total` ainda é o antigo.
  useEffect(() => {
    if (!carregando && pagina > totalDePaginas) setPagina(totalDePaginas)
  }, [carregando, pagina, totalDePaginas])

  const mudarBusca = useCallback((valor: string) => { setTextoDaBusca(valor) }, [])

  const mudarInativos = useCallback((valor: boolean) => {
    setPagina(1)
    setIncluirInativos(valor)
  }, [])

  const mudarTamanho = useCallback((valor: number) => {
    setPagina(1)
    setTamanho(valor)
  }, [])

  const irParaPagina = useCallback((valor: number) => { setPagina(valor) }, [])

  return {
    itens, total, totalDePaginas,
    textoDaBusca, busca,
    incluirInativos, pagina, tamanho,
    carregando, erro,
    mudarBusca, mudarInativos, mudarTamanho, irParaPagina,
    recarregar: carregar,
  }
}
```

- [ ] **Step 5: Rodar para ver passar**

```bash
cd web && npm test -- useBuscaPaginada
```

Expected: `Tests  13 passed (13)`. Se o teste do clamp falhar por uma renderização a mais, **não relaxe a asserção** — confira a ordem dos efeitos.

- [ ] **Step 6: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  136 passed (136)` (123 + 13) · build limpo · lint só com o warning alheio.

- [ ] **Step 7: Medir as mutações**

| # | Mutação em `useBuscaPaginada.ts` | Mortes | O que passa a acontecer em produção se ela sobreviver |
|---|---|---|---|
| M1 | `atrasoDoDebounce` → `0` no `setTimeout` | ≥ 1 | 3 teclas = 3 requisições |
| M2 | remover a guarda de sequência do bloco de sucesso | ≥ 1 | resposta obsoleta sobrescreve a lista |
| M3 | remover `setCarregando(true)` do início de `carregar` | ≥ 1 | **é o W3** — sem sinal de atividade nas recargas |
| M4 | remover o `useEffect` do clamp | ≥ 1 | página órfã depois que o total encolhe |
| M5 | remover `setPagina(1)` do debounce | ≥ 1 | buscar estando na página 7 mostra vazio |
| M6 | remover `setPagina(1)` de `mudarInativos` | ≥ 1 | idem, pelo checkbox |
| M7 | remover `setPagina(1)` de `mudarTamanho` | ≥ 1 | idem, pelo seletor |
| M8 | `Math.max(1, …)` → `Math.ceil(total / tamanho)` | ≥ 1 | "Página 1 de 0" na lista vazia |
| M9 | `setTotal(resposta.total)` → `setTotal(resposta.itens.length)` | ≥ 1 | paginação some justamente quando é necessária |
| M10 | remover `setErro(null)` do sucesso | ≥ 1 | erro antigo gruda na tela |
| M11 | remover a guarda de sequência do `finally` | ≥ 1 | "carregando" some com a resposta errada |

**M5, M6 e M7 exigem montagens DIFERENTES** — cada reset tem o seu teste, e as montagens são opostas. Medir as três com a mesma montagem faz você reportar morte onde não há; foi assim que o terceiro reset ficou sem prova na Fase 1B.

- [ ] **Step 8: Commit**

```bash
git add web/src/hooks/useBuscaPaginada.ts web/src/hooks/useBuscaPaginada.test.tsx
git commit -m "feat(web): useBuscaPaginada com debounce, cancelamento, clamp e W3"
```

**Definition of done da Task 3:** suíte em **136**; as 11 mutações medidas com ≥ 1 morte cada; **nenhuma tela modificada**; build e lint limpos.

---

### Task 4: Tokens de tema e a prova automatizada de contraste

**Files:**
- Create: `web/src/tema/contraste.ts`
- Create: `web/src/tema/contraste.test.ts`
- Modify: `web/src/index.css` (hoje tem **uma** linha)
- Modify: `web/src/components/TelaCarregando.tsx` (passa a usar os tokens)

**Interfaces:**
- Consumes: nada.
- Produces: as classes utilitárias do Tailwind derivadas dos tokens — `bg-chrome`, `text-marca`, `bg-acao`, `text-tinta`, `border-borda-campo`, `font-mono`, etc. **Todas as tasks de 5 em diante usam só estas classes; nenhuma volta a escrever `text-gray-*` ou `bg-gray-*`.**

**A propriedade que esta task instala, e é a mais valiosa da fase:** a spec §3 diz *"todo tom novo que a implementação precisar tem de ser medido antes de entrar"*, e registra a armadilha — tons claros de verde-água e âmbar reprovam em AA como texto sobre branco **apesar de** funcionarem como fundo de botão com texto branco. Escolher paleta olhando só o botão é o erro clássico. Aqui isso deixa de depender de disciplina: o teste **lê o `@theme` do `index.css`** e mede. Token novo sem par declarado **reprova a suíte**.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Tests  136 passed (136)`.

- [ ] **Step 2: Escrever as funções de contraste, que ainda não existem**

`web/src/tema/contraste.ts`:

```ts
/**
 * Luminância relativa de uma cor `#RRGGBB`, pela fórmula da WCAG 2.x.
 *
 * Implementada à mão em nove linhas em vez de trazer uma dependência: a fase inteira existe para
 * *reduzir* superfície (a Task 5 da 1B recusou até `jest-dom`), e o que se ganharia era isto aqui.
 */
export function luminanciaRelativa(hex: string): number {
  const canais = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
  const [r, g, b] = canais.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** Razão de contraste entre duas cores `#RRGGBB`. Vai de 1 (idênticas) a 21 (preto/branco). */
export function razaoDeContraste(a: string, b: string): number {
  const la = luminanciaRelativa(a)
  const lb = luminanciaRelativa(b)
  const [claro, escuro] = la > lb ? [la, lb] : [lb, la]
  return (claro + 0.05) / (escuro + 0.05)
}
```

- [ ] **Step 3: Escrever o teste que MEDE o `@theme`**

`web/src/tema/contraste.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { razaoDeContraste, luminanciaRelativa } from './contraste'

// Lê o CSS de verdade, em vez de duplicar a paleta num objeto TS. Duplicar criaria a divergência
// clássica: alguém muda o token no CSS, o teste segue medindo o valor velho e reporta verde sobre
// uma cor que já não está na tela.
const css = readFileSync(new URL('../index.css', import.meta.url), 'utf8')

function tokens(): Record<string, string> {
  const bloco = css.match(/@theme\s*\{([\s\S]*?)\n\}/)
  if (!bloco) throw new Error('bloco @theme não encontrado em index.css')
  const mapa: Record<string, string> = {}
  for (const [, nome, valor] of bloco[1].matchAll(/--color-([\w-]+):\s*(#[0-9a-fA-F]{6})\s*;/g)) {
    mapa[nome] = valor
  }
  return mapa
}

const T = tokens()

// AA da WCAG: 4.5:1 para texto normal, 3:1 para componente de interface (borda de campo, ícone).
const TEXTO = 4.5
const INTERFACE = 3

const PARES: Array<{ frente: string; fundo: string; minimo: number; onde: string }> = [
  { frente: 'tinta', fundo: 'superficie', minimo: TEXTO, onde: 'texto padrão sobre cartão' },
  { frente: 'tinta', fundo: 'fundo', minimo: TEXTO, onde: 'texto padrão sobre o fundo da página' },
  { frente: 'tinta-fraca', fundo: 'superficie', minimo: TEXTO, onde: 'texto secundário e legendas' },
  { frente: 'superficie', fundo: 'chrome', minimo: TEXTO, onde: 'texto da barra de navegação' },
  { frente: 'marca', fundo: 'chrome', minimo: TEXTO, onde: 'logo sobre o chrome' },
  { frente: 'superficie', fundo: 'acao', minimo: TEXTO, onde: 'rótulo do botão primário' },
  { frente: 'superficie', fundo: 'acao-forte', minimo: TEXTO, onde: 'rótulo do primário sob hover' },
  { frente: 'acao', fundo: 'superficie', minimo: TEXTO, onde: 'link de ação e botão secundário' },
  { frente: 'acao', fundo: 'acao-fundo', minimo: TEXTO, onde: 'texto da pílula' },
  { frente: 'negativo', fundo: 'superficie', minimo: TEXTO, onde: 'mensagem de erro' },
  { frente: 'superficie', fundo: 'negativo', minimo: TEXTO, onde: 'rótulo do botão de perigo' },
  { frente: 'positivo-texto', fundo: 'superficie', minimo: TEXTO, onde: 'rótulo de estado aprovado/ativo' },
  { frente: 'superficie', fundo: 'positivo', minimo: TEXTO, onde: 'selo de estado positivo com texto branco' },
  { frente: 'borda-campo', fundo: 'superficie', minimo: INTERFACE, onde: 'borda de input e de botão secundário' },
  { frente: 'acao', fundo: 'fundo', minimo: INTERFACE, onde: 'anel de foco sobre o fundo da página' },
]

/**
 * Tokens que existem só como fundo ou separador e por isso não têm par de contraste próprio.
 * Cada exceção precisa de motivo escrito — a lista é curta de propósito: é ela que impede o
 * "declaro como decorativo e passo" de virar a saída fácil.
 */
const SEM_EXIGENCIA: Record<string, string> = {
  borda: 'separador decorativo entre linhas de lista; nenhuma informação depende de enxergá-lo',
}

describe('paleta declarada em index.css', () => {
  it('declara todos os tokens que o plano da fase fixou', () => {
    for (const nome of [
      'chrome', 'marca', 'acao', 'acao-forte', 'acao-fundo',
      'positivo', 'positivo-texto', 'negativo',
      'tinta', 'tinta-fraca', 'borda', 'borda-campo', 'fundo', 'superficie',
    ]) {
      expect(T[nome], `token --color-${nome} ausente em index.css`).toBeTruthy()
    }
  })

  it.each(PARES)('$frente sobre $fundo passa AA ($onde)', ({ frente, fundo, minimo }) => {
    const razao = razaoDeContraste(T[frente], T[fundo])
    expect(razao, `${frente} sobre ${fundo} deu ${razao.toFixed(2)}:1`).toBeGreaterThanOrEqual(minimo)
  })

  it('não deixa entrar tom novo sem medição', () => {
    // A guarda que faz a regra da spec §3 valer sozinha: quem acrescentar um `--color-*` tem de
    // declarar onde ele é usado, ou registrá-lo em SEM_EXIGENCIA com motivo.
    const medidos = new Set(PARES.flatMap((p) => [p.frente, p.fundo]))
    const naoMedidos = Object.keys(T).filter((n) => !medidos.has(n) && !(n in SEM_EXIGENCIA))

    expect(naoMedidos, `tokens sem par de contraste declarado: ${naoMedidos.join(', ')}`).toEqual([])
  })

  it('mantém verde e vermelho reservados a estado — nenhum deles é o chrome nem a ação', () => {
    // A regra que sustenta a paleta (spec §3): cor de identidade nunca significa estado. Ela é o
    // que faz a tela de Qualidade da Fase 5 funcionar, quando "Aprovado" e "Abrir retrabalho"
    // aparecem na mesma linha.
    expect(T.chrome).not.toBe(T.positivo)
    expect(T.chrome).not.toBe(T.negativo)
    expect(T.acao).not.toBe(T.positivo)
    expect(T.acao).not.toBe(T.negativo)
  })
})

describe('razaoDeContraste', () => {
  it('dá 21 entre preto e branco', () => {
    expect(razaoDeContraste('#000000', '#ffffff')).toBeCloseTo(21, 1)
  })

  it('dá 1 para a mesma cor', () => {
    expect(razaoDeContraste('#134E4A', '#134E4A')).toBeCloseTo(1, 5)
  })

  it('é simétrica', () => {
    expect(razaoDeContraste('#134E4A', '#ffffff')).toBeCloseTo(razaoDeContraste('#ffffff', '#134E4A'), 10)
  })

  it('linearizea o canal escuro pela rampa, não pela potência', () => {
    // Abaixo de 0.03928 a WCAG usa `c / 12.92`, não a exponencial. Trocar por `** 2.4` em toda a
    // faixa erra justamente nos tons muito escuros — que é onde vive o chrome.
    expect(luminanciaRelativa('#050505')).toBeCloseTo(0.00152, 4)
  })
})
```

- [ ] **Step 4: Rodar para ver falhar**

```bash
cd web && npm test -- contraste
```

Expected: FAIL — `bloco @theme não encontrado em index.css`.

- [ ] **Step 5: Escrever o `@theme`**

`web/src/index.css`, substituindo a única linha de hoje:

```css
@import "tailwindcss";

/*
  Tokens da Fase 1D. A paleta e a regra que a sustenta estão em
  `docs/superpowers/specs/2026-08-06-fase-1d-ui-e-ux-design.md` §3.

  REGRA: cor de identidade nunca significa estado; cor de estado nunca decora. Verde e vermelho
  são RESERVADOS — aprovado/ativo e reprovado/perda/erro. É isso que faz a tela de Qualidade da
  Fase 5 funcionar, quando "Aprovado" e "Abrir retrabalho" dividem a mesma linha.

  Todo tom novo aqui precisa de par declarado em `src/tema/contraste.test.ts`, senão a suíte
  reprova. Não é zelo: os tons claros de verde-água (#0D9488) e âmbar (#D97706) REPROVAM em AA
  como texto sobre branco apesar de funcionarem como fundo de botão — escolher paleta olhando só
  o botão é o erro clássico, e o teste existe para que ele não passe.
*/
@theme {
  /* Pilha do sistema: zero asset para baixar no wifi da fábrica, e trocar por fonte própria
     depois é mudar este token, não reescrever tela. Monoespaçada para código de peça e material —
     decisão funcional (alinha na coluna, facilita conferir contra o desenho na bancada). */
  --font-sans: ui-sans-serif, system-ui, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
  --font-mono: ui-monospace, "Cascadia Mono", "Segoe UI Mono", "Roboto Mono", Menlo, monospace;

  /* Identidade */
  --color-chrome: #134E4A;       /* barra de navegação e cabeçalho */
  --color-marca: #5EEAD4;        /* logo, SEMPRE sobre o chrome escuro */

  /* Ação — mesma família do chrome, por escolha explícita do usuário contra a recomendação de
     dois matizes. O custo é real e a compensação é de FORMA, não de cor: o primário ganha peso
     por tamanho e densidade tipográfica, e o contorno neutro fica reservado ao secundário.
     Nenhuma tela deve ter dois botões com o mesmo peso visual. */
  --color-acao: #3E6E68;
  --color-acao-forte: #2F544F;   /* hover/active do primário */
  --color-acao-fundo: #E8F0EF;   /* fundo tingido da pílula; a tinta é a MESMA --color-acao */

  /* Estado — reservados */
  --color-positivo: #16A34A;         /* SÓ como fundo, com texto branco */
  --color-positivo-texto: #15803D;   /* o tom que passa AA como TEXTO sobre claro */
  --color-negativo: #DC2626;         /* passa AA nos dois papéis */

  /* Neutros, com viés verde-acinzentado para casarem com o chrome */
  --color-tinta: #1C2422;
  --color-tinta-fraca: #55605D;
  --color-borda: #D3DEDB;        /* separador decorativo */
  --color-borda-campo: #6B7A77;  /* borda de input/botão secundário — precisa de 3:1 */
  --color-fundo: #F7FAF9;
  --color-superficie: #FFFFFF;
}
```

**Repare no par `positivo` / `positivo-texto`, porque é a armadilha da spec em ato:** `#16A34A` sobre branco fica em torno de **3,3:1** e **reprova** AA como texto, embora branco *sobre* ele passe com folga. Por isso são dois tokens com papéis diferentes, e por isso o teste mede os dois sentidos.

**Valores esperados** (aproximados — **quem mede é o teste, não esta tabela**): chrome/branco ≈ 9,5:1 · ação/branco ≈ 5,8:1 · marca/chrome ≈ 6,4:1 · tinta-fraca/branco ≈ 6,5:1 · ação/fundo-de-pílula ≈ 5,0:1 (a menor margem da paleta) · negativo/branco ≈ 4,8:1 · positivo-texto/branco ≈ 5,0:1 · borda-campo/branco ≈ 4,5:1. **Se algum par reprovar, escureça o tom até passar e reporte o valor final** — não relaxe o mínimo, e não remova o par da lista.

- [ ] **Step 6: Rodar para ver passar**

```bash
cd web && npm test -- contraste
```

Expected: `Tests  20 passed (20)` (15 pares + 5 dos outros blocos). Se algum par reprovar, ajuste o **tom**, nunca o limite.

- [ ] **Step 7: Confirmar que o Tailwind gera as classes**

```bash
cd web && npm run build
```

Expected: build limpo. O CSS emitido cresce em relação aos ~9,9 kB da baseline — é esperado.

Confirme que as utilitárias existem de fato, e não só os custom properties:

```bash
cd web && grep -c "bg-chrome\|text-marca" dist/assets/*.css
```

Expected: `0` **por enquanto** — o Tailwind v4 só emite a classe quando algum arquivo a usa, e nenhuma tela usa ainda. A conferência de verdade acontece na Task 5, quando a primeira primitiva referenciar as classes. **Não conclua daqui que os tokens não funcionaram.**

- [ ] **Step 8: Migrar a `TelaCarregando` para os tokens**

É a primeira consumidora e a mais simples — serve de prova de fogo do `@theme`.

`web/src/components/TelaCarregando.tsx`:

```tsx
// Estado 'carregando' da sessao. Texto explicito de proposito: numa conexao lenta (wifi de
// chao de fabrica) uma tela em branco faz o usuario recarregar, e o reload no meio do
// init-refresh pode derrubar a sessao. Mostrar que algo acontece tira o incentivo de recarregar.
export function TelaCarregando() {
  return (
    <div className="min-h-screen bg-fundo flex flex-col items-center justify-center gap-4">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-borda border-t-acao" />
      <p className="text-tinta-fraca">Restaurando sessão…</p>
    </div>
  )
}
```

- [ ] **Step 9: Confirmar que a classe agora é emitida**

```bash
cd web && npm run build && grep -c "border-t-acao" dist/assets/*.css
```

Expected: ≥ `1`. **Este é o passo que prova que o `@theme` está ligado ao Tailwind** — se der `0`, o bloco não está sendo lido (confira que ele vem **depois** do `@import "tailwindcss"`).

- [ ] **Step 10: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  156 passed (156)` (136 + 20) · build limpo · lint só com o warning alheio.

- [ ] **Step 11: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `index.css` — trocar `--color-acao-fundo` por `#F3F7F6` (clarear a pílula, aumentando o contraste) | **0** — é a direção segura; serve de **controle**, para confirmar que o teste não é um carimbo |
| M2 | `index.css` — trocar `--color-tinta-fraca` por `#8A9693` (clarear até reprovar) | ≥ 1 |
| M3 | `index.css` — trocar `--color-positivo-texto` por `#16A34A` (o valor do `positivo`) | ≥ 1 — **é a armadilha da spec, encenada** |
| M4 | `index.css` — acrescentar `--color-ambar: #D97706;` sem declarar par | ≥ 1 (o teste "não deixa entrar tom novo sem medição") |
| M5 | `contraste.ts` — trocar `c <= 0.03928 ? c / 12.92 : …` por só a exponencial | ≥ 1 |
| M6 | `contraste.ts` — trocar os coeficientes `0.2126 / 0.7152 / 0.0722` por `1/3` cada | ≥ 1 |
| M7 | `index.css` — trocar `--color-marca` por `#16A34A` | ≥ 1 (o teste da regra identidade × estado) |

**M1 é controle deliberado, e o único da fase cuja resposta certa é zero.** Reporte-a assim — uma mutação que melhora o contraste *deve* passar. Se ela matar alguma coisa, o teste está preso ao valor em vez de à propriedade.

- [ ] **Step 12: Commit**

```bash
git add web/src/index.css web/src/tema/contraste.ts web/src/tema/contraste.test.ts web/src/components/TelaCarregando.tsx
git commit -m "feat(web): tokens de tema com prova automatizada de contraste AA"
```

**Definition of done da Task 4:** suíte em **156**; `grep -c "border-t-acao" dist/assets/*.css` ≥ 1; as 7 mutações medidas (M1 com 0 mortes, as outras com ≥ 1); os valores de contraste medidos **reportados um a um** no relatório.

---

### Task 5: Primitivas de página e formulário — `Pagina`, `Botao`, `Campo`, `BannerDeErro`

**Files:**
- Create: `web/src/components/Pagina.tsx` + `Pagina.test.tsx`
- Create: `web/src/components/Botao.tsx` + `Botao.test.tsx`
- Create: `web/src/components/Campo.tsx` + `Campo.test.tsx`
- Create: `web/src/components/BannerDeErro.tsx` + `BannerDeErro.test.tsx`

**Interfaces:**
- Consumes: os tokens da Task 4.
- Produces:

```ts
export function Pagina(props: { titulo: string; acao?: ReactNode; children: ReactNode }): JSX.Element
export function Botao(props: ButtonHTMLAttributes<HTMLButtonElement> & {
  variante?: 'primario' | 'secundario' | 'perigo'
  carregando?: boolean
  rotuloCarregando?: string
}): JSX.Element
export function Campo(props: { rotulo: string; children: (id: string) => ReactNode; dica?: string }): JSX.Element
export function BannerDeErro(props: { mensagem: string | null }): JSX.Element | null
```

**Critério de pronto de uma primitiva (spec §5):** ela é usada por **pelo menos duas telas** e **não sobrou nenhuma cópia da forma antiga** no repositório. As quatro daqui têm 6, 7, 6 e 7 consumidores respectivamente — a conferência final é a Task 12.

**A regra de peso visual (spec §3), que vale para todas as tasks seguintes:** como a ação é monocromática com o chrome, o botão primário fica discreto, e **botão discreto custa clique**. A compensação é de forma: o primário ganha peso por tamanho e densidade tipográfica, e o contorno neutro fica reservado ao secundário. **Nenhuma tela deve ter dois botões com o mesmo peso visual.**

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Tests  156 passed (156)`.

- [ ] **Step 2: Escrever o teste do `Botao`, que ainda não existe**

`web/src/components/Botao.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { Botao } from './Botao'

afterEach(cleanup)

describe('Botao', () => {
  it('renderiza o rótulo e dispara o clique', () => {
    const aoClicar = vi.fn()
    render(<Botao onClick={aoClicar}>Adicionar</Botao>)

    fireEvent.click(screen.getByText('Adicionar'))

    expect(aoClicar).toHaveBeenCalledTimes(1)
  })

  it('desabilita e troca o rótulo enquanto a mutação está em voo', () => {
    // A dívida "botão desabilitado durante mutação" (spec §9). Sem isto, o PCP clica "Adicionar"
    // duas vezes no wifi lento e o segundo POST volta 409 — a tela acusando de duplicado o
    // cadastro que ela mesma acabou de fazer.
    render(<Botao carregando rotuloCarregando="Salvando…">Adicionar</Botao>)

    const botao = screen.getByRole('button') as HTMLButtonElement
    expect(botao.disabled).toBe(true)
    expect(botao.textContent).toBe('Salvando…')
  })

  it('não dispara o clique quando está carregando', () => {
    const aoClicar = vi.fn()
    render(<Botao carregando onClick={aoClicar}>Adicionar</Botao>)

    fireEvent.click(screen.getByRole('button'))

    expect(aoClicar).not.toHaveBeenCalled()
  })

  it('mantém o rótulo normal quando carregando não recebe rótulo próprio', () => {
    render(<Botao carregando>Adicionar</Botao>)

    expect(screen.getByRole('button').textContent).toBe('Adicionar')
    expect((screen.getByRole('button') as HTMLButtonElement).disabled).toBe(true)
  })

  it('respeita o disabled vindo de fora, sem estar carregando', () => {
    const aoClicar = vi.fn()
    render(<Botao disabled onClick={aoClicar}>Anterior</Botao>)

    fireEvent.click(screen.getByRole('button'))

    expect(aoClicar).not.toHaveBeenCalled()
  })

  it('dá pesos visuais diferentes a primário, secundário e perigo', () => {
    // A propriedade sob teste é "os três são DISTINGUÍVEIS", não a lista exata de classes: um
    // teste que fixasse as classes quebraria em todo ajuste de espaçamento e viraria ruído.
    const { container } = render(
      <div>
        <Botao variante="primario">P</Botao>
        <Botao variante="secundario">S</Botao>
        <Botao variante="perigo">X</Botao>
      </div>,
    )
    const classes = Array.from(container.querySelectorAll('button')).map((b) => b.className)

    expect(new Set(classes).size).toBe(3)
    expect(classes[0]).toContain('bg-acao')       // primário: preenchido
    expect(classes[1]).toContain('border')        // secundário: contorno neutro
    expect(classes[2]).toContain('bg-negativo')   // perigo: vermelho reservado a estado
  })

  it('usa primário quando a variante não é dita', () => {
    render(<Botao>Adicionar</Botao>)

    expect(screen.getByRole('button').className).toContain('bg-acao')
  })

  it('tem indicação de foco visível', () => {
    // Critério de aceite da spec §11: foco visível em todo controle interativo. `focus-visible`,
    // e não `focus`: o anel não deve aparecer no clique de mouse, só na navegação por teclado.
    render(<Botao>Adicionar</Botao>)

    expect(screen.getByRole('button').className).toContain('focus-visible:outline')
  })

  it('é do tipo button por padrão, para não submeter formulário sem querer', () => {
    // `<button>` sem `type` dentro de `<form>` é `submit` por especificação. Todo botão de ação
    // secundária dentro de um formulário submeteria o formulário — defeito silencioso e clássico.
    render(<Botao>Cancelar</Botao>)

    expect(screen.getByRole('button').getAttribute('type')).toBe('button')
  })

  it('aceita type=submit quando o chamador pede', () => {
    render(<Botao type="submit">Adicionar</Botao>)

    expect(screen.getByRole('button').getAttribute('type')).toBe('submit')
  })
})
```

- [ ] **Step 3: Rodar para ver falhar**

```bash
cd web && npm test -- Botao
```

Expected: FAIL — `Failed to resolve import "./Botao"`.

- [ ] **Step 4: Escrever o `Botao`**

`web/src/components/Botao.tsx`:

```tsx
import type { ButtonHTMLAttributes, ReactNode } from 'react'

export type VarianteDeBotao = 'primario' | 'secundario' | 'perigo'

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variante?: VarianteDeBotao
  /** Mutação em voo: desabilita e, se houver, troca o rótulo. */
  carregando?: boolean
  rotuloCarregando?: string
  children: ReactNode
}

// `focus-visible` (e não `focus`): o anel aparece na navegação por teclado e não no clique de
// mouse. Critério de aceite da spec §11.
const BASE =
  'inline-flex items-center justify-center rounded-lg transition-colors ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao ' +
  'disabled:opacity-50 disabled:cursor-not-allowed'

// O peso visual é o que compensa a ação ser monocromática com o chrome (spec §3): o primário é
// maior e mais denso; o secundário é contorno neutro. Dois botões com o mesmo peso na mesma tela
// devolvem ao usuário a decisão que o desenho deveria ter tomado por ele.
const POR_VARIANTE: Record<VarianteDeBotao, string> = {
  primario: 'bg-acao text-superficie px-5 py-2.5 font-semibold hover:bg-acao-forte',
  secundario: 'border border-borda-campo text-tinta px-4 py-2 hover:bg-acao-fundo',
  perigo: 'bg-negativo text-superficie px-5 py-2.5 font-semibold hover:brightness-90',
}

export function Botao({
  variante = 'primario',
  carregando = false,
  rotuloCarregando,
  className = '',
  disabled,
  children,
  ...resto
}: Props) {
  return (
    <button
      // `<button>` sem `type` dentro de `<form>` é `submit` por especificação — um "Cancelar"
      // herdaria isso e submeteria o formulário. O default seguro é `button`; quem submete pede.
      type="button"
      {...resto}
      disabled={disabled || carregando}
      className={`${BASE} ${POR_VARIANTE[variante]} ${className}`}
    >
      {carregando && rotuloCarregando ? rotuloCarregando : children}
    </button>
  )
}
```

**Atenção à ordem de `{...resto}`:** ele vem **depois** de `type="button"` (para o chamador poder pedir `submit`) e **antes** de `disabled`/`className` (para que `carregando` e as classes de variante não sejam sobrescritos por acidente). Trocar essa ordem é um defeito silencioso.

- [ ] **Step 5: Rodar para ver passar**

```bash
cd web && npm test -- Botao
```

Expected: `Tests  10 passed (10)`.

- [ ] **Step 6: Escrever o teste do `Campo`**

`web/src/components/Campo.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Campo } from './Campo'

afterEach(cleanup)

describe('Campo', () => {
  it('liga o rótulo ao controle por id, e não por aninhamento', () => {
    // Ligação explícita: leitor de tela anuncia o rótulo, e clicar no texto foca o campo. Hoje as
    // telas usam `placeholder` como rótulo — que some assim que o usuário digita a primeira letra,
    // deixando o campo sem identificação para quem voltar depois de uma interrupção na bancada.
    render(<Campo rotulo="Código">{(id) => <input id={id} />}</Campo>)

    const controle = screen.getByLabelText('Código') as HTMLInputElement
    expect(controle.tagName).toBe('INPUT')
    expect(controle.id).toBeTruthy()
  })

  it('dá ids diferentes a dois campos com o mesmo rótulo', () => {
    // `useId` por instância. Ids repetidos fariam `getByLabelText` casar sempre com o primeiro, e
    // o clique no segundo rótulo focaria o campo errado — na tela e no leitor de tela.
    const { container } = render(
      <div>
        <Campo rotulo="Código">{(id) => <input id={id} />}</Campo>
        <Campo rotulo="Código">{(id) => <input id={id} />}</Campo>
      </div>,
    )
    const ids = Array.from(container.querySelectorAll('input')).map((i) => i.id)

    expect(ids[0]).not.toBe(ids[1])
  })

  it('mostra a dica quando ela existe e a associa ao controle', () => {
    render(
      <Campo rotulo="Unidade" dica="UN, KG, M…">
        {(id, idDaDica) => <input id={id} aria-describedby={idDaDica} />}
      </Campo>,
    )

    const controle = screen.getByLabelText('Unidade')
    const dica = screen.getByText('UN, KG, M…')
    expect(controle.getAttribute('aria-describedby')).toBe(dica.id)
  })

  it('não deixa aria-describedby pendurado quando não há dica', () => {
    // Apontar para um id inexistente faz o leitor de tela anunciar vazio — pior que não apontar.
    render(
      <Campo rotulo="Código">
        {(id, idDaDica) => <input id={id} aria-describedby={idDaDica} />}
      </Campo>,
    )

    expect(screen.getByLabelText('Código').getAttribute('aria-describedby')).toBeNull()
  })

  it('serve a select, não só a input', () => {
    render(
      <Campo rotulo="Tipo">
        {(id) => <select id={id}><option>Bruto</option></select>}
      </Campo>,
    )

    expect((screen.getByLabelText('Tipo') as HTMLSelectElement).tagName).toBe('SELECT')
  })
})
```

- [ ] **Step 7: Escrever o `Campo`**

`web/src/components/Campo.tsx`:

```tsx
import { useId, type ReactNode } from 'react'

interface Props {
  rotulo: string
  /**
   * Recebe o `id` gerado — e, quando há dica, o id dela — e devolve o controle:
   * `{(id, idDaDica) => <input id={id} aria-describedby={idDaDica} … />}`.
   * O segundo parâmetro pode ser ignorado em campo sem dica.
   */
  children: (id: string, idDaDica?: string) => ReactNode
  dica?: string
}

/**
 * Rótulo + controle, ligados por `id` explícito.
 *
 * O padrão é *render prop* em vez de `<Campo type="text" …>` porque as telas usam `input` e
 * `select`, e o `select` de `ComponentesPage` tem lista fechada com `<option>` próprios. Uma
 * primitiva que tentasse abstrair os dois viraria um repasse de props sem valor. O que a
 * primitiva realmente entrega é a **ligação acessível**, e é isso que ela guarda.
 *
 * Classes do controle ficam com o chamador — via `CLASSES_DE_CONTROLE`, exportada abaixo — para
 * que ele possa acrescentar `font-mono` num campo de código sem lutar contra a primitiva.
 */
export function Campo({ rotulo, children, dica }: Props) {
  const id = useId()
  const idDaDica = `${id}-dica`

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium text-tinta">
        {rotulo}
      </label>
      {/* O `aria-describedby` vai no CONTROLE, não num wrapper: leitor de tela associa a descrição
          ao campo focado, e num `<div>` externo ele nunca é anunciado. Por isso a render prop
          recebe os dois ids. Quando não há dica o segundo é `undefined`, e React omite o atributo
          em vez de apontar para um id inexistente — apontar para o vazio é pior que não apontar. */}
      {children(id, dica ? idDaDica : undefined)}
      {dica && <p id={idDaDica} className="text-sm text-tinta-fraca">{dica}</p>}
    </div>
  )
}

/** Aparência única de todo input/select do sistema. Use com `className={CLASSES_DE_CONTROLE}`. */
export const CLASSES_DE_CONTROLE =
  'w-full rounded-lg border border-borda-campo bg-superficie px-3 py-2.5 text-tinta ' +
  'placeholder:text-tinta-fraca ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao'
```

**O teste da dica exige que o chamador use o segundo parâmetro.** Os dois casos do Step 6 que envolvem dica precisam ser escritos assim:

```tsx
<Campo rotulo="Unidade" dica="UN, KG, M…">
  {(id, idDaDica) => <input id={id} aria-describedby={idDaDica} />}
</Campo>
```

Campo sem dica ignora o segundo parâmetro — `{(id) => <input id={id} />}` continua válido, e é a forma usada em quase todas as telas.

- [ ] **Step 8: Rodar `Campo` até ficar verde**

```bash
cd web && npm test -- Campo
```

Expected: `Tests  5 passed (5)`.

- [ ] **Step 9: Escrever `BannerDeErro` com seu teste**

`web/src/components/BannerDeErro.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { BannerDeErro } from './BannerDeErro'

afterEach(cleanup)

describe('BannerDeErro', () => {
  it('mostra a mensagem', () => {
    render(<BannerDeErro mensagem="Seu perfil não tem permissão para esta ação." />)

    expect(screen.getByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  it('não renderiza nada quando não há mensagem', () => {
    // O chamador passa o estado direto (`<BannerDeErro mensagem={erro} />`) em vez de repetir
    // `{erro && …}` em sete telas. Só funciona se o nulo for tratado aqui.
    const { container } = render(<BannerDeErro mensagem={null} />)

    expect(container.innerHTML).toBe('')
  })

  it('é anunciado como alerta', () => {
    // `role="alert"` faz o leitor de tela ler a mensagem assim que ela aparece, sem exigir que o
    // usuário navegue até ela. Numa tela de bancada, o erro costuma estar longe do foco.
    render(<BannerDeErro mensagem="Sem conexão com o servidor." />)

    expect(screen.getByRole('alert').textContent).toBe('Sem conexão com o servidor.')
  })
})
```

`web/src/components/BannerDeErro.tsx`:

```tsx
interface Props {
  /** Aceita `null` para o chamador passar o estado direto, sem `{erro && …}` em sete telas. */
  mensagem: string | null
}

export function BannerDeErro({ mensagem }: Props) {
  if (!mensagem) return null

  return (
    <p
      role="alert"
      className="rounded-lg border border-negativo/30 bg-negativo/5 px-4 py-3 text-negativo"
    >
      {mensagem}
    </p>
  )
}
```

- [ ] **Step 10: Escrever `Pagina` com seu teste**

`web/src/components/Pagina.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Pagina } from './Pagina'

afterEach(cleanup)

describe('Pagina', () => {
  it('mostra o título como o h1 da tela', () => {
    render(<Pagina titulo="Setores"><p>conteúdo</p></Pagina>)

    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Setores')
    expect(screen.getByText('conteúdo')).toBeTruthy()
  })

  it('mostra a ação de cabeçalho quando ela existe', () => {
    render(<Pagina titulo="Pedidos" acao={<button>Novo</button>}><p>c</p></Pagina>)

    expect(screen.getByText('Novo')).toBeTruthy()
  })

  it('funciona sem ação', () => {
    render(<Pagina titulo="Setores"><p>c</p></Pagina>)

    expect(screen.getByRole('heading', { level: 1 })).toBeTruthy()
  })

  it('é o landmark main da página', () => {
    // Um `main` por tela dá ao leitor de tela o atalho "ir para o conteúdo" e separa o conteúdo do
    // shell de navegação, que envolve todas as telas a partir da Task 7.
    render(<Pagina titulo="Setores"><p>c</p></Pagina>)

    expect(screen.getByRole('main')).toBeTruthy()
  })

  it('não impõe altura mínima de tela — quem faz isso é o shell', () => {
    // `min-h-screen` DENTRO da página, com o shell também aplicando, produz barra de rolagem
    // permanente de alguns pixels. As 6 telas de hoje têm `min-h-screen`; ele sai daqui.
    expect(screen.queryByRole('main')).toBeNull()
    const { container } = render(<Pagina titulo="Setores"><p>c</p></Pagina>)
    expect(container.querySelector('main')!.className).not.toContain('min-h-screen')
  })
})
```

`web/src/components/Pagina.tsx`:

```tsx
import type { ReactNode } from 'react'

interface Props {
  titulo: string
  /** Ação principal da tela, alinhada ao título (ex.: "Novo pedido"). */
  acao?: ReactNode
  children: ReactNode
}

/**
 * Substitui as SEIS cópias de `min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4`.
 *
 * `max-w-3xl` (768px) e não `max-w-md` (448px): a spec §7 registra que busca + filtro + seletor de
 * tamanho + paginação não cabem em 448px. `min-h-screen` sai daqui e vai para o `AppShell` — as
 * duas coisas juntas produzem rolagem permanente de alguns pixels.
 *
 * O respiro é generoso de propósito (direção "sóbria e espaçada", spec §3). O custo foi medido e
 * aceito: na mesma altura de tela, ~3 itens onde a densa mostraria ~6.
 */
export function Pagina({ titulo, acao, children }: Props) {
  return (
    <main className="mx-auto w-full max-w-3xl px-4 py-8 sm:px-6 flex flex-col gap-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight text-tinta">{titulo}</h1>
        {acao}
      </header>
      {children}
    </main>
  )
}
```

- [ ] **Step 11: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  179 passed (179)` (156 + 10 + 5 + 3 + 5) · build limpo · lint só com o warning alheio.

- [ ] **Step 12: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `Botao.tsx` — remover `carregando` de `disabled={disabled \|\| carregando}` | ≥ 1 |
| M2 | `Botao.tsx` — trocar `type="button"` por `type="submit"` | ≥ 1 |
| M3 | `Botao.tsx` — mover `{...resto}` para depois de `disabled` | ≥ 1 |
| M4 | `Botao.tsx` — dar a mesma string de classe a `primario` e `secundario` | ≥ 1 |
| M5 | `Botao.tsx` — trocar `focus-visible:outline-2` por nada | ≥ 1 |
| M6 | `Campo.tsx` — trocar `useId()` por uma constante `'campo'` | ≥ 1 |
| M7 | `Campo.tsx` — remover `htmlFor={id}` do `<label>` | ≥ 1 |
| M8 | `Campo.tsx` — trocar `dica ? idDaDica : undefined` por `idDaDica` | ≥ 1 |
| M9 | `BannerDeErro.tsx` — trocar `if (!mensagem) return null` por `if (false)` | ≥ 1 |
| M10 | `BannerDeErro.tsx` — remover `role="alert"` | ≥ 1 |
| M11 | `Pagina.tsx` — trocar `<h1>` por `<h2>` | ≥ 1 |
| M12 | `Pagina.tsx` — trocar `<main>` por `<div>` | ≥ 1 |
| M13 | `Pagina.tsx` — acrescentar `min-h-screen` às classes | ≥ 1 |

- [ ] **Step 13: Commit**

```bash
git add web/src/components/Pagina.tsx web/src/components/Pagina.test.tsx web/src/components/Botao.tsx web/src/components/Botao.test.tsx web/src/components/Campo.tsx web/src/components/Campo.test.tsx web/src/components/BannerDeErro.tsx web/src/components/BannerDeErro.test.tsx
git commit -m "feat(web): primitivas Pagina, Botao, Campo e BannerDeErro"
```

**Definition of done da Task 5:** suíte em **179**; as 13 mutações medidas com ≥ 1 morte cada; **nenhuma tela consumindo as primitivas ainda** (isso é das Tasks 8–12); build e lint limpos.

---

### Task 6: Primitivas de lista — `ListaDeCadastro`, `Pilula`, `EstadoVazio`, `ControlesDePaginacao`

**Files:**
- Create: `web/src/components/ListaDeCadastro.tsx` + `ListaDeCadastro.test.tsx`
- Create: `web/src/components/Pilula.tsx` + `Pilula.test.tsx`
- Create: `web/src/components/EstadoVazio.tsx` + `EstadoVazio.test.tsx`
- Create: `web/src/components/ControlesDePaginacao.tsx` + `ControlesDePaginacao.test.tsx`

**Interfaces:**
- Consumes: `Botao` (Task 5), tokens (Task 4).
- Produces:

```ts
export function ListaDeCadastro(props: { children: ReactNode; rotulo?: string }): JSX.Element
export function ItemDeCadastro(props: { ativo?: boolean; acao?: ReactNode; children: ReactNode }): JSX.Element
export function Pilula(props: { children: ReactNode; tom?: 'neutro' | 'positivo' | 'negativo' }): JSX.Element
export function EstadoVazio(props: { titulo: string; descricao?: string; acao?: ReactNode }): JSX.Element
export function ControlesDePaginacao(props: {
  pagina: number; totalDePaginas: number; total: number; aoMudarPagina: (p: number) => void
}): JSX.Element
```

**Por que `ListaDeCadastro` não recebe os itens por prop:** as quatro listas do sistema mostram coisas diferentes (nome; código + descrição + unidade; código + descrição + tipo; código + tipo) e uma primitiva que tentasse abstrair isso viraria seis render props. O que ela guarda é o que **não** varia: a semântica de lista, o espaçamento, e o item com inativo distinguível mais slot de ação.

**O estado vazio é decidido pela TELA, não pela lista** — a spec §9 exige distinguir "nenhum resultado para a busca", "catálogo vazio" e "erro de rede", que hoje renderizam a mesma lista vazia e muda. Uma lista que renderizasse o vazio sozinha não teria como saber qual dos três é.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Tests  179 passed (179)`.

- [ ] **Step 2: Escrever o teste de `ListaDeCadastro` / `ItemDeCadastro`**

`web/src/components/ListaDeCadastro.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { ListaDeCadastro, ItemDeCadastro } from './ListaDeCadastro'

afterEach(cleanup)

describe('ListaDeCadastro', () => {
  it('é uma lista com um item por filho', () => {
    render(
      <ListaDeCadastro>
        <ItemDeCadastro>Corte</ItemDeCadastro>
        <ItemDeCadastro>Solda</ItemDeCadastro>
      </ListaDeCadastro>,
    )

    expect(screen.getByRole('list')).toBeTruthy()
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
  })

  it('aceita um rótulo acessível para distinguir duas listas na mesma tela', () => {
    render(<ListaDeCadastro rotulo="Agrupamentos"><ItemDeCadastro>A</ItemDeCadastro></ListaDeCadastro>)

    expect(screen.getByRole('list', { name: 'Agrupamentos' })).toBeTruthy()
  })
})

describe('ItemDeCadastro', () => {
  it('mostra o conteúdo e a ação', () => {
    render(
      <ListaDeCadastro>
        <ItemDeCadastro acao={<button>Inativar</button>}>Corte</ItemDeCadastro>
      </ListaDeCadastro>,
    )

    expect(screen.getByText('Corte')).toBeTruthy()
    expect(screen.getByText('Inativar')).toBeTruthy()
  })

  it('distingue item inativo de item ativo', () => {
    // Distinção que NÃO é só cor: `line-through` mais tinta fraca. Cor sozinha exclui quem não a
    // percebe, e a lista de inativos é justamente onde o usuário decide se reativa ou não.
    const { container } = render(
      <ListaDeCadastro>
        <ItemDeCadastro ativo>Ativo</ItemDeCadastro>
        <ItemDeCadastro ativo={false}>Inativo</ItemDeCadastro>
      </ListaDeCadastro>,
    )
    const [itemAtivo, itemInativo] = Array.from(container.querySelectorAll('li'))

    expect(itemInativo.textContent).toContain('Inativo')
    expect(itemInativo.innerHTML).toContain('line-through')
    expect(itemAtivo.innerHTML).not.toContain('line-through')
  })

  it('trata item sem a prop ativo como ativo', () => {
    // `Agrupamento` não tem coluna `Ativo` — a lista do PedidoDetalhe usa o item sem a prop, e não
    // pode sair riscada por causa disso.
    const { container } = render(
      <ListaDeCadastro><ItemDeCadastro>AGR-01</ItemDeCadastro></ListaDeCadastro>,
    )

    expect(container.querySelector('li')!.innerHTML).not.toContain('line-through')
  })

  it('anuncia o estado inativo em texto, não só no traço', () => {
    // `line-through` é visual e não chega ao leitor de tela. Sem isto, quem usa leitor ouve
    // "Corte" nos dois casos e não tem como saber qual está inativo.
    render(
      <ListaDeCadastro><ItemDeCadastro ativo={false}>Corte</ItemDeCadastro></ListaDeCadastro>,
    )

    expect(screen.getByText('(inativo)')).toBeTruthy()
  })
})
```

`web/src/components/ListaDeCadastro.tsx`:

```tsx
import type { ReactNode } from 'react'

/**
 * O `<ul>` das quatro listas de cadastro. NÃO recebe os itens por prop: as quatro mostram campos
 * diferentes, e abstrair isso viraria seis render props sem ganho. O que a primitiva guarda é o
 * que não varia — semântica de lista e espaçamento.
 *
 * O estado vazio fica com a TELA (`EstadoVazio`): a spec §9 exige distinguir "nenhum resultado
 * para a busca" de "catálogo vazio" e de "erro de rede", e daqui não dá para saber qual é.
 */
export function ListaDeCadastro({ children, rotulo }: { children: ReactNode; rotulo?: string }) {
  return (
    <ul aria-label={rotulo} className="flex flex-col gap-2">
      {children}
    </ul>
  )
}

export function ItemDeCadastro({
  ativo = true,
  acao,
  children,
}: {
  /** Ausente = ativo. `Agrupamento` não tem coluna `Ativo` e usa o item sem a prop. */
  ativo?: boolean
  acao?: ReactNode
  children: ReactNode
}) {
  return (
    <li className="flex items-center justify-between gap-3 rounded-lg border border-borda bg-superficie px-4 py-3">
      <span className={ativo ? 'text-tinta' : 'text-tinta-fraca line-through'}>
        {children}
        {/* O traço é visual e não chega ao leitor de tela; sem este texto, ativo e inativo soam
            idênticos para quem não vê a lista. */}
        {!ativo && <span className="ml-2 no-underline">(inativo)</span>}
      </span>
      {acao}
    </li>
  )
}
```

- [ ] **Step 3: Rodar até ficar verde**

```bash
cd web && npm test -- ListaDeCadastro
```

Expected: `Tests  7 passed (7)`.

- [ ] **Step 4: Escrever `EstadoVazio` com seu teste**

`web/src/components/EstadoVazio.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { EstadoVazio } from './EstadoVazio'

afterEach(cleanup)

describe('EstadoVazio', () => {
  it('mostra título e descrição', () => {
    render(<EstadoVazio titulo="Nenhum componente encontrado" descricao="Tente outro termo de busca." />)

    expect(screen.getByText('Nenhum componente encontrado')).toBeTruthy()
    expect(screen.getByText('Tente outro termo de busca.')).toBeTruthy()
  })

  it('funciona só com título', () => {
    render(<EstadoVazio titulo="Catálogo vazio" />)

    expect(screen.getByText('Catálogo vazio')).toBeTruthy()
  })

  it('mostra a ação sugerida quando ela existe', () => {
    render(<EstadoVazio titulo="Catálogo vazio" acao={<button>Cadastrar o primeiro</button>} />)

    expect(screen.getByText('Cadastrar o primeiro')).toBeTruthy()
  })

  it('é anunciado como região de status, não como erro', () => {
    // `role="status"` e não `role="alert"`: lista vazia é informação, não falha. Anunciar como
    // alerta interromperia a leitura para dizer que não há nada — e o usuário que acabou de
    // filtrar já sabe disso.
    render(<EstadoVazio titulo="Nenhum resultado" />)

    expect(screen.getByRole('status').textContent).toContain('Nenhum resultado')
  })
})
```

`web/src/components/EstadoVazio.tsx`:

```tsx
import type { ReactNode } from 'react'

/**
 * Estado vazio explícito. Hoje "nenhum resultado para a busca", "catálogo vazio" e "erro de rede"
 * renderizam a mesma lista vazia e muda — o usuário não tem como distinguir "não achei" de
 * "não perguntei" (spec §9).
 *
 * O texto vem da tela, porque só ela sabe qual dos três é.
 */
export function EstadoVazio({
  titulo,
  descricao,
  acao,
}: {
  titulo: string
  descricao?: string
  acao?: ReactNode
}) {
  return (
    <div
      role="status"
      className="flex flex-col items-center gap-2 rounded-lg border border-dashed border-borda bg-superficie px-6 py-12 text-center"
    >
      <p className="font-medium text-tinta">{titulo}</p>
      {descricao && <p className="text-sm text-tinta-fraca">{descricao}</p>}
      {acao && <div className="mt-2">{acao}</div>}
    </div>
  )
}
```

- [ ] **Step 5: Escrever `Pilula` com seu teste**

`web/src/components/Pilula.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Pilula } from './Pilula'

afterEach(cleanup)

describe('Pilula', () => {
  it('mostra o rótulo', () => {
    render(<Pilula>Montagem</Pilula>)

    expect(screen.getByText('Montagem')).toBeTruthy()
  })

  it('usa a tinta de ação sobre fundo tingido no tom neutro', () => {
    // A mesma `--color-acao` do botão, agora sobre fundo de baixa saturação. Um segundo tom só
    // para a pílula seria uma cor a mais para manter coerente, sem ganho (spec §3).
    render(<Pilula>Kit</Pilula>)

    const classes = screen.getByText('Kit').className
    expect(classes).toContain('bg-acao-fundo')
    expect(classes).toContain('text-acao')
  })

  it('reserva verde e vermelho para estado', () => {
    const { container } = render(
      <div>
        <Pilula tom="positivo">Aprovado</Pilula>
        <Pilula tom="negativo">Reprovado</Pilula>
      </div>,
    )
    const [positiva, negativa] = Array.from(container.querySelectorAll('span'))

    // `positivo-texto`, não `positivo`: o tom cheio reprova AA como texto sobre claro — é a
    // armadilha registrada na spec §3, e a razão de os dois tokens existirem separados.
    expect(positiva.className).toContain('text-positivo-texto')
    expect(negativa.className).toContain('text-negativo')
  })
})
```

`web/src/components/Pilula.tsx`:

```tsx
import type { ReactNode } from 'react'

export type TomDePilula = 'neutro' | 'positivo' | 'negativo'

// `positivo-texto` e não `positivo`: o verde cheio reprova AA como texto sobre claro, apesar de
// funcionar como fundo com texto branco. É a armadilha que a spec §3 registra e que o teste de
// contraste da Task 4 mede.
const POR_TOM: Record<TomDePilula, string> = {
  neutro: 'bg-acao-fundo text-acao',
  positivo: 'bg-positivo/10 text-positivo-texto',
  negativo: 'bg-negativo/10 text-negativo',
}

/**
 * Rótulo curto de categoria ou estado (tipo do componente, tipo do agrupamento, status do pedido).
 *
 * O tom `neutro` usa a MESMA tinta do botão primário sobre fundo tingido — não é engano: é a mesma
 * cor em dois contextos. Verde e vermelho ficam reservados a estado de verdade.
 */
export function Pilula({ children, tom = 'neutro' }: { children: ReactNode; tom?: TomDePilula }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${POR_TOM[tom]}`}>
      {children}
    </span>
  )
}
```

- [ ] **Step 6: Escrever `ControlesDePaginacao` com seu teste**

`web/src/components/ControlesDePaginacao.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { ControlesDePaginacao } from './ControlesDePaginacao'

afterEach(cleanup)

describe('ControlesDePaginacao', () => {
  it('mostra a posição e o total', () => {
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={() => {}} />)

    expect(screen.getByText(/Página 2 de 5/)).toBeTruthy()
    expect(screen.getByText(/97/)).toBeTruthy()
  })

  it('avança uma página no "Próxima"', () => {
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Próxima'))

    expect(aoMudarPagina).toHaveBeenCalledWith(3)
  })

  it('recua uma página no "Anterior"', () => {
    // Na Fase 1B este botão nunca foi clicado por teste nenhum: mutá-lo para `pagina + 1`
    // sobrevivia, e a paginação só andaria para frente sem ninguém ver (achado I3 da Task 6).
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Anterior'))

    expect(aoMudarPagina).toHaveBeenCalledWith(1)
  })

  it('desabilita "Anterior" na primeira página', () => {
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={1} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Anterior'))

    expect((screen.getByText('Anterior') as HTMLButtonElement).disabled).toBe(true)
    expect(aoMudarPagina).not.toHaveBeenCalled()
  })

  it('desabilita "Próxima" na última página', () => {
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={5} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Próxima'))

    expect((screen.getByText('Próxima') as HTMLButtonElement).disabled).toBe(true)
    expect(aoMudarPagina).not.toHaveBeenCalled()
  })

  it('não aparece quando há uma página só', () => {
    // Controles de paginação numa lista de 3 itens são ruído, e "Página 1 de 1" não informa nada.
    const { container } = render(
      <ControlesDePaginacao pagina={1} totalDePaginas={1} total={3} aoMudarPagina={() => {}} />,
    )

    expect(container.innerHTML).toBe('')
  })

  it('é anunciado como navegação', () => {
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={() => {}} />)

    expect(screen.getByRole('navigation', { name: 'Paginação' })).toBeTruthy()
  })
})
```

`web/src/components/ControlesDePaginacao.tsx`:

```tsx
import { Botao } from './Botao'

interface Props {
  pagina: number
  totalDePaginas: number
  /** Total sob o filtro, vindo do servidor — não `itens.length`. */
  total: number
  aoMudarPagina: (pagina: number) => void
}

/**
 * Anterior / posição / Próxima.
 *
 * `flex-wrap` não é detalhe: no celular Android da fábrica os três controles não cabem lado a
 * lado, e sem a quebra a página rola na horizontal — o que a spec §11 barra explicitamente.
 */
export function ControlesDePaginacao({ pagina, totalDePaginas, total, aoMudarPagina }: Props) {
  // Uma página só: os controles não informam nada e viram ruído.
  if (totalDePaginas <= 1) return null

  return (
    <nav aria-label="Paginação" className="flex flex-wrap items-center justify-between gap-3">
      <Botao variante="secundario" disabled={pagina <= 1} onClick={() => aoMudarPagina(pagina - 1)}>
        Anterior
      </Botao>
      <span className="text-sm text-tinta-fraca">
        Página {pagina} de {totalDePaginas} — {total} no total
      </span>
      <Botao
        variante="secundario"
        disabled={pagina >= totalDePaginas}
        onClick={() => aoMudarPagina(pagina + 1)}
      >
        Próxima
      </Botao>
    </nav>
  )
}
```

- [ ] **Step 7: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  200 passed (200)` (179 + 7 + 4 + 3 + 7) · build limpo · lint só com o warning alheio.

- [ ] **Step 8: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `ListaDeCadastro.tsx` — trocar `<ul>`/`<li>` por `<div>` | ≥ 1 |
| M2 | `ListaDeCadastro.tsx` — trocar `ativo = true` por `ativo = false` no default | ≥ 1 |
| M3 | `ListaDeCadastro.tsx` — inverter a condição da classe (`ativo ? riscado : normal`) | ≥ 1 |
| M4 | `ListaDeCadastro.tsx` — remover o `(inativo)` | ≥ 1 |
| M5 | `EstadoVazio.tsx` — trocar `role="status"` por `role="alert"` | ≥ 1 |
| M6 | `Pilula.tsx` — trocar `text-positivo-texto` por `text-positivo` | ≥ 1 |
| M7 | `ControlesDePaginacao.tsx` — trocar `aoMudarPagina(pagina - 1)` por `pagina + 1` no "Anterior" | ≥ 1 |
| M8 | `ControlesDePaginacao.tsx` — trocar `disabled={pagina <= 1}` por `false` | ≥ 1 |
| M9 | `ControlesDePaginacao.tsx` — trocar `disabled={pagina >= totalDePaginas}` por `false` | ≥ 1 |
| M10 | `ControlesDePaginacao.tsx` — remover o `if (totalDePaginas <= 1) return null` | ≥ 1 |
| M11 | `ControlesDePaginacao.tsx` — remover `flex-wrap` | **0 esperado** — jsdom não faz layout; **é o buraco desta task, e vai declarado no relatório** |

**M11 é a fronteira honesta da rede:** nenhum teste em jsdom prova que a página não rola na horizontal, porque jsdom não calcula layout. A verificação de viewport de celular é **manual** e acontece na Task 12, com o dev server e o DevTools em 360px de largura. **Declare M11 como sobrevivente conhecido** — não invente asserção de classe para fingir que ela morreu; classe presente não é layout correto.

- [ ] **Step 9: Commit**

```bash
git add web/src/components/ListaDeCadastro.tsx web/src/components/ListaDeCadastro.test.tsx web/src/components/Pilula.tsx web/src/components/Pilula.test.tsx web/src/components/EstadoVazio.tsx web/src/components/EstadoVazio.test.tsx web/src/components/ControlesDePaginacao.tsx web/src/components/ControlesDePaginacao.test.tsx
git commit -m "feat(web): primitivas de lista, pilula, estado vazio e paginacao"
```

**Definition of done da Task 6:** suíte em **200**; as 11 mutações medidas (M11 declarada como sobrevivente conhecido, com o motivo); build e lint limpos; **nenhuma tela modificada ainda**.

---

### Task 7: Shell de navegação e a tabela de permissões

**Files:**
- Create: `web/src/auth/permissoes.ts` + `permissoes.test.ts`
- Create: `web/src/auth/usePermissao.ts`
- Create: `web/src/components/AppShell.tsx` + `AppShell.test.tsx`
- Modify: `web/src/App.tsx` (as 6 rotas protegidas viram filhas de uma rota de layout)

**Interfaces:**
- Consumes: `useAuth` (`web/src/auth/AuthContext.tsx`), tokens (Task 4), `Botao` (Task 5).
- Produces:

```ts
export type Recurso = 'setores' | 'materiais' | 'componentes' | 'pedidos' | 'agrupamentos'
export function podeEscrever(perfil: string, recurso: Recurso): boolean
export function usePodeEscrever(recurso: Recurso): boolean
export function AppShell(): JSX.Element   // renderiza <Outlet/>
```

**Hoje não existe shell nenhum.** Conferido no disco: 6 das 7 telas repetem `min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4`, a navegação vive dentro da `HomePage` (`HomePage.tsx:47-54`, uma fileira de `<Link>` com borda), e **nenhuma das seis telas internas tem caminho de volta que não seja o botão do navegador**.

**Gating: na AÇÃO, não no link.** Decisão do usuário em 2026-08-06, e ela **diverge da letra da spec §6** — o motivo está nas Global Constraints e foi medido: os controllers liberam leitura para qualquer usuário autenticado. Aqui a Task entrega a **tabela** e o **hook**; quem os consome são as Tasks 8–10, escondendo formulário e botões de (in)ativar. **O `try/catch` do F2 continua em toda tela** — o 403 do backend é a fronteira real, e esconder o botão não substitui autorização.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Tests  200 passed (200)`.

- [ ] **Step 2: Escrever o teste das permissões**

`web/src/auth/permissoes.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { podeEscrever } from './permissoes'

describe('podeEscrever', () => {
  it('deixa Administrador escrever em tudo', () => {
    for (const r of ['setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos'] as const) {
      expect(podeEscrever('Administrador', r), r).toBe(true)
    }
  })

  it('deixa PCP escrever em componentes, pedidos e agrupamentos', () => {
    expect(podeEscrever('PCP', 'componentes')).toBe(true)
    expect(podeEscrever('PCP', 'pedidos')).toBe(true)
    expect(podeEscrever('PCP', 'agrupamentos')).toBe(true)
  })

  it('NÃO deixa PCP escrever em setores nem materiais', () => {
    // Espelha `[Authorize(Roles = "Administrador")]` de SetoresController e MateriaisController.
    // Se esta linha inverter, o PCP ganha um formulário que o backend vai recusar com 403.
    expect(podeEscrever('PCP', 'setores')).toBe(false)
    expect(podeEscrever('PCP', 'materiais')).toBe(false)
  })

  it('não deixa Operador, Almoxarifado, Qualidade nem Gestao escreverem em nada', () => {
    // `Gestao` sem acento: é o valor que está em `db/seed.sql`, e o perfil chega do backend como
    // claim. Escrever "Gestão" aqui faria a comparação falhar em silêncio.
    for (const p of ['Operador', 'Almoxarifado', 'Qualidade', 'Gestao']) {
      for (const r of ['setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos'] as const) {
        expect(podeEscrever(p, r), `${p} / ${r}`).toBe(false)
      }
    }
  })

  it('nega perfil desconhecido em vez de liberar', () => {
    // Perfil novo no banco sem entrada aqui tem que cair no lado seguro.
    expect(podeEscrever('PerfilQueNaoExiste', 'pedidos')).toBe(false)
    expect(podeEscrever('', 'pedidos')).toBe(false)
  })
})
```

`web/src/auth/permissoes.ts`:

```ts
export type Recurso = 'setores' | 'materiais' | 'componentes' | 'pedidos' | 'agrupamentos'

/**
 * Espelho dos `[Authorize(Roles = …)]` do backend, conferidos no disco em 2026-08-06.
 *
 * **Isto é conveniência de interface, não segurança.** A autorização real é do backend e continua
 * sendo: esconder um botão não impede requisição nenhuma. O que esta tabela evita é o usuário
 * preencher um formulário inteiro para receber 403 no fim.
 *
 * Se um `[Authorize(Roles)]` mudar em `src/`, esta tabela tem de mudar junto — a divergência é
 * silenciosa nos dois sentidos: liberar demais mostra formulário que o backend recusa; liberar de
 * menos esconde ação que o usuário podia fazer.
 */
const ESCRITA: Record<Recurso, readonly string[]> = {
  setores: ['Administrador'],
  materiais: ['Administrador'],
  componentes: ['Administrador', 'PCP'],
  pedidos: ['PCP', 'Administrador'],
  agrupamentos: ['PCP', 'Administrador'],
}

export function podeEscrever(perfil: string, recurso: Recurso): boolean {
  return ESCRITA[recurso].includes(perfil)
}
```

`web/src/auth/usePermissao.ts`:

```ts
import { useAuth } from './AuthContext'
import { podeEscrever, type Recurso } from './permissoes'

/**
 * `podeEscrever` ligado à sessão. Sessão não autenticada devolve `false` — as telas de cadastro só
 * existem dentro do `ProtectedRoute`, então este caso é a montagem de teste, não produção.
 */
export function usePodeEscrever(recurso: Recurso): boolean {
  const { estado } = useAuth()
  return estado.status === 'autenticado' && podeEscrever(estado.usuario.perfil, recurso)
}
```

- [ ] **Step 3: Rodar e confirmar**

```bash
cd web && npm test -- permissoes
```

Expected: `Tests  5 passed (5)`.

- [ ] **Step 4: Escrever o teste do `AppShell`**

`web/src/components/AppShell.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AppShell } from './AppShell'

afterEach(cleanup)
afterEach(() => { vi.resetModules() })

const logout = vi.fn()

// O `AuthProvider` de verdade dispara init-refresh no mount; aqui só interessa o que o shell faz
// com a sessão já resolvida.
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: {
      status: 'autenticado',
      usuario: { id: 1, nomeUsuario: 'pcp', nomeCompleto: 'Planejamento e Controle', perfil: 'PCP' },
    },
    login: async () => {},
    logout,
  }),
}))

function renderizarShell(rotaInicial = '/') {
  return render(
    <MemoryRouter initialEntries={[rotaInicial]}>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="/" element={<p>conteúdo da home</p>} />
          <Route path="/setores" element={<p>conteúdo de setores</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('AppShell', () => {
  it('renderiza a tela filha', () => {
    // Sem o `<Outlet/>` o shell aparece e o conteúdo some — e o build passa.
    renderizarShell()

    expect(screen.getByText('conteúdo da home')).toBeTruthy()
  })

  it('dá caminho de volta a partir de qualquer tela interna', () => {
    // Hoje as seis telas internas só voltam pelo botão do navegador. Este é o defeito de navegação
    // que o shell existe para fechar.
    renderizarShell('/setores')

    expect(screen.getByRole('navigation', { name: 'Principal' })).toBeTruthy()
    expect(screen.getAllByRole('link').some((l) => l.getAttribute('href') === '/')).toBe(true)
  })

  it('leva a todas as áreas do sistema', () => {
    renderizarShell()

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    for (const d of ['/', '/pedidos', '/componentes', '/materiais', '/setores']) {
      expect(destinos, `link para ${d}`).toContain(d)
    }
  })

  it('mostra os links mesmo para perfil que não pode escrever neles', () => {
    // Decisão do usuário em 2026-08-06: gating vai na AÇÃO, não no link. O PCP não escreve em
    // Setores nem Materiais, mas LÊ os dois — e a Fase 4 depende disso para o Almoxarifado.
    renderizarShell()

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    expect(destinos).toContain('/setores')
    expect(destinos).toContain('/materiais')
  })

  it('marca o item da tela atual', () => {
    renderizarShell('/setores')

    const atual = screen.getAllByRole('link').find((l) => l.getAttribute('aria-current') === 'page')
    expect(atual?.getAttribute('href')).toBe('/setores')
  })

  it('mostra quem está logado e o perfil', () => {
    renderizarShell()

    expect(screen.getByText('Planejamento e Controle')).toBeTruthy()
    expect(screen.getByText('PCP')).toBeTruthy()
  })

  it('sai da sessão pelo botão Sair', () => {
    renderizarShell()

    fireEvent.click(screen.getByText('Sair'))

    expect(logout).toHaveBeenCalledTimes(1)
  })

  it('abre e fecha a gaveta pelo botão de menu', () => {
    renderizarShell()

    const botao = screen.getByRole('button', { name: 'Abrir menu' })
    expect(botao.getAttribute('aria-expanded')).toBe('false')

    fireEvent.click(botao)
    expect(screen.getByRole('button', { name: 'Fechar menu' }).getAttribute('aria-expanded')).toBe('true')

    fireEvent.click(screen.getByRole('button', { name: 'Fechar menu' }))
    expect(screen.getByRole('button', { name: 'Abrir menu' }).getAttribute('aria-expanded')).toBe('false')
  })

  it('fecha a gaveta ao navegar', () => {
    // Sem isto, no celular a gaveta continua cobrindo a tela que o usuário acabou de abrir — e ele
    // acha que o clique não funcionou.
    renderizarShell()

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    const linkDaGaveta = screen.getAllByRole('link').filter((l) => l.getAttribute('href') === '/setores')
    fireEvent.click(linkDaGaveta[linkDaGaveta.length - 1])

    expect(screen.getByRole('button', { name: 'Abrir menu' })).toBeTruthy()
  })
})
```

- [ ] **Step 5: Rodar para ver falhar**

```bash
cd web && npm test -- AppShell
```

Expected: FAIL — `Failed to resolve import "./AppShell"`.

- [ ] **Step 6: Escrever o `AppShell`**

`web/src/components/AppShell.tsx`:

```tsx
import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

interface ItemDeNavegacao {
  para: string
  rotulo: string
}

/**
 * Todos os itens aparecem para todos os perfis, de propósito: a leitura de todos estes recursos é
 * liberada para qualquer usuário autenticado no backend (conferido em 2026-08-06). O gating de
 * perfil vive na AÇÃO — formulário e botões de (in)ativar —, não aqui.
 *
 * Com 10+ telas esta barra fica apertada e vai exigir agrupamento (ex.: "Cadastros" com submenu).
 * Custo conhecido e aceito na spec §6: o agrupamento entra quando doer, não agora.
 */
const ITENS: ItemDeNavegacao[] = [
  { para: '/', rotulo: 'Início' },
  { para: '/pedidos', rotulo: 'Pedidos' },
  { para: '/componentes', rotulo: 'Componentes' },
  { para: '/materiais', rotulo: 'Materiais' },
  { para: '/setores', rotulo: 'Setores' },
]

const LINK_BASE =
  'rounded-lg px-3 py-2 text-sm font-medium transition-colors ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca'

function classesDoLink({ isActive }: { isActive: boolean }): string {
  // Sobre o chrome escuro o anel de foco é `marca`, não `acao`: o anel em água fosco sobre
  // petróleo teria contraste insuficiente e o foco sumiria justamente na navegação por teclado.
  return `${LINK_BASE} ${isActive ? 'bg-superficie/15 text-superficie' : 'text-superficie/80 hover:bg-superficie/10 hover:text-superficie'}`
}

export function AppShell() {
  const { estado, logout } = useAuth()
  const [gavetaAberta, setGavetaAberta] = useState(false)

  const usuario = estado.status === 'autenticado' ? estado.usuario : null

  // A gaveta fecha no `onClick` de cada link dela (mais abaixo), e não num efeito sobre a rota:
  // o efeito rodaria também quando a navegação vem de outro lugar (um cartão da Home, o `Navigate`
  // do login), e fechar algo que já está fechado é render à toa. Sem isto, no celular a gaveta
  // continua cobrindo a tela que o usuário acabou de abrir e o clique parece não ter funcionado.
  return (
    <div className="min-h-screen bg-fundo font-sans text-tinta">
      <header className="bg-chrome">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-3 sm:px-6">
          <span className="text-lg font-semibold tracking-tight text-marca">Rastru</span>

          {/* Barra: some abaixo de 768px, onde a gaveta assume. */}
          <nav aria-label="Principal" className="hidden md:flex md:items-center md:gap-1">
            {ITENS.map((i) => (
              <NavLink key={i.para} to={i.para} end={i.para === '/'} className={classesDoLink}>
                {i.rotulo}
              </NavLink>
            ))}
          </nav>

          <div className="hidden md:flex md:items-center md:gap-3">
            {usuario && (
              <span className="text-right text-sm leading-tight text-superficie/80">
                <span className="block font-medium text-superficie">{usuario.nomeCompleto}</span>
                <span className="block text-xs">{usuario.perfil}</span>
              </span>
            )}
            <button
              type="button"
              onClick={logout}
              className={`${LINK_BASE} border border-superficie/40 text-superficie hover:bg-superficie/10`}
            >
              Sair
            </button>
          </div>

          <button
            type="button"
            onClick={() => setGavetaAberta((a) => !a)}
            aria-expanded={gavetaAberta}
            aria-label={gavetaAberta ? 'Fechar menu' : 'Abrir menu'}
            className={`${LINK_BASE} border border-superficie/40 text-superficie md:hidden`}
          >
            {gavetaAberta ? '✕' : '☰'}
          </button>
        </div>

        {/* Gaveta: mesma lista, empilhada, só abaixo de 768px. O celular Android da fábrica é uso
            declarado, não hipótese. */}
        {gavetaAberta && (
          <nav aria-label="Menu" className="flex flex-col gap-1 border-t border-superficie/15 px-4 pb-4 md:hidden">
            {ITENS.map((i) => (
              <NavLink
                key={i.para}
                to={i.para}
                end={i.para === '/'}
                onClick={() => setGavetaAberta(false)}
                className={classesDoLink}
              >
                {i.rotulo}
              </NavLink>
            ))}
            {usuario && (
              <span className="px-3 pt-2 text-sm text-superficie/80">
                {usuario.nomeCompleto} · {usuario.perfil}
              </span>
            )}
            <button
              type="button"
              onClick={logout}
              className={`${LINK_BASE} self-start border border-superficie/40 text-superficie`}
            >
              Sair
            </button>
          </nav>
        )}
      </header>

      <Outlet />
    </div>
  )
}
```

**Um ponto de atenção no teste, e ele não é contornável:** com a gaveta **aberta**, `Sair` e o nome do usuário existem **duas vezes** no DOM (barra + gaveta), e `getByText('Sair')` falha com *"found multiple elements"*. Na montagem dos testes a gaveta começa fechada, então o teste de logout passa como está; o teste da gaveta usa `getAllByRole` por isso. **Se você mudar a estrutura, ajuste os testes para `getAllBy*` — não remova um dos dois botões.** No celular a barra está escondida por CSS, e é o botão da gaveta que o usuário alcança; remover um deles deixaria um dos dois tamanhos de tela sem saída de sessão.

- [ ] **Step 7: Rodar até ficar verde**

```bash
cd web && npm test -- AppShell
```

Expected: `Tests  9 passed (9)`.

- [ ] **Step 8: Ligar o shell no roteador**

`web/src/App.tsx`:

```tsx
import { Routes, Route, Navigate } from 'react-router-dom'
import { LoginPage } from './pages/LoginPage'
import { HomePage } from './pages/HomePage'
import { SetoresPage } from './pages/SetoresPage'
import { MateriaisPage } from './pages/MateriaisPage'
import { ComponentesPage } from './pages/ComponentesPage'
import { PedidosPage } from './pages/PedidosPage'
import { PedidoDetalhePage } from './pages/PedidoDetalhePage'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { AppShell } from './components/AppShell'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      {/*
        Rota de layout: o shell embrulha TODAS as telas internas de uma vez, em vez de cada tela
        repetir cabeçalho e caminho de volta. O `ProtectedRoute` sobe para cá junto — antes ele
        estava repetido em seis linhas, e uma tela nova podia nascer sem ele por esquecimento.
      */}
      <Route element={<ProtectedRoute><AppShell /></ProtectedRoute>}>
        <Route path="/" element={<HomePage />} />
        <Route path="/setores" element={<SetoresPage />} />
        <Route path="/materiais" element={<MateriaisPage />} />
        <Route path="/componentes" element={<ComponentesPage />} />
        <Route path="/pedidos" element={<PedidosPage />} />
        <Route path="/pedidos/:id" element={<PedidoDetalhePage />} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
```

- [ ] **Step 9: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  214 passed (214)` (200 + 5 + 9) · build limpo · lint só com o warning alheio.

**Se algum teste de tela quebrar aqui, leia com cuidado:** os testes de tela renderizam a tela **direto**, sem o `App`, então o shell não entra em nenhum deles. Uma quebra aqui significa que você mexeu numa tela — o que esta task não faz.

- [ ] **Step 10: Ver no navegador, antes de commitar**

```bash
cd web && npm run dev
```

Abra `http://localhost:5173`, entre com `admin` / `Admin@123` (a API precisa estar de pé: `dotnet run --project src/Rastreamento.Api`) e confirme, **manualmente**:

- a barra aparece em todas as telas internas e o item da tela atual está marcado;
- em 360px de largura (DevTools → Toggle device toolbar) a barra vira o botão de menu e a gaveta abre;
- **nenhuma tela rola na horizontal** em 360px — este é o critério da spec §11 que nenhum teste em jsdom alcança;
- `Tab` percorre os links com anel de foco visível sobre o chrome escuro.

**Reporte o que viu, item por item.** As telas ainda estão com o visual antigo dentro do shell novo — é esperado; o retrofit começa na Task 8.

- [ ] **Step 11: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `permissoes.ts` — acrescentar `'PCP'` a `setores` | ≥ 1 |
| M2 | `permissoes.ts` — trocar `.includes(perfil)` por `.length > 0` | ≥ 1 |
| M3 | `permissoes.ts` — acrescentar `'Gestao'` a `pedidos` | ≥ 1 |
| M4 | `AppShell.tsx` — remover o `<Outlet />` | ≥ 1 |
| M5 | `AppShell.tsx` — remover o item `/setores` de `ITENS` | ≥ 1 |
| M6 | `AppShell.tsx` — trocar `NavLink` por `Link` (perde o `aria-current`) | ≥ 1 |
| M7 | `AppShell.tsx` — remover `end={i.para === '/'}` | ≥ 1 (o "Início" ficaria marcado em toda tela) |
| M8 | `AppShell.tsx` — remover `onClick={() => setGavetaAberta(false)}` dos links da gaveta | ≥ 1 |
| M9 | `AppShell.tsx` — trocar `onClick={logout}` por `onClick={() => {}}` | ≥ 1 |
| M10 | `AppShell.tsx` — trocar `aria-label` do botão por texto fixo `'Menu'` | ≥ 1 |
| M11 | `App.tsx` — tirar o `ProtectedRoute` da rota de layout | **0 esperado** — nenhum teste monta o `App`; **declare como sobrevivente conhecido** |

**M11 é a segunda fronteira honesta da fase** (a primeira foi o `flex-wrap` da Task 6): não há teste de roteamento no projeto, então a proteção das rotas é mantida por leitura de código, não por prova. Não invente um teste de `App` só para fechá-la — **declare** e siga. Se a review de branch quiser fechar, é decisão dela.

- [ ] **Step 12: Commit**

```bash
git add web/src/auth/permissoes.ts web/src/auth/permissoes.test.ts web/src/auth/usePermissao.ts web/src/components/AppShell.tsx web/src/components/AppShell.test.tsx web/src/App.tsx
git commit -m "feat(web): shell de navegacao com gaveta e tabela de permissoes"
```

**Definition of done da Task 7:** suíte em **214**; as 11 mutações medidas (M11 declarada); a conferência manual do Step 10 **relatada item por item**; build e lint limpos.

---

### Task 8: Retrofit de `SetoresPage`, `MateriaisPage` e `PedidosPage`

**Files:**
- Modify: `web/src/pages/SetoresPage.tsx` + `SetoresPage.test.tsx`
- Modify: `web/src/pages/MateriaisPage.tsx` + `MateriaisPage.test.tsx`
- Modify: `web/src/pages/PedidosPage.tsx` + `PedidosPage.test.tsx`

**Interfaces:**
- Consumes: `Pagina`, `Botao`, `Campo`+`CLASSES_DE_CONTROLE`, `BannerDeErro` (Task 5); `ListaDeCadastro`+`ItemDeCadastro`, `Pilula`, `EstadoVazio` (Task 6); `usePodeEscrever` (Task 7); `mensagemDeErro` (Task 2).
- Produces: nada de novo. **É consumo puro** — se você precisou criar uma primitiva aqui, ela faltou nas Tasks 5–6 e isso é achado a reportar.

**As três não recebem redesenho** (spec §7): são formulário + lista, e o container novo mais as primitivas as resolvem por inteiro. **Se durante a execução alguma destoar de fato ao lado das novas, absorva na própria fase** — são as telas mais simples do sistema — e não abra fase nova.

> **A duplicação do ciclo de CRUD entre as telas é DELIBERADA e já foi adjudicada — não é achado.**
> Decisão do usuário no pre-flight de 2026-08-06, com a análise medida antes de perguntar.
>
> A review de branch da 1B registrou que "a duplicação deixou de ser barata", e o defeito I1
> apareceu nas três telas ao mesmo tempo — o argumento a favor de extrair um `useCadastroSimples`
> é real. O que decide contra é o **alcance**: o hook serviria **só `SetoresPage` e
> `MateriaisPage`**. `PedidosPage` não tem `ativo`/`reativar` (não há coluna `Ativo` em `Pedido`),
> e `ComponentesPage` carrega pelo `useBuscaPaginada` — o `carregar` dela é outro. Extrair uma
> abstração para **dois** consumidores é o mesmo problema que a spec §5 nomeia no critério das
> primitivas, invertido.
>
> **Se a review levantar isto, a resposta é este bloco.** Reabrir exige medição nova — por exemplo,
> um terceiro consumidor real aparecendo.

**Quatro mudanças de comportamento entram junto do markup**, e são as que a spec §9 pede:
1. `mensagemDeErro` no lugar do texto fixo em todo `catch`;
2. `EstadoVazio` distinguindo lista vazia de erro;
3. botão desabilitado durante a mutação (`enviando`);
4. gating na ação — `usePodeEscrever` esconde formulário e botões de (in)ativar.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Tests  214 passed (214)`.

- [ ] **Step 2: Reescrever a `SetoresPage`**

`web/src/pages/SetoresPage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import {
  listarSetores, criarSetor, definirAtivoSetor, ehConflito, type SetorDto,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { EstadoVazio } from '../components/EstadoVazio'

export function SetoresPage() {
  const [setores, setSetores] = useState<SetorDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [nome, setNome] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('setores')

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setSetores(await listarSetores(comInativos))
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os setores.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarSetor(nome)
      if (ehConflito(resultado)) {
        if (resultado.existeInativo) {
          setErro(`Já existe um setor "${nome}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um setor com este nome.')
        }
        return
      }
      setNome('')
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o setor.'))
    } finally {
      setEnviando(false)
    }
  }

  // O `try/catch` continua sendo a fronteira REAL de perfil (F2): esconder o botão é conveniência
  // de interface, e o 403 do backend segue valendo para quem chamar a API por fora da tela.
  async function alternarAtivo(setor: SetorDto) {
    try {
      await definirAtivoSetor(setor.id, !setor.ativo)
      setErro(null)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar o setor.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoSetor(id, true)
      setErro(null)
      setIdReativavel(null)
      setNome('')
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível reativar o setor.'))
    }
  }

  return (
    <Pagina titulo="Setores">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4 sm:flex-row sm:items-end">
          <div className="flex-1">
            <Campo rotulo="Nome do setor">
              {(id) => (
                <input
                  id={id}
                  value={nome}
                  onChange={(e) => setNome(e.target.value)}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…">Adicionar</Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      <label className="flex items-center gap-2 text-sm text-tinta-fraca">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => setIncluirInativos(e.target.checked)}
          className="size-4 accent-acao"
        />
        Mostrar inativos
      </label>

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : setores.length === 0 ? (
        // Estado vazio distinguível do erro: o banner acima já cobre a falha, e aqui só sobra o
        // caso "não há setores". Antes os dois renderizavam a mesma lista vazia e muda.
        <EstadoVazio
          titulo="Nenhum setor cadastrado"
          descricao={podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {setores.map((s) => (
            <ItemDeCadastro
              key={s.id}
              ativo={s.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(s)}>
                  {s.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              {s.nome}
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
```

- [ ] **Step 3: Atualizar o teste da `SetoresPage`**

Os dois testes que já existem passam a precisar de uma sessão **com** permissão de escrita (senão o formulário e o checkbox somem). Reescreva `web/src/pages/SetoresPage.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { SetoresPage } from './SetoresPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// O perfil da sessão passa a governar o que a tela mostra, então ele é variável de teste agora.
let perfil = 'Administrador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const CORTE = { id: 1, nome: 'Corte', ativo: true }

describe('SetoresPage', () => {
  beforeEach(() => {
    perfil = 'Administrador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra os setores que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
  })

  // I1 (achado da review de branch da 1B): `carregar` escreve `erro` no `catch` mas nunca o limpa
  // no caminho de sucesso. A carga inicial falha; marcar "Mostrar inativos" dispara uma recarga que
  // dá certo — a lista nova tem que aparecer E a mensagem tem que sumir.
  it('limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
        return respostaJson([CORTE])
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await screen.findByText('Não foi possível carregar os setores.')

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await screen.findByText('Corte')
    expect(screen.queryByText('Não foi possível carregar os setores.')).toBeNull()
  })

  it('explica o 403 em vez do texto genérico', async () => {
    // A dívida da Task 2 chegando à tela: sem `mensagemDeErro`, um Operador que tentasse inativar
    // leria "Não foi possível alterar o setor" e tentaria de novo, indefinidamente.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => respostaJson([CORTE]),
      '/api/setores/1/ativo': () => respostaJson({ erro: 'proibido' }, 403),
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByText('Inativar'))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  it('mostra estado vazio quando não há setores', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Nenhum setor cadastrado')).toBeTruthy()
  })

  it('desabilita o botão enquanto o cadastro está em voo', async () => {
    // O POST fica pendurado de propósito, para a asserção acontecer com a mutação EM VOO. Sem o
    // `enviando`, o PCP clica duas vezes no wifi lento e o segundo POST volta 409 — a tela
    // acusando de duplicado o cadastro que ela mesma acabou de fazer.
    let liberar: (r: Response) => void = () => {}
    let jaListou = false
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        // A primeira chamada é a listagem (GET); a segunda é o POST.
        if (!jaListou) { jaListou = true; return respostaJson([]) }
        return new Promise<Response>((r) => { liberar = r })
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const botao = await screen.findByText('Salvando…')
    expect((botao as HTMLButtonElement).disabled).toBe(true)

    // Solta a resposta no fim para o teste não terminar com promise pendurada, o que vaza estado
    // para o caso seguinte.
    liberar(respostaJson({ id: 2, nome: 'Solda', ativo: true }, 201))
  })

  it('esconde formulário e ação de inativar para quem não pode escrever', async () => {
    // PCP lê setores mas não escreve — `[Authorize(Roles = "Administrador")]` no backend. O link
    // continua no shell de propósito; o que some é a ação.
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
    expect(screen.queryByLabelText('Nome do setor')).toBeNull()
    expect(screen.queryByText('Inativar')).toBeNull()
  })
})
```

**Sobre o `jaListou` do último teste:** ele distingue GET de POST **contando chamadas**, o que é frágil se a tela passar a fazer uma listagem a mais. Uma alternativa mais robusta é olhar `init.method` — `fetchPorRota` recebe a URL, então isso exige alterar o kit para repassar o segundo argumento. **Não altere o kit por causa deste teste**; se a contagem quebrar numa task futura, aí sim é o momento.

- [ ] **Step 4: Rodar até ficar verde**

```bash
cd web && npm test -- SetoresPage
```

Expected: `Tests  6 passed (6)`.

- [ ] **Step 5: Commit da `SetoresPage`**

```bash
git add web/src/pages/SetoresPage.tsx web/src/pages/SetoresPage.test.tsx
git commit -m "feat(web): retrofit da SetoresPage com as primitivas e gating de acao"
```

- [ ] **Step 6: Reescrever a `MateriaisPage`**

`web/src/pages/MateriaisPage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import {
  listarMateriais, criarMaterial, definirAtivoMaterial, ehConflito,
  type MaterialDto, type NovoMaterial,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'

const FORMULARIO_VAZIO: NovoMaterial = { codigo: '', descricao: '', unidadeMedida: '' }

export function MateriaisPage() {
  const [materiais, setMateriais] = useState<MaterialDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [form, setForm] = useState<NovoMaterial>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('materiais')

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setMateriais(await listarMateriais(comInativos))
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os materiais.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarMaterial(form)
      if (ehConflito(resultado)) {
        // O conflito é sempre sobre o código (UQ_Material_Codigo); descrição repetida passa.
        if (resultado.existeInativo) {
          setErro(`Já existe um material com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um material com este código.')
        }
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o material.'))
    } finally {
      setEnviando(false)
    }
  }

  async function alternarAtivo(material: MaterialDto) {
    try {
      await definirAtivoMaterial(material.id, !material.ativo)
      setErro(null)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar o material.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoMaterial(id, true)
      setErro(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível reativar o material.'))
    }
  }

  return (
    <Pagina titulo="Materiais">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  // Monoespaçada em código de material: alinha na coluna e facilita conferir
                  // contra o desenho na bancada (spec §4). É funcional, não decorativo.
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Unidade" dica="UN, KG, M…">
              {(id, idDaDica) => (
                /* Texto livre de propósito: NVARCHAR(10) sem CHECK no DDL, sem lista fechada. */
                <input
                  id={id}
                  aria-describedby={idDaDica}
                  value={form.unidadeMedida}
                  onChange={(e) => setForm({ ...form, unidadeMedida: e.target.value })}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <Campo rotulo="Descrição">
            {(id) => (
              <input
                id={id}
                value={form.descricao}
                onChange={(e) => setForm({ ...form, descricao: e.target.value })}
                required
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      <label className="flex items-center gap-2 text-sm text-tinta-fraca">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => setIncluirInativos(e.target.checked)}
          className="size-4 accent-acao"
        />
        Mostrar inativos
      </label>

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : materiais.length === 0 ? (
        <EstadoVazio
          titulo="Nenhum material cadastrado"
          descricao={podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {materiais.map((m) => (
            <ItemDeCadastro
              key={m.id}
              ativo={m.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(m)}>
                  {m.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              <span className="font-mono font-semibold">{m.codigo}</span>
              {' — '}
              {m.descricao}
              {' '}
              <Pilula>{m.unidadeMedida}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
```

- [ ] **Step 7: Atualizar o teste da `MateriaisPage`**

O arquivo atual tem 2 testes que renderizam a tela e batem em `/api/materiais`. Aplique **as mesmas** mudanças da `SetoresPage`:

1. acrescente o `vi.mock('../auth/AuthContext', …)` com a variável `perfil` e `beforeEach(() => { perfil = 'Administrador' })`;
2. troque os mocks de `fetch` inline por `fetchPorRota` + `respostaJson`;
3. **os campos deixam de ter `placeholder`** — troque todo `getByPlaceholderText('Código')` por `getByLabelText('Código')`, e idem para "Descrição" e "Unidade";
4. acrescente os quatro testes novos, com os mesmos textos da `SetoresPage` adaptados:
   - explica o 403 em vez do texto genérico (rota `/api/materiais/1/ativo`);
   - mostra estado vazio (`'Nenhum material cadastrado'`);
   - desabilita o botão enquanto o cadastro está em voo (`'Salvando…'`);
   - esconde formulário e ação para quem não pode escrever (`perfil = 'PCP'`).

Expected ao fim: `Tests  6 passed (6)` em `npm test -- MateriaisPage`.

- [ ] **Step 8: Reescrever a `PedidosPage`**

`web/src/pages/PedidosPage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarPedidos, criarPedido, ehConflito, formatarDataHora,
  type PedidoDto, type NovoPedido,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'

const FORMULARIO_VAZIO: NovoPedido = { numero: '', cliente: '' }

/**
 * Status do `CK_Pedido_Status`. `Concluido` é o único que ganha tom positivo; `Cancelado`, o
 * negativo. Os intermediários ficam neutros — verde e vermelho são reservados a estado que exige
 * decisão, e "em produção" não exige nenhuma.
 */
function tomDoStatus(status: string): 'neutro' | 'positivo' | 'negativo' {
  if (status === 'Concluido') return 'positivo'
  if (status === 'Cancelado') return 'negativo'
  return 'neutro'
}

export function PedidosPage() {
  const [pedidos, setPedidos] = useState<PedidoDto[]>([])
  const [form, setForm] = useState<NovoPedido>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('pedidos')

  async function carregar() {
    setCarregando(true)
    try {
      setPedidos(await listarPedidos())
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os pedidos.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      const resultado = await criarPedido(form)
      if (ehConflito(resultado)) {
        // Pedido não tem reativação (não há coluna Ativo): o caminho é abrir o que já existe.
        setErro('Já existe um pedido com este número.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar()
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o pedido.'))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Pagina titulo="Pedidos">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código do pedido">
              {(id) => (
                <input
                  id={id}
                  value={form.numero}
                  onChange={(e) => setForm({ ...form, numero: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Cliente">
              {(id) => (
                <input
                  id={id}
                  value={form.cliente}
                  onChange={(e) => setForm({ ...form, cliente: e.target.value })}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Abrindo…" className="self-start">
            Abrir pedido
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : pedidos.length === 0 ? (
        <EstadoVazio
          titulo="Nenhum pedido aberto"
          descricao={podeEscrever ? 'Use o formulário acima para abrir o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {pedidos.map((p) => (
            <ItemDeCadastro key={p.id}>
              {/*
                O item inteiro é o alvo do clique, e não só o número: numa tela de bancada com
                tablet, alvo pequeno erra. `after:absolute after:inset-0` estende a área clicável do
                link ao cartão sem aninhar elementos interativos.
              */}
              <Link
                to={`/pedidos/${p.id}`}
                className="flex flex-col gap-1 after:absolute after:inset-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
              >
                <span className="font-medium">
                  <span className="font-mono">{p.numero}</span> — {p.cliente}
                </span>
                <span className="flex items-center gap-2 text-sm text-tinta-fraca">
                  <Pilula tom={tomDoStatus(p.status)}>{p.status}</Pilula>
                  aberto em {formatarDataHora(p.dataAbertura)}
                </span>
              </Link>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
```

**O `after:inset-0` exige `relative` no `<li>`** — acrescente `relative` às classes de `ItemDeCadastro` em `web/src/components/ListaDeCadastro.tsx` e **rode `npm test -- ListaDeCadastro` para confirmar que nada quebrou**. Sem o `relative`, o pseudo-elemento se ancora no viewport e cobre a tela inteira: todo clique em qualquer lugar navegaria para o último pedido da lista. **É um defeito de tela cheia que nenhum teste em jsdom pega** — confirme no navegador, na Task 12.

- [ ] **Step 9: Atualizar o teste da `PedidosPage`**

O arquivo criado na Task 1 tem 4 testes. Ajuste:

1. acrescente o `vi.mock('../auth/AuthContext', …)` com `perfil` (default `'PCP'`, que **pode** escrever pedidos);
2. troque `getByPlaceholderText('Código do pedido')` por `getByLabelText('Código do pedido')`, e `'Cliente'` idem;
3. acrescente três testes:
   - **estado vazio**: `'Nenhum pedido aberto'`;
   - **gating**: `perfil = 'Operador'` → `queryByLabelText('Código do pedido')` é `null` e a lista continua visível;
   - **status como pílula**: um pedido `Concluido` e um `Cancelado` na mesma lista, asserindo que os dois textos aparecem.

Expected: `Tests  7 passed (7)` em `npm test -- PedidosPage`.

- [ ] **Step 10: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  225 passed (225)` (214 + 4 na Setores + 4 na Materiais + 3 na Pedidos) · build limpo · lint só com o warning alheio.

**Se o número divergir, reporte o número real e a composição** — não ajuste o plano; a contagem exata depende de quantos testes antigos você fundiu ou dividiu.

- [ ] **Step 11: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `SetoresPage.tsx` — trocar `mensagemDeErro(e, …)` por só o fallback no `catch` de `alternarAtivo` | ≥ 1 |
| M2 | `SetoresPage.tsx` — trocar `podeEscrever &&` por `true &&` no formulário | ≥ 1 |
| M3 | `SetoresPage.tsx` — remover `setEnviando(true)` | ≥ 1 |
| M4 | `SetoresPage.tsx` — remover o `finally { setEnviando(false) }` | ≥ 1 (o botão nunca voltaria) |
| M5 | `SetoresPage.tsx` — trocar `setores.length === 0` por `false` | ≥ 1 |
| M6 | `MateriaisPage.tsx` — trocar `podeEscrever` de `'materiais'` por `'componentes'` | ≥ 1 (o PCP ganharia formulário que o backend recusa) |
| M7 | `MateriaisPage.tsx` — trocar `{m.unidadeMedida}` da pílula por `{m.tipo}` (campo inexistente) | ≥ 1 |
| M8 | `PedidosPage.tsx` — trocar `tomDoStatus` para devolver `'positivo'` sempre | ≥ 1 |
| M9 | `PedidosPage.tsx` — trocar `usePodeEscrever('pedidos')` por `('setores')` | ≥ 1 |
| M10 | `PedidosPage.tsx` — remover `setErro(null)` do sucesso de `carregar` | ≥ 1 |

- [ ] **Step 12: Commit**

```bash
git add web/src/pages/MateriaisPage.tsx web/src/pages/MateriaisPage.test.tsx web/src/pages/PedidosPage.tsx web/src/pages/PedidosPage.test.tsx web/src/components/ListaDeCadastro.tsx
git commit -m "feat(web): retrofit de MateriaisPage e PedidosPage com as primitivas"
```

**Definition of done da Task 8:** as três telas sem nenhuma classe antiga (`max-w-md`, `border rounded px-3 py-2`, `text-gray-*`, `text-red-600`); as 10 mutações medidas com ≥ 1 morte; suíte, build e lint verdes.

---

### Task 9: Re-layout da `ComponentesPage` e adoção do `useBuscaPaginada`

**Files:**
- Modify: `web/src/pages/ComponentesPage.tsx` (227 linhas hoje)
- Modify: `web/src/pages/ComponentesPage.test.tsx` (**987 linhas, 33 testes** — a maior reescrita da fase)

**Interfaces:**
- Consumes: `useBuscaPaginada` (Task 3), `mensagemDeErro` (Task 2), todas as primitivas (Tasks 5–6), `usePodeEscrever` (Task 7).
- Produces: nada.

**A tela que a spec §7 marca como re-layout obrigatório:** busca + filtro + seletor de tamanho + paginação **não cabem em 448px**. É também a única com busca, e por isso a única consumidora do hook nesta fase.

**Esta task apaga cerca de 60 linhas de lógica da tela** — a guarda de sequência (`sequenciaRef`), os três `mudar*` com `setPagina(1)`, o `carregar` com quatro parâmetros e o cálculo de `totalDePaginas` **vivem no hook agora**. Apagar sem substituir é o risco: confira que cada propriedade continua provada, ou pela suíte do hook, ou pela da tela.

**Aviso sobre a suíte de tela:** os 33 testes de hoje foram escritos contra o comportamento **sem debounce**. Vários assertam "digitou 3 letras → 3 requisições" ou olham `fetchMock.mock.calls[n]` contando chamadas. **Eles vão quebrar, e a quebra é correta.** O que **não** vale é apagar teste para a suíte fechar: cada um que sair tem de sair porque a propriedade dele mudou de dono (foi para `useBuscaPaginada.test.tsx`) ou porque o comportamento mudou de propósito. **Liste no relatório, um a um, os testes removidos e o motivo.**

- [ ] **Step 1: Confirmar a baseline e inventariar a suíte que vai mudar**

```bash
cd web && npm test -- ComponentesPage
```

Expected: `Tests  33 passed (33)`.

Liste os nomes antes de mexer — é este inventário que o relatório vai comparar no fim:

```bash
cd web && grep -n "^\s*it(" src/pages/ComponentesPage.test.tsx
```

- [ ] **Step 2: Reescrever a `ComponentesPage`**

`web/src/pages/ComponentesPage.tsx`:

```tsx
import { useState, type FormEvent } from 'react'
import {
  listarComponentes, criarComponente, definirAtivoComponente, ehConflito,
  type ComponenteDto, type NovoComponente, type TipoDeComponente,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { useBuscaPaginada } from '../hooks/useBuscaPaginada'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'
import { ControlesDePaginacao } from '../components/ControlesDePaginacao'

const FORMULARIO_VAZIO: NovoComponente = { codigo: '', descricao: '', tipo: 'Fabricado' }

/** As três opções de `CK_Componente_Tipo`. Lista fechada, ao contrário de `unidadeMedida`. */
const TIPOS: TipoDeComponente[] = ['Bruto', 'Fabricado', 'Montagem']

/** Dentro do teto de 100 do backend, de propósito: um valor acima viraria 400. */
const TAMANHOS = [20, 50, 100]

export function ComponentesPage() {
  const [form, setForm] = useState<NovoComponente>(FORMULARIO_VAZIO)
  const [erroDeEscrita, setErroDeEscrita] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('componentes')

  // `listarComponentes` é passada direto por ser estável (função de módulo) e por a assinatura
  // dela já ser exatamente `(FiltroDeBusca) => Promise<{itens, total, …}>`. Nada de lambda inline
  // aqui: o hook a guarda num ref justamente para tolerar isso, mas passar a estável é mais claro.
  const lista = useBuscaPaginada<ComponenteDto>({ buscar: listarComponentes })

  // Dois erros, e não um: o de LEITURA vem do hook e é apagado pela recarga seguinte; o de
  // ESCRITA (conflito de código, 403) tem de sobreviver à recarga que o próprio salvar dispara.
  // Um estado só faria a mensagem de duplicidade piscar e sumir — o defeito que a review da Task 11
  // da Fase 1A chamou de "erro que pisca".
  const erroDeLeitura = lista.erro === null
    ? null
    : mensagemDeErro(lista.erro, 'Não foi possível carregar os componentes.')

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErroDeEscrita(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarComponente(form)
      if (ehConflito(resultado)) {
        // O conflito é sempre sobre o código (UQ_Componente_Codigo); descrição repetida passa.
        if (resultado.existeInativo) {
          setErroDeEscrita(`Já existe um componente com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErroDeEscrita('Já existe um componente com este código.')
        }
        return
      }
      setForm(FORMULARIO_VAZIO)
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível salvar o componente.'))
    } finally {
      setEnviando(false)
    }
  }

  // O 403 do backend é a fronteira real de perfil (F2): esconder o botão é conveniência, e o
  // try/catch é o que faz a tela dizer alguma coisa quando ele chega assim mesmo.
  async function alternarAtivo(componente: ComponenteDto) {
    try {
      await definirAtivoComponente(componente.id, !componente.ativo)
      setErroDeEscrita(null)
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível alterar o componente.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoComponente(id, true)
      setErroDeEscrita(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível reativar o componente.'))
    }
  }

  const buscando = lista.textoDaBusca.trim() !== ''

  return (
    <Pagina titulo="Componentes">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            {/* Lista fechada (CK_Componente_Tipo): select, não input livre. */}
            <Campo rotulo="Tipo">
              {(id) => (
                <select
                  id={id}
                  value={form.tipo}
                  onChange={(e) => setForm({ ...form, tipo: e.target.value as TipoDeComponente })}
                  className={CLASSES_DE_CONTROLE}
                >
                  {TIPOS.map((t) => <option key={t} value={t}>{t}</option>)}
                </select>
              )}
            </Campo>
          </div>
          <Campo rotulo="Descrição">
            {(id) => (
              <input
                id={id}
                value={form.descricao}
                onChange={(e) => setForm({ ...form, descricao: e.target.value })}
                required
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erroDeEscrita ?? erroDeLeitura} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      {/*
        A barra de filtros é o que não cabia em 448px (spec §7). Em `max-w-3xl` os três controles
        cabem lado a lado a partir de `sm`, e empilham no celular sem rolagem horizontal.
      */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end">
        <div className="flex-1">
          <Campo rotulo="Buscar por código ou descrição">
            {(id) => (
              <input
                id={id}
                value={lista.textoDaBusca}
                onChange={(e) => lista.mudarBusca(e.target.value)}
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
        </div>
        <label className="flex items-center gap-2 text-sm text-tinta-fraca sm:pb-2.5">
          <input
            type="checkbox"
            checked={lista.incluirInativos}
            onChange={(e) => lista.mudarInativos(e.target.checked)}
            className="size-4 accent-acao"
          />
          Mostrar inativos
        </label>
        <Campo rotulo="Por página">
          {(id) => (
            <select
              id={id}
              value={lista.tamanho}
              onChange={(e) => lista.mudarTamanho(Number(e.target.value))}
              className={CLASSES_DE_CONTROLE}
            >
              {TAMANHOS.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          )}
        </Campo>
      </div>

      {lista.carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : lista.itens.length === 0 ? (
        // Os três vazios que a spec §9 manda distinguir: busca sem resultado, catálogo vazio e —
        // acima, no banner — erro de rede. Antes os três renderizavam a mesma lista muda.
        <EstadoVazio
          titulo={buscando ? 'Nenhum componente encontrado' : 'Nenhum componente cadastrado'}
          descricao={
            buscando
              ? `Nada corresponde a "${lista.textoDaBusca}".`
              : podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined
          }
        />
      ) : (
        <ListaDeCadastro>
          {lista.itens.map((c) => (
            <ItemDeCadastro
              key={c.id}
              ativo={c.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(c)}>
                  {c.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              <span className="font-mono font-semibold">{c.codigo}</span>
              {' — '}
              {c.descricao}
              {' '}
              <Pilula>{c.tipo}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}

      <ControlesDePaginacao
        pagina={lista.pagina}
        totalDePaginas={lista.totalDePaginas}
        total={lista.total}
        aoMudarPagina={lista.irParaPagina}
      />
    </Pagina>
  )
}
```

- [ ] **Step 3: Adaptar a suíte da tela**

Trabalhe **em cima** do arquivo existente, não do zero. Três classes de mudança:

**(a) Harness.** Acrescente no topo, como nas outras telas:

```tsx
import { respostaJson, fetchPorRota } from '../testes/api'

let perfil = 'Administrador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))
```

E, porque a tela agora tem debounce, **todo teste que digita na busca** precisa de timers falsos e do helper de avanço:

```tsx
async function avancar(ms: number) {
  await act(async () => { await vi.advanceTimersByTimeAsync(ms) })
}
```

**Use timers falsos só nos testes que tocam a busca.** Ligá-los no arquivo inteiro obrigaria a reescrever os 20 testes que não têm nada com debounce, e o `findBy*` deles passaria a depender de avanço manual. `vi.useFakeTimers()` dentro do `it`, `vi.useRealTimers()` no `afterEach`.

**(b) Seletores.** Os campos deixam de ter `placeholder`:

| Antes | Depois |
|---|---|
| `getByPlaceholderText('Código')` | `getByLabelText('Código')` |
| `getByPlaceholderText('Descrição')` | `getByLabelText('Descrição')` |
| `getByLabelText('Tipo')` | continua (já era `aria-label`, agora é `<label>`) |
| `getByPlaceholderText('Buscar por código ou descrição')` | `getByLabelText('Buscar por código ou descrição')` |

**(c) Testes cuja propriedade mudou de dono.** Estes saem daqui **porque estão provados em `useBuscaPaginada.test.tsx`**, e o relatório tem de nomeá-los: reset de página nos três gatilhos, guarda de sequência da corrida, clamp, `totalDePaginas`, e o W3. **O que NÃO sai:** tudo que prova a integração da tela — corpo do POST, ramo 201, conflito com e sem inativo, alternar ativo, reativar, e a montagem da URL.

**Acrescente três testes que só existem no nível da tela:**

```tsx
it('manda o formulário inteiro no POST, com o tipo escolhido', async () => {
  // C1 da review da Task 6: `criarComponente(form)` -> `criarComponente({...form, tipo: 'Bruto'})`
  // matava ZERO. O usuário escolhia "Montagem" e o sistema gravava "Bruto", em silêncio — e `tipo`
  // é justamente o campo que governa a Receita Padrão da 1C.
  const fetchMock = fetchPorRota({
    '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
  fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'CMP-1' } })
  fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
  fireEvent.change(screen.getByLabelText('Tipo'), { target: { value: 'Montagem' } })
  fireEvent.click(screen.getByText('Adicionar'))

  const post = fetchMock.mock.calls.find((c) => (c[1] as RequestInit)?.method === 'POST')!
  expect((post[1] as RequestInit).body)
    .toBe(JSON.stringify({ codigo: 'CMP-1', descricao: 'Suporte', tipo: 'Montagem' }))
})

it('distingue "nada corresponde à busca" de "catálogo vazio"', async () => {
  vi.useFakeTimers()
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
  }))

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
  await avancar(0)
  expect(screen.getByText('Nenhum componente cadastrado')).toBeTruthy()

  fireEvent.change(screen.getByLabelText('Buscar por código ou descrição'), { target: { value: 'XPTO' } })
  await avancar(300)

  expect(screen.getByText('Nenhum componente encontrado')).toBeTruthy()
  expect(screen.getByText('Nada corresponde a "XPTO".')).toBeTruthy()
})

it('esconde formulário e ação de inativar para quem não pode escrever', async () => {
  perfil = 'Operador'
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/componentes': () => respostaJson({
      itens: [{ id: 1, codigo: 'CMP-1', descricao: 'Suporte', tipo: 'Bruto', ativo: true }],
      total: 1, pagina: 1, tamanho: 20,
    }),
  }))

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

  expect(await screen.findByText('CMP-1')).toBeTruthy()
  expect(screen.queryByLabelText('Código')).toBeNull()
  expect(screen.queryByText('Inativar')).toBeNull()
})
```

- [ ] **Step 4: Rodar até ficar verde**

```bash
cd web && npm test -- ComponentesPage
```

Expected: verde. **Reporte o número final e a conta**: quantos dos 33 sobreviveram, quantos saíram (com o nome e o motivo de cada), quantos entraram.

- [ ] **Step 5: Confirmar que o erro de escrita sobrevive à recarga**

Este é o defeito mais provável desta task — o `erroDeEscrita ?? erroDeLeitura` existe para evitá-lo. Se não houver teste cobrindo, escreva:

```tsx
it('mantém a mensagem de código duplicado depois da recarga da lista', async () => {
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/componentes': () => respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: false, idExistente: 9 }, 409),
  }))
  // …preenche e submete…
  expect(await screen.findByText('Já existe um componente com este código.')).toBeTruthy()
})
```

- [ ] **Step 6: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: tudo verde. Reporte o total.

- [ ] **Step 7: Medir as mutações**

| # | Mutação em `ComponentesPage.tsx` | Mortes esperadas |
|---|---|---|
| M1 | `criarComponente(form)` → `criarComponente({ ...form, tipo: 'Bruto' })` | ≥ 1 — **é o C1 da 1B, encenado** |
| M2 | `criarComponente(form)` → trocar `codigo` por `descricao` no objeto | ≥ 1 |
| M3 | remover `setForm(FORMULARIO_VAZIO)` do ramo de sucesso | ≥ 1 |
| M4 | remover `await lista.recarregar()` do ramo de sucesso | ≥ 1 |
| M5 | trocar `erroDeEscrita ?? erroDeLeitura` por só `erroDeLeitura` | ≥ 1 |
| M6 | trocar `buscando` por `false` | ≥ 1 |
| M7 | trocar `resultado.existeInativo` por `!resultado.existeInativo` | ≥ 1 |
| M8 | trocar `!componente.ativo` por `true` em `alternarAtivo` | ≥ 1 — **é o I7 da 1B**: o botão escrito "Inativar" num item já inativo |
| M9 | trocar `usePodeEscrever('componentes')` por `('setores')` | ≥ 1 |
| M10 | remover o `<ControlesDePaginacao>` | ≥ 1 |
| M11 | remover `setEnviando(false)` do `finally` | ≥ 1 |

- [ ] **Step 8: Commit**

```bash
git add web/src/pages/ComponentesPage.tsx web/src/pages/ComponentesPage.test.tsx
git commit -m "feat(web): re-layout da ComponentesPage sobre useBuscaPaginada e primitivas"
```

**Definition of done da Task 9:** `grep -c "sequenciaRef" src/pages/ComponentesPage.tsx` = 0 (a guarda vive no hook agora); as 11 mutações medidas com ≥ 1 morte; **a lista de testes removidos, com o motivo de cada, no relatório**; suíte, build e lint verdes.

---

### Task 10: Re-layout do `PedidoDetalhePage`

**Files:**
- Modify: `web/src/pages/PedidoDetalhePage.tsx`
- Modify: `web/src/pages/PedidoDetalhePage.test.tsx`

**Interfaces:**
- Consumes: todas as primitivas, `usePodeEscrever('agrupamentos')`, `mensagemDeErro`.
- Produces: nada.

**A segunda tela que a spec §7 marca como re-layout:** detalhe do pedido, formulário, modal e lista de agrupamentos disputam a mesma coluna estreita.

**Três decisões da tela atual que NÃO se desfazem** (estão comentadas no código de hoje e cada uma custou um defeito):

1. **Não existe early return de página inteira.** `carregar()` roda após cada cadastro e exclusão; um `if (carregando) return <p>…</p>` demoliria a tela toda a cada ação — é isso que se sente como "lentidão", não a rede (o hop do proxy do Vite foi medido em ~5–15 ms). Pior: o early return ficava **antes** do bloco de erro, e a mensagem de recusa da exclusão era escrita e imediatamente escondida — o "erro que pisca" da review da Task 11.
2. **No modal, "Excluir" vem antes de "Cancelar" no DOM, e "Cancelar" tem mais peso visual.** Esta é a única exclusão física do sistema. Dois botões idênticos trocam a pausa deliberada por um sorteio.
3. **`autoFocus` no "Cancelar".** Mesma intenção, aplicada ao teclado: sem foco explícito, quem navega por teclado sem ler tabularia direto para o botão destrutivo.

**O que muda no modal:** ele ganha as primitivas (`Botao variante="perigo"` / `variante="secundario"`), o fundo e a borda dos tokens. **Não** ganha Esc, clique-fora nem focus-trap — segue fora de escopo, como já estava.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test -- PedidoDetalhePage
```

Expected: `Tests  4 passed (4)`.

- [ ] **Step 2: Reescrever a tela**

`web/src/pages/PedidoDetalhePage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import {
  obterPedido, listarAgrupamentos, criarAgrupamento, excluirAgrupamento, ehConflito,
  formatarDataHora, type PedidoDto, type AgrupamentoDto, type NovoAgrupamento,
  type ResultadoExclusao,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'

const FORMULARIO_VAZIO: NovoAgrupamento = { codigo: '', tipo: 'Kit' }

// Tipado contra a união, e não `Record<string, string>`: com o tipo frouxo, renomear ou perder uma
// chave compila, passa os testes e passa o lint — e em runtime `MOTIVO_DA_RECUSA[desfecho]` vira
// undefined e a tela fica MUDA no caso que mais acontece. O tipo forte faz o tsc cobrar o mapa.
const MOTIVO_DA_RECUSA: Record<Exclude<ResultadoExclusao, 'ok'>, string> = {
  AgrupamentoNaoVazio: 'Este agrupamento já tem estrutura e não pode mais ser excluído.',
  PedidoNaoAberto: 'O pedido não está mais aberto: não dá para excluir agrupamentos dele.',
  NaoEncontrado: 'Este agrupamento já não existe mais.',
}

export function PedidoDetalhePage() {
  const { id } = useParams<{ id: string }>()
  const pedidoId = Number(id)

  const [pedido, setPedido] = useState<PedidoDto | null>(null)
  const [agrupamentos, setAgrupamentos] = useState<AgrupamentoDto[]>([])
  const [form, setForm] = useState<NovoAgrupamento>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)
  const [pendenteExclusao, setPendenteExclusao] = useState<AgrupamentoDto | null>(null)

  const podeEscrever = usePodeEscrever('agrupamentos')

  // Recebe o id como argumento (em vez de fechar sobre `pedidoId` de fora) porque a dependência do
  // useEffect precisa aparecer usada dentro do corpo do callback, senão o exhaustive-deps acusa
  // 'carregar' como dependência faltando.
  async function carregar(id: number) {
    setCarregando(true)
    try {
      // Duas chamadas de propósito: o Pedido e o sub-recurso de Agrupamentos são rotas separadas.
      const [p, a] = await Promise.all([obterPedido(id), listarAgrupamentos(id)])
      setPedido(p)
      setAgrupamentos(a)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar o pedido.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(pedidoId) }, [pedidoId])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      const resultado = await criarAgrupamento(pedidoId, form)
      if (ehConflito(resultado)) {
        setErro('Já existe um agrupamento com este código neste pedido.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar(pedidoId)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o agrupamento.'))
    } finally {
      setEnviando(false)
    }
  }

  // `excluirAgrupamento` só lança para status fora de 204/404/409 — os dois 409 e o 404 chegam como
  // retorno normal, tratados pelo MOTIVO_DA_RECUSA.
  async function excluir(agrupamentoId: number) {
    setErro(null)
    try {
      const desfecho = await excluirAgrupamento(agrupamentoId)
      if (desfecho !== 'ok') setErro(MOTIVO_DA_RECUSA[desfecho])
      await carregar(pedidoId)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível excluir o agrupamento.'))
    }
  }

  function confirmarExclusao() {
    if (!pendenteExclusao) return
    const agrupamentoId = pendenteExclusao.id
    setPendenteExclusao(null)
    excluir(agrupamentoId)
  }

  // SEM early return de página inteira, de propósito — ele existia e causava DOIS defeitos:
  // demolia a tela a cada ação (o que se sente como lentidão) e escondia a mensagem de recusa da
  // exclusão atrás do "Carregando…". O estado de carregamento fica ESCOPADO à lista.
  return (
    <Pagina titulo={pedido ? pedido.numero : 'Pedido'}>
      {pedido && (
        <div className="flex flex-col gap-2 rounded-lg border border-borda bg-superficie p-4">
          <p className="text-lg text-tinta">{pedido.cliente}</p>
          <p className="flex flex-wrap items-center gap-2 text-sm text-tinta-fraca">
            <Pilula>{pedido.tipo}</Pilula>
            <Pilula>{pedido.status}</Pilula>
            aberto em {formatarDataHora(pedido.dataAbertura)}
          </p>
        </div>
      )}

      <h2 className="text-lg font-medium text-tinta">Agrupamentos</h2>

      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código do agrupamento">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Tipo">
              {(id) => (
                <select
                  id={id}
                  value={form.tipo}
                  onChange={(e) => setForm({ ...form, tipo: e.target.value as NovoAgrupamento['tipo'] })}
                  className={CLASSES_DE_CONTROLE}
                >
                  <option value="Kit">Kit</option>
                  <option value="Avulso">Avulso</option>
                </select>
              )}
            </Campo>
          </div>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : agrupamentos.length === 0 ? (
        <EstadoVazio
          titulo="Nenhum agrupamento neste pedido"
          descricao={podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro rotulo="Agrupamentos">
          {agrupamentos.map((a) => (
            <ItemDeCadastro
              key={a.id}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => setPendenteExclusao(a)}>Excluir</Botao>
              )}
            >
              <span className="font-mono font-semibold">{a.codigo}</span>{' '}
              <Pilula>{a.tipo}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}

      {pendenteExclusao && (
        <div className="fixed inset-0 z-10 flex items-center justify-center bg-tinta/50 p-4">
          <div
            role="dialog"
            aria-modal="true"
            className="flex w-full max-w-sm flex-col gap-4 rounded-lg bg-superficie p-5 shadow-lg"
          >
            <p className="text-tinta">
              Excluir o agrupamento <strong className="font-mono">{pendenteExclusao.codigo}</strong>?
              Esta ação não pode ser desfeita.
            </p>
            {/*
              Ordem e peso visual são deliberados, não estética. Esta é a única exclusão física do
              sistema, e até a Fase 1A os dois botões tinham a MESMA classe — um modal de confirmação
              com dois botões idênticos troca a pausa deliberada por um sorteio. "Excluir" em
              vermelho (convenção de ação destrutiva) e "Cancelar" à DIREITA, onde cai o polegar num
              tablet. NÃO trocar a ordem nem igualar os pesos.
            */}
            <div className="flex flex-wrap justify-end gap-2">
              <Botao variante="perigo" onClick={confirmarExclusao}>Excluir</Botao>
              {/*
                autoFocus: mesma intenção, aplicada ao teclado. No DOM "Excluir" vem antes de
                "Cancelar" (ordem visual decidida, não mexer) — sem foco explícito, quem navega por
                teclado sem ler tabularia direto para o botão destrutivo.
                NÃO adicionar Esc / clique-fora / focus-trap: fora de escopo, como já estava.
              */}
              <Botao variante="secundario" onClick={() => setPendenteExclusao(null)} autoFocus>
                Cancelar
              </Botao>
            </div>
          </div>
        </div>
      )}
    </Pagina>
  )
}
```

**Repare no que sumiu:** o `<Link to="/pedidos">&larr; Pedidos</Link>` do topo. O caminho de volta agora é o shell. **Se ao ver no navegador (Task 12) faltar o retorno específico "para a lista de pedidos"**, acrescente-o como `acao` da `Pagina` e diga que fez — não é regressão, é uma decisão que só o uso resolve.

- [ ] **Step 3: Ajustar a suíte**

Os 4 testes da Task 1 continuam válidos em intenção. Ajustes:

1. acrescentar o `vi.mock('../auth/AuthContext', …)` com `perfil` default `'PCP'`;
2. `renderizarDetalhe` continua igual;
3. o teste do cabeçalho: `screen.findByText('PED-001')` agora casa com o `<h1>` da `Pagina` — segue valendo;
4. acrescentar dois testes:

```tsx
it('esconde o formulário e o botão de excluir para quem não pode escrever', async () => {
  perfil = 'Operador'
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/pedidos/7': () => respostaJson(PEDIDO),
    '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
  }))

  renderizarDetalhe()

  expect(await screen.findByText('AGR-01')).toBeTruthy()
  expect(screen.queryByLabelText('Código do agrupamento')).toBeNull()
  expect(screen.queryByText('Excluir')).toBeNull()
})

it('mostra estado vazio quando o pedido não tem agrupamentos', async () => {
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/pedidos/7': () => respostaJson(PEDIDO),
    '/api/pedidos/7/agrupamentos': () => respostaJson([]),
  }))

  renderizarDetalhe()

  expect(await screen.findByText('Nenhum agrupamento neste pedido')).toBeTruthy()
})
```

**Atenção ao teste "pede confirmação antes de excluir":** com o gating, o botão do item existe só para quem escreve — mantenha `perfil = 'PCP'` no `beforeEach`. E o truque de pegar o "Excluir" **dentro** do diálogo continua necessário, porque os dois existem ao mesmo tempo.

- [ ] **Step 4: Rodar e medir**

```bash
cd web && npm test -- PedidoDetalhePage && npm test && npm run build && npm run lint
```

Expected: `Tests  6 passed (6)` no arquivo; suíte inteira verde.

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | trocar o `onClick` do "Excluir" do item por `() => excluir(a.id)` | ≥ 1 |
| M2 | trocar `variante="perigo"` do modal por `"secundario"` | **0 esperado** — nenhum teste olha o peso visual; **declare** |
| M3 | remover `autoFocus` do "Cancelar" | **0 esperado** — jsdom não prova ordem de tabulação; **declare** |
| M4 | trocar `MOTIVO_DA_RECUSA.AgrupamentoNaoVazio` pelo texto de `PedidoNaoAberto` | ≥ 1 |
| M5 | mover o `<BannerDeErro>` para depois do bloco `carregando ? …` | ≥ 1 se houver teste do "erro que pisca"; se der 0, **escreva o teste** |
| M6 | trocar `usePodeEscrever('agrupamentos')` por `('setores')` | ≥ 1 |
| M7 | remover `setEnviando(false)` do `finally` | ≥ 1 |

**M2 e M3 são fronteira declarada, não lacuna a fechar com asserção de classe.** O peso visual e a ordem de foco são exatamente o que a spec §11 manda verificar **no navegador** — Task 12.

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/PedidoDetalhePage.tsx web/src/pages/PedidoDetalhePage.test.tsx
git commit -m "feat(web): re-layout do PedidoDetalhePage com as primitivas"
```

**Definition of done da Task 10:** as 7 mutações medidas (M2 e M3 declaradas); as três decisões preservadas (sem early return; ordem e peso do modal; `autoFocus` no Cancelar) — **confirme uma a uma no relatório**; suíte, build e lint verdes.

---

### Task 11: Home — cartões de contagem com números reais

**Files:**
- Modify: `web/src/pages/HomePage.tsx`
- Modify: `web/src/pages/HomePage.test.tsx` (reescrita — a tela muda de papel)

**Interfaces:**
- Consumes: `listarComponentes`, `listarSetores`, `listarMateriais`, `listarPedidos`, `mensagemDeErro`, `Pagina`, `Botao`, `BannerDeErro`.
- Produces: nada.

**A Home perde o emprego de menu** (spec §8): a navegação virou o shell na Task 7, e a fileira de `<Link>` com borda de `HomePage.tsx:47-54` fica sem função. No lugar entram **cartões de contagem com números verdadeiros** e atalhos.

**Nada de número fake, e nada de rota crua** — a spec §8 fecha isso com dois motivos concretos: um número inventado é uma afirmação falsa numa tela que vai à banca; e o teste de fumaça **não distingue número fake de número real**, então a suíte ficaria verde provando uma mentira. Mock que precisa ser removido depois é o tipo de coisa que sobrevive até a defesa.

**De onde vêm os números, sem tocar no backend:**

| Cartão | Fonte | Custo |
|---|---|---|
| Componentes | `listarComponentes({ busca: '', incluirInativos: false, pagina: 1, tamanho: 1 })` → `.total` | 1 requisição, **nenhum item trafegado** — o `total` do `PaginaDe<T>` foi feito para isto |
| Pedidos abertos | `listarPedidos()` → `.filter(p => p.status === 'Aberto').length` | 1 requisição |
| Materiais | `listarMateriais(false)` → `.length` | 1 requisição |
| Setores | `listarSetores(false)` → `.length` | 1 requisição |

**Cartões que dependem de domínio inexistente NÃO aparecem.** "Peças em produção" depende do `EstruturaItem` (Fase 2); "reprovados do dia", do Relatório Dimensional (Fase 5). A grade nasce com o que tem número verdadeiro e **cresce por fase** — o layout fica pronto, e a Fase 6 preenche as lacunas com KPI de verdade em vez de redesenhar a Home.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test -- HomePage
```

Expected: `Tests  3 passed (3)`.

- [ ] **Step 2: Reescrever a `HomePage`**

`web/src/pages/HomePage.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, listarMateriais, listarSetores, listarPedidos,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'

interface Contagens {
  pedidosAbertos: number
  componentes: number
  materiais: number
  setores: number
}

function CartaoDeContagem({ titulo, valor, para }: { titulo: string; valor: number | null; para: string }) {
  return (
    <Link
      to={para}
      className="flex flex-col gap-1 rounded-lg border border-borda bg-superficie px-5 py-6 transition-colors hover:border-acao focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      {/* Traço em vez de zero enquanto carrega: "0 pedidos" é uma afirmação, e ela seria falsa. */}
      <span className="text-3xl font-semibold text-tinta">{valor === null ? '—' : valor}</span>
      <span className="text-sm text-tinta-fraca">{titulo}</span>
    </Link>
  )
}

export function HomePage() {
  const [contagens, setContagens] = useState<Contagens | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar() {
    setCarregando(true)
    setErro(null)
    try {
      // `tamanho: 1` no de componentes: só o `total` interessa, e assim nenhum item trafega. As
      // outras três listagens ainda não são paginadas no backend (dívida rastreada da 1B: o
      // `PaginaDto<T>` não foi migrado para Setor/Material) — quando forem, este cartão vira o
      // molde das outras.
      const [paginaDeComponentes, pedidos, materiais, setores] = await Promise.all([
        listarComponentes({ busca: '', incluirInativos: false, pagina: 1, tamanho: 1 }),
        listarPedidos(),
        listarMateriais(false),
        listarSetores(false),
      ])
      setContagens({
        pedidosAbertos: pedidos.filter((p) => p.status === 'Aberto').length,
        componentes: paginaDeComponentes.total,
        materiais: materiais.length,
        setores: setores.length,
      })
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os números do sistema.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  return (
    <Pagina titulo="Início">
      <BannerDeErro mensagem={erro} />

      {carregando && <p className="text-tinta-fraca">Carregando…</p>}

      <div className="grid gap-4 sm:grid-cols-2">
        <CartaoDeContagem titulo="pedidos abertos" valor={contagens?.pedidosAbertos ?? null} para="/pedidos" />
        <CartaoDeContagem titulo="componentes ativos" valor={contagens?.componentes ?? null} para="/componentes" />
        <CartaoDeContagem titulo="materiais ativos" valor={contagens?.materiais ?? null} para="/materiais" />
        <CartaoDeContagem titulo="setores ativos" valor={contagens?.setores ?? null} para="/setores" />
      </div>
    </Pagina>
  )
}
```

**Repare no que a Home NÃO faz mais:** ela não chama `/me` nem mostra usuário/perfil — isso mudou de casa para o shell na Task 7, e mantê-lo aqui duplicaria a informação em duas alturas da mesma tela. Ela também não tem botão "Sair" nem "Recarregar": o primeiro é do shell; o segundo era uma muleta de quando não havia estado de erro decente.

- [ ] **Step 3: Reescrever o teste da Home**

`web/src/pages/HomePage.test.tsx`, substituindo os 3 testes da Task 1:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

const PEDIDOS = [
  { id: 1, numero: 'PED-001', cliente: 'Alfa', tipo: 'Normal', status: 'Aberto', dataAbertura: '2026-08-06T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 2, numero: 'PED-002', cliente: 'Beta', tipo: 'Normal', status: 'Concluido', dataAbertura: '2026-08-05T09:00:00-03:00', criadoPorUsuarioId: 1 },
]

function apiCompleta() {
  return fetchPorRota({
    // `total: 41` com UM item: é o `total` sob o filtro que vale, não `itens.length`. Se a tela
    // ler o array, ela mostra "1 componente" num catálogo de 41 — e é justamente por isso que a
    // chamada usa `tamanho: 1`.
    '/api/componentes': () => respostaJson({ itens: [{ id: 1, codigo: 'C', descricao: 'D', tipo: 'Bruto', ativo: true }], total: 41, pagina: 1, tamanho: 1 }),
    '/api/pedidos': () => respostaJson(PEDIDOS),
    '/api/materiais': () => respostaJson([{ id: 1, codigo: 'M1', descricao: 'Aço', unidadeMedida: 'KG', ativo: true }]),
    '/api/setores': () => respostaJson([
      { id: 1, nome: 'Corte', ativo: true },
      { id: 2, nome: 'Solda', ativo: true },
    ]),
  })
}

describe('HomePage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra o total de componentes vindo do campo total, não do tamanho da página', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('41')).toBeTruthy()
  })

  it('conta só os pedidos abertos', async () => {
    // Dois pedidos, um Aberto e um Concluido: contar `.length` daria 2 e a tela mentiria.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(cartao.textContent).toContain('1')
  })

  it('mostra as contagens de materiais e setores', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    expect(screen.getByText('materiais ativos').closest('a')!.textContent).toContain('1')
    expect(screen.getByText('setores ativos').closest('a')!.textContent).toContain('2')
  })

  it('pede só um item ao contar componentes', async () => {
    // A propriedade que torna o cartão barato: `tamanho: 1`. Se alguém trocar por 20, a Home passa
    // a trafegar 20 componentes para mostrar um número.
    const fetchMock = apiCompleta()
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const url = String(fetchMock.mock.calls.find((c) => String(c[0]).startsWith('/api/componentes'))![0])
    expect(url).toContain('tamanho=1')
  })

  it('leva a cada área pelo cartão', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    expect(destinos).toEqual(expect.arrayContaining(['/pedidos', '/componentes', '/materiais', '/setores']))
  })

  it('mostra traço, e não zero, enquanto os números não chegaram', () => {
    // "0 pedidos abertos" numa fábrica que tem pedidos é uma afirmação falsa. O traço diz "ainda
    // não sei", que é a verdade naquele instante.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.getAllByText('—').length).toBe(4)
  })

  it('explica a falha quando alguma das listagens não responde', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ erro: 'x' }, 500),
      '/api/pedidos': () => respostaJson(PEDIDOS),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(
      await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.'),
    ).toBeTruthy()
  })
})
```

- [ ] **Step 4: Rodar e medir**

```bash
cd web && npm test -- HomePage && npm test && npm run build && npm run lint
```

Expected: `Tests  7 passed (7)` no arquivo; suíte inteira verde.

| # | Mutação em `HomePage.tsx` | Mortes esperadas |
|---|---|---|
| M1 | `paginaDeComponentes.total` → `paginaDeComponentes.itens.length` | ≥ 1 |
| M2 | `tamanho: 1` → `tamanho: 20` | ≥ 1 |
| M3 | `pedidos.filter(…).length` → `pedidos.length` | ≥ 1 |
| M4 | `valor === null ? '—' : valor` → `valor ?? 0` | ≥ 1 |
| M5 | trocar `'Aberto'` por `'EmProducao'` no filtro | ≥ 1 |
| M6 | trocar `materiais.length` por `setores.length` | ≥ 1 |
| M7 | remover o `catch` inteiro (deixar a promise rejeitar) | ≥ 1 |

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/HomePage.tsx web/src/pages/HomePage.test.tsx
git commit -m "feat(web): Home com cartoes de contagem reais, sem numero fake"
```

**Definition of done da Task 11:** as 7 mutações medidas com ≥ 1 morte; `grep -c "'/me'" src/pages/HomePage.tsx` = 0 (a identidade vive no shell); **nenhum número constante na tela**; suíte, build e lint verdes.

---

### Task 12: `LoginPage`, varredura de conformidade e atualização das specs

**Files:**
- Modify: `web/src/pages/LoginPage.tsx` + `LoginPage.test.tsx`
- Modify: `specs/06-roadmap-mvp.md`
- Modify: `CLAUDE.md` (uma seção nova)

**Interfaces:**
- Consumes: tudo.
- Produces: nada de código. Esta task **fecha** a fase.

**Três trabalhos distintos, e a ordem importa:** primeiro o Login (última tela sem identidade), depois a varredura que prova o critério de aceite da spec §11, e só então as specs — que descrevem o que ficou, não o que se pretendia.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: tudo verde. Anote os números — são a baseline do relatório final.

- [ ] **Step 2: Aplicar a identidade à `LoginPage`**

O layout já é adequado (cartão centralizado, `max-w-sm`) — a spec §7 diz que ela só recebe identidade.

`web/src/pages/LoginPage.tsx`:

```tsx
import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'

export function LoginPage() {
  const { estado, login } = useAuth()
  const [nomeUsuario, setNomeUsuario] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  if (estado.status === 'autenticado') return <Navigate to="/" replace />

  // Aviso discreto quando o usuário chegou aqui por sessão perdida (onSessionLost), não por acesso
  // normal. Comunica que foi rotina de autenticação, não erro dele.
  const sessaoExpirada = estado.status === 'anonimo' && estado.motivo === 'sessao-expirada'

  async function aoEnviar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      await login(nomeUsuario, senha)
    } catch {
      // Mensagem ÚNICA e genérica: honra o não-oráculo do backend, que responde o mesmo 401 para
      // usuário inexistente, conta trancada e senha errada. Variar a mensagem por caso aqui
      // desfaria no front a defesa que o backend paga BCrypt para manter.
      setErro('Usuário ou senha inválidos.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    // A única tela fora do shell, e por isso a única que ainda carrega `min-h-screen`.
    <div className="min-h-screen bg-chrome font-sans flex items-center justify-center p-4">
      <form
        onSubmit={aoEnviar}
        className="flex w-full max-w-sm flex-col gap-5 rounded-xl bg-superficie p-6 shadow-lg"
      >
        {/* A marca aparece sobre o chrome escuro do fundo, não dentro do cartão claro: o
            verde-água só tem contraste AA sobre o petróleo. */}
        <h1 className="text-center text-2xl font-semibold tracking-tight text-chrome">Rastru</h1>

        {sessaoExpirada && (
          // Âmbar seria um quinto matiz para manter coerente, e os tons claros dele reprovam AA
          // como texto (spec §3). Aviso de rotina fica no cinza-esverdeado dos neutros.
          <p className="rounded-lg border border-borda bg-fundo px-3 py-2 text-center text-sm text-tinta-fraca">
            Sessão expirada. Entre novamente.
          </p>
        )}

        <Campo rotulo="Usuário">
          {(id) => (
            <input
              id={id}
              value={nomeUsuario}
              onChange={(e) => setNomeUsuario(e.target.value)}
              required
              autoComplete="username"
              className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>

        <Campo rotulo="Senha">
          {(id) => (
            <input
              id={id}
              type="password"
              value={senha}
              onChange={(e) => setSenha(e.target.value)}
              required
              autoComplete="current-password"
              className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>

        <BannerDeErro mensagem={erro} />

        <Botao type="submit" carregando={enviando} rotuloCarregando="Entrando…">Entrar</Botao>
      </form>
    </div>
  )
}
```

**Os 4 testes da Task 1 continuam válidos sem mudança** — eles já usam `getByLabelText('Usuário')`, que passa de label envolvente para `htmlFor` sem quebrar. **Confirme rodando; se algum quebrar, não relaxe a asserção.**

Acrescente um:

```tsx
it('desabilita o botão enquanto o login está em voo', async () => {
  let liberar: (r: Response) => void = () => {}
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/auth/refresh': () => respostaJson({ erro: 'sem sessão' }, 401),
    '/api/auth/login': () => new Promise<Response>((r) => { liberar = r }),
  }))

  renderizarLogin()
  fireEvent.change(await screen.findByLabelText('Usuário'), { target: { value: 'admin' } })
  fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'Admin@123' } })
  fireEvent.click(screen.getByText('Entrar'))

  expect((await screen.findByText('Entrando…') as HTMLButtonElement).disabled).toBe(true)
  liberar(respostaJson({ erro: 'x' }, 401))
})
```

- [ ] **Step 3: Rodar e commitar o Login**

```bash
cd web && npm test -- LoginPage
```

Expected: `Tests  5 passed (5)`.

```bash
git add web/src/pages/LoginPage.tsx web/src/pages/LoginPage.test.tsx
git commit -m "feat(web): identidade visual na LoginPage"
```

- [ ] **Step 4: Varredura de conformidade — o critério de aceite da spec §11**

*"Nenhuma cópia da forma antiga sobrou no repositório"*, verificável por busca. Rode **cada uma** e reporte o número:

```bash
cd web
grep -rn "max-w-md mx-auto" src/          # esperado: 0 — o container antigo
grep -rn "min-h-screen" src/              # esperado: 2 — só AppShell.tsx e LoginPage.tsx
grep -rn "border rounded px-3 py-2" src/  # esperado: 0 — o input/botão copiado
grep -rn "text-gray-" src/                # esperado: 0 — a paleta antiga
grep -rn "bg-gray-\|border-gray-" src/    # esperado: 0
grep -rn "text-red-600" src/              # esperado: 0 — virou text-negativo
grep -rn "bg-amber-\|text-amber-" src/    # esperado: 0
grep -rn "placeholder=" src/pages/        # esperado: 0 — placeholder deixou de fazer papel de rótulo
```

**Qualquer resultado diferente do esperado é trabalho desta task, não observação.** Se sobrou classe antiga, migre; se sobrou `min-h-screen` numa terceira tela, tire.

E a conferência de que as primitivas têm consumidor real (critério da spec §5 — *"primitiva com um consumidor só é abstração prematura"*):

```bash
cd web
for p in Pagina Botao Campo BannerDeErro ListaDeCadastro Pilula EstadoVazio ControlesDePaginacao; do
  echo -n "$p: "; grep -rl "from '../components/$p'" src/pages/ | wc -l
done
```

Expected: `Pagina` 6, `Botao` ≥ 6, `Campo` ≥ 6, `BannerDeErro` ≥ 6, `ListaDeCadastro` 4, `Pilula` ≥ 3, `EstadoVazio` ≥ 5, `ControlesDePaginacao` 1.

**`ControlesDePaginacao` com um consumidor só é a exceção conhecida, e ela é legítima:** só a `ComponentesPage` tem paginação hoje porque só ela é paginada no backend. Está declarada aqui para não virar achado de review. As demais precisam de ≥ 2; **se alguma tiver menos, reporte** — ou faltou um consumidor, ou a primitiva não devia existir.

- [ ] **Step 5: Verificação manual no navegador — o que jsdom não alcança**

Suba os dois lados:

```bash
dotnet run --project src/Rastreamento.Api
cd web && npm run dev
```

Com `admin` / `Admin@123` (Administrador) e depois `pcp` / a senha do seed (PCP), confirme **item por item** e **relate cada um**:

| # | O que verificar | Por que nenhum teste pega |
|---|---|---|
| V1 | Em **360px** de largura, **nenhuma** das 7 telas rola na horizontal | jsdom não calcula layout |
| V2 | A barra vira gaveta abaixo de 768px, e a gaveta fecha ao navegar | idem |
| V3 | `Tab` percorre toda tela com **anel de foco visível**, inclusive sobre o chrome escuro | jsdom não renderiza foco |
| V4 | No modal de exclusão, o foco começa em **"Cancelar"** e "Excluir" está visivelmente mais pesado | M2/M3 da Task 10, declaradas |
| V5 | Nenhum par texto/fundo parece apagado — cruze com os números medidos na Task 4 | o teste mede tokens, não composições |
| V6 | Clicar em qualquer ponto de um cartão de pedido navega para **aquele** pedido | o `after:inset-0` sem `relative` cobriria a tela inteira |
| V7 | Com o perfil **PCP**: Setores e Materiais aparecem no menu e listam, **sem** formulário nem botão de inativar | o gating está testado, a composição real não |
| V8 | Desligue a API e recarregue: a mensagem é "Sem conexão com o servidor…", não o texto genérico | o `TypeError` real do `fetch` não é o que o mock produz |

**V8 é a única prova de ponta a ponta de que `mensagemDeErro` acerta o caso de rede** — o mock dos testes rejeita com `Error`, não com o `TypeError` que o navegador lança. Faça e reporte.

- [ ] **Step 6: Registrar a fase em `specs/06-roadmap-mvp.md`**

Acrescente, **depois** do bloco de citação da Fase 1 (que hoje termina em "Dívidas rastreadas de 1A: camada global de erro de API no front e gating de navegação por perfil."), e **antes** de `## Fase 2`:

```markdown
## Fase 1D — Identidade visual e UX

- Tokens de tema, primitivas de interface à mão sobre Tailwind e shell de navegação.
- Retrofit das 7 telas existentes para o padrão novo.
- Critério de pronto: mesma primitiva nas 7 telas, estados carregando/vazio/erro em toda tela que
  busca dados, navegação por teclado com foco visível, contraste AA medido por teste, e nenhuma
  tela rolando na horizontal em viewport de celular.

> **Esta fase NÃO tem aresta de dependência.** Ela não bloqueia nem é bloqueada pela 1C, e pode
> rodar antes ou depois dela. A letra é rótulo cronológico, não ordem obrigatória — sem esta frase,
> a sequência 1B → 1C → 1D se lê como dependência, e ela não é.
>
> **1D concluída em 2026-08-XX.** Fecha três dívidas de UX que vinham da 1A e da 1B: camada global
> de erro de API (`ErroDeApi` + `mensagemDeErro`), gating de perfil, e botão desabilitado durante
> mutação. Fecha também o `useBuscaPaginada` (debounce, cancelamento, clamp, reset) e o W3
> (`setCarregando` sem prova).
>
> **O gating de perfil ficou na AÇÃO, não no link** — o link continua visível para todos porque a
> leitura de todos estes recursos é liberada a qualquer usuário autenticado no backend; o que some
> para quem não pode escrever é o formulário e os botões de (in)ativar. Esconder o link de Materiais
> do Almoxarifado tiraria dele uma leitura de que a **Fase 4** depende.
>
> **Corolário registrado:** se daqui a três fases o sistema precisar de outra passada de UI, isso não
> é uma fase planejada que faltou — é sinal de que o padrão não pegou. Não existe "Fase 1D parte 2".
```

**Substitua `2026-08-XX` pela data real da conclusão.**

- [ ] **Step 7: Registrar as convenções novas em `CLAUDE.md`**

Acrescente uma seção **depois** de "Convenções de nomenclatura":

```markdown
## Interface (a partir da Fase 1D)

O padrão visual e de interação nasceu na Fase 1D e vale para **toda tela nova**. A spec de origem é
`docs/superpowers/specs/2026-08-06-fase-1d-ui-e-ux-design.md`.

- **Tela nova começa por `<Pagina titulo="…">`**, dentro da rota de layout do `AppShell` em
  `web/src/App.tsx`. Não escreva container próprio, e nunca `min-h-screen` numa tela — quem faz isso
  é o shell (a `LoginPage` é a única exceção, por ficar fora dele).
- **Não escreva campo, botão, banner de erro, item de lista, pílula, paginação ou estado vazio à
  mão.** As primitivas estão em `web/src/components/`. Se faltar uma, crie-a lá com teste próprio —
  não a embuta na tela.
- **Cores só pelos tokens** de `web/src/index.css` (`text-tinta`, `bg-acao`, `border-borda`…).
  `text-gray-*`, `text-red-600` e afins não existem mais no repositório, e a varredura da Fase 1D
  confirmou zero ocorrências.
- **Tom novo tem de ser medido antes de entrar.** `web/src/tema/contraste.test.ts` lê o `@theme` do
  `index.css` e reprova token sem par de contraste declarado. Os tons claros de verde-água e âmbar
  reprovam AA como texto sobre branco apesar de funcionarem como fundo de botão — a regra existe
  por causa disso.
- **Cor de identidade nunca significa estado; cor de estado nunca decora.** Verde (`positivo`) e
  vermelho (`negativo`) são reservados a aprovado/ativo e reprovado/perda/erro. É o que faz a tela
  de Qualidade da Fase 5 funcionar, quando "Aprovado" e "Abrir retrabalho" dividem a mesma linha.
- **Tela que busca dados tem os três estados**: carregando, vazio (com texto que distingue "não
  achei" de "não há nada") e erro (via `mensagemDeErro`), **cada um com teste que morre se o estado
  sumir**.
- **Busca paginada usa `useBuscaPaginada`** (`web/src/hooks/`), nunca `useState` + `useEffect` à
  mão: ele já resolve debounce, cancelamento por sequência, clamp de página e reset de filtro.
- **Gating de perfil vai na AÇÃO, não no link.** `usePodeEscrever(recurso)` esconde formulário e
  botões de escrita; o link continua visível porque leitura é de todos. **A tabela
  `web/src/auth/permissoes.ts` espelha os `[Authorize(Roles)]` do backend — mudou lá, muda aqui.**
  E o `try/catch` do F2 continua obrigatório: o 403 é a fronteira real, esconder botão não é
  segurança.
- **Teste de tela usa `web/src/testes/api.ts`** (`respostaJson`, `fetchPorRota`) e declara
  `// @vitest-environment jsdom` no topo, com `afterEach(cleanup)` explícito — este projeto não usa
  `globals: true`.
- **`npm run build` faz parte do ciclo**, não só `npm test`: erro de tipo em `.test.tsx` quebra o
  build sem quebrar a suíte (o Vitest não faz typecheck).
```

- [ ] **Step 8: Varredura de segredo e commit final**

O repositório é **público**. Antes de qualquer push:

```bash
git diff main --stat
git diff main | grep -inE "password|senha|secret|token|api[_-]?key|connectionstring" | grep -v "CancellationToken\|accessToken\|refreshToken\|tokenRef\|getToken\|setToken"
```

Expected: nada além de identificadores de código. **Se aparecer valor literal, pare e reporte.**

```bash
git add specs/06-roadmap-mvp.md CLAUDE.md
git commit -m "docs: registra a Fase 1D no roadmap e as convencoes de interface"
```

- [ ] **Step 9: Relatório de fechamento da fase**

Reporte, em números medidos e não estimados:

- suíte final (`npm test`), build e lint, com a composição por arquivo;
- **crescimento da suíte**: 96 no início → N no fim, e quantos testes vieram de cada task;
- o resultado de **cada** comando de varredura do Step 4;
- o resultado de **cada** verificação manual V1–V8 do Step 5;
- a lista consolidada dos **sobreviventes declarados** da fase: `flex-wrap` (Task 6, M11),
  `ProtectedRoute` na rota de layout (Task 7, M11), peso do botão de perigo e `autoFocus`
  (Task 10, M2/M3) — e qualquer outro que tenha aparecido;
- os **valores de contraste medidos** de cada par (Task 4).

**Definition of done da Task 12:** as 8 varreduras do Step 4 com o resultado esperado; as 8 verificações manuais feitas e relatadas; `specs/06-roadmap-mvp.md` e `CLAUDE.md` atualizados; varredura de segredo limpa; suíte, build e lint verdes.

---

## Depois da fase

1. **Review de branch inteira** (Opus) — o escopo é a **integração entre tasks**, não repetição das
   reviews individuais: as costuras entre primitivas e telas, o shell atravessando as 7, e a
   coerência entre o que o teste de contraste mede e o que a tela compõe.
2. `superpowers:finishing-a-development-branch` → **PR**. O projeto retomou o rastro por PR nas
   #1/#2/#3/#4/#5; a 1A entrou por push direto e isso foi registrado como perda.
3. **Fica de fora e tem dono próprio:** as dívidas **I2 e I3** da review de branch da 1B (o `Trim`
   de `LocalizarDuplicado` sem prova; o teste negativo de autorização que fixa um perfil por
   controller). São **backend**, e o prazo delas é **fix pass antes da 1C**.

## O que esta fase deliberadamente NÃO faz

Nomeado, não esquecido (spec §12):

- **Tema escuro.** Não é uma quinta paleta — dobra os estados a provar em cada primitiva.
- **Qualquer mudança em `src/`.** Front puro. Se você mexeu no backend, saiu do escopo.
- **KPIs de produção.** Fase 6, e dependem de domínio das Fases 2 e 5.
- **Biblioteca de componentes de terceiros.**
- **Esc / clique-fora / focus-trap no modal** do `PedidoDetalhePage` — já estava fora na 1A e continua.
- **Migrar `Setor`/`Material` para `PaginaDto<T>`** — dívida de backend rastreada, com motivo escrito.
