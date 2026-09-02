// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AgrupamentoDetalhePage } from './AgrupamentoDetalhePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import type { NoDaEstrutura } from '../api/estrutura'

afterEach(cleanup)

// Molde de PedidoDetalhePage.test.tsx: o perfil da sessão governa o gating de escrita
// (`usePodeEscrever('estrutura')` libera PCP e Administrador — `web/src/auth/permissoes.ts`).
let perfil = 'PCP'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const PECA: NoDaEstrutura = {
  id: 100,
  componenteId: 10,
  codigoDoComponente: 'CH-100',
  descricao: 'Chassi',
  quantidade: 1,
  nivelHierarquico: 'Peca',
  requerRelatorioDimensional: false,
  materiais: [],
  roteiro: [],
  filhos: [],
}

const COMPONENTE_BUSCA = { id: 10, codigo: 'CH-100', descricao: 'Chassi', tipo: 'Fabricado', ativo: true }

/** `PaginaDe<ComponenteDto>` que o `SeletorComBusca` busca ao montar (carga inicial, sem debounce:
    `busca === ''` no mount) — todo teste com `podeEscrever` monta o formulário, e o formulário
    monta o seletor, então esta rota precisa estar declarada mesmo quando o teste não interage com
    o combobox (senão `fetchPorRotaComEstrutura` rejeita com "fetch não esperado"). */
const COMPONENTES_BUSCA = { itens: [COMPONENTE_BUSCA], total: 1, pagina: 1, tamanho: 20 }

/**
 * Mock de `fetch` desta tela: distingue GET de POST na MESMA rota
 * (`/api/agrupamentos/21/estrutura`, tanto `obterEstrutura` quanto `criarPeca`) pelo `method` do
 * `init` — o que `fetchPorRota` (testes/api.ts) não faz, porque ela roteia só por caminho. Molde de
 * `fetchPorRotaGravando`/`fetchComPostQueFalha` de `ComponenteDetalhePage.test.tsx`.
 *
 * `estruturaAposCriar`: o que a SEGUNDA chamada de GET (o `carregar()` que roda depois do POST com
 * sucesso) devolve — `null` significa "mesma resposta da primeira", usado pelos testes que não
 * provam recarregamento.
 */
function montarFetch({
  estruturaInicial,
  estruturaAposCriar = null,
  respostaCriar = null,
}: {
  estruturaInicial: NoDaEstrutura[]
  estruturaAposCriar?: NoDaEstrutura[] | null
  respostaCriar?: { status: number; corpo: unknown } | null
}) {
  let getsDeEstrutura = 0
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    const metodo = init?.method ?? 'GET'
    if (caminho === '/api/componentes') return Promise.resolve(respostaJson(COMPONENTES_BUSCA))
    if (caminho === '/api/agrupamentos/21/estrutura') {
      if (metodo === 'POST') {
        if (respostaCriar) return Promise.resolve(respostaJson(respostaCriar.corpo, respostaCriar.status))
        return Promise.resolve(respostaJson({ ...PECA, id: 101 }, 201))
      }
      getsDeEstrutura += 1
      const dados = getsDeEstrutura === 1 || !estruturaAposCriar ? estruturaInicial : estruturaAposCriar
      return Promise.resolve(respostaJson(dados))
    }
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
}

function renderizarDetalhe() {
  return render(
    <MemoryRouter initialEntries={['/agrupamentos/21']}>
      <Routes>
        <Route path="/agrupamentos/:id" element={<AgrupamentoDetalhePage />} />
      </Routes>
    </MemoryRouter>,
  )
}

/**
 * Escolhe `CH-100` no `SeletorComBusca`, preenche a quantidade e marca (ou não) o checkbox.
 *
 * O clique na opção é escopado ao `listbox` (não `screen.findByText` global): os testes de I1/I2
 * do fix pass da Task 8 carregam a árvore com a MESMA Peça (`CH-100`) já visível na tela, então um
 * `findByText` sem escopo acha dois `CH-100` — o nó da árvore e a opção do combobox — e lança
 * "Found multiple elements" em vez de selecionar.
 */
