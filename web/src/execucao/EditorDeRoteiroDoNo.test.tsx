// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, fireEvent, waitFor } from '@testing-library/react'
import { EditorDeRoteiroDoNo } from './EditorDeRoteiroDoNo'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import type { RoteiroDoNoDto } from '../api/execucao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const SETORES = [
  { id: 1, nome: 'Corte', ativo: true }, { id: 3, nome: 'Dobra', ativo: true }, { id: 4, nome: 'Solda', ativo: true },
]

/** Corte (alcançado) → Dobra → Solda, na ordem 1-2-3; chega FORA de ordem de propósito. */
const ROTEIRO: RoteiroDoNoDto = {
  estruturaItemId: 7,
  passos: [
    { setorId: 4, nome: 'Solda', ordem: 3, alcancado: false },
    { setorId: 1, nome: 'Corte', ordem: 1, alcancado: true },
    { setorId: 3, nome: 'Dobra', ordem: 2, alcancado: false },
  ],
}

function montarFetch(roteiros: RoteiroDoNoDto[], put: () => Response = () => respostaJson(ROTEIRO)) {
  let gets = 0
  const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (caminho === '/api/setores') return Promise.resolve(respostaJson(SETORES))
    if (caminho === '/api/estrutura/7/roteiro') {
      if (init?.method === 'PUT') return Promise.resolve(put())
      const r = roteiros[Math.min(gets, roteiros.length - 1)]
      gets += 1
      return Promise.resolve(respostaJson(r))
    }
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
  return { fetchMock, getsDoRoteiro: () => gets }
}

function corpoDoPut(fetchMock: ReturnType<typeof vi.fn>): unknown {
  const chamada = fetchMock.mock.calls.find((c) => (c[1] as RequestInit | undefined)?.method === 'PUT')
  expect(chamada).toBeTruthy()
  return JSON.parse((chamada![1] as RequestInit).body as string)
}

const passos = () => within(screen.getByRole('list', { name: 'Passos do roteiro' })).getAllByRole('listitem')
/** O texto do passo sem o do botão "Remover" que vive na mesma linha. */
const textos = () => passos().map((p) => p.firstElementChild!.textContent)

