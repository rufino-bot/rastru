// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AgrupamentoDetalhePage } from './AgrupamentoDetalhePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import type { NoDaEstrutura } from '../api/estrutura'
import type { AgrupamentoDto, ComponenteDto } from '../api/cadastros'
import type { LivroDoNoDto, PosicoesDoNoDto } from '../api/execucao'
import { movimentacao } from '../testes/execucao'
import type { FilhoPadraoDto } from '../api/receitaPadrao'

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
  quantidadePorPai: null,
  semRoteiro: true,
}

// `temSolido: true` preserva a intenção original da fixture: com `exigirSolido` ligado no
// formulário de criar Peça, um `temSolido` indefinido contaria como "sem sólido" e bloquearia
// `CH-100` em todo teste que cria Peça por este seletor.
const COMPONENTE_BUSCA: ComponenteDto = { id: 10, codigo: 'CH-100', descricao: 'Chassi', tipo: 'Fabricado', ativo: true, temSolido: true }

/** Só para os dois testes de `exigirSolido` — os demais continuam usando `COMPONENTE_BUSCA`. */
const COMPONENTE_SEM_SOLIDO_BUSCA: ComponenteDto = { id: 20, codigo: 'SF-050', descricao: 'Suporte sem sólido', tipo: 'Fabricado', ativo: true, temSolido: false }

/** `AgrupamentoDto` que `GET /agrupamentos/21` devolve — Task 8b, o cabeçalho da tela. */
const AGRUPAMENTO: AgrupamentoDto = {
  id: 21, pedidoId: 4, codigo: 'AGR-01', tipo: 'Kit',
  criadoEm: '2026-07-28T09:30:00-03:00', criadoPorUsuarioId: 1,
}

/** O que a árvore mostra depois de um `acrescentarFilho` bem-sucedido em `PECA` — só usado pelo
    teste que prova o recarregamento (não pelos que só provam o corpo do POST). */
const ITEM_FILHO: NoDaEstrutura = {
  id: 150,
  componenteId: 10,
  codigoDoComponente: 'CH-100',
  descricao: 'Sub-item',
  quantidade: 3,
  nivelHierarquico: 'Item',
  requerRelatorioDimensional: false,
  materiais: [],
  roteiro: [],
  filhos: [],
  quantidadePorPai: 3,
  semRoteiro: true,
}
const PECA_COM_FILHO: NoDaEstrutura = { ...PECA, filhos: [ITEM_FILHO] }

/** `PaginaDe<ComponenteDto>` que o `SeletorComBusca` busca ao montar (carga inicial, sem debounce:
    `busca === ''` no mount) — todo teste com `podeEscrever` monta o formulário, e o formulário
    monta o seletor, então esta rota precisa estar declarada mesmo quando o teste não interage com
    o combobox (senão `fetchPorRotaComEstrutura` rejeita com "fetch não esperado"). */
const COMPONENTES_BUSCA = {
  itens: [COMPONENTE_BUSCA, COMPONENTE_SEM_SOLIDO_BUSCA],
  total: 2,
  pagina: 1,
  tamanho: 20,
}

/**
 * Mock de `fetch` desta tela: distingue GET de POST na MESMA rota
 * (`/api/agrupamentos/21/estrutura`, tanto `obterEstrutura` quanto `criarPeca`) pelo `method` do
 * `init` — o que `fetchPorRota` (testes/api.ts) não faz, porque ela roteia só por caminho. Molde de
 * `fetchPorRotaGravando`/`fetchComPostQueFalha` de `ComponenteDetalhePage.test.tsx`.
 *
 * `estruturaAposCriar`: o que a SEGUNDA chamada de GET (o `carregar()` que roda depois de QUALQUER
 * escrita bem-sucedida — criar Peça, acrescentar filho, editar ou excluir, todas chamam a mesma
 * `carregar`) devolve — `null` significa "mesma resposta da primeira", usado pelos testes que não
 * provam recarregamento.
 *
 * `respostaAgrupamento`/`respostaFilhos`/`respostaEditar`/`respostaExcluir`: Task 8b, override do
 * desfecho de `GET /agrupamentos/21` e dos três verbos de escrita da árvore
 * (`POST /estrutura/:paiId/filhos`, `PUT /estrutura/:id`, `DELETE /estrutura/:id`) — `null` usa o
 * desfecho de sucesso padrão. Toda chamada existente de `montarFetch` (as 12 anteriores à Task 8b)
 * continua funcionando sem tocar nestes quatro parâmetros: a Task 8b faz a tela buscar
 * `GET /agrupamentos/21` incondicionalmente no mount (para o cabeçalho), e sem este caso aqui TODOS
 * os testes anteriores quebrariam com "fetch não esperado".
 *
 * Fase 3: `posicoes` é o que `GET /agrupamentos/21/posicoes` devolve (a carga busca as duas juntas),
 * e `receitaDoPai`, o que `GET /componentes/10/filhos-padrao` devolve — a receita do Componente de
 * `PECA`, que o painel de acrescentar filho busca para pré-preencher a razão. `livro` é o histórico
 * de QUALQUER nó (`GET /estrutura/:id/movimentacoes`) e o Roteiro de qualquer nó vem vazio — o
 * painel de detalhes (Task 9 do plano 3) busca os dois ao abrir; `respostaEstorno` é o desfecho de
 * `POST /movimentacoes/:id/estorno`.
 */
function montarFetch({
  estruturaInicial,
  estruturaAposCriar = null,
  respostaCriar = null,
  respostaAgrupamento = null,
  respostaFilhos = null,
  respostaEditar = null,
  respostaExcluir = null,
  posicoes = [],
  receitaDoPai = [],
  livro = { movimentacoes: [], montagens: [] },
  respostaEstorno = null,
  importacoes = [],
  respostaImportar = null,
}: {
  estruturaInicial: NoDaEstrutura[]
  estruturaAposCriar?: NoDaEstrutura[] | null
  respostaCriar?: { status: number; corpo: unknown } | null
  respostaAgrupamento?: { status: number; corpo: unknown } | null
  respostaFilhos?: { status: number; corpo: unknown } | null
  respostaEditar?: { status: number; corpo: unknown } | null
  respostaExcluir?: { status: number; corpo: unknown } | null
  posicoes?: PosicoesDoNoDto[]
  receitaDoPai?: FilhoPadraoDto[]
  livro?: LivroDoNoDto
  respostaEstorno?: { status: number; corpo: unknown } | null
  /** O que `GET /agrupamentos/21/importacoes` devolve: os rascunhos em conferência. */
  importacoes?: unknown[]
  /** Desfecho de `POST /agrupamentos/21/importacoes`; `null` = 201 com a importação 5. */
  respostaImportar?: { status: number; corpo: unknown } | null
}) {
  let getsDeEstrutura = 0
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    const metodo = init?.method ?? 'GET'
    if (caminho === '/api/componentes') return Promise.resolve(respostaJson(COMPONENTES_BUSCA))
    if (caminho === '/api/agrupamentos/21/importacoes') {
      if (metodo === 'POST') {
        if (respostaImportar) return Promise.resolve(respostaJson(respostaImportar.corpo, respostaImportar.status))
        return Promise.resolve(respostaJson({ id: 5, agrupamentoId: 21 }, 201))
      }
      return Promise.resolve(respostaJson(importacoes))
    }
    if (caminho === '/api/agrupamentos/21/posicoes') return Promise.resolve(respostaJson(posicoes))
    if (caminho === '/api/componentes/10/filhos-padrao') return Promise.resolve(respostaJson(receitaDoPai))
    if (caminho === '/api/setores') return Promise.resolve(respostaJson([]))
    if (/^\/api\/estrutura\/\d+\/movimentacoes$/.test(caminho)) return Promise.resolve(respostaJson(livro))
    if (/^\/api\/estrutura\/\d+\/roteiro$/.test(caminho)) {
      return Promise.resolve(respostaJson({ estruturaItemId: Number(caminho.split('/')[3]), passos: [] }))
    }
    if (/^\/api\/movimentacoes\/\d+\/estorno$/.test(caminho) && metodo === 'POST') {
      if (respostaEstorno) return Promise.resolve(respostaJson(respostaEstorno.corpo, respostaEstorno.status))
      return Promise.resolve(respostaJson({}, 201))
    }
    if (caminho === '/api/agrupamentos/21') {
      if (respostaAgrupamento) return Promise.resolve(respostaJson(respostaAgrupamento.corpo, respostaAgrupamento.status))
      return Promise.resolve(respostaJson(AGRUPAMENTO))
    }
    if (caminho === '/api/agrupamentos/21/estrutura') {
      if (metodo === 'POST') {
        if (respostaCriar) return Promise.resolve(respostaJson(respostaCriar.corpo, respostaCriar.status))
        return Promise.resolve(respostaJson({ ...PECA, id: 101 }, 201))
      }
      getsDeEstrutura += 1
      const dados = getsDeEstrutura === 1 || !estruturaAposCriar ? estruturaInicial : estruturaAposCriar
      return Promise.resolve(respostaJson(dados))
    }
    if (/^\/api\/estrutura\/\d+\/filhos$/.test(caminho) && metodo === 'POST') {
      if (respostaFilhos) return Promise.resolve(respostaJson(respostaFilhos.corpo, respostaFilhos.status))
      return Promise.resolve(respostaJson({ ...ITEM_FILHO, id: 201 }, 201))
    }
    if (/^\/api\/estrutura\/\d+$/.test(caminho) && metodo === 'PUT') {
      if (respostaEditar) return Promise.resolve(respostaJson(respostaEditar.corpo, respostaEditar.status))
      return Promise.resolve(respostaJson({ ...PECA }, 200))
    }
    if (/^\/api\/estrutura\/\d+$/.test(caminho) && metodo === 'DELETE') {
      if (respostaExcluir) return Promise.resolve(respostaJson(respostaExcluir.corpo, respostaExcluir.status))
      return Promise.resolve(new Response(null, { status: 204 }))
    }
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
}

