// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, fireEvent, waitFor } from '@testing-library/react'
import { HistoricoDeSeparacoes } from './HistoricoDeSeparacoes'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import type { SeparacaoDto } from '../api/separacao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

function separacao(sobre: Partial<SeparacaoDto>): SeparacaoDto {
  return {
    id: 1, tipo: 'Separacao', materialId: 3, materialCodigo: 'PAR-M8', materialDescricao: 'Parafuso M8',
    unidadeMedida: 'UN', quantidade: 8, dataHora: '2026-10-16T09:00:00-03:00', usuarioId: 12,
    usuarioNome: 'Separador do turno', estornoDeId: null, estornavel: true,
    ...sobre,
  }
}

// O autor da sessão é o 12. A `DE_OUTRO` vem estornável do servidor (quem pede é PCP, digamos), mas
// a sessão destes testes só estorna o que é dela.
const MINHA = separacao({})
const DE_OUTRO = separacao({
  id: 2, materialId: 5, materialCodigo: 'CH-2', materialDescricao: 'Chapa 2 mm', unidadeMedida: 'KG',
  quantidade: 2.5, dataHora: '2026-10-16T10:15:00-03:00', usuarioId: 13, usuarioNome: 'Outro separador',
})
const ESTORNADA = separacao({ id: 3, quantidade: 4, dataHora: '2026-10-16T08:00:00-03:00', estornavel: false })
const ESTORNO = separacao({
  id: 4, tipo: 'Estorno', quantidade: 4, dataHora: '2026-10-16T11:30:00-03:00', estornoDeId: 3,
  estornavel: false,
})

const soOAutor = (autorId: number): boolean => autorId === 12

function montarFetch(leituras: SeparacaoDto[][], estornos: Record<string, () => Response> = {}) {
  let gets = 0
  const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url)
    if (caminho === '/api/estrutura/7/separacoes' && (init?.method ?? 'GET') === 'GET') {
      const l = leituras[Math.min(gets, leituras.length - 1)]
      gets += 1
      return Promise.resolve(respostaJson(l))
    }
    const estorno = estornos[caminho]
    if (estorno && init?.method === 'POST') return Promise.resolve(estorno())
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
  return { fetchMock, getsDoHistorico: () => gets }
}

function renderizar(podeEstornar = soOAutor, aoEstornar = vi.fn()) {
  render(<HistoricoDeSeparacoes noId={7} podeEstornar={podeEstornar} aoEstornar={aoEstornar} />)
  return { aoEstornar }
}

const linhas = () => within(screen.getByRole('list', { name: 'Separações, da mais recente à mais antiga' })).getAllByRole('listitem')