describe('EditorDeRoteiroDoNo', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra carregando antes do roteiro chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    render(<EditorDeRoteiroDoNo noId={7} podeEditar={false} aoSalvar={vi.fn()} />)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra os passos em ordem, com o alcançado marcado', async () => {
    vi.stubGlobal('fetch', montarFetch([ROTEIRO]).fetchMock)

    render(<EditorDeRoteiroDoNo noId={7} podeEditar={false} aoSalvar={vi.fn()} />)

    await screen.findByRole('list', { name: 'Passos do roteiro' })
    expect(textos()).toEqual(['1. Cortealcançado', '2. Dobra', '3. Solda'])
  })

  it('quem não é PCP só lê: sem remover, adicionar nem salvar, e sem buscar Setores', async () => {
    const { fetchMock } = montarFetch([ROTEIRO])
    vi.stubGlobal('fetch', fetchMock)

    render(<EditorDeRoteiroDoNo noId={7} podeEditar={false} aoSalvar={vi.fn()} />)
    await screen.findByRole('list', { name: 'Passos do roteiro' })

    expect(screen.queryByRole('button')).toBeNull()
    expect(fetchMock.mock.calls.some((c) => String(c[0]).startsWith('/api/setores'))).toBe(false)
  })

  it('passo alcançado não se remove; os outros, sim', async () => {
    vi.stubGlobal('fetch', montarFetch([ROTEIRO]).fetchMock)

    render(<EditorDeRoteiroDoNo noId={7} podeEditar aoSalvar={vi.fn()} />)
    await screen.findByRole('list', { name: 'Passos do roteiro' })

    expect(screen.queryByRole('button', { name: 'Remover o passo 1 (Corte)' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Remover o passo 2 (Dobra)' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Remover o passo 3 (Solda)' })).toBeTruthy()
  })

  it('o PCP remove, acrescenta no fim — inclusive um Setor repetido — e salva a lista inteira', async () => {
    const aoSalvar = vi.fn()
    const { fetchMock } = montarFetch([ROTEIRO], () => respostaJson({
      estruturaItemId: 7,
      passos: [
        { setorId: 1, nome: 'Corte', ordem: 1, alcancado: true },
        { setorId: 4, nome: 'Solda', ordem: 2, alcancado: false },
        { setorId: 1, nome: 'Corte', ordem: 3, alcancado: false },
      ],
    }))
    vi.stubGlobal('fetch', fetchMock)

    render(<EditorDeRoteiroDoNo noId={7} podeEditar aoSalvar={aoSalvar} />)
    await screen.findByRole('list', { name: 'Passos do roteiro' })
    expect(screen.getByRole('button', { name: 'Salvar roteiro' })).toHaveProperty('disabled', true)
    fireEvent.click(screen.getByRole('button', { name: 'Remover o passo 2 (Dobra)' }))
    // Setores ativos só (regra de `RoteiroInvalido`): o seletor nasce da lista de ativos.
    await waitFor(() => expect(within(screen.getByLabelText('Setor')).getAllByRole('option')).toHaveLength(4))
    fireEvent.change(screen.getByLabelText('Setor'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar passo' }))
    fireEvent.click(screen.getByRole('button', { name: 'Salvar roteiro' }))

    await waitFor(() => expect(aoSalvar).toHaveBeenCalledTimes(1))
    expect(corpoDoPut(fetchMock)).toEqual({ passos: [1, 4, 1] })
    expect(textos()).toEqual(['1. Cortealcançado', '2. Solda', '3. Corte'])
    expect(screen.getByRole('button', { name: 'Salvar roteiro' })).toHaveProperty('disabled', true)
    expect(String(fetchMock.mock.calls.find((c) => String(c[0]).startsWith('/api/setores'))![0]))
      .toBe('/api/setores?incluirInativos=false')
  })

  it('409 mostra a frase do servidor e recarrega o roteiro, e a tela junto', async () => {
    const aoSalvar = vi.fn()
    const { fetchMock, getsDoRoteiro } = montarFetch([ROTEIRO], () => respostaJson(
      { erro: 'PassoJaAlcancado', mensagem: 'O passo 2 (Dobra) já foi alcançado.' }, 409))
    vi.stubGlobal('fetch', fetchMock)

    render(<EditorDeRoteiroDoNo noId={7} podeEditar aoSalvar={aoSalvar} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Remover o passo 2 (Dobra)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Salvar roteiro' }))

    expect(await screen.findByText('O passo 2 (Dobra) já foi alcançado.')).toBeTruthy()
    await waitFor(() => expect(getsDoRoteiro()).toBe(2))
    // Recarregado: o passo removido volta, porque o servidor não aceitou.
    await waitFor(() => expect(passos()).toHaveLength(3))
    // Fix pass (review Important 3): 409 é escrita obsoleta (spec §8.3) — não só o Roteiro
    // recarrega, a árvore e as posições da tela também, pelo MESMO `aoSalvar` do caminho de
    // sucesso (o irmão `HistoricoDoNo` já chama `aoEstornar` no 409 dele).
    expect(aoSalvar).toHaveBeenCalledTimes(1)
  })

  it('recusa que não é 409 mostra a frase, NÃO recarrega e mantém a edição', async () => {
    // Fix pass (review Important 2): mutar `if (ehConflito(e))` para `if (true)` fazia os 8 testes
    // originais passarem do mesmo jeito, porque nenhum media isto.
    const aoSalvar = vi.fn()
    const { fetchMock, getsDoRoteiro } = montarFetch([ROTEIRO], () => respostaJson(
      { erro: 'RoteiroInvalido', mensagem: 'Setor inativo não pode entrar no Roteiro.' }, 400))
    vi.stubGlobal('fetch', fetchMock)

    render(<EditorDeRoteiroDoNo noId={7} podeEditar aoSalvar={aoSalvar} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Remover o passo 2 (Dobra)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Salvar roteiro' }))

    expect(await screen.findByText('Setor inativo não pode entrar no Roteiro.')).toBeTruthy()
    await waitFor(() => expect(getsDoRoteiro()).toBe(1))
    // Não recarregado: a remoção continua na tela, ao contrário do 409 acima.
    expect(textos()).toEqual(['1. Cortealcançado', '2. Solda'])
    expect(aoSalvar).not.toHaveBeenCalled()
  })

  it('toque duplo em "Salvar roteiro" manda um PUT só', async () => {
    // Fix pass (review Important 1): mesmo padrão de `toque duplo envia uma vez só`
    // (`FormularioDeQuantidade.test.tsx`) — dois toques no botão persistente, sem esperar o
    // primeiro responder.
    let resolver: (r: Response) => void = () => {}
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
      const caminho = String(url).split('?')[0]
      if (caminho === '/api/setores') return Promise.resolve(respostaJson(SETORES))
      if (caminho === '/api/estrutura/7/roteiro') {
        if (init?.method === 'PUT') return new Promise<Response>((r) => { resolver = r })
        return Promise.resolve(respostaJson(ROTEIRO))
      }
      return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<EditorDeRoteiroDoNo noId={7} podeEditar aoSalvar={vi.fn()} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Remover o passo 2 (Dobra)' }))
    // Referência ao MESMO nó (React reaproveita o elemento ao redesenhar, não o recria) — assim a
    // segunda checagem de `disabled` vale mesmo depois de o rótulo virar "Salvando…".
    const salvar = screen.getByRole('button', { name: 'Salvar roteiro' })
    fireEvent.click(salvar)
    fireEvent.click(salvar)

    expect(salvar).toHaveProperty('disabled', true)
    // "Remover"/"Adicionar passo" também ficam presos enquanto salva — uma edição no meio do
    // salvamento não é silenciosamente sobrescrita pela lista que o servidor devolver.
    expect(screen.getByRole('button', { name: 'Remover o passo 2 (Solda)' })).toHaveProperty('disabled', true)
    resolver(respostaJson(ROTEIRO))
    await waitFor(() => expect(fetchMock.mock.calls.filter((c) => c[1]?.method === 'PUT')).toHaveLength(1))
  })

  it('falha ao carregar os Setores avisa o PCP, sem derrubar o roteiro', async () => {
    // Fix pass (review Important 4): antes, `.catch(() => {})` engolia o erro — o PCP via um
    // seletor vazio, sem explicação nenhuma.
    const fetchMock = vi.fn((url: string | URL) => {
      const caminho = String(url).split('?')[0]
      if (caminho === '/api/setores') return Promise.resolve(respostaJson({}, 500))
      if (caminho === '/api/estrutura/7/roteiro') return Promise.resolve(respostaJson(ROTEIRO))
      return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<EditorDeRoteiroDoNo noId={7} podeEditar aoSalvar={vi.fn()} />)
    await screen.findByRole('list', { name: 'Passos do roteiro' })

    // 500 sem corpo cai no ramo genérico de `mensagemDeErro` (`e.status >= 500`), que ganha do
    // `fallback` desta tela — a mesma frase que `HistoricoDoNo` já usa para o mesmo status.
    expect(await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')).toBeTruthy()
  })

  it('sem Roteiro, o PCP lê o que fazer e quem não é PCP lê a quem pedir', async () => {
    vi.stubGlobal('fetch', montarFetch([{ estruturaItemId: 7, passos: [] }]).fetchMock)

    const { unmount } = render(<EditorDeRoteiroDoNo noId={7} podeEditar aoSalvar={vi.fn()} />)
    expect(await screen.findByText(/Adicione os Setores na ordem em que ele passa por eles/)).toBeTruthy()
    unmount()

    render(<EditorDeRoteiroDoNo noId={7} podeEditar={false} aoSalvar={vi.fn()} />)
    expect(await screen.findByText(/Quem cadastra o Roteiro é o PCP/)).toBeTruthy()
  })

  it('falha ao carregar vira banner', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({}, 500)))

    render(<EditorDeRoteiroDoNo noId={7} podeEditar={false} aoSalvar={vi.fn()} />)

    expect((await screen.findByRole('alert')).textContent)
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
  })
})
