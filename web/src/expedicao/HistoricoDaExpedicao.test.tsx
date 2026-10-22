// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, fireEvent, waitFor } from '@testing-library/react'
import { HistoricoDaExpedicao } from './HistoricoDaExpedicao'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import { registro } from '../testes/expedicao'
import type { RegistroDaExpedicaoDto } from '../api/expedicao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const ROTA = '/api/estrutura/30/expedicao'

// Um de cada tipo, fora de ordem de propósito: a tela não depende da ordem em que o servidor os manda.
const AFERICAO = registro({ tipo: 'Afericao', id: 1, resultado: 'NaoConforme', dataHora: '2026-10-20T08:00:00-03:00' })
const CONTAGEM = registro({
  tipo: 'Contagem', id: 2, conformes: 4, naoConformes: 2, dataHora: '2026-10-20T09:00:00-03:00',
})
const CONSERTO = registro({
  tipo: 'Conserto', id: 90, quantidade: 1, setorNome: 'Solda', dataHora: '2026-10-20T10:00:00-03:00',
})
const PEDIDO = registro({
  tipo: 'PedidoDeLiberacao', id: 5, quantidade: 1, texto: 'Sobra de dobra', dataHora: '2026-10-20T11:00:00-03:00',
})
const RECUSA = registro({
  tipo: 'RespostaDeLiberacao', id: 6, quantidade: 0, texto: null, usuarioId: 20, usuarioNome: 'Gestora',
  dataHora: '2026-10-20T12:00:00-03:00', estornavel: false,
})
// A remessa vem estornável de propósito: mesmo assim, o histórico não a estorna (C20).
const REMESSA = registro({
  tipo: 'Remessa', id: 3, quantidade: 4, dataHora: '2026-10-20T13:00:00-03:00', estornavel: true,
})
const TODOS = [CONTAGEM, REMESSA, AFERICAO, RECUSA, CONSERTO, PEDIDO]

const soOAutor = (autorId: number) => autorId === 12

function montarFetch(leituras: RegistroDaExpedicaoDto[][], estornos: Record<string, () => Response> = {}) {
  let gets = 0
  const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url)
    if (caminho === ROTA && (init?.method ?? 'GET') === 'GET') {
      const l = leituras[Math.min(gets, leituras.length - 1)]
      gets += 1
      return Promise.resolve(respostaJson(l))
    }
    const estorno = estornos[caminho]
    if (estorno && init?.method === 'POST') return Promise.resolve(estorno())
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
  return { fetchMock, gets: () => gets }
}

function renderizar(podeEstornar = soOAutor, versao = 0) {
  const aoEstornar = vi.fn()
  const r = render(
    <HistoricoDaExpedicao pecaId={30} podeEstornar={podeEstornar} aoEstornar={aoEstornar} versao={versao} />,
  )
  return { aoEstornar, rerender: (v: number) => r.rerender(
    <HistoricoDaExpedicao pecaId={30} podeEstornar={podeEstornar} aoEstornar={aoEstornar} versao={v} />,
  ) }
}

const abrir = () => fireEvent.click(screen.getByText(/^Histórico/))
const linhas = () =>
  within(screen.getByRole('list', { name: 'Histórico da expedição, do mais recente ao mais antigo' })).getAllByRole('listitem')

