// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { PedidoDetalhePage } from './PedidoDetalhePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// O perfil da sessão passa a governar o que a tela mostra (Task 10). Default `'PCP'`: pode
// escrever agrupamentos (`podeEscrever` em `web/src/auth/permissoes.ts` libera `PCP` e
// `Administrador`), o que preserva o comportamento dos testes existentes (formulário e botão de
// excluir precisam estar visíveis para eles).
let perfil = 'PCP'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

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
    perfil = 'PCP'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // I1 (achado da review de branch da 1D): nenhuma das seis telas que buscam dados tinha teste
  // provando o indicador "Carregando…" — só vazio e erro tinham. Molde de
  // `LoginPage.test.tsx` ("desabilita o botão enquanto o login está em voo"): fetch que nunca
  // resolve, e asserção SÍNCRONA (sem `await`/`findBy*`) de que o indicador está na tela antes de
  // qualquer resposta chegar.
  it('mostra o indicador de carregando antes da resposta da API chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizarDetalhe()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

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
    // Padrão de PedidosPage.test.tsx:73-79: primeira listagem devolve o agrupamento, as
    // seguintes devolvem [] — é o que torna o recarregamento pós-exclusão observável (M2/M6).
    let listagens = 0
    const fetchMock = fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => {
        listagens += 1
        return respostaJson(listagens === 1 ? [AGRUPAMENTO] : [])
      },
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

    // A segunda listagem devolve [] (lista diferente da primeira): 'AGR-01' SUMIR da tela só
    // acontece se `excluir()` recarregar de fato depois do 204. Se apagar o `await
    // carregar(pedidoId)` de PedidoDetalhePage.tsx, a lista antiga (com AGR-01) fica na tela para
    // sempre e esta espera nunca resolve — timeout, não falso-positivo.
    await waitFor(() => expect(screen.queryByText('AGR-01')).toBeNull())
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

  it('explica o motivo quando a exclusão é recusada porque o agrupamento não existe mais', async () => {
    // Segundo desfecho do mapa (M1): prova que a mensagem varia por código, não é constante —
    // com um único caso fixado, trocar o valor de `NaoEncontrado` em MOTIVO_DA_RECUSA mata 0.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'NaoEncontrado' }, 404),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    expect(await screen.findByText('Este agrupamento já não existe mais.')).toBeTruthy()
  })

  it('explica o motivo quando a exclusão é recusada porque o pedido não está mais aberto', async () => {
    // Terceiro desfecho do mapa (A2 do segundo fix pass): segundo o comentário de
    // cadastros.ts:192-194, PedidoNaoAberto é o código que MAIS chega na prática (a ordem das
    // guardas no backend é existe -> Pedido Aberto -> vazio). O 409 é discriminado pelo campo
    // `erro` do corpo — sem este teste, mutar PedidoDetalhePage.tsx:25 matava 0.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'PedidoNaoAberto' }, 409),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    expect(
      await screen.findByText('O pedido não está mais aberto: não dá para excluir agrupamentos dele.'),
    ).toBeTruthy()
  })

  // M5 do Step 4 (fase1d-task-10-brief.md): mover o `<BannerDeErro>` para depois do bloco
  // `carregando ? …` sobrevive a TODOS os testes acima — RTL não olha ordem de DOM por padrão, só
  // se o texto existe em algum lugar. Mas é exatamente essa posição relativa que causou o "erro
  // que pisca" da review da Task 11 no desenho antigo (o early return escondia o banner atrás do
  // "Carregando…"). Sem early return isso não pode mais acontecer por ESCONDER o banner, mas a
  // ORDEM ainda importa para quem usa a tela: o aviso de recusa precisa estar ACIMA da lista, não
  // abaixo dela, onde exigiria rolar para ver. Esta asserção prende a ordem no DOM.
  it('mostra o banner de erro antes da lista de agrupamentos, não depois', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'AgrupamentoNaoVazio' }, 409),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    const banner = await screen.findByRole('alert')
    const lista = screen.getByRole('list', { name: 'Agrupamentos' })
    expect(banner.compareDocumentPosition(lista) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('cadastra o agrupamento, limpa o formulário e recarrega a lista quando o salvar dá certo', async () => {
    // A3.1: nenhum dos testes acima exercita o formulário — só a exclusão. POST e GET caem na
    // MESMA rota (`/pedidos/7/agrupamentos`), então o contador precisa distinguir as TRÊS
    // chamadas na ordem em que acontecem: 1) GET no mount (lista vazia), 2) POST do submit
    // (sucesso, sem `erro`), 3) GET do `carregar(pedidoId)` pós-salvar (lista com o novo item).
    // 'AGR-01' aparecer só depois do submit é o que torna o recarregamento observável (padrão
    // M2/M6, como no teste de exclusão acima).
    let chamadas = 0
    const fetchMock = fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return respostaJson(AGRUPAMENTO, 201)
        return respostaJson([AGRUPAMENTO])
      },
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('PED-001')
    expect(screen.queryByText('AGR-01')).toBeNull()

    fireEvent.change(screen.getByLabelText('Código do agrupamento'), { target: { value: 'AGR-01' } })
    fireEvent.click(screen.getByText('Adicionar'))

    // Prova o `await carregar(pedidoId)`: se ele for apagado de PedidoDetalhePage.tsx:73, a
    // terceira chamada nunca acontece, a lista fica vazia para sempre e este findByText estoura
    // por timeout, não por falso-positivo.
    expect(await screen.findByText('AGR-01')).toBeTruthy()
    // Prova o `setForm(FORMULARIO_VAZIO)`: se ele for apagado da linha 72, o campo continuaria
    // com 'AGR-01' digitado.
    expect((screen.getByLabelText('Código do agrupamento') as HTMLInputElement).value).toBe('')

    // B2: fetchPorRota (testes/api.ts:29-36) casa só por caminho — ignora método e corpo. Sem
    // esta asserção, trocar o POST de criarAgrupamento por GET, ou deixar de enviar o `form` no
    // corpo, sobrevive ao teste. `init` é o segundo argumento passado ao fetch global, montado
    // por fetchComToken (client.ts:43-47): `{ ...init, headers: Headers, credentials: 'include' }`.
    const chamadaPost = fetchMock.mock.calls.find((c) => (c[1] as RequestInit | undefined)?.method === 'POST')
    expect(chamadaPost).toBeTruthy()
    expect(JSON.parse((chamadaPost![1] as RequestInit).body as string)).toEqual({ codigo: 'AGR-01', tipo: 'Kit' })
  })

  // M7 do Step 4: remover `setEnviando(false)` do `finally` de `salvar` sobrevive ao teste acima
  // (ele só olha o efeito COLATERAL do submit — a lista recarregada —, nunca o estado do próprio
  // botão). Sob a mutação, `enviando` fica travado em `true` para sempre e o botão "Adicionar"
  // nunca reabilita, mesmo depois do cadastro concluir com sucesso.
  it('reabilita o botão "Adicionar" depois que o cadastro conclui', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))

    renderizarDetalhe()
    await screen.findByText('PED-001')

    fireEvent.change(screen.getByLabelText('Código do agrupamento'), { target: { value: 'AGR-01' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await waitFor(() => {
      expect((screen.getByText('Adicionar') as HTMLButtonElement).disabled).toBe(false)
    })
  })

  it('mostra o erro de duplicidade e mantém o formulário preenchido quando o salvar é recusado', async () => {
    // A3.1, ramo de conflito: o `return` de PedidoDetalhePage.tsx:70 acontece ANTES do
    // `setForm(FORMULARIO_VAZIO)` — sem ele, o fluxo continua para `setForm` e `carregar()`. A
    // mensagem de erro sozinha SOBREVIVE a essa mutação (nada a apaga depois dela), por isso é a
    // asserção do valor do campo que pega essa mutação. Para a asserção ser alcançada, o 409 vale
    // só na SEGUNDA chamada da rota (o POST); da terceira chamada em diante ela devolve lista
    // vazia com sucesso — senão a terceira chamada (a de `carregar()`, que só acontece sob a
    // mutação) bateria de novo no 409, `listarAgrupamentos` lançaria (cadastros.ts:174) e o catch
    // de PedidoDetalhePage.tsx:74 sobrescreveria a mensagem de conflito por 'Não foi possível
    // carregar o pedido.' antes da asserção do campo rodar.
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => {
        chamadas += 1
        if (chamadas === 2) return respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: false, idExistente: 1 }, 409)
        return respostaJson([])
      },
    }))

    renderizarDetalhe()
    await screen.findByText('PED-001')

    fireEvent.change(screen.getByLabelText('Código do agrupamento'), { target: { value: 'AGR-01' } })
    fireEvent.click(screen.getByText('Adicionar'))

    expect(
      await screen.findByText('Já existe um agrupamento com este código neste pedido.'),
    ).toBeTruthy()
    expect((screen.getByLabelText('Código do agrupamento') as HTMLInputElement).value).toBe('AGR-01')
  })

  // Task 10: o formulário e o botão de excluir passam a existir só para quem tem
  // `usePodeEscrever('agrupamentos')` — a lista continua visível para todo mundo, porque
  // `agrupamentos` é recurso de LEITURA aberta e ESCRITA restrita (PCP/Administrador).
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

  // Achado Important da review da Task 10 (plan-mandated): o Step 2 do brief não trazia o
  // `erro === null &&` que as outras quatro telas retrofitadas têm. Sem ele, `agrupamentos` fica
  // `[]` no catch de `carregar` (nunca é preenchido), e `.length === 0` sozinho também é verdade
  // numa falha de rede — "Nenhum agrupamento neste pedido" apareceria JUNTO do banner de erro,
  // convidando a criar o primeiro a partir de uma falha de conexão. É a mesma forma do Critical
  // que o fix pass da Task 8 pagou (achado C1), e nenhum dos testes anteriores exercitava o
  // caminho de falha de leitura.
  it('não mostra o estado vazio quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => Promise.reject(new Error('rede caiu')),
    }))

    renderizarDetalhe()

    await screen.findByText('Não foi possível carregar o pedido.')
    expect(screen.queryByText('Nenhum agrupamento neste pedido')).toBeNull()
  })
})
