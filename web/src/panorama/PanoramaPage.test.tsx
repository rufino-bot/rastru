// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, act, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom'
import { PanoramaPage } from './PanoramaPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'
import { INTERVALO_DA_EXECUCAO_MS } from '../hooks/useCargaPeriodica'
import type { PecaDoPanoramaDto } from '../api/panorama'
import { pecaDeTeste } from './pecaDeTeste'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers() })

let perfil = 'Qualidade'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 3, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

/** Mostra a query string atual, para os testes lerem o que a tela escreveu na URL. */
function MarcaDaUrl() {
  const { search } = useLocation()
  return <code aria-label="url">{search}</code>
}

function renderizar(caminho = '/panorama') {
  return render(
    <MemoryRouter initialEntries={[caminho]}>
      <MarcaDaUrl />
      <Routes>
        <Route path="/panorama" element={<PanoramaPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

const urlAtual = () => screen.getByLabelText('url').textContent

function peca(id: number, codigo: string, o: Partial<PecaDoPanoramaDto> = {}, entrega = '2026-11-20', numero = '293') {
  return pecaDeTeste({
    estruturaItemId: id,
    componente: { id, codigo, descricao: `Descrição de ${codigo}`, miniaturaVersao: null },
    pedido: { id: 1, numero, dataEntrega: entrega, atrasado: false, retrabalho: false, pausa: null },
    ...o,
  })
}

// Na ordem do servidor: o prazo do meio vem primeiro, de propósito, para a ordem original não coincidir com nenhuma.
const TRES = [
  peca(1, 'B-02', { quantidade: 2 }, '2026-11-20'),
  peca(2, 'C-03', { quantidade: 9 }, '2026-11-18'),
  peca(3, 'A-01', { quantidade: 5 }, '2026-11-25'),
]

const rotas = (pecas: PecaDoPanoramaDto[]) => ({ '/api/panorama': () => respostaJson({ pecas }) })

const tabela = () => screen.getByRole('table')
const codigosDaTabela = () =>
  within(tabela()).getAllByRole('link').map((l) => l.textContent)
const cabecalho = (nome: string) => within(tabela()).getByRole('columnheader', { name: nome })

beforeEach(() => {
  perfil = 'Qualidade'
  _resetParaTeste()
  inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
})

describe('PanoramaPage — os três estados', () => {
  it('mostra carregando antes da lista chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra "Nenhuma Peça em aberto." quando o servidor manda a lista vazia', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas([])))

    renderizar()

    expect(await screen.findByText('Nenhuma Peça em aberto.')).toBeTruthy()
    expect(screen.queryByText('Nenhuma Peça casa com o filtro.')).toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('mostra "Nenhuma Peça casa com o filtro." quando a busca esconde tudo', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas(TRES)))

    renderizar('/panorama?busca=zzz')

    expect(await screen.findByText('Nenhuma Peça casa com o filtro.')).toBeTruthy()
    expect(screen.queryByText('Nenhuma Peça em aberto.')).toBeNull()
  })

  it('o filtro de uma opção que não está mais na lista continua filtrando, e o vazio é o do filtro', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas(TRES)))

    renderizar('/panorama?setor=99')

    expect(await screen.findByText('Nenhuma Peça casa com o filtro.')).toBeTruthy()
    expect(screen.queryByText('Nenhuma Peça em aberto.')).toBeNull()
  })

  it('"Limpar filtros" apaga facetas, busca e intervalo, e devolve a lista', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas(TRES)))
    renderizar('/panorama?busca=zzz&setor=99&de=2026-11-01&ordem=qtd-asc')

    fireEvent.click(await screen.findByRole('button', { name: 'Limpar filtros' }))

    expect(urlAtual()).toBe('?ordem=qtd-asc')
    expect(codigosDaTabela()).toHaveLength(3)
  })

  it('mostra a mensagem de erro quando a carga falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/panorama': () => respostaJson({ erro: 'Falha', mensagem: 'O Panorama está fora do ar.' }, 409) }))

    renderizar()

    expect((await screen.findByRole('alert')).textContent).toContain('O Panorama está fora do ar.')
    expect(screen.queryByText('Nenhuma Peça em aberto.')).toBeNull()
  })

  it('escreve "Atualizado às HH:MM" depois da carga', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas(TRES)))

    renderizar()

    expect(await screen.findByText(/Atualizado às \d{2}:\d{2}/)).toBeTruthy()
  })
})