describe('HistoricoDaExpedicao', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('nasce recolhido e só carrega ao abrir, e o resumo ganha a contagem', async () => {
    const { fetchMock, gets } = montarFetch([TODOS])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()

    const resumo = screen.getByText('Histórico')
    expect(resumo.tagName).toBe('SUMMARY')
    expect(resumo.closest('details')!.open).toBe(false)
    expect(gets()).toBe(0)

    abrir()

    expect(await screen.findByText('Histórico (6)')).toBeTruthy()
    expect(gets()).toBe(1)
  })

  it('mostra o carregando enquanto a leitura não volta', async () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))
    renderizar()

    abrir()

    expect(await screen.findByText('Carregando…')).toBeTruthy()
  })

  it('vazio diz que não há registro', async () => {
    vi.stubGlobal('fetch', montarFetch([[]]).fetchMock)
    renderizar()

    abrir()

    expect(await screen.findByText('Nenhum registro na expedição.')).toBeTruthy()
    expect(screen.getByText('Histórico (0)')).toBeTruthy()
  })

  it('o erro de carga aparece dentro do histórico', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaJson({}, 400))))
    renderizar()

    abrir()

    const alerta = await screen.findByRole('alert')
    expect(alerta.textContent).toContain('Não foi possível carregar o histórico.')
    expect(alerta.closest('details')).toBe(screen.getByText('Histórico').closest('details'))
  })

  it('os rótulos de cada tipo, do mais recente ao mais antigo', async () => {
    vi.stubGlobal('fetch', montarFetch([TODOS]).fetchMock)
    renderizar()

    abrir()

    await screen.findByText('Remessa de 4')
    const textos = linhas().map((l) => l.textContent)
    expect(textos[0]).toContain('Remessa de 4')
    expect(textos[0]).toContain('Estorna-se em Remessas recentes.')
    expect(textos[1]).toContain('Liberação recusada')
    expect(textos[1]).toContain('Gestora · 20/10/2026 12:00')
    expect(textos[2]).toContain('Pedido de liberação de 1')
    expect(textos[2]).toContain('Justificativa: Sobra de dobra')
    expect(textos[3]).toContain('Conserto de 1 em Solda')
    expect(textos[4]).toContain('Contagem: 4 conformes e 2 não conformes')
    expect(textos[5]).toContain('Aferição não conforme')
    expect(textos[5]).toContain('Qualidade do turno · 20/10/2026 08:00')
  })

  it('a resposta que libera, a aferição conforme, a contagem de um lado só e a observação', async () => {
    vi.stubGlobal('fetch', montarFetch([[
      registro({ tipo: 'RespostaDeLiberacao', id: 7, quantidade: 2, texto: 'Pode ir' }),
      registro({ tipo: 'Contagem', id: 8, conformes: 6, naoConformes: 0 }),
      registro({ tipo: 'Afericao', id: 9, resultado: 'Conforme' }),
    ]]).fetchMock)
    renderizar()

    abrir()

    expect(await screen.findByText('Liberação de 2')).toBeTruthy()
    expect(screen.getByText('Observação: Pode ir')).toBeTruthy()
    expect(screen.getByText('Contagem: 6 conformes')).toBeTruthy()
    expect(screen.getByText('Aferição conforme')).toBeTruthy()
  })

  it('o registro estornado leva a marca, e não oferece estorno', async () => {
    vi.stubGlobal('fetch', montarFetch([[{ ...AFERICAO, estornada: true, estornavel: false }]]).fetchMock)
    renderizar()

    abrir()

    const [linha] = await waitFor(() => linhas())
    expect(within(linha).getByText('estornado')).toBeTruthy()
    expect(within(linha).queryByRole('button', { name: /^Estornar/ })).toBeNull()
  })

  it('Estornar só onde estornavel e a sessão pode, e nenhum Estornar na Remessa', async () => {
    vi.stubGlobal('fetch', montarFetch([TODOS]).fetchMock)
    renderizar()

    abrir()

    await screen.findByText('Remessa de 4')
    const [remessa, recusa, pedido, conserto, contagem, afericao] = linhas()
    expect(within(remessa).queryByRole('button')).toBeNull()
    // A recusa não é estornável (e é de outra pessoa).
    expect(within(recusa).queryByRole('button')).toBeNull()
    for (const linha of [pedido, conserto, contagem, afericao]) {
      expect(within(linha).getByRole('button', { name: /^Estornar/ })).toBeTruthy()
    }
  })

  it('não oferece Estornar no registro de outra pessoa quando a sessão não pode', async () => {
    vi.stubGlobal('fetch', montarFetch([[{ ...CONSERTO, usuarioId: 13, usuarioNome: 'Outra pessoa' }]]).fetchMock)
    renderizar()

    abrir()

    const [linha] = await waitFor(() => linhas())
    expect(within(linha).queryByRole('button')).toBeNull()
  })

  it('estornar a aferição confirma, chama a rota dela, recarrega e avisa', async () => {
    const { fetchMock, gets } = montarFetch([TODOS, [{ ...AFERICAO, estornada: true, estornavel: false }]], {
      '/api/afericoes/1/estorno': () => respostaJson({ ...AFERICAO, estornada: true }),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { aoEstornar } = renderizar()

    abrir()
    await screen.findByText('Remessa de 4')
    fireEvent.click(screen.getByRole('button', { name: /^Estornar aferição não conforme/ }))
    const dialogo = screen.getByRole('dialog')
    expect(dialogo.textContent).toContain(
      'Estornar aferição não conforme de Qualidade do turno em 20/10/2026 08:00? O estorno fica no histórico.',
    )
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(aoEstornar).toHaveBeenCalledTimes(1))
    expect(gets()).toBe(2)
    expect(await screen.findByText('estornado')).toBeTruthy()
  })

  it('cada tipo estorna pela rota dele: contagem, conserto pelo livro e liberação', async () => {
    const rotas = ['/api/contagens/2/estorno', '/api/movimentacoes/90/estorno', '/api/liberacoes/5/estorno']
    const { fetchMock } = montarFetch([TODOS], Object.fromEntries(rotas.map((r) => [r, () => respostaJson([])])))
    vi.stubGlobal('fetch', fetchMock)
    const { aoEstornar } = renderizar()

    abrir()
    await screen.findByText('Remessa de 4')
    for (const nome of [/^Estornar contagem/, /^Estornar conserto/, /^Estornar pedido de liberação/]) {
      fireEvent.click(screen.getByRole('button', { name: nome }))
      fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      await waitFor(() => expect((screen.getByRole('button', { name: nome }) as HTMLButtonElement).disabled).toBe(false))
    }

    await waitFor(() => expect(aoEstornar).toHaveBeenCalledTimes(3))
    const postados = fetchMock.mock.calls.filter((c) => c[1]?.method === 'POST').map((c) => String(c[0]))
    expect(postados).toEqual(rotas)
  })

  it('o 409 do estorno do conserto mostra a frase do servidor e recarrega', async () => {
    const frase = 'Não dá para estornar: a quantidade já foi levada ao conserto.'
    const { fetchMock, gets } = montarFetch([TODOS], {
      '/api/movimentacoes/90/estorno': () => respostaJson({ erro: 'EstornoImpossivel', mensagem: frase }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { aoEstornar } = renderizar()

    abrir()
    await screen.findByText('Remessa de 4')
    fireEvent.click(screen.getByRole('button', { name: /^Estornar conserto/ }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar' }))

    expect((await screen.findByRole('alert')).textContent).toContain(frase)
    await waitFor(() => expect(aoEstornar).toHaveBeenCalledTimes(1))
    expect(gets()).toBe(2)
    // A lista continua de pé.
    expect(linhas()).toHaveLength(6)
  })

  it('a versão nova recarrega o histórico já aberto, e não o que nunca abriu', async () => {
    const { fetchMock, gets } = montarFetch([[AFERICAO], [AFERICAO, CONTAGEM]])
    vi.stubGlobal('fetch', fetchMock)
    const { rerender } = renderizar()

    rerender(1)
    expect(gets()).toBe(0)

    abrir()
    await screen.findByText('Histórico (1)')
    rerender(2)

    expect(await screen.findByText('Histórico (2)')).toBeTruthy()
    expect(gets()).toBe(2)
  })
})
