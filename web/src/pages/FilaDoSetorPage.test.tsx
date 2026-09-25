// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, act } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { FilaDoSetorPage } from './FilaDoSetorPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'
import { CHASSI, PARAFUSO, SUPORTE, DESTINO_MONTAGEM, destino, fila, no } from '../testes/execucao'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers(); localStorage.clear() })

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 12, nomeUsuario: 'op', nomeCompleto: 'Operador', perfil: 'Operador' } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const PECA_B = no({ id: 9, descricao: 'Base', codigoDoComponente: 'BA-01', paiId: null, paiDescricao: null })

const FILA_CHEIA = fila({
  aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }],
  emTrabalho: [{ no: PECA_B, ordem: 1, quantidade: 2.5 }],
  aguardandoColeta: [{ no: SUPORTE, ordem: 1, quantidade: 4, destino: destino() }],
  aguardandoMontagem: [{
    pai: CHASSI, faltaMontar: 10, daParaMontar: 2,
    filhos: [
      { no: SUPORTE, quantidadePorPai: 4, presente: 9, necessarioParaProxima: 12, faltaParaProxima: 3 },
      { no: PARAFUSO, quantidadePorPai: 1, presente: 5, necessarioParaProxima: 3, faltaParaProxima: 0 },
    ],
  }],
  sobra: [
    { no: SUPORTE, origem: 'UltimoPasso', ordem: 2, quantidade: 5, emMaisDeUmSetor: false },
    { no: PARAFUSO, origem: 'Montagem', ordem: null, quantidade: 1, emMaisDeUmSetor: true },
  ],
})

