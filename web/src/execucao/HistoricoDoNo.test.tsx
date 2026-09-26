// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, fireEvent, waitFor } from '@testing-library/react'
import { HistoricoDoNo } from './HistoricoDoNo'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import { movimentacao } from '../testes/execucao'
import type { LivroDoNoDto, MontagemDto } from '../api/execucao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const INICIO = movimentacao()
const TERMINO_DE_OUTRO = movimentacao({
  id: 42, tipo: 'Termino', usuarioId: 13, usuarioNome: 'Outro operador',
  origem: { posicao: 'NoSetor', setorId: 1, setorNome: 'Corte', ordem: 1 },
  destino: { posicao: 'AguardandoColeta', setorId: 1, setorNome: 'Corte', ordem: 1 },
})
const JA_ESTORNADO = movimentacao({ id: 43, estornada: true })
const ESTORNO = movimentacao({
  id: 44, tipo: 'Estorno', estornoDeId: 43,
  origem: { posicao: 'NoSetor', setorId: 1, setorNome: 'Corte', ordem: 1 },
  destino: { posicao: 'AIniciar', setorId: null, setorNome: null, ordem: null },
})
const BAIXA = movimentacao({
  id: 45, tipo: 'Montagem', montagemId: 5,
  origem: { posicao: 'AguardandoMontagem', setorId: 4, setorNome: 'Solda', ordem: null },
  destino: { posicao: 'Montado', setorId: null, setorNome: null, ordem: null },
})
const MONTAGEM: MontagemDto = {
  id: 5, estruturaItemId: 7, setorId: 4, setorNome: 'Solda', quantidade: 2,
  dataHora: '2026-09-25T11:00:00-03:00', usuarioId: 12, usuarioNome: 'Operador do Corte', estornada: false, baixas: [],
}

/**
 * O autor da sessão é o 12 — dono de `INICIO` e de `MONTAGEM`, não de `TERMINO_DE_OUTRO`. O
 * `: boolean` explícito impede o TypeScript de inferir um predicado de tipo (`autorId is 12`), que
 * recusaria `() => true` no teste do PCP.
 */
const soOAutor = (autorId: number): boolean => autorId === 12