describe('PanoramaPage — ordem', () => {
  it('a ordem original é a que o servidor mandou, e não escreve nada na URL', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas(TRES)))

    renderizar()

    await screen.findByRole('table')
    expect(codigosDaTabela()).toEqual(['B-02', 'C-03', 'A-01'])
    expect(urlAtual()).toBe('')
    expect(cabecalho('Prazo').getAttribute('aria-sort')).toBe('none')
  })

  it('clicar em "Prazo" duas vezes inverte, a terceira volta, com aria-sort e ?ordem= acompanhando', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas(TRES)))
    renderizar()
    await screen.findByRole('table')
    const clicar = () => fireEvent.click(within(tabela()).getByRole('button', { name: 'Prazo' }))

    clicar()
    expect(codigosDaTabela()).toEqual(['C-03', 'B-02', 'A-01'])
    expect(cabecalho('Prazo').getAttribute('aria-sort')).toBe('ascending')
    expect(urlAtual()).toBe('?ordem=prazo-asc')

    clicar()
    expect(codigosDaTabela()).toEqual(['A-01', 'B-02', 'C-03'])
    expect(cabecalho('Prazo').getAttribute('aria-sort')).toBe('descending')
    expect(urlAtual()).toBe('?ordem=prazo-desc')

    clicar()
    expect(codigosDaTabela()).toEqual(['B-02', 'C-03', 'A-01'])
    expect(cabecalho('Prazo').getAttribute('aria-sort')).toBe('none')
    expect(urlAtual()).toBe('')
  })

  it('o seletor dos cartões muda a mesma ordem, e "Ordem da tela" a desfaz', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas(TRES)))
    renderizar()
    await screen.findByRole('table')
    const seletor = screen.getByLabelText('Ordenar por') as HTMLSelectElement
    expect([...seletor.options].map((o) => o.textContent)).toEqual([
      'Ordem da tela', 'Prazo (mais próximo)', 'Prazo (mais distante)', 'OS (crescente)', 'OS (decrescente)',
      'Código (A→Z)', 'Código (Z→A)', 'Descrição (A→Z)', 'Descrição (Z→A)', 'Qtd (maior)', 'Qtd (menor)',
    ])

    fireEvent.change(seletor, { target: { value: 'qtd-desc' } })

    expect(codigosDaTabela()).toEqual(['C-03', 'A-01', 'B-02'])
    expect(cabecalho('Qtd').getAttribute('aria-sort')).toBe('descending')
    expect(within(screen.getByRole('list', { name: 'Peças em aberto' })).getAllByRole('link').map((l) => l.textContent))
      .toEqual(['C-03', 'A-01', 'B-02'])
    expect(urlAtual()).toBe('?ordem=qtd-desc')

    fireEvent.change(seletor, { target: { value: 'original' } })

    expect(codigosDaTabela()).toEqual(['B-02', 'C-03', 'A-01'])
    expect(urlAtual()).toBe('')
  })

  it('uma ordem desconhecida na URL vale a original', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas(TRES)))

    renderizar('/panorama?ordem=lixo')

    await screen.findByRole('table')
    expect(codigosDaTabela()).toEqual(['B-02', 'C-03', 'A-01'])
  })
})

describe('PanoramaPage — a vista na URL', () => {
  it('F5 com ?setor=3&ordem=codigo-desc&de=2026-11-01 mantém a vista', async () => {
    const noSetor3 = { itensEm: [{ setorId: 3, nome: 'Solda' }] }
    vi.stubGlobal('fetch', fetchPorRota(rotas([
      peca(1, 'B-02', noSetor3),
      peca(2, 'C-03'),
      peca(3, 'A-01', noSetor3),
      peca(4, 'D-04', noSetor3, '2026-10-30'),
    ])))

    renderizar('/panorama?setor=3&ordem=codigo-desc&de=2026-11-01')

    await screen.findByRole('table')
    // Só o Setor 3, a partir de 01/11, por código decrescente: a D-04 vence antes de 01/11 e a C-03 não está no Setor.
    expect(codigosDaTabela()).toEqual(['B-02', 'A-01'])
    expect(cabecalho('Código').getAttribute('aria-sort')).toBe('descending')
    expect((screen.getByLabelText('Prazo de entrega: de') as HTMLInputElement).value).toBe('2026-11-01')
  })

  it('a busca e o intervalo vão para a URL e saem dela quando voltam ao vazio', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas(TRES)))
    renderizar()
    await screen.findByRole('table')

    fireEvent.change(screen.getByLabelText('Buscar'), { target: { value: 'c-03' } })
    expect(urlAtual()).toBe('?busca=c-03')
    expect(codigosDaTabela()).toEqual(['C-03'])

    fireEvent.change(screen.getByLabelText('Buscar'), { target: { value: '' } })
    expect(urlAtual()).toBe('')

    fireEvent.change(screen.getByLabelText('Prazo de entrega: de'), { target: { value: '2026-11-19' } })
    fireEvent.change(screen.getByLabelText('Prazo de entrega: até'), { target: { value: '2026-11-21' } })
    expect(urlAtual()).toBe('?de=2026-11-19&ate=2026-11-21')
    expect(codigosDaTabela()).toEqual(['B-02'])

    fireEvent.click(screen.getByRole('button', { name: 'Limpar' }))
    expect(urlAtual()).toBe('')
  })
})