describe('HistoricoDeSeparacoes', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra as separações e estornos do mais recente ao mais antigo, com autor e data', async () => {
    // Fora de ordem de propósito: a tela não depende da ordem em que o servidor as manda.
    vi.stubGlobal('fetch', montarFetch([[MINHA, ESTORNADA, ESTORNO, DE_OUTRO]]).fetchMock)

    renderizar()

    await screen.findByText('Separação de 8 UN')
    const [primeira, segunda, terceira, quarta] = linhas()
    expect(within(primeira).getByText('Estorno de 4 UN')).toBeTruthy()
    expect(within(primeira).getByText('Desfaz a separação de 16/10/2026 08:00.')).toBeTruthy()
    expect(within(segunda).getByText('Separação de 2,5 KG')).toBeTruthy()
    expect(within(segunda).getByText('CH-2 Chapa 2 mm')).toBeTruthy()
    expect(within(segunda).getByText('Outro separador · 16/10/2026 10:15')).toBeTruthy()
    expect(within(terceira).getByText('Separação de 8 UN')).toBeTruthy()
    expect(within(terceira).getByText('Separador do turno · 16/10/2026 09:00')).toBeTruthy()
    expect(within(quarta).getByText('Separação de 4 UN')).toBeTruthy()
    // A separação desfeita leva a marca; a que segue valendo, não.
    expect(within(quarta).getByText('estornada')).toBeTruthy()
    expect(within(terceira).queryByText('estornada')).toBeNull()
  })

  it('Estornar só aparece em linha estornável e que a sessão pode estornar', async () => {
    vi.stubGlobal('fetch', montarFetch([[ESTORNO, DE_OUTRO, MINHA, ESTORNADA]]).fetchMock)

    renderizar()

    await screen.findByText('Separação de 8 UN')
    const [estorno, deOutro, minha, estornada] = linhas()
    expect(within(minha).getByRole('button', { name: 'Estornar a separação de 8 UN de PAR-M8, em 16/10/2026 09:00' })).toBeTruthy()
    // Estornável no servidor, mas de outro autor: a sessão receberia o 403.
    expect(within(deOutro).queryByRole('button', { name: /Estornar/ })).toBeNull()
    // Do autor, mas o servidor diz que não é estornável (já estornada, ou o próprio estorno).
    expect(within(estornada).queryByRole('button', { name: /Estornar/ })).toBeNull()
    expect(within(estorno).queryByRole('button', { name: /Estornar/ })).toBeNull()
  })

  it('PCP vê Estornar na separação de outro autor', async () => {
    vi.stubGlobal('fetch', montarFetch([[DE_OUTRO]]).fetchMock)

    renderizar(() => true)

    expect(await screen.findByRole('button', { name: /^Estornar a separação de 2,5 KG de CH-2/ })).toBeTruthy()
  })

  it('estornar chama a rota, recarrega e avisa aoEstornar', async () => {
    const depois = [separacao({ id: 5, tipo: 'Estorno', estornoDeId: 1, estornavel: false }), { ...MINHA, estornavel: false }]
    const { fetchMock, getsDoHistorico } = montarFetch([[MINHA], depois], {
      '/api/separacoes/1/estorno': () => respostaJson(separacao({ id: 5, tipo: 'Estorno', estornoDeId: 1, estornavel: false }), 201),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { aoEstornar } = renderizar()

    fireEvent.click(await screen.findByRole('button', { name: /^Estornar a separação de 8 UN/ }))
    // Pede confirmação antes de gravar.
    expect(fetchMock.mock.calls.some(([url]) => String(url) === '/api/separacoes/1/estorno')).toBe(false)
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(aoEstornar).toHaveBeenCalledTimes(1))
    const posts = fetchMock.mock.calls.filter(([url]) => String(url) === '/api/separacoes/1/estorno')
    expect(posts).toHaveLength(1)
    expect(posts[0][1]?.method).toBe('POST')
    expect(getsDoHistorico()).toBe(2)
    await waitFor(() => expect(screen.queryByRole('button', { name: /^Estornar a separação/ })).toBeNull())
  })

  it('um 409 JaEstornada mostra a frase e recarrega', async () => {
    const { fetchMock, getsDoHistorico } = montarFetch([[MINHA], [{ ...MINHA, estornavel: false }]], {
      '/api/separacoes/1/estorno': () => respostaJson({ erro: 'JaEstornada' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { aoEstornar } = renderizar()

    fireEvent.click(await screen.findByRole('button', { name: /^Estornar a separação de 8 UN/ }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('Esta separação já foi estornada.')).toBeTruthy()
    await waitFor(() => expect(getsDoHistorico()).toBe(2))
    // Outra pessoa estornou: os materiais do nó também mudaram.
    expect(aoEstornar).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(screen.queryByRole('button', { name: /^Estornar a separação/ })).toBeNull())
  })

  it('erro que não é conflito mostra a frase e não recarrega', async () => {
    const { fetchMock, getsDoHistorico } = montarFetch([[MINHA]], {
      '/api/separacoes/1/estorno': () => respostaJson({ erro: 'Proibido' }, 403),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { aoEstornar } = renderizar()

    fireEvent.click(await screen.findByRole('button', { name: /^Estornar a separação de 8 UN/ }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('Só quem fez o registro, o PCP ou o Administrador pode estorná-lo.')).toBeTruthy()
    expect(getsDoHistorico()).toBe(1)
    expect(aoEstornar).not.toHaveBeenCalled()
  })

  it('mostra carregando antes de o histórico chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra "Nenhuma separação registrada." quando não há nenhuma', async () => {
    vi.stubGlobal('fetch', montarFetch([[]]).fetchMock)

    renderizar()

    expect(await screen.findByText('Nenhuma separação registrada.')).toBeTruthy()
  })

  it('mostra o erro de carga', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaJson({}, 400))))

    renderizar()

    expect(await screen.findByText('Não foi possível carregar as separações.')).toBeTruthy()
    expect(screen.queryByRole('status')).toBeNull()
  })
})
