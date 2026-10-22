// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import { linhaDaRemessa, remessa } from '../testes/expedicao'
import type { RemessaDto } from '../api/expedicao'
import type { EstadoSessao } from '../auth/estadoDaSessao'

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { RemessasRecentes } = await import('./RemessasRecentes')

afterEach(() => { cleanup(); vi.unstubAllGlobals(); estadoAtual = { status: 'anonimo' } })

function como(perfil: string, id = 12) {
  estadoAtual = { status: 'autenticado', usuario: { id, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
}

// Duas Peças de Pedidos diferentes, a segunda tirada de duas posições (duas linhas `Expedicao`).
const DUAS_PECAS = remessa({
  id: 3,
  linhas: [
    linhaDaRemessa({ pecaId: 30, pedidoId: 1, pedidoNumero: 'PED-1', quantidade: 4, origem: 'Conforme' }),
    linhaDaRemessa({
      pecaId: 40, pecaDescricao: 'Tampa', codigoDoComponente: null, pedidoId: 2, pedidoNumero: 'PED-2',
      quantidade: 2, origem: 'Conforme',
    }),
    linhaDaRemessa({
      pecaId: 40, pecaDescricao: 'Tampa', codigoDoComponente: null, pedidoId: 2, pedidoNumero: 'PED-2',
      quantidade: 1, origem: 'NaoConforme',
    }),
  ],
})

/** Responde a lista em ordem, repetindo a última; o `POST` do estorno responde `estorno()`. */
function montarFetch(
  listas: (RemessaDto[] | Promise<RemessaDto[]>)[],
  estorno: () => Response | Promise<Response> = () => respostaJson([], 200),
) {
  let gets = 0
  const estornos: string[] = []
  const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (caminho === '/api/remessas/recentes') {
      const r = listas[Math.min(gets, listas.length - 1)]
      gets += 1
      return Promise.resolve(r).then((l) => respostaJson(l))
    }
    if (/^\/api\/remessas\/\d+\/estorno$/.test(caminho) && init?.method === 'POST') {
      estornos.push(caminho)
      return Promise.resolve(estorno())
    }
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
  return { fetchMock, gets: () => gets, estornos }
}

function renderizar(props: { versao?: number } = {}) {
  const aoEstornar = vi.fn()
  const elemento = (versao?: number) => <RemessasRecentes aoEstornar={aoEstornar} versao={versao} />
  const r = render(elemento(props.versao))
  return { aoEstornar, rerender: (versao: number) => r.rerender(elemento(versao)) }
}

const lista = () => screen.findByRole('list', { name: 'Remessas recentes' })
const botaoDeEstornar = () => screen.getByRole('button', { name: /^Estornar remessa/ })

describe('RemessasRecentes', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
    como('Qualidade')
  })

  it('mostra carregando e depois a seção com o título', async () => {
    vi.stubGlobal('fetch', montarFetch([[DUAS_PECAS]]).fetchMock)

    renderizar()

    expect(screen.getByRole('heading', { level: 2, name: 'Remessas recentes' })).toBeTruthy()
    expect(screen.getByRole('status')).toBeTruthy()
    await lista()
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('lista as remessas com as Peças e quantidades', async () => {
    vi.stubGlobal('fetch', montarFetch([[DUAS_PECAS, remessa({ id: 2, usuarioNome: 'Outra pessoa', usuarioId: 9, estornada: true, estornavel: false })]]).fetchMock)

    renderizar()

    const remessas = within(await lista()).getAllByRole('listitem').filter((li) => li.parentElement?.getAttribute('aria-label') === 'Remessas recentes')
    expect(remessas).toHaveLength(2)
    const primeira = within(remessas[0])
    expect(primeira.getByText('20/10/2026 15:00 · Qualidade do turno')).toBeTruthy()
    // Uma linha por Peça: as duas posições de origem da Tampa somam.
    const pecas = primeira.getByRole('list', { name: 'Peças da remessa' })
    expect(within(pecas).getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'QD-01 — Quadro (PED-1) · 4',
      'Tampa (PED-2) · 3',
    ])
    expect(primeira.queryByText('estornada')).toBeNull()
    expect(within(remessas[1]).getByText('estornada')).toBeTruthy()
  })

  it('vazio diz "Nenhuma remessa nos últimos 30 dias."', async () => {
    vi.stubGlobal('fetch', montarFetch([[]]).fetchMock)

    renderizar()

    expect(await screen.findByText('Nenhuma remessa nos últimos 30 dias.')).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Remessas recentes' })).toBeNull()
  })

  it('erro de carga mostra "Não foi possível carregar as remessas."', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({}, 400)))

    renderizar()

    expect((await screen.findByRole('alert')).textContent).toBe('Não foi possível carregar as remessas.')
    expect(screen.queryByText('Nenhuma remessa nos últimos 30 dias.')).toBeNull()
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('Estornar remessa abre o painel com as Peças e "Reabre o Pedido {número}." uma vez por Pedido que ela reabre', async () => {
    const concluidas = remessa({
      id: 4,
      linhas: [
        linhaDaRemessa({ pecaId: 30, pedidoId: 1, pedidoNumero: 'PED-1', pedidoConcluido: true, quantidade: 4 }),
        linhaDaRemessa({ pecaId: 31, pecaDescricao: 'Base', codigoDoComponente: 'BA-01', pedidoId: 1, pedidoNumero: 'PED-1', pedidoConcluido: true, quantidade: 2 }),
        linhaDaRemessa({ pecaId: 40, pecaDescricao: 'Tampa', codigoDoComponente: null, pedidoId: 2, pedidoNumero: 'PED-2', pedidoConcluido: false, quantidade: 1 }),
        linhaDaRemessa({ pecaId: 50, pecaDescricao: 'Pé', codigoDoComponente: null, pedidoId: 3, pedidoNumero: 'PED-3', pedidoConcluido: true, quantidade: 1 }),
      ],
    })
    vi.stubGlobal('fetch', montarFetch([[concluidas]]).fetchMock)

    renderizar()
    await lista()
    fireEvent.click(botaoDeEstornar())

    const painel = screen.getByRole('form', { name: 'Estornar remessa' })
    const pecas = within(painel).getByRole('list', { name: 'Peças que a remessa desfaz' })
    expect(within(pecas).getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'QD-01 — Quadro (PED-1) · 4', 'BA-01 — Base (PED-1) · 2', 'Tampa (PED-2) · 1', 'Pé (PED-3) · 1',
    ])
    // PED-1 tem duas Peças na remessa e reabre uma vez só; PED-2 não estava concluído.
    expect(within(painel).getAllByText(/^Reabre o Pedido/).map((p) => p.textContent))
      .toEqual(['Reabre o Pedido PED-1.', 'Reabre o Pedido PED-3.'])
    expect(within(painel).getByRole('button', { name: 'Estornar' })).toBeTruthy()
    // O botão que abriu o painel some enquanto ele está aberto.
    expect(screen.queryByRole('button', { name: /^Estornar remessa/ })).toBeNull()
  })

  it('sem Pedido concluído o painel não diz que reabre nada', async () => {
    vi.stubGlobal('fetch', montarFetch([[DUAS_PECAS]]).fetchMock)

    renderizar()
    await lista()
    fireEvent.click(botaoDeEstornar())

    expect(within(screen.getByRole('form', { name: 'Estornar remessa' })).queryByText(/Reabre o Pedido/)).toBeNull()
  })

  it('confirmar estorna, recarrega as remessas, avisa a tela e devolve o foco ao botão', async () => {
    const { fetchMock, gets, estornos } = montarFetch([[DUAS_PECAS], [{ ...DUAS_PECAS, estornada: true, estornavel: false }]])
    vi.stubGlobal('fetch', fetchMock)

    const { aoEstornar } = renderizar()
    await lista()
    fireEvent.click(botaoDeEstornar())
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(estornos).toEqual(['/api/remessas/3/estorno']))
    await waitFor(() => expect(gets()).toBe(2))
    await waitFor(() => expect(aoEstornar).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(screen.queryByRole('form', { name: 'Estornar remessa' })).toBeNull())
    expect(screen.getByText('estornada')).toBeTruthy()
    // O botão sumiu com a remessa estornada: o foco cai no título da seção, não no <body>.
    const titulo = screen.getByRole('heading', { level: 2, name: 'Remessas recentes' })
    await waitFor(() => expect(titulo.contains(document.activeElement)).toBe(true))
  })

  it('a recusa do servidor fica dentro do painel, que continua aberto, e as remessas recarregam', async () => {
    const frase = 'A remessa já foi estornada.'
    const { fetchMock, gets } = montarFetch(
      [[DUAS_PECAS], [{ ...DUAS_PECAS, estornada: true, estornavel: false }]],
      () => respostaJson({ erro: 'JaEstornado', mensagem: frase }, 409),
    )
    vi.stubGlobal('fetch', fetchMock)

    const { aoEstornar } = renderizar()
    await lista()
    fireEvent.click(botaoDeEstornar())
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(gets()).toBe(2))
    const painel = screen.getByRole('form', { name: 'Estornar remessa' })
    expect(within(painel).getByRole('alert').textContent).toBe(frase)
    expect(aoEstornar).toHaveBeenCalledTimes(1)
  })

  it('dois toques em Estornar mandam um estorno só', async () => {
    let responder: (r: Response) => void = () => {}
    const { fetchMock, estornos } = montarFetch([[DUAS_PECAS]], () => new Promise<Response>((r) => { responder = r }))
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await lista()
    fireEvent.click(botaoDeEstornar())
    const confirmar = screen.getByRole('button', { name: 'Estornar' })
    fireEvent.click(confirmar)
    fireEvent.click(confirmar)

    await waitFor(() => expect(estornos).toHaveLength(1))
    responder(respostaJson([], 200))
  })

  it('sem permissão não mostra Estornar remessa', async () => {
    // A Qualidade que não é a autora, a Gestão e o Operador não estornam a remessa de outra pessoa.
    for (const [perfil, id] of [['Qualidade', 99], ['Gestao', 99], ['Operador', 12]] as const) {
      como(perfil, id)
      vi.stubGlobal('fetch', montarFetch([[DUAS_PECAS]]).fetchMock)

      renderizar()
      await lista()

      expect(screen.queryByRole('button', { name: /^Estornar remessa/ }), perfil).toBeNull()
      // A leitura é de todos.
      expect(screen.getByText('Tampa (PED-2) · 3'), perfil).toBeTruthy()
      cleanup()
    }
  })

  it('PCP estorna a remessa de outra pessoa, mas não a que o servidor diz não estornável', async () => {
    como('PCP', 99)
    vi.stubGlobal('fetch', montarFetch([[DUAS_PECAS, remessa({ id: 2, estornavel: false })]]).fetchMock)

    renderizar()
    await lista()

    expect(screen.getAllByRole('button', { name: /^Estornar remessa/ })).toHaveLength(1)
  })

  it('o botão de cada remessa tem nome próprio', async () => {
    como('PCP', 99)
    vi.stubGlobal('fetch', montarFetch([[DUAS_PECAS, remessa({ id: 2, dataHora: '2026-10-19T08:30:00-03:00' })]]).fetchMock)

    renderizar()
    await lista()

    expect(screen.getAllByRole('button', { name: /^Estornar remessa/ }).map((b) => b.getAttribute('aria-label')))
      .toEqual([
        'Estornar remessa de 20/10/2026 15:00, de Qualidade do turno',
        'Estornar remessa de 19/10/2026 08:30, de Qualidade do turno',
      ])
  })

  it('muda a versão, recarrega; a mesma versão não', async () => {
    const { fetchMock, gets } = montarFetch([[DUAS_PECAS]])
    vi.stubGlobal('fetch', fetchMock)

    const { rerender } = renderizar({ versao: 0 })
    await lista()
    expect(gets()).toBe(1)

    rerender(0)
    expect(gets()).toBe(1)
    rerender(1)
    await waitFor(() => expect(gets()).toBe(2))
  })
})