function renderizar(caminho = '/fila/1') {
  return render(
    <MemoryRouter initialEntries={[caminho]}>
      <Routes>
        <Route path="/fila/:setorId" element={<FilaDoSetorPage />} />
        <Route path="/fila" element={<p>escolha de setor</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('FilaDoSetorPage — leitura', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra carregando antes da fila chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('o título nomeia o Setor da fila', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()

    expect(await screen.findByRole('heading', { level: 1, name: 'Fila — Corte' })).toBeTruthy()
  })

  it('a iniciar e em trabalho mostram nó, caminho, quantidade e passo', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()

    const aIniciar = await screen.findByRole('list', { name: 'A iniciar aqui' })
    expect(within(aIniciar).getByText('SUP-01 — Suporte')).toBeTruthy()
    expect(within(aIniciar).getByText('PED-2026-01 › AG-01 › Chassi')).toBeTruthy()
    expect(within(aIniciar).getByText('10 a iniciar · passo 1')).toBeTruthy()
    const emTrabalho = screen.getByRole('list', { name: 'Em trabalho' })
    expect(within(emTrabalho).getByText('2,5 em trabalho · passo 1')).toBeTruthy()
    // Peça: o caminho para no Agrupamento.
    expect(within(emTrabalho).getByText('PED-2026-01 › AG-01')).toBeTruthy()
  })

  it('aguardando coleta mostra o destino calculado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        aguardandoColeta: [
          { no: SUPORTE, ordem: 1, quantidade: 4, destino: destino() },
          { no: SUPORTE, ordem: 3, quantidade: 2, destino: DESTINO_MONTAGEM },
        ],
      })),
    }))

    renderizar()

    const coleta = await screen.findByRole('list', { name: 'Aguardando coleta' })
    expect(within(coleta).getByText('Destino: Dobra (passo 2)')).toBeTruthy()
    expect(within(coleta).getByText('Destino: Montagem de Chassi (sugestão: Solda)')).toBeTruthy()
  })

  it('aguardando montagem diz quanto dá para montar e o que falta para a próxima', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()

    const montagem = await screen.findByRole('list', { name: 'Aguardando montagem' })
    expect(within(montagem).getByText('Dá para montar 2; falta montar 10.')).toBeTruthy()
    const filhos = within(montagem).getByRole('list', { name: 'Filhos de Chassi' })
    expect(within(filhos).getByText('SUP-01 — Suporte: 9 aqui, 4 por unidade — falta 3 de 12 para a próxima')).toBeTruthy()
    // Filho que já basta para a próxima unidade não mostra "falta 0".
    expect(within(filhos).getByText('Parafuso: 5 aqui, 1 por unidade')).toBeTruthy()
  })

  it('sem próxima unidade a montar, não há "falta" nenhum', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        aguardandoMontagem: [{
          pai: CHASSI, faltaMontar: 2, daParaMontar: 2,
          filhos: [{ no: SUPORTE, quantidadePorPai: 4, presente: 8, necessarioParaProxima: null, faltaParaProxima: null }],
        }],
      })),
    }))

    renderizar()

    expect(await screen.findByText('SUP-01 — Suporte: 8 aqui, 4 por unidade')).toBeTruthy()
  })

  it('a sobra é só informada, e diz quando não dá para saber em que Setor está', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()

    const sobra = await screen.findByRole('list', { name: 'Sobra' })
    expect(within(sobra).getByText('5 a mais no passo 2: o pai já tem o que precisa.')).toBeTruthy()
    expect(within(sobra).getByText('1 a mais aguardando montagem do que o pai precisa.')).toBeTruthy()
    expect(within(sobra).getByText(/não dá para saber em qual está a unidade a mais/)).toBeTruthy()
    // O descarte é da Fase 5: nenhuma ação na seção.
    expect(within(sobra).queryByRole('button')).toBeNull()
  })

  it('seção sem nada não aparece', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }] })),
    }))

    renderizar()

    await screen.findByRole('list', { name: 'A iniciar aqui' })
    expect(screen.queryByRole('list', { name: 'Em trabalho' })).toBeNull()
    expect(screen.queryByRole('list', { name: 'Sobra' })).toBeNull()
  })

  it('fila vazia diz "nada neste Setor agora", não erro', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(fila()) }))

    renderizar()

    expect(await screen.findByText('Nada neste Setor agora')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('falha na primeira carga: banner, sem seções e sem estado vazio', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson({}, 500) }))

    renderizar()

    expect((await screen.findByRole('alert')).textContent)
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(screen.queryByText('Nada neste Setor agora')).toBeNull()
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('lembra o Setor cuja fila carregou', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()
    await screen.findByRole('heading', { level: 1, name: 'Fila — Corte' })

    expect(localStorage.getItem('rastru.fila.setorId')).toBe('1')
  })

  it('não lembra um Setor que deu 404', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/99/fila': () => respostaJson({ title: 'Not Found' }, 404) }))

    renderizar('/fila/99')

    expect((await screen.findByRole('alert')).textContent).toBe('Este registro não existe mais.')
    expect(localStorage.getItem('rastru.fila.setorId')).toBeNull()
  })

  it('Id que não é número: banner, sem buscar nada', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/fila/abc')

    expect(screen.getByRole('alert').textContent).toBe('Este Setor não existe.')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('"Trocar de Setor" leva à escolha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()
    await screen.findByRole('heading', { level: 1, name: 'Fila — Corte' })
    act(() => { screen.getByRole('link', { name: 'Trocar de Setor' }).click() })

    expect(screen.getByText('escolha de setor')).toBeTruthy()
  })

  it('atualiza sozinha a cada 30 s', async () => {
    vi.useFakeTimers()
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => {
        chamadas += 1
        return respostaJson(chamadas === 1
          ? fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }] })
          : fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 6 }] }))
      },
    }))

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByText('10 a iniciar · passo 1')).toBeTruthy()

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.getByText('6 a iniciar · passo 1')).toBeTruthy()
  })

  it('atualização que falha mantém a fila na tela, com o aviso', async () => {
    vi.useFakeTimers()
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson(fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }] }))
        return Promise.reject(new TypeError('Failed to fetch'))
      },
    }))

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.getByRole('alert').textContent).toBe('Sem conexão com o servidor. Verifique a rede e tente de novo.')
    expect(screen.getByText('10 a iniciar · passo 1')).toBeTruthy()
  })
})
