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
