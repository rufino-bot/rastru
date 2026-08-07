// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
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
    // `erro` do corpo — sem este teste, mutar PedidoDetalhePage.tsx:16 matava 0.
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

  it('cadastra o agrupamento, limpa o formulário e recarrega a lista quando o salvar dá certo', async () => {
    // A3.1: nenhum dos testes acima exercita o formulário — só a exclusão. POST e GET caem na
    // MESMA rota (`/pedidos/7/agrupamentos`), então o contador precisa distinguir as TRÊS
    // chamadas na ordem em que acontecem: 1) GET no mount (lista vazia), 2) POST do submit
    // (sucesso, sem `erro`), 3) GET do `carregar(pedidoId)` pós-salvar (lista com o novo item).
    // 'AGR-01' aparecer só depois do submit é o que torna o recarregamento observável (padrão
    // M2/M6, como no teste de exclusão acima).
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return respostaJson(AGRUPAMENTO)
        return respostaJson([AGRUPAMENTO])
      },
    }))

    renderizarDetalhe()
    await screen.findByText('PED-001')
    expect(screen.queryByText('AGR-01')).toBeNull()

    fireEvent.change(screen.getByPlaceholderText('Código do agrupamento'), { target: { value: 'AGR-01' } })
    fireEvent.click(screen.getByText('Adicionar'))

    // Prova o `await carregar(pedidoId)`: se ele for apagado de PedidoDetalhePage.tsx:64, a
    // terceira chamada nunca acontece, a lista fica vazia para sempre e este findByText estoura
    // por timeout, não por falso-positivo.
    expect(await screen.findByText('AGR-01')).toBeTruthy()
    // Prova o `setForm(FORMULARIO_VAZIO)`: se ele for apagado da linha 63, o campo continuaria
    // com 'AGR-01' digitado.
    expect((screen.getByPlaceholderText('Código do agrupamento') as HTMLInputElement).value).toBe('')
  })

  it('mostra o erro de duplicidade e mantém o formulário preenchido quando o salvar é recusado', async () => {
    // A3.1, ramo de conflito: o `return` de PedidoDetalhePage.tsx:61 acontece ANTES do
    // `setForm(FORMULARIO_VAZIO)` — só a mensagem de erro não provaria isso (sobreviveria à
    // mutação "apagar o return"), por isso a asserção do valor do campo é obrigatória aqui.
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        return respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: false, idExistente: 1 }, 409)
      },
    }))

    renderizarDetalhe()
    await screen.findByText('PED-001')

    fireEvent.change(screen.getByPlaceholderText('Código do agrupamento'), { target: { value: 'AGR-01' } })
    fireEvent.click(screen.getByText('Adicionar'))

    expect(
      await screen.findByText('Já existe um agrupamento com este código neste pedido.'),
    ).toBeTruthy()
    expect((screen.getByPlaceholderText('Código do agrupamento') as HTMLInputElement).value).toBe('AGR-01')
  })
})