function montarFetch(livros: LivroDoNoDto[], estornos: Record<string, () => Response> = {}) {
  let gets = 0
  const fetchMock = vi.fn((url: string | URL, _init?: RequestInit) => {
    const caminho = String(url)
    if (caminho === '/api/estrutura/7/movimentacoes') {
      const l = livros[Math.min(gets, livros.length - 1)]
      gets += 1
      return Promise.resolve(respostaJson(l))
    }
    const estorno = estornos[caminho]
    if (estorno) return Promise.resolve(estorno())
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
  return { fetchMock, getsDoLivro: () => gets }
}

function renderizar(podeEstornar = soOAutor, aoEstornar = vi.fn()) {
  render(<HistoricoDoNo noId={7} podeEstornar={podeEstornar} aoEstornar={aoEstornar} />)
  return { aoEstornar }
}

const linhaDo = (id: number) => screen.getByText(new RegExp(`^nº ${id} ·`)).closest('li')!

describe('HistoricoDoNo', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra carregando antes do livro chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('cada registro diz o quê, de onde para onde, quem e quando', async () => {
    vi.stubGlobal('fetch', montarFetch([{ movimentacoes: [INICIO], montagens: [] }]).fetchMock)

    renderizar()

    const linha = (await screen.findByText('nº 41 · Início de 4')).closest('li')!
    expect(within(linha).getByText('a iniciar → Corte (passo 1)')).toBeTruthy()
    // A data vem em GMT-3 e é mostrada sem passar por `Date` (`formatarDataHora`).
    expect(within(linha).getByText('Operador do Corte · 25/09/2026 10:14')).toBeTruthy()
  })

  it('"Estornar" só aparece para quem pode, e só no que se estorna', async () => {
    vi.stubGlobal('fetch', montarFetch([{
      movimentacoes: [INICIO, TERMINO_DE_OUTRO, JA_ESTORNADO, ESTORNO, BAIXA], montagens: [],
    }]).fetchMock)

    renderizar()
    await screen.findByText('nº 41 · Início de 4')

    expect(within(linhaDo(41)).getByRole('button', { name: 'Estornar o registro nº 41' })).toBeTruthy()
    // Registro de outra pessoa, sem ser PCP/Administrador.
    expect(within(linhaDo(42)).queryByRole('button')).toBeNull()
    // Já estornado: marcado, sem botão.
    expect(within(linhaDo(43)).getByText('estornado')).toBeTruthy()
    expect(within(linhaDo(43)).queryByRole('button')).toBeNull()
    // Estorno não se estorna; aponta o que desfaz.
    expect(within(linhaDo(44)).queryByRole('button')).toBeNull()
    expect(within(linhaDo(44)).getByText('Desfaz o registro nº 43.')).toBeTruthy()
    // Baixa de filho: estorna-se a montagem, no pai.
    expect(within(linhaDo(45)).queryByRole('button')).toBeNull()
    expect(within(linhaDo(45)).getByText(/estorna-se a montagem inteira, no pai/)).toBeTruthy()
  })

  it('PCP e Administrador veem "Estornar" também no registro alheio', async () => {
    vi.stubGlobal('fetch', montarFetch([{ movimentacoes: [TERMINO_DE_OUTRO], montagens: [] }]).fetchMock)

    renderizar(() => true)

    expect(await screen.findByRole('button', { name: 'Estornar o registro nº 42' })).toBeTruthy()
  })

  it('estornar pede confirmação, grava, recarrega o livro e avisa a tela', async () => {
    const { fetchMock, getsDoLivro } = montarFetch(
      [{ movimentacoes: [INICIO], montagens: [] }, { movimentacoes: [{ ...INICIO, estornada: true }], montagens: [] }],
      { '/api/movimentacoes/41/estorno': () => respostaJson(movimentacao({ id: 46, tipo: 'Estorno' }), 201) },
    )
    vi.stubGlobal('fetch', fetchMock)
    const { aoEstornar } = renderizar()

    fireEvent.click(await screen.findByRole('button', { name: 'Estornar o registro nº 41' }))
    const dialogo = screen.getByRole('dialog')
    expect(within(dialogo).getByText(/Estornar o registro nº 41 \(Início de 4\)\?/)).toBeTruthy()
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('estornado')).toBeTruthy()
    expect(getsDoLivro()).toBe(2)
    expect(aoEstornar).toHaveBeenCalledTimes(1)
    const chamada = fetchMock.mock.calls.find((c) => String(c[0]) === '/api/movimentacoes/41/estorno')!
    expect((chamada[1] as RequestInit).method).toBe('POST')
  })

  it('cancelar a confirmação não estorna', async () => {
    const { fetchMock } = montarFetch([{ movimentacoes: [INICIO], montagens: [] }])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar o registro nº 41' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/estorno'))).toBe(false)
  })

  it('409 mostra a frase do servidor e recarrega', async () => {
    const { getsDoLivro, fetchMock } = montarFetch([{ movimentacoes: [INICIO], montagens: [] }], {
      '/api/movimentacoes/41/estorno': () => respostaJson(
        { erro: 'EstornoImpossivel', mensagem: 'A quantidade já saiu de Corte (passo 1) depois deste registro.' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { aoEstornar } = renderizar()

    fireEvent.click(await screen.findByRole('button', { name: 'Estornar o registro nº 41' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('A quantidade já saiu de Corte (passo 1) depois deste registro.')).toBeTruthy()
    await waitFor(() => expect(getsDoLivro()).toBe(2))
    expect(aoEstornar).toHaveBeenCalledTimes(1)
  })

  it('403 sem frase diz quem pode estornar', async () => {
    vi.stubGlobal('fetch', montarFetch([{ movimentacoes: [INICIO], montagens: [] }], {
      '/api/movimentacoes/41/estorno': () => respostaJson({ erro: 'Proibido' }, 403),
    }).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar o registro nº 41' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('Só quem fez o registro, o PCP ou o Administrador pode estorná-lo.')).toBeTruthy()
  })

  it('as montagens deste nó se estornam inteiras', async () => {
    const { fetchMock } = montarFetch([{ movimentacoes: [], montagens: [MONTAGEM] }], {
      '/api/montagens/5/estorno': () => respostaJson([], 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    const montagens = await screen.findByRole('list', { name: 'Montagens deste nó' })
    expect(within(montagens).getByText('Montagem nº 5 de 2 em Solda')).toBeTruthy()
    fireEvent.click(within(montagens).getByRole('button', { name: 'Estornar a montagem nº 5' }))
    expect(within(screen.getByRole('dialog')).getByText(/Os filhos voltam a aguardar montagem/)).toBeTruthy()
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(fetchMock.mock.calls.some((c) => String(c[0]) === '/api/montagens/5/estorno')).toBe(true))
  })

  it('montagem já estornada não oferece "Estornar"', async () => {
    vi.stubGlobal('fetch', montarFetch([{ movimentacoes: [], montagens: [{ ...MONTAGEM, estornada: true }] }]).fetchMock)

    renderizar()

    expect(await screen.findByText('estornada')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Estornar a montagem nº 5' })).toBeNull()
  })

  it('livro vazio diz que não há registro', async () => {
    vi.stubGlobal('fetch', montarFetch([{ movimentacoes: [], montagens: [] }]).fetchMock)

    renderizar()

    expect(await screen.findByText('Nenhum registro ainda.')).toBeTruthy()
  })

  it('falha ao carregar vira banner', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({}, 500)))

    renderizar()

    expect((await screen.findByRole('alert')).textContent)
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(screen.queryByText('Nenhum registro ainda.')).toBeNull()
  })
})
