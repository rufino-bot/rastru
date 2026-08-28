// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// Contagem por status: Aberto 2, EmProducao 1, AguardandoExpedicao 0, Concluido 1, Cancelado 1.
// O zero de AguardandoExpedicao é o caso que a spec §3.1 exige mostrar, e ele SÓ existe porque
// nenhum pedido do fixture tem esse status — se alguém acrescentar um, o teste do zero morre e
// aponta para cá.
const PEDIDOS = [
  { id: 1, numero: 'PED-001', cliente: 'Alfa', tipo: 'Normal', status: 'Aberto', dataAbertura: '2026-08-06T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 2, numero: 'PED-002', cliente: 'Beta', tipo: 'Normal', status: 'Concluido', dataAbertura: '2026-08-05T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 3, numero: 'PED-003', cliente: 'Gama', tipo: 'Normal', status: 'Aberto', dataAbertura: '2026-08-01T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 4, numero: 'PED-004', cliente: 'Delta', tipo: 'Normal', status: 'EmProducao', dataAbertura: '2026-08-03T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 5, numero: 'PED-005', cliente: 'Epsilon', tipo: 'Normal', status: 'Cancelado', dataAbertura: '2026-07-20T09:00:00-03:00', criadoPorUsuarioId: 1 },
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

  // I1 (achado da review de branch da 1D): nenhuma das seis telas que buscam dados tinha teste
  // provando o indicador "Carregando…" — só vazio e erro tinham. Molde de
  // `LoginPage.test.tsx` ("desabilita o botão enquanto o login está em voo"): fetch que nunca
  // resolve, e asserção SÍNCRONA (sem `await`/`findBy*`) de que o indicador está na tela antes de
  // qualquer resposta chegar.
  it('mostra o indicador de carregando antes da resposta da API chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra o total de componentes vindo do campo total, não do tamanho da página', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('41')).toBeTruthy()
  })

  it('conta só os pedidos abertos', async () => {
    // Cinco pedidos, DOIS Abertos: contar `.length` daria 5 e a tela mentiria.
    // `within(cartao).getByText('2')`, não `textContent.toContain('2')`: o cartão de componentes
    // mostra "41", e um `toContain` passaria com a fiação de pedidos e componentes trocada.
    // `getByText` casa o nó de texto inteiro, então discrimina.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).getByText('2')).toBeTruthy()
  })

  it('mostra as contagens de materiais e setores', async () => {
    // Mesmo motivo do teste acima: `getByText` escopado ao cartão, não `textContent.toContain`.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    expect(within(screen.getByText('materiais ativos').closest('a')!).getByText('1')).toBeTruthy()
    expect(within(screen.getByText('setores ativos').closest('a')!).getByText('2')).toBeTruthy()
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

  it('mostra os cinco status com a contagem, inclusive o que esta zerado', async () => {
    // O zerado (AguardandoExpedicao) é o caso que a spec §3.1 nomeia: omitir um status porque não
    // há nenhum pedido nele faria o leitor concluir que aquele estado não existe no sistema.
    // A pílula traz rótulo e contagem NUMA STRING SÓ — ver o comentário da renderização na
    // HomePage: contagem em elemento próprio colidiria com o `getByText('2')` do teste acima.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).getByText('Aberto 2')).toBeTruthy()
    expect(within(cartao).getByText('EmProducao 1')).toBeTruthy()
    expect(within(cartao).getByText('AguardandoExpedicao 0')).toBeTruthy()
    expect(within(cartao).getByText('Concluido 1')).toBeTruthy()
    expect(within(cartao).getByText('Cancelado 1')).toBeTruthy()
  })

  it('nao mostra o resumo por status enquanto os numeros nao chegaram', () => {
    // Cinco zeros seriam cinco afirmações falsas; cinco traços seriam ruído (o número grande do
    // cartão já diz "—"). O resumo simplesmente não existe até o dado chegar.
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.queryByText(/^Aberto \d/)).toBeNull()
    expect(screen.queryByText(/^AguardandoExpedicao \d/)).toBeNull()
  })

  it('nao aninha link dentro do cartao de pedidos', async () => {
    // Requisito explícito da spec §3.1: o cartão INTEIRO é um `<Link>`, então uma pílula clicável
    // ali dentro seria `<a>` dentro de `<a>` — HTML inválido, e o navegador desmonta a árvore de
    // um jeito que quebra a navegação por teclado. Esta asserção morre no dia em que alguém
    // "melhorar" o resumo tornando cada status filtrável.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).queryAllByRole('link')).toHaveLength(0)
  })
})