async function preencherFormulario(quantidade: number, marcarRequerRelatorio = false) {
  fireEvent.click(screen.getByRole('combobox'))
  const listbox = await screen.findByRole('listbox')
  fireEvent.click(await within(listbox).findByText('CH-100'))
  fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: String(quantidade) } })
  if (marcarRequerRelatorio) fireEvent.click(screen.getByLabelText(/requer relatório dimensional/i))
}

describe('AgrupamentoDetalhePage', () => {
  beforeEach(() => {
    perfil = 'PCP'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // Teste 1. Molde de PedidoDetalhePage.test.tsx (I1): fetch que nunca resolve, asserção SÍNCRONA
  // (sem await/findBy*) de que o indicador está na tela antes de qualquer resposta chegar.
  it('mostra o estado de carregando enquanto busca', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizarDetalhe()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  // Teste 2. Também prova o Minor 4 herdado da re-review da Task 7 (`ArvoreDeEstrutura.tsx:33`):
  // o rótulo da lista raiz passou de "Estrutura da peça" (singular) para "Estrutura do
  // agrupamento" — não é delta de teste novo, cabe nesta mesma asserção de acessibilidade.
  it('mostra a árvore quando há estrutura', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()

    expect(await screen.findByText('Chassi')).toBeTruthy()
    expect(screen.getByRole('list', { name: 'Estrutura do agrupamento' })).toBeTruthy()
  })

  // Teste 3. Diferente de `SeletorComBusca` (que distingue "não achei" de "catálogo vazio" porque
  // tem busca), esta tela não tem busca — só existe UM caminho para a lista vazia: a Peça ainda
  // não foi criada. O texto nomeia esse caminho, em vez de um "Nenhum resultado" genérico que
  // pareceria busca sem resultado.
  it('estado vazio distingue "ainda não há estrutura" de "não achei"', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [] }))

    renderizarDetalhe()

    // m2 do fix pass da Task 8: a segunda asserção original (`queryByText(/não achei/i)`) não podia
    // falhar — nenhum caminho do código renderiza essa literal, e a propriedade que o nome do teste
    // promete já é provada pela asserção acima (o texto que NOMEIA o único caminho da lista vazia
    // desta tela, que não tem busca). Removida em vez de mantida como cobertura encenada.
    expect(await screen.findByText('Este agrupamento ainda não tem estrutura')).toBeTruthy()
  })

  // Teste 4. `fetch` real rejeita com `TypeError` quando a requisição nem sai (DNS, rede, CORS) —
  // é o caso que `mensagemDeErro` traduz para a frase específica de rede, e não a genérica de
  // fallback da tela. Prova também que o estado vazio e a árvore ficam de fora (o mesmo Critical
  // C1 que `PedidoDetalhePage`/`ComponenteDetalhePage` já pagaram).
  it('erro de rede cai em BannerDeErro com mensagemDeErro', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('failed to fetch'))))

    renderizarDetalhe()

    expect(
      await screen.findByText('Sem conexão com o servidor. Verifique a rede e tente de novo.'),
    ).toBeTruthy()
    expect(screen.queryByText('Este agrupamento ainda não tem estrutura')).toBeNull()
  })

  // Teste 5. O gatilho é catálogo paginado (o mesmo de `ComponenteDetalhePage`): um `<select>` com
  // a lista inteira de Componentes não escala. Prova o `role="combobox"` de `SeletorComBusca` E o
  // corpo do POST — sem a segunda parte, trocar `componente.id` por outra coisa no corpo
  // sobreviveria ao teste (a árvore recarregada é igual nos dois casos).
  it('criar Peça usa SeletorComBusca para escolher o Componente', async () => {
    const fetchMock = montarFetch({ estruturaInicial: [], estruturaAposCriar: [PECA] })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Este agrupamento ainda não tem estrutura')
    expect(screen.getByRole('combobox')).toBeTruthy()

    await preencherFormulario(5, true)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    // Prova o recarregamento: 'Chassi' só aparece depois do POST, na segunda chamada de GET.
    expect(await screen.findByText('Chassi')).toBeTruthy()

    const chamadaPost = fetchMock.mock.calls.find((c) => (c[1] as RequestInit | undefined)?.method === 'POST')
    expect(chamadaPost).toBeTruthy()
    expect(JSON.parse((chamadaPost![1] as RequestInit).body as string)).toEqual({
      componenteId: 10,
      quantidade: 5,
      requerRelatorioDimensional: true,
    })
  })

  // m3 do fix pass da Task 8 (o item mais visível, os demais ficam para a review de branch):
  // depois de criar a Peça com sucesso, o formulário volta ao estado inicial — sem isto, um
  // formulário preenchido continuaria preenchido e ninguém saberia que a criação anterior já foi
  // aplicada, convidando a clicar "Criar Peça" de novo com os mesmos dados.
  it('formulário volta ao estado inicial depois de criar a Peça com sucesso', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [], estruturaAposCriar: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Este agrupamento ainda não tem estrutura')
    await preencherFormulario(5, true)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    expect(await screen.findByText('Chassi')).toBeTruthy()

    expect(screen.getByRole('combobox')).toHaveProperty('value', '')
    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '')
    expect(screen.getByLabelText(/requer relatório dimensional/i)).toHaveProperty('checked', false)
  })

  // Teste 6. `mensagem` nomeia o CAMINHO do ciclo (a única informação que permite ao operador
  // consertar a receita — comentário de `ConflitoDeEstrutura` em `api/estrutura.ts`). Sem este
  // teste, `resultado.mensagem ?? '...'` poderia virar sempre o fallback genérico sem quebrar nada.
  it('409 de ciclo mostra a mensagem que nomeia o caminho, não um erro genérico', async () => {
    const mensagemDoServidor = 'A receita tem um ciclo: 2 -> 3 -> 2. Não é possível montar essa Peça.'
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [],
      respostaCriar: { status: 409, corpo: { erro: 'CicloNaReceita', mensagem: mensagemDoServidor } },
    }))

    renderizarDetalhe()
    await screen.findByText('Este agrupamento ainda não tem estrutura')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    expect(await screen.findByText(mensagemDoServidor)).toBeTruthy()
  })

  // I1 do fix pass da Task 8: `erro` era UM estado só, compartilhado entre carga e escrita — um
  // erro de escrita (o mesmo 409 do teste 6, agora com a árvore JÁ carregada) apagava a árvore
  // inteira, sobrando só o banner. É exatamente o cenário que a mensagem do ciclo existe para
  // socorrer: o usuário precisa ver a árvore para consertar a receita, não perder a visão dela.
  it('409 de ciclo na escrita não apaga a árvore já carregada', async () => {
    const mensagemDoServidor = 'A receita tem um ciclo: 2 -> 3 -> 2. Não é possível montar essa Peça.'
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      respostaCriar: { status: 409, corpo: { erro: 'CicloNaReceita', mensagem: mensagemDoServidor } },
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    expect(await screen.findByText(mensagemDoServidor)).toBeTruthy()
    expect(screen.getByText('Chassi')).toBeTruthy()
    expect(screen.getByRole('list', { name: 'Estrutura do agrupamento' })).toBeTruthy()
  })

  // I2 do fix pass da Task 8: o `catch` de `salvar` (m6 do segundo fix pass: sem número de linha
  // de propósito — o arquivo já moveu duas vezes desde a review que citou uma) não tinha nenhum
  // teste que o protegesse — em 409 `criarPeca` RESOLVE (`lerNoOuConflito` devolve o
  // objeto de conflito), então o teste 6/I1 nunca entra no `catch`. Todo status não-409 lança, e um
  // 403 é o caso que o brief da Fase 1D nomeia como a fronteira REAL ("esconder botão não é
  // segurança"): mesmo com o formulário visível (perfil desatualizado no front), o backend pode
  // recusar, e a tela precisa mostrar a recusa em vez de quebrar — e sem apagar a árvore.
  it('403 na escrita vira mensagem, não exceção, e a árvore continua visível', async () => {
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      respostaCriar: { status: 403, corpo: {} },
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
    expect(screen.getByText('Chassi')).toBeTruthy()
    expect(screen.getByRole('list', { name: 'Estrutura do agrupamento' })).toBeTruthy()
  })

  // Teste 7. Gating na AÇÃO, não no link: quem não escreve não vê o formulário, mas continua
  // vendo a árvore — leitura é de todo perfil (molde do comentário de `permissoes.ts` sobre
  // `estrutura`, que libera só PCP/Administrador para escrita).
  it('perfil sem escrita não vê o formulário, mas vê a árvore', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()

    expect(await screen.findByText('Chassi')).toBeTruthy()
    expect(screen.queryByRole('combobox')).toBeNull()
    expect(screen.queryByLabelText('Quantidade')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Criar Peça' })).toBeNull()
  })

  // Teste 9 (decisão do usuário na review da Task 7): a marca é rótulo/pílula NEUTRA, nunca cor de
  // estado — os dois nós da fixture (uma true, uma false) provam a condição nos dois sentidos: a
  // marca aparece só na Peça marcada, não na outra.
  it('Peça marcada como exigindo relatório dimensional mostra isso na linha', async () => {
    const outraPeca: NoDaEstrutura = { ...PECA, id: 200, codigoDoComponente: 'CH-200', descricao: 'Base' }
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [{ ...PECA, requerRelatorioDimensional: true }, outraPeca],
    }))

    renderizarDetalhe()

    expect(await screen.findByText('Chassi')).toBeTruthy()
    expect(screen.getByText('Base')).toBeTruthy()

    // Escopado na árvore (`role="list"`), e não em `screen` global: o CHECKBOX do formulário de
    // criação também se chama "Requer relatório dimensional" (é o rótulo do campo), e sem o
    // escopo `getAllByText` global contaria os dois — o do formulário e o da linha da árvore —
    // achando 2 em vez de 1 mesmo com a marca aparecendo só na Peça certa.
    const arvore = screen.getByRole('list', { name: 'Estrutura do agrupamento' })
    expect(within(arvore).getAllByText('Requer relatório dimensional')).toHaveLength(1)

    // A cor não é verde/vermelho (estado): mesmo critério de teste que
    // `ArvoreDeEstrutura.test.tsx` já usa para o "Ad-hoc".
    const linhaChassi = screen.getByTestId('linha-no-100')
    expect(linhaChassi.innerHTML).not.toContain('text-positivo')
    expect(linhaChassi.innerHTML).not.toContain('text-negativo')
  })

  // I3 do segundo fix pass da Task 8: o primeiro fix trocou o `setErro(null)` do início de `salvar`
  // por `setErroEscrita(null)` — e `carregar` nunca zerava `erro`, só o escrevia no `catch`.
  // Resultado medido pela re-review: depois de uma carga que falha, uma escrita bem-sucedida
  // recarrega os dados (prova: 'Chassi' aparece), mas o banner da falha ANTIGA continuava na tela
  // e a guarda `erro === null &&` do ramo da árvore (agora reparada) ficava presa para sempre com
  // ele. Molde de `ComponenteDetalhePage`: `setErroComponente(null)` no INÍCIO de cada carga
  // (`:168`), não só no `catch`.
  it('recarga bem-sucedida depois de uma carga falha limpa o banner de carga antigo', async () => {
    let getsDeEstrutura = 0
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
      const caminho = String(url).split('?')[0]
      const metodo = init?.method ?? 'GET'
      if (caminho === '/api/componentes') return Promise.resolve(respostaJson(COMPONENTES_BUSCA))
      if (caminho === '/api/agrupamentos/21/estrutura') {
        if (metodo === 'POST') return Promise.resolve(respostaJson({ ...PECA, id: 101 }, 201))
        getsDeEstrutura += 1
        if (getsDeEstrutura === 1) return Promise.reject(new TypeError('failed to fetch'))
        return Promise.resolve(respostaJson([PECA]))
      }
      return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Sem conexão com o servidor. Verifique a rede e tente de novo.')

    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    // A árvore volta (prova que o caminho de sucesso — POST 201, recarga OK, `setNos` — rodou
    // inteiro) E o banner da carga que falhou antes some. Antes do fix, o formulário era resetado
    // (prova de sucesso) mas a tela ficava com o banner de rede e SEM a árvore para sempre.
    expect(await screen.findByText('Chassi')).toBeTruthy()
    expect(screen.queryByText('Sem conexão com o servidor. Verifique a rede e tente de novo.')).toBeNull()
  })

  // m5 do segundo fix pass da Task 8: o comentário que licenciava `erro ?? erroEscrita` afirmava
  // que os dois nunca coexistem — falso, medido pela re-review: o `<form>` só depende de
  // `podeEscrever`, não de `erro`, e o `SeletorComBusca` busca em `/componentes`, rota diferente da
  // que falhou. Com um `??` só, o erro de CARGA engolia o de ESCRITA. Molde de
  // `ComponenteDetalhePage`: um `<BannerDeErro>` por estado, nunca dois disputando um slot.
  it('erro de carga e erro de escrita aparecem os dois, em banners separados', async () => {
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
      const caminho = String(url).split('?')[0]
      const metodo = init?.method ?? 'GET'
      if (caminho === '/api/componentes') return Promise.resolve(respostaJson(COMPONENTES_BUSCA))
      if (caminho === '/api/agrupamentos/21/estrutura') {
        if (metodo === 'POST') return Promise.resolve(respostaJson({}, 403))
        return Promise.reject(new TypeError('failed to fetch'))
      }
      return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Sem conexão com o servidor. Verifique a rede e tente de novo.')

    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    // O 403 da escrita não é engolido pelo erro de carga que já estava na tela.
    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
    // ...e o banner de carga continua visível também: os dois coexistem, cada um no seu slot.
    expect(screen.getByText('Sem conexão com o servidor. Verifique a rede e tente de novo.')).toBeTruthy()
  })

  // m7 do segundo fix pass da Task 8: a guarda `erro === null &&` do ramo da árvore não tinha
  // matador — o teste do I3 acima não cobre, porque nele `nos` só fica populado QUANDO `erro` já
  // voltou a `null` (carga falha com `nos` vazio, depois recarga com sucesso). Este teste cobre o
  // caso que a guarda existe para tratar: `nos` JÁ populado de uma carga anterior, e uma RECARGA
  // que falha — a árvore precisa sumir (ela ficaria obsoleta), não continuar mostrando dados de
  // antes da falha ao lado do banner.
  it('recarga que falha depois de nós já carregados esconde a árvore obsoleta', async () => {
    let getsDeEstrutura = 0
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
      const caminho = String(url).split('?')[0]
      const metodo = init?.method ?? 'GET'
      if (caminho === '/api/componentes') return Promise.resolve(respostaJson(COMPONENTES_BUSCA))
      if (caminho === '/api/agrupamentos/21/estrutura') {
        if (metodo === 'POST') return Promise.resolve(respostaJson({ ...PECA, id: 101 }, 201))
        getsDeEstrutura += 1
        if (getsDeEstrutura === 1) return Promise.resolve(respostaJson([PECA]))
        return Promise.reject(new TypeError('failed to fetch'))
      }
      return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Chassi')

    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    expect(await screen.findByText('Sem conexão com o servidor. Verifique a rede e tente de novo.')).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Estrutura do agrupamento' })).toBeNull()
  })

  // m4 do fix pass da Task 8, molde de `ComponenteDetalhePage.test.tsx` ("trata id inválido sem
  // tentar buscar nada"): com `:id` não numérico o ramo `Number.isNaN` do título era morto (o
  // título virava "Agrupamento " com espaço final) e a tela ainda disparava
  // `GET /agrupamentos/NaN/estrutura`. Agora ela nem tenta.
  it('id não numérico mostra mensagem, sem disparar busca', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    render(
      <MemoryRouter initialEntries={['/agrupamentos/abc']}>
        <Routes>
          <Route path="/agrupamentos/:id" element={<AgrupamentoDetalhePage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Este agrupamento não existe.')).toBeTruthy()
    expect(fetchMock).not.toHaveBeenCalled()
  })
})