function renderizarDetalhe() {
  return render(
    <MemoryRouter initialEntries={['/agrupamentos/21']}>
      <Routes>
        <Route path="/agrupamentos/:id" element={<AgrupamentoDetalhePage />} />
        <Route path="/importacoes/:id" element={<p>Tela de conferência</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

/** Abre o painel de Peça pelo botão do cabeçalho — a tela abre em leitura, sem formulário. */
function abrirNovaPeca() {
  fireEvent.click(screen.getByRole('button', { name: 'Nova Peça' }))
}

/** Abre o painel de Peça e o preenche (ver `preencherPainelDePeca`). */
async function preencherFormulario(quantidade: number, marcarRequerRelatorio = false) {
  abrirNovaPeca()
  await preencherPainelDePeca(quantidade, marcarRequerRelatorio)
}

/**
 * Escolhe `CH-100` no `SeletorComBusca` do painel de Peça (já aberto), preenche a quantidade e marca
 * (ou não) o checkbox.
 *
 * O clique na opção é escopado ao `listbox` (não `screen.findByText` global): os testes de I1/I2
 * do fix pass da Task 8 carregam a árvore com a MESMA Peça (`CH-100`) já visível na tela, então um
 * `findByText` sem escopo acha dois `CH-100` — o nó da árvore e a opção do combobox — e lança
 * "Found multiple elements" em vez de selecionar.
 */
async function preencherPainelDePeca(quantidade: number, marcarRequerRelatorio = false) {
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

  // Teste 2. Também prova o Minor 4 herdado da re-review da Task 7: o `aria-label` do `<ul>` raiz
  // da `ArvoreDeEstrutura` passou de "Estrutura da peça" (singular) para "Estrutura do
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
    abrirNovaPeca()
    expect(screen.getByRole('combobox')).toBeTruthy()
    await preencherPainelDePeca(5, true)
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
  // depois de criar a Peça com sucesso o painel fecha, e reabri-lo mostra o formulário no estado
  // inicial — sem isto, o painel reaberto voltaria preenchido e convidaria a clicar "Criar Peça" de
  // novo com os mesmos dados de uma criação que já foi aplicada.
  it('formulário volta ao estado inicial depois de criar a Peça com sucesso', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [], estruturaAposCriar: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Este agrupamento ainda não tem estrutura')
    await preencherFormulario(5, true)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    expect(await screen.findByText('Chassi')).toBeTruthy()

    abrirNovaPeca()
    expect(screen.getByRole('combobox')).toHaveProperty('value', '')
    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '')
    expect(screen.getByLabelText(/requer relatório dimensional/i)).toHaveProperty('checked', false)
  })

  // Task 8: `exigirSolido` (regra 18) vai só no formulário de criar Peça. Sem este teste de tela,
  // remover a prop dali deixava a suíte inteira verde — só a suíte de `SeletorComBusca` provava a
  // marca, nunca o lugar onde ela é ligada.
  it('no formulário de criar Peça, componente sem sólido aparece marcado e não é selecionável', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [] }))

    renderizarDetalhe()
    await screen.findByText('Este agrupamento ainda não tem estrutura')
    abrirNovaPeca()

    fireEvent.click(screen.getByRole('combobox'))
    const listbox = await screen.findByRole('listbox')
    const opcaoSemSolido = await within(listbox).findByRole('option', { name: /SF-050/ })
    expect(opcaoSemSolido.getAttribute('aria-disabled')).toBe('true')

    fireEvent.click(opcaoSemSolido)

    expect(screen.getByRole('combobox')).toHaveProperty('value', '')
    expect(screen.getByRole('button', { name: 'Criar Peça' })).toHaveProperty('disabled', true)
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
    // A tela abre em leitura, então o combobox, a "Quantidade" e o "Criar Peça" já faltam antes de
    // qualquer clique, para qualquer perfil: o que prova o gating é o botão que abriria o painel
    // não existir.
    expect(screen.queryByRole('button', { name: 'Nova Peça' })).toBeNull()
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

    // Escopado na árvore (`role="list"`), e não em `screen` global: o CHECKBOX do painel de Peça
    // também se chama "Requer relatório dimensional" (é o rótulo do campo). O painel abre fechado,
    // então hoje o escopo não muda o resultado; ele garante que a contagem continua sendo só a da
    // linha da árvore se o painel um dia abrir junto.
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
  // ele. Molde de `ComponenteDetalhePage`: o `setErroComponente(null)` que abre o efeito de carga
  // do componente, no INÍCIO de cada carga, não só no `catch`.
  it('recarga bem-sucedida depois de uma carga falha limpa o banner de carga antigo', async () => {
    let getsDeEstrutura = 0
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
      const caminho = String(url).split('?')[0]
      const metodo = init?.method ?? 'GET'
      if (caminho === '/api/componentes') return Promise.resolve(respostaJson(COMPONENTES_BUSCA))
      // Task 8b: a tela busca o cabeçalho do Agrupamento incondicionalmente no mount, à parte da
      // estrutura — sem esta rota, os três mocks manuais abaixo rejeitariam com "fetch não
      // esperado" assim que a Task 8b entrou (medido: os 12 testes anteriores usavam `montarFetch`,
      // que já ganhou a rota; estes três montam o `fetch` à mão, então precisam dela também).
      if (caminho === '/api/agrupamentos/21') return Promise.resolve(respostaJson(AGRUPAMENTO))
      // Fase 3: a carga busca as posições junto da estrutura (as duas falham juntas).
      if (caminho === '/api/agrupamentos/21/posicoes') return Promise.resolve(respostaJson([]))
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
      // Task 8b: a tela busca o cabeçalho do Agrupamento incondicionalmente no mount, à parte da
      // estrutura — sem esta rota, os três mocks manuais abaixo rejeitariam com "fetch não
      // esperado" assim que a Task 8b entrou (medido: os 12 testes anteriores usavam `montarFetch`,
      // que já ganhou a rota; estes três montam o `fetch` à mão, então precisam dela também).
      if (caminho === '/api/agrupamentos/21') return Promise.resolve(respostaJson(AGRUPAMENTO))
      // Fase 3: a carga busca as posições junto da estrutura (as duas falham juntas).
      if (caminho === '/api/agrupamentos/21/posicoes') return Promise.resolve(respostaJson([]))
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
      // Task 8b: a tela busca o cabeçalho do Agrupamento incondicionalmente no mount, à parte da
      // estrutura — sem esta rota, os três mocks manuais abaixo rejeitariam com "fetch não
      // esperado" assim que a Task 8b entrou (medido: os 12 testes anteriores usavam `montarFetch`,
      // que já ganhou a rota; estes três montam o `fetch` à mão, então precisam dela também).
      if (caminho === '/api/agrupamentos/21') return Promise.resolve(respostaJson(AGRUPAMENTO))
      // Fase 3: a carga busca as posições junto da estrutura (as duas falham juntas).
      if (caminho === '/api/agrupamentos/21/posicoes') return Promise.resolve(respostaJson([]))
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

  // ---------------------------------------------------------------------------------------------
  // Task 8b: cabeçalho (obterAgrupamento), decisão do usuário de 2026-09-02.
  // ---------------------------------------------------------------------------------------------

  it('cabeçalho mostra código e tipo do Agrupamento quando a busca dá certo', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [] }))

    renderizarDetalhe()

    expect(await screen.findByRole('heading', { name: 'AGR-01 — Kit' })).toBeTruthy()
  })

  // O caminho degradado: `obterAgrupamento` falha, e a tela NÃO ganha um terceiro banner — o
  // título só cai de volta para o que ela já mostrava antes desta task (`Agrupamento {id}`).
  it('cabeçalho degrada para "Agrupamento {id}" quando a busca do Agrupamento falha, sem banner novo', async () => {
    const fetchMock = montarFetch({
      estruturaInicial: [],
      respostaAgrupamento: { status: 500, corpo: {} },
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Este agrupamento ainda não tem estrutura')

    // Prova que a busca do cabeçalho foi de fato tentada (não é só o título inicial, antes da
    // resposta chegar) — sem isto a asserção do título abaixo passaria mesmo se a chamada nunca
    // tivesse sido feita.
    await waitFor(() => {
      expect(fetchMock.mock.calls.some((c) => String(c[0]) === '/api/agrupamentos/21')).toBe(true)
    })
    expect(screen.getByRole('heading', { name: 'Agrupamento 21' })).toBeTruthy()
    expect(screen.queryAllByRole('alert')).toHaveLength(0)
  })

  // ---------------------------------------------------------------------------------------------
  // Task 8b: acrescentar filho, editar, excluir — os nove testes nomeados no brief.
  // ---------------------------------------------------------------------------------------------

  // Teste 1. O gatilho é o mesmo de sempre: catálogo paginado não cabe num `<select>`. Escopado no
  // painel (`data-testid="painel-de-escrita"`) porque o painel de Peça também tem um
  // `SeletorComBusca`; os dois painéis nunca coexistem (um por vez), mas o escopo nomeia de qual dos
  // dois o teste fala e continua achando um só se essa exclusividade quebrar.
  it('acrescentar sub-Item de catálogo escolhe o Componente pelo SeletorComBusca', async () => {
    const fetchMock = montarFetch({ estruturaInicial: [PECA] })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    expect(within(painel).getByRole('combobox')).toBeTruthy()

    fireEvent.click(within(painel).getByRole('combobox'))
    const listbox = await within(painel).findByRole('listbox')
    fireEvent.click(await within(listbox).findByText('CH-100'))
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '3' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade por pai'), { target: { value: '2' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Acrescentar' }))

    await waitFor(() => expect(screen.queryByTestId('painel-de-escrita')).toBeNull())

    const chamadaPost = fetchMock.mock.calls.find(
      (c) => String(c[0]) === '/api/estrutura/100/filhos' && (c[1] as RequestInit | undefined)?.method === 'POST',
    )
    expect(chamadaPost).toBeTruthy()
    expect(JSON.parse((chamadaPost![1] as RequestInit).body as string)).toEqual({
      componenteId: 10,
      descricao: null,
      quantidade: 3,
      quantidadePorPai: 2,
    })
  })

  // Task 8: o painel de acrescentar filho NÃO liga `exigirSolido` — Item pode ser ad-hoc, e a
  // regra 18 é só da Peça. Par negativo de
  // `no formulário de criar Peça, componente sem sólido aparece marcado e não é selecionável`:
  // o mesmo componente sem sólido que lá fica bloqueado, aqui é selecionável.
  it('no painel de acrescentar filho, modo catálogo, componente sem sólido é selecionável', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.click(within(painel).getByRole('combobox'))
    const listbox = await within(painel).findByRole('listbox')
    fireEvent.click(await within(listbox).findByText('SF-050'))

    expect(within(painel).getByRole('combobox')).toHaveProperty('value', 'SF-050 — Suporte sem sólido')
  })

  // Teste 2. `NovoFilho` modela os dois modos; a árvore já distingue ad-hoc de catálogo por forma e
  // rótulo (a pílula "Ad-hoc") — este teste prova que o PAINEL também tem os dois caminhos.
  it('acrescentar sub-Item ad-hoc envia componenteId nulo e a descrição digitada', async () => {
    const fetchMock = montarFetch({ estruturaInicial: [PECA] })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.click(within(painel).getByLabelText('Ad-hoc'))
    fireEvent.change(within(painel).getByLabelText('Descrição'), { target: { value: 'Parafuso especial' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '10' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade por pai'), { target: { value: '2' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Acrescentar' }))

    await waitFor(() => expect(screen.queryByTestId('painel-de-escrita')).toBeNull())

    const chamadaPost = fetchMock.mock.calls.find(
      (c) => String(c[0]) === '/api/estrutura/100/filhos' && (c[1] as RequestInit | undefined)?.method === 'POST',
    )
    expect(chamadaPost).toBeTruthy()
    expect(JSON.parse((chamadaPost![1] as RequestInit).body as string)).toEqual({
      componenteId: null,
      descricao: 'Parafuso especial',
      quantidade: 10,
      quantidadePorPai: 2,
    })
  })

  // Teste 3. Sem recarregar, o novo nó só apareceria depois de um F5 — a mesma propriedade que o
  // teste de "criar Peça" já prova para o formulário do topo, provada agora para o painel.
  it('a árvore é recarregada depois de acrescentar', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA], estruturaAposCriar: [PECA_COM_FILHO] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.click(within(painel).getByRole('combobox'))
    const listbox = await within(painel).findByRole('listbox')
    fireEvent.click(await within(listbox).findByText('CH-100'))
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '3' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade por pai'), { target: { value: '2' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Acrescentar' }))

    expect(await screen.findByText('Sub-item')).toBeTruthy()
  })

  // Teste 4. `mensagem` nomeia o CAMINHO do ciclo — o teste protege a mesma cadeia que o teste
  // equivalente de "criar Peça" já protege, agora para `acrescentarFilho`.
  it('409 de ciclo ao acrescentar mostra a mensagem que nomeia o caminho', async () => {
    const mensagemDoServidor = 'A receita tem um ciclo: 100 -> 150 -> 100. Não é possível acrescentar esse item.'
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      respostaFilhos: { status: 409, corpo: { erro: 'CicloNaReceita', mensagem: mensagemDoServidor } },
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.click(within(painel).getByRole('combobox'))
    const listbox = await within(painel).findByRole('listbox')
    fireEvent.click(await within(listbox).findByText('CH-100'))
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '3' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade por pai'), { target: { value: '2' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Acrescentar' }))

    const bannerDoPainel = await screen.findByText(mensagemDoServidor)
    // O painel NÃO fecha em conflito (só em sucesso), e a árvore continua visível.
    expect(screen.getByTestId('painel-de-escrita')).toBeTruthy()
    expect(screen.getByText('Chassi')).toBeTruthy()
    // m1 do fix pass da Task 8b: o banner do painel mora DENTRO do `<form>` (m9 estendido) — mesmo
    // molde do banner de "Criar Peça" (teste posicional "banner de erro de escrita fica dentro do
    // formulário de criar Peça (m9 herdado da Task 8)", que só cobre AQUELE form).
    expect(bannerDoPainel.closest('form')).toBe(screen.getByTestId('painel-de-escrita'))
  })

  it('acrescentar filho a nó já iniciado mostra a frase do servidor no painel', async () => {
    const frase = 'Chassi já entrou em produção: não se acrescenta filho a um nó já iniciado.'
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      respostaFilhos: { status: 409, corpo: { erro: 'PaiJaIniciado', mensagem: frase } },
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.click(within(painel).getByRole('combobox'))
    const listbox = await within(painel).findByRole('listbox')
    fireEvent.click(await within(listbox).findByText('CH-100'))
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '3' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade por pai'), { target: { value: '2' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Acrescentar' }))

    const banner = await screen.findByText(frase)
    expect(banner.closest('form')).toBe(screen.getByTestId('painel-de-escrita'))
  })

  // Teste 5. D4 por asserção de corpo: nenhum campo além de descrição, quantidade e razão (nem
  // `componenteId`) vaza no PUT. Na Peça a razão vai `null` — é o que o backend exige (regra 26).
  it('editar uma Peça envia descrição, quantidade e a razão nula', async () => {
    const fetchMock = montarFetch({ estruturaInicial: [PECA] })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))
    const painel = screen.getByTestId('painel-de-escrita')
    // Pré-preenchido com o que a árvore já mostra (a descrição RESOLVIDA, regra 19).
    expect(within(painel).getByLabelText('Descrição')).toHaveProperty('value', 'Chassi')
    expect(within(painel).getByLabelText('Quantidade')).toHaveProperty('value', '1')

    fireEvent.change(within(painel).getByLabelText('Descrição'), { target: { value: 'Chassi reforçado' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '2' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Salvar edição' }))

    await waitFor(() => expect(screen.queryByTestId('painel-de-escrita')).toBeNull())

    const chamadaPut = fetchMock.mock.calls.find(
      (c) => String(c[0]) === '/api/estrutura/100' && (c[1] as RequestInit | undefined)?.method === 'PUT',
    )
    expect(chamadaPut).toBeTruthy()
    expect(JSON.parse((chamadaPut![1] as RequestInit).body as string)).toEqual({
      descricao: 'Chassi reforçado',
      quantidade: 2,
      quantidadePorPai: null,
    })
  })

  // Teste 6. Exclusão em cascata sem aviso é perda de dado por surpresa — a confirmação precisa
  // dizer isso explicitamente, não só "excluir este nó?".
  it('excluir pede confirmação, e a confirmação diz que a subárvore vai junto', async () => {
    const fetchMock = montarFetch({ estruturaInicial: [PECA] })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))

    const dialogo = await screen.findByRole('dialog')
    expect(within(dialogo).getByText(/subárvore/i)).toBeTruthy()
    // A pausa deliberada é a propriedade sob teste: nada foi excluído ainda.
    expect(fetchMock.mock.calls.some((c) => (c[1] as RequestInit | undefined)?.method === 'DELETE')).toBe(false)
  })

  // Teste 7. O par do teste 6: sem esta prova, "pede confirmação" seria decorativo (o clique em
  // "Excluir" do diálogo é que dispara o DELETE, não a abertura do diálogo).
  it('cancelar a confirmação não chama a API', async () => {
    const fetchMock = montarFetch({ estruturaInicial: [PECA] })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))
    const dialogo = await screen.findByRole('dialog')
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(fetchMock.mock.calls.some((c) => (c[1] as RequestInit | undefined)?.method === 'DELETE')).toBe(false)
  })

  // Teste 8. O 404 é DESFECHO (`excluirNo` traduz para `'NaoEncontrado'`), não exceção — a tela
  // informa com uma frase própria e recarrega, em vez de cair no fallback genérico do `catch`.
  it('404 na exclusão é tratado como desfecho', async () => {
    const fetchMock = montarFetch({
      estruturaInicial: [PECA],
      respostaExcluir: { status: 404, corpo: null },
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))
    const dialogo = await screen.findByRole('dialog')
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Excluir' }))

    expect(await screen.findByText('Este nó já não existe mais.')).toBeTruthy()
    expect(screen.queryByText('Não foi possível excluir o nó.')).toBeNull()
  })

  // Teste 9. Gating na AÇÃO: a árvore continua visível (leitura é de todo perfil), mas nenhuma das
  // três ações aparece, e o painel de escrita/diálogo de confirmação nunca chegam a montar.
  it('perfil sem escrita não vê nenhuma das três ações', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()

    expect(await screen.findByText('Chassi')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Acrescentar filho' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Excluir' })).toBeNull()
    expect(screen.queryByTestId('painel-de-escrita')).toBeNull()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  // ---------------------------------------------------------------------------------------------
  // Fix pass da Task 8b: I1, I2, I3/M-K, m3 e m5 da review.
  // ---------------------------------------------------------------------------------------------

  // I1: a suíte tinha o diálogo abrir, o cancelamento e o 404 — nenhum teste de um DELETE que dá
  // certo. `estruturaAposCriar: []` prova a recarga: o nó só some da árvore na SEGUNDA chamada de
  // GET, depois do DELETE — sem `await carregar(agrupamentoId)` em `confirmarExclusao`, 'Chassi'
  // continuaria na tela (mutação M-H, remedida antes do conserto: 483/483 verde sem o `await`).
  it('excluir com sucesso remove o nó da árvore e recarrega (I1)', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA], estruturaAposCriar: [] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))
    const dialogo = await screen.findByRole('dialog')
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Excluir' }))

    // O nó some (a recarga rodou) e a tela cai no estado vazio — não fica só sem 'Chassi' por
    // acidente de outro motivo.
    await waitFor(() => expect(screen.queryByText('Chassi')).toBeNull())
    expect(await screen.findByText('Este agrupamento ainda não tem estrutura')).toBeTruthy()
  })

  // I2 (metade acrescentar): o `catch` de `salvarPainel` não tinha teste que o protegesse — em 409
  // o cliente RESOLVE (não lança), então o teste de ciclo (teste 4) nunca passa por `catch` nenhum.
  // Só um status que lança (403) alcança o `catch`.
  it('403 ao acrescentar filho vira mensagem, não exceção, e o painel continua aberto (I2)', async () => {
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      respostaFilhos: { status: 403, corpo: {} },
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.click(within(painel).getByRole('combobox'))
    const listbox = await within(painel).findByRole('listbox')
    fireEvent.click(await within(listbox).findByText('CH-100'))
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '3' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade por pai'), { target: { value: '2' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Acrescentar' }))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
    expect(screen.getByTestId('painel-de-escrita')).toBeTruthy()
    expect(screen.getByText('Chassi')).toBeTruthy()
  })

  // I2 (metade excluir): mesmo raciocínio do parágrafo acima, agora para `confirmarExclusao` — o
  // 404 do teste 8 é DESFECHO tratado sem lançar; só um 403 alcança o `catch` de verdade.
  it('403 ao excluir vira mensagem, não exceção, e a árvore continua visível (I2)', async () => {
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      respostaExcluir: { status: 403, corpo: {} },
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))
    const dialogo = await screen.findByRole('dialog')
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Excluir' }))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
    expect(screen.getByText('Chassi')).toBeTruthy()
  })

  // I3 / M-K: a metade "descrição vazia volta a herdar" da regra 19 não tinha matador — sem o
  // `descricaoPainel.trim() === '' ? null : descricaoPainel` de `corpoDaEdicao`, o PUT mandaria a
  // string vazia em vez de `null`, e o backend não voltaria a herdar a descrição do Componente.
  it('editar um nó de catálogo e esvaziar a descrição envia null (volta a herdar, I3/M-K)', async () => {
    const fetchMock = montarFetch({ estruturaInicial: [PECA] })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.change(within(painel).getByLabelText('Descrição'), { target: { value: '' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '2' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Salvar edição' }))

    await waitFor(() => expect(screen.queryByTestId('painel-de-escrita')).toBeNull())

    const chamadaPut = fetchMock.mock.calls.find(
      (c) => String(c[0]) === '/api/estrutura/100' && (c[1] as RequestInit | undefined)?.method === 'PUT',
    )
    expect(chamadaPut).toBeTruthy()
    expect(JSON.parse((chamadaPut![1] as RequestInit).body as string)).toEqual({
      descricao: null,
      quantidade: 2,
      quantidadePorPai: null,
    })
  })

  // m3: a exclusividade mútua painel <-> confirmação não tinha matador. Abre "Editar" num nó,
  // depois pede a exclusão do MESMO nó (o cenário real é outro nó, mas o efeito sob teste — o
  // painel fechar quando a confirmação abre — é o mesmo): sem `fecharPainel()` em `pedirExclusao`
  // (e sem `setNoParaExcluir(null)` em `abrirEditar`/`abrirAcrescentarFilho`), os dois ficariam
  // abertos ao mesmo tempo.
  it('pedir exclusão fecha o painel de acrescentar/editar aberto (m3)', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))
    expect(screen.getByTestId('painel-de-escrita')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))

    expect(await screen.findByRole('dialog')).toBeTruthy()
    expect(screen.queryByTestId('painel-de-escrita')).toBeNull()
  })

  // m9 (fix pass 2 da Task 8b): o teste acima ('pedir exclusão fecha o painel...') fecha só UM lado
  // da exclusividade — painel aberto, depois pede exclusão NO MESMO nó. O outro lado é o cenário que
  // o comentário de `abrirEditar`/`abrirAcrescentarFilho` (`AgrupamentoDetalhePage.tsx`, acima de
  // `pedirExclusao`) nomeia como motivador: a confirmação de exclusão aberta NUM nó, e "Editar"
  // clicado em OUTRO nó. Sem `setNoParaExcluir(null)` em `abrirEditar`, a confirmação do primeiro nó
  // ficaria na tela ao mesmo tempo que o painel do segundo.
  it('abrir "Editar" em outro nó fecha a confirmação de exclusão aberta em nó diferente (m9)', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA_COM_FILHO] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    await screen.findByText('Sub-item')

    const acoesDoChassi = screen.getByTestId(`acoes-do-no-${PECA_COM_FILHO.id}`)
    fireEvent.click(within(acoesDoChassi).getByRole('button', { name: 'Excluir' }))
    expect(await screen.findByRole('dialog')).toBeTruthy()

    const acoesDoSubItem = screen.getByTestId(`acoes-do-no-${ITEM_FILHO.id}`)
    fireEvent.click(within(acoesDoSubItem).getByRole('button', { name: 'Editar' }))

    expect(screen.queryByRole('dialog')).toBeNull()
  })

  // m5: editar um nó AD-HOC (`componenteId: null`) e esvaziar a descrição precisa desabilitar o
  // botão — a mesma guarda que já existe para o modo ad-hoc do acrescentar, no outro ramo do mesmo
  // `if` dentro da constante `painelInvalido`, aplicada ao lado da edição. Sem a guarda o PUT
  // sairia com `descricao: null` e o backend recusaria com 400 (`ErroDeDescricaoObrigatoria`,
  // regra 19), mas o usuário só veria o fallback genérico.
  it('editar um nó ad-hoc e esvaziar a descrição desabilita o botão de salvar (m5)', async () => {
    const noAdHoc: NoDaEstrutura = {
      ...PECA, componenteId: null, codigoDoComponente: null, descricao: 'Parafuso especial',
    }
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [noAdHoc] }))

    renderizarDetalhe()
    await screen.findByText('Parafuso especial')

    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.change(within(painel).getByLabelText('Descrição'), { target: { value: '' } })

    expect(within(painel).getByRole('button', { name: 'Salvar edição' })).toHaveProperty('disabled', true)
  })

  // ---------------------------------------------------------------------------------------------
  // Excedentes: os dois itens herdados da Task 8 (m8/m9) que o brief pede para fechar nesta task.
  // Nenhum dos nove testes acima os cobre — são cenários próprios (duas escritas em sequência; e
  // uma checagem de POSIÇÃO no DOM), por isso contam à parte, com a justificativa aqui.
  // ---------------------------------------------------------------------------------------------

  // m8 herdado da Task 8: `setErroEscrita(null)` no início de `salvar` não tinha teste que morresse
  // sem ele — o comportamento está certo (o banner do 403 precisa sumir quando a escrita seguinte
  // dá certo), só faltava a prova. Precisa de DUAS escritas em sequência, cenário que nenhum dos
  // testes existentes (nem os nove acima) exercita.
  it('duas escritas seguidas — 403 e depois 201 — fazem o banner do 403 sumir (m8 herdado da Task 8)', async () => {
    let tentativasDePost = 0
    let getsDeEstrutura = 0
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
      const caminho = String(url).split('?')[0]
      const metodo = init?.method ?? 'GET'
      if (caminho === '/api/componentes') return Promise.resolve(respostaJson(COMPONENTES_BUSCA))
      if (caminho === '/api/agrupamentos/21') return Promise.resolve(respostaJson(AGRUPAMENTO))
      // Fase 3: a carga busca as posições junto da estrutura (as duas falham juntas).
      if (caminho === '/api/agrupamentos/21/posicoes') return Promise.resolve(respostaJson([]))
      if (caminho === '/api/agrupamentos/21/estrutura') {
        if (metodo === 'POST') {
          tentativasDePost += 1
          if (tentativasDePost === 1) return Promise.resolve(respostaJson({}, 403))
          return Promise.resolve(respostaJson({ ...PECA, id: 101 }, 201))
        }
        // A carga inicial (antes de qualquer escrita) devolve vazio; a recarga que roda depois do
        // 201 (segunda tentativa) devolve `[PECA]` — sem essa distinção 'Chassi' nunca apareceria,
        // mesmo com o POST tendo dado certo.
        getsDeEstrutura += 1
        return Promise.resolve(respostaJson(getsDeEstrutura === 1 ? [] : [PECA]))
      }
      return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Este agrupamento ainda não tem estrutura')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))
    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()

    // O formulário NÃO foi resetado (só reseta em sucesso), então o segundo clique reusa o que já
    // está preenchido — é exatamente o caminho real: o usuário tenta de novo sem redigitar tudo.
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    expect(await screen.findByText('Chassi')).toBeTruthy()
    expect(screen.queryByText('Seu perfil não tem permissão para esta ação.')).toBeNull()
  })

  // m9 herdado da Task 8: a causa raiz era cópia parcial de molde ("um banner por estado" sem "cada
  // um na sua seção") — o conserto é posicional (mover `erroEscrita` para DENTRO do `<form>`), e
  // este teste prova a posição, não só o texto (que os testes de 403 já provam fartamente).
  it('banner de erro de escrita fica dentro do formulário de criar Peça (m9 herdado da Task 8)', async () => {
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
      const caminho = String(url).split('?')[0]
      const metodo = init?.method ?? 'GET'
      if (caminho === '/api/componentes') return Promise.resolve(respostaJson(COMPONENTES_BUSCA))
      if (caminho === '/api/agrupamentos/21') return Promise.resolve(respostaJson(AGRUPAMENTO))
      // Fase 3: a carga busca as posições junto da estrutura (as duas falham juntas).
      if (caminho === '/api/agrupamentos/21/posicoes') return Promise.resolve(respostaJson([]))
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

    const bannerDeEscrita = await screen.findByText('Seu perfil não tem permissão para esta ação.')
    expect(bannerDeEscrita.closest('form')).toBeTruthy()
  })
  // ---------------------------------------------------------------------------------------------
  // Fase 3: a razão (regra 26), o "Sem Roteiro" e onde está cada nó.
  // ---------------------------------------------------------------------------------------------

  it('acrescentar filho exige a Quantidade por pai', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.click(within(painel).getByLabelText('Ad-hoc'))
    fireEvent.change(within(painel).getByLabelText('Descrição'), { target: { value: 'Calço' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '20' } })

    expect(within(painel).getByRole('button', { name: 'Acrescentar' })).toHaveProperty('disabled', true)
    fireEvent.change(within(painel).getByLabelText('Quantidade por pai'), { target: { value: '2' } })
    expect(within(painel).getByRole('button', { name: 'Acrescentar' })).toHaveProperty('disabled', false)
  })

  it('a razão vem preenchida quando a receita do pai lista o filho escolhido', async () => {
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      receitaDoPai: [{ id: 1, componenteFilhoId: 10, codigo: 'CH-100', descricao: 'Chassi', quantidadePadrao: 4 }],
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    // A receita chega depois da abertura do painel; a escolha do Componente vem depois dela.
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.some((c) => String(c[0]) === '/api/componentes/10/filhos-padrao')).toBe(true))
    fireEvent.click(within(painel).getByRole('combobox'))
    const listbox = await within(painel).findByRole('listbox')
    fireEvent.click(await within(listbox).findByText('CH-100'))

    await waitFor(() => expect(within(painel).getByLabelText('Quantidade por pai')).toHaveProperty('value', '4'))
  })

  it('Componente fora da receita do pai deixa a razão para quem cadastra', async () => {
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      receitaDoPai: [{ id: 1, componenteFilhoId: 99, codigo: 'X-99', descricao: 'Outro', quantidadePadrao: 4 }],
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.click(within(painel).getByRole('combobox'))
    const listbox = await within(painel).findByRole('listbox')
    fireEvent.click(await within(listbox).findByText('CH-100'))

    expect(within(painel).getByLabelText('Quantidade por pai')).toHaveProperty('value', '')
  })

  it('editar um Item mostra a razão dele e envia a nova', async () => {
    const fetchMock = montarFetch({ estruturaInicial: [PECA_COM_FILHO] })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Sub-item')
    fireEvent.click(within(screen.getByTestId('acoes-do-no-150')).getByRole('button', { name: 'Editar' }))
    const painel = screen.getByTestId('painel-de-escrita')
    expect(within(painel).getByLabelText('Quantidade por pai')).toHaveProperty('value', '3')
    fireEvent.change(within(painel).getByLabelText('Quantidade por pai'), { target: { value: '5' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Salvar edição' }))

    await waitFor(() => expect(screen.queryByTestId('painel-de-escrita')).toBeNull())
    const chamadaPut = fetchMock.mock.calls.find(
      (c) => String(c[0]) === '/api/estrutura/150' && (c[1] as RequestInit | undefined)?.method === 'PUT',
    )
    expect(JSON.parse((chamadaPut![1] as RequestInit).body as string)).toEqual({
      descricao: 'Sub-item', quantidade: 3, quantidadePorPai: 5,
    })
  })

  it('editar a Peça não pede razão', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))

    expect(within(screen.getByTestId('painel-de-escrita')).queryByLabelText('Quantidade por pai')).toBeNull()
  })

  it('reduzir abaixo do que já andou mostra a frase do servidor', async () => {
    const frase = 'Já saíram 4 de "a iniciar" e 0 foram montados: a quantidade não pode ficar abaixo de 4.'
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      respostaEditar: { status: 409, corpo: { erro: 'QuantidadeAbaixoDoMovimentado', mensagem: frase } },
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))
    fireEvent.change(within(screen.getByTestId('painel-de-escrita')).getByLabelText('Quantidade'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Salvar edição' }))

    expect(await screen.findByText(frase)).toBeTruthy()
  })

  // Fix round 1 (Important da review da Task 8): um 409 de editar significa que a tela ficou
  // velha (spec §8.3) — a árvore e as posições recarregam, sem fechar o painel nem apagar a frase.
  it('editar com 409 mantém o painel aberto e recarrega a árvore e as posições', async () => {
    const frase = 'Já saíram 4 de "a iniciar" e 0 foram montados: a quantidade não pode ficar abaixo de 4.'
    const fetchMock = montarFetch({
      estruturaInicial: [PECA],
      respostaEditar: { status: 409, corpo: { erro: 'QuantidadeAbaixoDoMovimentado', mensagem: frase } },
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('Chassi')
    const getsDeEstrutura = () => fetchMock.mock.calls.filter(
      (c) => String(c[0]) === '/api/agrupamentos/21/estrutura',
    ).length
    const getsDePosicoes = () => fetchMock.mock.calls.filter(
      (c) => String(c[0]) === '/api/agrupamentos/21/posicoes',
    ).length
    const getsDeEstruturaAntes = getsDeEstrutura()
    const getsDePosicoesAntes = getsDePosicoes()

    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))
    fireEvent.change(within(screen.getByTestId('painel-de-escrita')).getByLabelText('Quantidade'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Salvar edição' }))

    expect(await screen.findByText(frase)).toBeTruthy()
    // O painel continua aberto — o 409 não fecha (só recarrega por baixo).
    expect(screen.getByTestId('painel-de-escrita')).toBeTruthy()
    await waitFor(() => expect(getsDeEstrutura()).toBe(getsDeEstruturaAntes + 1))
    expect(getsDePosicoes()).toBe(getsDePosicoesAntes + 1)
  })

  it('excluir em conflito de concorrência diz para tentar de novo', async () => {
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      respostaExcluir: { status: 409, corpo: { erro: 'ConflitoDeConcorrencia' } },
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(within(screen.getByTestId('acoes-do-no-100')).getByRole('button', { name: 'Excluir' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Excluir' }))

    expect(await screen.findByText('Outra pessoa registrou neste item ao mesmo tempo; atualize e tente de novo.')).toBeTruthy()
  })

  it('mostra onde está cada nó, e o "Sem Roteiro"', async () => {
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      posicoes: [{ estruturaItemId: 100, totalMontado: null, saldos: [
        { posicao: 'NoSetor', setorId: 1, setorNome: 'Corte', ordem: 1, quantidade: 1 },
      ] }],
    }))

    renderizarDetalhe()

    const linha = await screen.findByTestId('linha-no-100')
    expect(within(linha).getByText('1 em Corte (passo 1)')).toBeTruthy()
    expect(within(linha).getByText('Sem Roteiro')).toBeTruthy()
  })

  it('falha ao carregar as posições é falha de carga: banner, sem árvore', async () => {
    vi.stubGlobal('fetch', vi.fn((url: string | URL) => {
      const caminho = String(url).split('?')[0]
      if (caminho === '/api/componentes') return Promise.resolve(respostaJson(COMPONENTES_BUSCA))
      if (caminho === '/api/agrupamentos/21') return Promise.resolve(respostaJson(AGRUPAMENTO))
      if (caminho === '/api/agrupamentos/21/estrutura') return Promise.resolve(respostaJson([PECA]))
      if (caminho === '/api/agrupamentos/21/posicoes') return Promise.resolve(respostaJson({}, 500))
      return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    }))

    renderizarDetalhe()

    expect(await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Estrutura do agrupamento' })).toBeNull()
  })
  it('"Detalhes" abre o Roteiro e o histórico do nó, para todo perfil', async () => {
    perfil = 'Gestao'
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Detalhes' }))

    const painel = screen.getByRole('region', { name: 'Detalhes de Chassi' })
    expect(await within(painel).findByText('Nenhum registro ainda.')).toBeTruthy()
    expect(within(painel).getByText(/Este nó não tem Roteiro/)).toBeTruthy()
    fireEvent.click(within(painel).getByRole('button', { name: 'Fechar' }))
    expect(screen.queryByRole('region', { name: 'Detalhes de Chassi' })).toBeNull()
  })

  it('"Detalhes" move o foco para o título do painel, para o celular rolar até ele (I3 da review de branch da Fase 3)', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Detalhes' }))

    expect(document.activeElement).toBe(await screen.findByRole('heading', { level: 2, name: 'Detalhes de Chassi' }))
  })

  it('estornar no detalhe recarrega a árvore e onde está cada nó', async () => {
    const fetchMock = montarFetch({
      estruturaInicial: [PECA],
      livro: { movimentacoes: [movimentacao({ id: 41, estruturaItemId: 100, usuarioId: 1 })], montagens: [] },
    })
    vi.stubGlobal('fetch', fetchMock)
    const getsDePosicoes = () => fetchMock.mock.calls.filter((c) => String(c[0]) === '/api/agrupamentos/21/posicoes').length

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Detalhes' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar o registro nº 41' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(getsDePosicoes()).toBe(2))
  })

  it('abrir "Editar" fecha o detalhe aberto', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Detalhes' }))
    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))

    expect(screen.queryByRole('region', { name: 'Detalhes de Chassi' })).toBeNull()
    expect(screen.getByTestId('painel-de-escrita')).toBeTruthy()
  })
  it('o PCP edita o Roteiro no detalhe; o Operador só lê', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Detalhes' }))
    expect(await screen.findByRole('button', { name: 'Adicionar passo' })).toBeTruthy()
    cleanup()

    perfil = 'Operador'
    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Detalhes' }))
    expect(await screen.findByText(/Quem cadastra o Roteiro é o PCP/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Adicionar passo' })).toBeNull()
  })
  // ---------------------------------------------------------------------------------------------
  // Fase 1F: a tela abre em leitura, "Nova Peça" abre o painel, e há um painel por vez.
  // ---------------------------------------------------------------------------------------------

  // O teste que antes vivia aqui por omissão: com o formulário fixo no topo, "tem formulário" era
  // verdade desde o primeiro render. Agora a leitura é o estado inicial, e o botão é a única porta.
  it('abre em leitura: sem formulario de Peca antes do clique', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    expect(screen.queryByRole('form', { name: 'Nova Peça' })).toBeNull()
    expect(screen.queryByRole('combobox')).toBeNull()
    expect(screen.getByRole('button', { name: 'Nova Peça' })).toBeTruthy()
  })

  it('Nova Peca abre o painel e some enquanto ele esta aberto', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    abrirNovaPeca()

    expect(screen.getByRole('form', { name: 'Nova Peça' })).toBeTruthy()
    // O botão do cabeçalho some (decisão D5 do plano da 1F): o caminho de saída é Cancelar ou salvar.
    expect(screen.queryByRole('button', { name: 'Nova Peça' })).toBeNull()
  })

  it('Cancelar da Peca fecha e descarta o digitado', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    await preencherFormulario(5, true)
    fireEvent.click(within(screen.getByRole('form', { name: 'Nova Peça' })).getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByRole('form', { name: 'Nova Peça' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Nova Peça' })).toBeTruthy()

    abrirNovaPeca()
    expect(screen.getByRole('combobox')).toHaveProperty('value', '')
    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '')
    expect(screen.getByLabelText(/requer relatório dimensional/i)).toHaveProperty('checked', false)
  })

  it('Cancelar da Peca descarta tambem o erro de escrita', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA], respostaCriar: { status: 403, corpo: {} } }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))
    await screen.findByText('Seu perfil não tem permissão para esta ação.')
    fireEvent.click(within(screen.getByRole('form', { name: 'Nova Peça' })).getByRole('button', { name: 'Cancelar' }))

    abrirNovaPeca()
    expect(screen.queryByText('Seu perfil não tem permissão para esta ação.')).toBeNull()
  })

  it('criar Peca com sucesso fecha o painel e recarrega a arvore', async () => {
    const fetchMock = montarFetch({ estruturaInicial: [], estruturaAposCriar: [PECA] })
    vi.stubGlobal('fetch', fetchMock)
    const getsDeEstrutura = () => fetchMock.mock.calls.filter(
      (c) => String(c[0]) === '/api/agrupamentos/21/estrutura' && ((c[1] as RequestInit | undefined)?.method ?? 'GET') === 'GET',
    ).length

    renderizarDetalhe()
    await screen.findByText('Este agrupamento ainda não tem estrutura')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    expect(await screen.findByText('Chassi')).toBeTruthy()
    expect(screen.queryByRole('form', { name: 'Nova Peça' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Nova Peça' })).toBeTruthy()
    expect(getsDeEstrutura()).toBe(2)
  })

  // O botão "Criar Peça" não pode ficar em "Salvando…" durante a recarga da árvore: a escrita já
  // terminou, e quem reabrir o painel nessa janela não pode herdar o estado de uma gravação antiga.
  it('o envio da Peca termina quando o POST resolve, sem esperar a recarga', async () => {
    const base = montarFetch({ estruturaInicial: [PECA], estruturaAposCriar: [PECA] })
    let getsDeEstrutura = 0
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) => {
      if (String(url).split('?')[0] === '/api/agrupamentos/21/estrutura' && (init?.method ?? 'GET') === 'GET') {
        getsDeEstrutura += 1
        if (getsDeEstrutura > 1) return new Promise<Response>(() => {})
      }
      return base(url, init)
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))
    await waitFor(() => expect(screen.queryByRole('form', { name: 'Nova Peça' })).toBeNull())

    abrirNovaPeca()
    expect(screen.getByRole('button', { name: 'Criar Peça' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Salvando…' })).toBeNull()
  })

  // Review Focus 5: nunca dois `<form>` de painel no documento, nas duas direções. A invariante é
  // a que os `getByTestId('painel-de-escrita')` dos testes do painel de nó assumem.
  it('abrir Editar com o painel de Peca aberto fecha o de Peca', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))

    expect(screen.getAllByRole('form')).toHaveLength(1)
    expect(screen.getByRole('form', { name: 'Editar nó' })).toBeTruthy()
    expect(screen.queryByRole('form', { name: 'Nova Peça' })).toBeNull()
    // O botão do cabeçalho volta, já que o painel dele fechou.
    expect(screen.getByRole('button', { name: 'Nova Peça' })).toBeTruthy()
  })

  it('abrir Acrescentar filho com o painel de Peca aberto fecha o de Peca', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    abrirNovaPeca()
    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))

    expect(screen.getAllByRole('form')).toHaveLength(1)
    expect(screen.getByRole('form', { name: 'Acrescentar sub-Item' })).toBeTruthy()
  })

  it('abrir Nova Peca com o painel de no aberto fecha o de no', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))
    // Com o painel de nó aberto o botão continua lá: só o painel de Peça o esconde (decisão D5).
    abrirNovaPeca()

    expect(screen.getAllByRole('form')).toHaveLength(1)
    expect(screen.getByRole('form', { name: 'Nova Peça' })).toBeTruthy()
    expect(screen.queryByTestId('painel-de-escrita')).toBeNull()
  })

  it('abrir Nova Peca fecha o detalhe do no e a confirmacao de exclusao', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Detalhes' }))
    expect(screen.getByRole('region', { name: 'Detalhes de Chassi' })).toBeTruthy()
    abrirNovaPeca()
    expect(screen.queryByRole('region', { name: 'Detalhes de Chassi' })).toBeNull()

    fireEvent.click(within(screen.getByRole('form', { name: 'Nova Peça' })).getByRole('button', { name: 'Cancelar' }))
    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))
    expect(screen.getByRole('dialog')).toBeTruthy()
    abrirNovaPeca()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  // Os outros dois caminhos de abertura também têm de fechar o painel de Peça: o teste
  // `abrir Editar com o painel de Peca aberto fecha o de Peca` cobre uma das quatro aberturas, e
  // cada uma tem a sua própria chamada de fechamento.
  it('pedir exclusao ou abrir o detalhe com o painel de Peca aberto fecha o de Peca', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    abrirNovaPeca()
    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))
    expect(screen.queryByRole('form', { name: 'Nova Peça' })).toBeNull()
    expect(screen.getByRole('dialog')).toBeTruthy()

    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancelar' }))
    abrirNovaPeca()
    fireEvent.click(screen.getByRole('button', { name: 'Detalhes' }))
    expect(screen.queryByRole('form', { name: 'Nova Peça' })).toBeNull()
    expect(screen.getByRole('region', { name: 'Detalhes de Chassi' })).toBeTruthy()
  })

  it('o Id do agrupamento continua no cabecalho ao lado do botao', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    const id = screen.getByText('Id 21')
    expect(screen.getByRole('button', { name: 'Nova Peça' }).parentElement).toBe(id.parentElement)

    abrirNovaPeca()
    expect(screen.getByText('Id 21')).toBeTruthy()
  })

  it('o painel de no tem o titulo como nome acessivel', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))

    expect(screen.getByRole('form', { name: 'Editar nó' })).toBe(screen.getByTestId('painel-de-escrita'))
  })

  // Trocar de nó com o painel aberto remonta o painel por `key`, e o foco volta ao primeiro campo.
  it('abrir Editar em outro no com o painel aberto devolve o foco ao primeiro campo', async () => {
    const outra: NoDaEstrutura = { ...PECA, id: 200, codigoDoComponente: 'CH-200', descricao: 'Base' }
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA, outra] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(within(screen.getByTestId('acoes-do-no-100')).getByRole('button', { name: 'Editar' }))
    const primeiro = within(screen.getByTestId('painel-de-escrita')).getByLabelText('Descrição')
    expect(document.activeElement).toBe(primeiro)

    fireEvent.click(within(screen.getByTestId('acoes-do-no-200')).getByRole('button', { name: 'Editar' }))
    const segundo = within(screen.getByTestId('painel-de-escrita')).getByLabelText('Descrição')

    expect(segundo).not.toBe(primeiro)
    expect(document.activeElement).toBe(segundo)
    expect(segundo).toHaveProperty('value', 'Base')
  })

  it('quem nao pode escrever nao ve Nova Peca e continua vendo o Id', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    expect(screen.queryByRole('button', { name: 'Nova Peça' })).toBeNull()
    expect(screen.getByText('Id 21')).toBeTruthy()
  })

  it('o estado vazio manda usar o botao Nova Peca', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [] }))

    renderizarDetalhe()

    expect(await screen.findByText('Use o botão Nova Peça para criar a primeira.')).toBeTruthy()
  })

  it('Cancelar da Peca devolve o foco ao Nova Peca', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    abrirNovaPeca()
    fireEvent.click(within(screen.getByRole('form', { name: 'Nova Peça' })).getByRole('button', { name: 'Cancelar' }))

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Nova Peça' }))
  })

  it('criar Peca com sucesso devolve o foco ao Nova Peca', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [], estruturaAposCriar: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Este agrupamento ainda não tem estrutura')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    expect(await screen.findByText('Chassi')).toBeTruthy()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Nova Peça' }))
  })

  // O painel de Peça também fecha quando o do nó abre. Aí o foco já está no primeiro campo do
  // painel do nó, e devolvê-lo ao "Nova Peça" o tiraria de quem acabou de pedir para editar.
  it('abrir Editar com o painel de Peca aberto deixa o foco no painel do no', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    abrirNovaPeca()
    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))

    const painelDoNo = screen.getByRole('form', { name: 'Editar nó' })
    expect(document.activeElement).not.toBe(document.body)
    expect(painelDoNo.contains(document.activeElement)).toBe(true)
  })

  it('com a Peca em voo, Cancelar fica desabilitado', async () => {
    const base = montarFetch({ estruturaInicial: [PECA] })
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) =>
      String(url).split('?')[0] === '/api/agrupamentos/21/estrutura' && init?.method === 'POST'
        ? new Promise<Response>(() => {})
        : base(url, init)))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    await preencherFormulario(5)
    fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

    await screen.findByRole('button', { name: 'Salvando…' })
    const painel = screen.getByRole('form', { name: 'Nova Peça' })
    const cancelar = within(painel).getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement
    expect(cancelar.disabled).toBe(true)
    fireEvent.click(cancelar)
    expect(screen.getByRole('form', { name: 'Nova Peça' })).toBeTruthy()
  })

  it('com a edicao do no em voo, Cancelar do painel do no fica desabilitado', async () => {
    const base = montarFetch({ estruturaInicial: [PECA] })
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'PUT' ? new Promise<Response>(() => {}) : base(url, init)))

    renderizarDetalhe()
    await screen.findByText('Chassi')
    fireEvent.click(screen.getByRole('button', { name: 'Editar' }))
    fireEvent.click(screen.getByRole('button', { name: 'Salvar edição' }))

    await screen.findByRole('button', { name: 'Salvando…' })
    const painel = screen.getByRole('form', { name: 'Editar nó' })
    const cancelar = within(painel).getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement
    expect(cancelar.disabled).toBe(true)
    fireEvent.click(cancelar)
    expect(screen.getByRole('form', { name: 'Editar nó' })).toBeTruthy()
  })

  describe('importar o BOM', () => {
    /** Escolhe um arquivo no campo do painel; `tamanho` força o `size` sem alocar o conteúdo. */
    function escolherArquivo(nome: string, tamanho?: number) {
      const arquivo = new File(['a;b'], nome)
      if (tamanho !== undefined) Object.defineProperty(arquivo, 'size', { value: tamanho })
      const campo = screen.getByLabelText(/Arquivo do BOM/) as HTMLInputElement
      fireEvent.change(campo, { target: { files: [arquivo] } })
      return arquivo
    }

    it('Importar_BOM_aparece_so_para_quem_escreve_em_estrutura', async () => {
      vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))
      const { unmount } = renderizarDetalhe()
      await screen.findByText('Chassi')
      expect(screen.getByRole('button', { name: 'Importar BOM' })).toBeTruthy()
      unmount()

      perfil = 'Operador'
      vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))
      renderizarDetalhe()
      await screen.findByText('Chassi')
      expect(screen.queryByRole('button', { name: 'Importar BOM' })).toBeNull()
    })

    it('Importar_BOM_abre_painel_com_campo_de_arquivo', async () => {
      vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))
      renderizarDetalhe()
      await screen.findByText('Chassi')

      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))

      const painel = screen.getByRole('form', { name: 'Importar BOM' })
      const campo = within(painel).getByLabelText(/Arquivo do BOM/) as HTMLInputElement
      expect(campo.type).toBe('file')
      expect(campo.accept).toBe('.xlsx,.csv')
      // O botão do cabeçalho some enquanto o painel está aberto, como o "Nova Peça".
      expect(screen.queryByRole('button', { name: 'Importar BOM' })).toBeNull()
      // Sem arquivo escolhido, não há o que enviar.
      expect((within(painel).getByRole('button', { name: 'Importar' }) as HTMLButtonElement).disabled).toBe(true)
    })

    it('Cancelar fecha o painel e devolve o foco ao botão Importar BOM', async () => {
      vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))

      fireEvent.click(within(screen.getByRole('form', { name: 'Importar BOM' })).getByRole('button', { name: 'Cancelar' }))

      expect(screen.queryByRole('form', { name: 'Importar BOM' })).toBeNull()
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Importar BOM' }))
    })

    it('abrir o painel de importação fecha o de Peça, e o contrário', async () => {
      vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))
      renderizarDetalhe()
      await screen.findByText('Chassi')

      abrirNovaPeca()
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))
      expect(screen.queryByRole('form', { name: 'Nova Peça' })).toBeNull()
      expect(screen.getByRole('form', { name: 'Importar BOM' })).toBeTruthy()

      abrirNovaPeca()
      expect(screen.queryByRole('form', { name: 'Importar BOM' })).toBeNull()
      expect(screen.getByRole('form', { name: 'Nova Peça' })).toBeTruthy()
    })

    it('abrir o painel de um nó fecha o de importação', async () => {
      vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))

      fireEvent.click(screen.getByRole('button', { name: 'Editar' }))

      expect(screen.queryByRole('form', { name: 'Importar BOM' })).toBeNull()
      expect(screen.getByRole('form', { name: 'Editar nó' })).toBeTruthy()
    })

    it('Arquivo_acima_de_5_MiB_e_recusado_sem_enviar', async () => {
      const fetchMock = montarFetch({ estruturaInicial: [PECA] })
      vi.stubGlobal('fetch', fetchMock)
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))

      escolherArquivo('bom-enorme.xlsx', 5 * 1024 * 1024 + 1)

      expect((await screen.findByRole('alert')).textContent).toContain('5 MiB')
      const enviar = screen.getByRole('button', { name: 'Importar' }) as HTMLButtonElement
      expect(enviar.disabled).toBe(true)
      fireEvent.submit(screen.getByRole('form', { name: 'Importar BOM' }))
      expect(fetchMock.mock.calls.some((c) => (c[1] as RequestInit | undefined)?.method === 'POST')).toBe(false)
    })

    it('arquivo de exatamente 5 MiB é aceito', async () => {
      vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))

      escolherArquivo('bom-no-limite.xlsx', 5 * 1024 * 1024)

      expect(screen.queryByRole('alert')).toBeNull()
      expect((screen.getByRole('button', { name: 'Importar' }) as HTMLButtonElement).disabled).toBe(false)
    })

    it('Sucesso_navega_para_a_conferencia', async () => {
      const fetchMock = montarFetch({ estruturaInicial: [PECA], respostaImportar: { status: 201, corpo: { id: 9, agrupamentoId: 21 } } })
      vi.stubGlobal('fetch', fetchMock)
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))
      const arquivo = escolherArquivo('bom.xlsx')

      fireEvent.click(screen.getByRole('button', { name: 'Importar' }))

      expect(await screen.findByText('Tela de conferência')).toBeTruthy()
      const post = fetchMock.mock.calls.find((c) => (c[1] as RequestInit | undefined)?.method === 'POST')!
      expect(post[0]).toBe('/api/agrupamentos/21/importacoes')
      expect(((post[1] as RequestInit).body as FormData).get('arquivo')).toBe(arquivo)
    })

    it('BomInvalido_lista_os_erros_dentro_do_painel', async () => {
      vi.stubGlobal('fetch', montarFetch({
        estruturaInicial: [PECA],
        respostaImportar: { status: 400, corpo: { erro: 'BomInvalido', mensagem: 'Linha 3: quantidade invalida.\nLinha 9: codigo vazio.' } },
      }))
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))
      escolherArquivo('bom.csv')

      fireEvent.click(screen.getByRole('button', { name: 'Importar' }))

      const painel = screen.getByRole('form', { name: 'Importar BOM' })
      const alerta = await within(painel).findByRole('alert')
      expect(alerta.textContent).toContain('O arquivo tem problemas:')
      expect(within(alerta).getAllByRole('listitem').map((li) => li.textContent))
        .toEqual(['Linha 3: quantidade invalida.', 'Linha 9: codigo vazio.'])
      // O painel continua aberto para tentar de novo, e a tela não navegou.
      expect(screen.queryByText('Tela de conferência')).toBeNull()
    })

    it('depois de um BomInvalido o arquivo precisa ser escolhido de novo, e o segundo envio leva o arquivo novo', async () => {
      const base = montarFetch({ estruturaInicial: [PECA] })
      let respostas = 0
      const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
        if (init?.method === 'POST' && String(url) === '/api/agrupamentos/21/importacoes') {
          respostas += 1
          return Promise.resolve(respostas === 1
            ? respostaJson({ erro: 'BomInvalido', mensagem: 'Linha 3: quantidade invalida.' }, 400)
            : respostaJson({ id: 9, agrupamentoId: 21 }, 201))
        }
        return base(url, init)
      })
      vi.stubGlobal('fetch', fetchMock)
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))
      escolherArquivo('bom.csv')
      const campo = screen.getByLabelText(/Arquivo do BOM/) as HTMLInputElement

      fireEvent.click(screen.getByRole('button', { name: 'Importar' }))

      await screen.findByText('Linha 3: quantidade invalida.')
      // O File antigo não fica para um reenvio às cegas: o campo zera e o botão espera um arquivo novo.
      expect(campo.value).toBe('')
      expect((screen.getByRole('button', { name: 'Importar' }) as HTMLButtonElement).disabled).toBe(true)

      const novo = escolherArquivo('bom-corrigido.csv')
      expect((screen.getByRole('button', { name: 'Importar' }) as HTMLButtonElement).disabled).toBe(false)
      fireEvent.click(screen.getByRole('button', { name: 'Importar' }))

      expect(await screen.findByText('Tela de conferência')).toBeTruthy()
      const posts = fetchMock.mock.calls.filter((c) => c[1]?.method === 'POST')
      expect(posts).toHaveLength(2)
      expect(((posts[1][1] as RequestInit).body as FormData).get('arquivo')).toBe(novo)
    })

    it('outro erro de envio também exige escolher o arquivo de novo', async () => {
      vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA], respostaImportar: { status: 403, corpo: {} } }))
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))
      escolherArquivo('bom.xlsx')

      fireEvent.click(screen.getByRole('button', { name: 'Importar' }))

      await screen.findByRole('alert')
      expect((screen.getByRole('button', { name: 'Importar' }) as HTMLButtonElement).disabled).toBe(true)
    })

    it('recarregar a árvore depois de uma escrita relê a lista de importações', async () => {
      const fetchMock = montarFetch({ estruturaInicial: [PECA], estruturaAposCriar: [PECA, { ...PECA, id: 101 }] })
      vi.stubGlobal('fetch', fetchMock)
      renderizarDetalhe()
      await screen.findByText('Chassi')
      const leituras = () => fetchMock.mock.calls
        .filter((c) => String(c[0]) === '/api/agrupamentos/21/importacoes' && (c[1]?.method ?? 'GET') === 'GET').length
      await waitFor(() => expect(leituras()).toBe(1))

      await preencherFormulario(5)
      fireEvent.click(screen.getByRole('button', { name: 'Criar Peça' }))

      await waitFor(() => expect(leituras()).toBe(2))
    })

    it('403 ao importar vira mensagem dentro do painel', async () => {
      vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA], respostaImportar: { status: 403, corpo: {} } }))
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))
      escolherArquivo('bom.xlsx')

      fireEvent.click(screen.getByRole('button', { name: 'Importar' }))

      const painel = screen.getByRole('form', { name: 'Importar BOM' })
      expect((await within(painel).findByRole('alert')).textContent)
        .toBe('Seu perfil não tem permissão para esta ação.')
    })

    it('com o envio em voo, Cancelar fica desabilitado', async () => {
      const base = montarFetch({ estruturaInicial: [PECA] })
      vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) =>
        init?.method === 'POST' ? new Promise<Response>(() => {}) : base(url, init)))
      renderizarDetalhe()
      await screen.findByText('Chassi')
      fireEvent.click(screen.getByRole('button', { name: 'Importar BOM' }))
      escolherArquivo('bom.xlsx')

      fireEvent.click(screen.getByRole('button', { name: 'Importar' }))

      await screen.findByRole('button', { name: 'Importando…' })
      const painel = screen.getByRole('form', { name: 'Importar BOM' })
      expect((within(painel).getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)
    })

    it('mostra as importações em conferência abaixo da árvore, para quem escreve', async () => {
      vi.stubGlobal('fetch', montarFetch({
        estruturaInicial: [PECA],
        importacoes: [{
          id: 5, nomeDoArquivo: 'bom-chassi.xlsx', criadoPor: 'Maria',
          criadoEm: '2026-10-02T08:00:00-03:00', atualizadoEm: '2026-10-02T09:30:00-03:00',
        }],
      }))
      renderizarDetalhe()

      expect((await screen.findByRole('link', { name: /^Continuar/ })).getAttribute('href')).toBe('/importacoes/5')
      const secao = screen.getByRole('heading', { name: 'Importações em conferência' })
      const arvore = screen.getByRole('list', { name: /estrutura/i })
      expect(arvore.compareDocumentPosition(secao) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    })

    it('quem não escreve não vê a seção nem a lê', async () => {
      perfil = 'Operador'
      const fetchMock = montarFetch({
        estruturaInicial: [PECA],
        importacoes: [{ id: 5, nomeDoArquivo: 'x.xlsx', criadoPor: 'M', criadoEm: '2026-10-02T08:00:00-03:00', atualizadoEm: '2026-10-02T08:00:00-03:00' }],
      })
      vi.stubGlobal('fetch', fetchMock)
      renderizarDetalhe()
      await screen.findByText('Chassi')

      expect(screen.queryByRole('heading', { name: 'Importações em conferência' })).toBeNull()
      expect(fetchMock.mock.calls.some((c) => String(c[0]).endsWith('/importacoes'))).toBe(false)
    })
  })
})