describe('PanoramaPage — recarga periódica', () => {
  it('recarrega a cada intervalo mantendo filtro e ordem, sem mostrar o carregando nem desmontar a lista', async () => {
    vi.useFakeTimers()
    let chamadas = 0
    vi.stubGlobal('fetch', vi.fn(() => {
      chamadas += 1
      return chamadas === 1
        ? Promise.resolve(respostaJson({ pecas: TRES }))
        // A segunda resposta nunca chega: se a tela voltasse ao carregando, ou desmontasse a lista, apareceria aqui.
        : new Promise<Response>(() => {})
    }))
    renderizar('/panorama?de=2026-11-19&ordem=codigo-desc')
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    const antes = within(tabela()).getByRole('link', { name: 'B-02' })
    // A C-03 vence antes de 19/11 e fica fora; as outras vêm por código decrescente.
    expect(codigosDaTabela()).toEqual(['B-02', 'A-01'])

    await act(async () => { await vi.advanceTimersByTimeAsync(INTERVALO_DA_EXECUCAO_MS) })

    expect(chamadas).toBe(2)
    expect(screen.queryByRole('status')).toBeNull()
    expect(within(tabela()).getByRole('link', { name: 'B-02' })).toBe(antes)
    expect(urlAtual()).toBe('?de=2026-11-19&ordem=codigo-desc')
  })

  it('a recarga que resolve troca os dados e mantém o mesmo nó da lista, sem passar pelo carregando', async () => {
    vi.useFakeTimers()
    let chamadas = 0
    vi.stubGlobal('fetch', vi.fn(() => {
      chamadas += 1
      return Promise.resolve(respostaJson({ pecas: chamadas === 1 ? TRES : [...TRES, peca(4, 'Z-09')] }))
    }))
    renderizar('/panorama')
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    const antes = within(tabela()).getByRole('link', { name: 'B-02' })
    const tabelaAntes = tabela()

    await act(async () => { await vi.advanceTimersByTimeAsync(INTERVALO_DA_EXECUCAO_MS) })

    expect(chamadas).toBe(2)
    expect(codigosDaTabela()).toContain('Z-09')
    expect(screen.queryByRole('status')).toBeNull()
    expect(tabela()).toBe(tabelaAntes)
    expect(within(tabela()).getByRole('link', { name: 'B-02' })).toBe(antes)
  })

  it('a resposta da recarga troca os dados e o filtro continua valendo', async () => {
    vi.useFakeTimers()
    let chamadas = 0
    vi.stubGlobal('fetch', vi.fn(() => {
      chamadas += 1
      return Promise.resolve(respostaJson({ pecas: chamadas === 1 ? TRES : [...TRES, peca(4, 'Z-09'), peca(5, 'X-05')] }))
    }))
    renderizar('/panorama?de=2026-11-19')
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(codigosDaTabela()).toEqual(['B-02', 'A-01'])

    await act(async () => { await vi.advanceTimersByTimeAsync(INTERVALO_DA_EXECUCAO_MS) })

    expect(codigosDaTabela()).toEqual(['B-02', 'A-01', 'Z-09', 'X-05'])
  })
})

describe('PanoramaPage — o "Embalado"', () => {
  const marcavel = peca(7, 'M-07', { embalagem: { vale: false, podeMarcar: true, emProducao: 0, ultima: null } })

  it('a Qualidade vê a caixa, e a Gestão só lê', async () => {
    vi.stubGlobal('fetch', fetchPorRota(rotas([marcavel])))
    renderizar()
    await screen.findByRole('table')
    expect(within(tabela()).getByRole('checkbox', { name: /Embalado/ })).toBeTruthy()

    cleanup()
    perfil = 'Gestao'
    renderizar()
    await screen.findByRole('table')
    expect(within(tabela()).queryByRole('checkbox')).toBeNull()
  })

  it('marcar grava e recarrega a lista', async () => {
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
      if (String(url).endsWith('/estrutura/7/embalagem')) {
        expect(init?.method).toBe('POST')
        return Promise.resolve(new Response(null, { status: 204 }))
      }
      return Promise.resolve(respostaJson({ pecas: [marcavel] }))
    })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()
    await screen.findByRole('table')

    fireEvent.click(within(tabela()).getByRole('checkbox', { name: /Embalado/ }))

    await vi.waitFor(() => {
      const paineis = fetchMock.mock.calls.filter(([u]) => String(u).endsWith('/panorama'))
      expect(paineis).toHaveLength(2)
    })
  })
})
