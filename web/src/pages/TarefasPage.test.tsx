// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, act, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { TarefasPage } from './TarefasPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import { SUPORTE, PARAFUSO, CHASSI, DESTINO_MONTAGEM, destino } from '../testes/execucao'
import type { TarefasDoSetorDto } from '../api/execucao'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers() })

// O perfil governa a entrega (Task 10 do plano 3); `Movimentador` é o padrão dos testes.
let perfil = 'Movimentador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 11, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

/** Corte: Suporte indo para a Dobra; Solda: Parafuso indo à montagem do Chassi; Pintura: a Peça, à expedição. */
const TAREFAS: TarefasDoSetorDto[] = [
  { setorId: 1, setorNome: 'Corte', itens: [{ no: SUPORTE, ordem: 1, quantidade: 4, destino: destino() }] },
  { setorId: 4, setorNome: 'Solda', itens: [{ no: PARAFUSO, ordem: 2, quantidade: 10, destino: DESTINO_MONTAGEM }] },
  {
    setorId: 5, setorNome: 'Pintura',
    itens: [{ no: CHASSI, ordem: 3, quantidade: 2, destino: destino({ tipo: 'Expedicao', setorId: null, setorNome: null, ordem: null }) }],
  },
]

function montarFetch(respostas: TarefasDoSetorDto[][], entrega: () => Response = () => respostaJson([], 201)) {
  let gets = 0
  const fetchMock = vi.fn((url: string | URL, _init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (caminho === '/api/tarefas') {
      const r = respostas[Math.min(gets, respostas.length - 1)]
      gets += 1
      return Promise.resolve(respostaJson(r))
    }
    if (caminho === '/api/entregas') return Promise.resolve(entrega())
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
  return { fetchMock, getsDasTarefas: () => gets }
}

function corpoDaEntrega(fetchMock: ReturnType<typeof vi.fn>): unknown {
  const chamadas = fetchMock.mock.calls.filter((c) => String(c[0]) === '/api/entregas')
  expect(chamadas).toHaveLength(1)
  return JSON.parse((chamadas[0][1] as RequestInit).body as string)
}

function renderizar() {
  return render(<MemoryRouter><TarefasPage /></MemoryRouter>)
}

describe('TarefasPage', () => {
  beforeEach(() => {
    perfil = 'Movimentador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra carregando antes da lista chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('sem tarefa, diz que não há nada a levar', async () => {
    vi.stubGlobal('fetch', montarFetch([[]]).fetchMock)

    renderizar()

    expect(await screen.findByText('Nenhum item pronto para levar agora')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Entregar/ })).toBeNull()
  })

  it('falha na primeira carga: banner, sem lista nem estado vazio', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({}, 500)))

    renderizar()

    expect((await screen.findByRole('alert')).textContent)
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(screen.queryByText('Nenhum item pronto para levar agora')).toBeNull()
  })

  it('agrupa pelo Setor de origem e mostra o destino de cada item', async () => {
    vi.stubGlobal('fetch', montarFetch([TAREFAS]).fetchMock)

    renderizar()

    const corte = await screen.findByRole('list', { name: 'Prontos em Corte' })
    expect(within(corte).getByText('4 pronto(s) · passo 1')).toBeTruthy()
    expect(within(corte).getByText('Destino: Dobra (passo 2)')).toBeTruthy()
    expect(within(screen.getByRole('list', { name: 'Prontos em Solda' }))
      .getByText('Destino: Montagem de Chassi (sugestão: Solda)')).toBeTruthy()
    expect(within(screen.getByRole('list', { name: 'Prontos em Pintura' }))
      .getByText('Destino: Local de expedição')).toBeTruthy()
  })

  it('entrega vários itens numa requisição só, com o Setor de montagem na sugestão, e recarrega', async () => {
    const { fetchMock, getsDasTarefas } = montarFetch([TAREFAS, []])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByLabelText('Levar Parafuso'))
    fireEvent.click(screen.getByLabelText('Levar CH-01 — Chassi'))
    // Montagem: o `<select>` nasce na sugestão; os outros destinos não têm escolha nenhuma.
    expect(screen.getByLabelText('Setor de montagem')).toHaveProperty('value', '4')
    expect(screen.getAllByLabelText('Setor de montagem')).toHaveLength(1)
    const quantidades = screen.getAllByLabelText('Quantidade')
    expect(quantidades.map((q) => (q as HTMLInputElement).value)).toEqual(['4', '10', '2'])
    fireEvent.change(quantidades[1], { target: { value: '8' } })
    fireEvent.click(screen.getByRole('button', { name: 'Entregar 3 itens' }))

    expect(await screen.findByText('Nenhum item pronto para levar agora')).toBeTruthy()
    expect(corpoDaEntrega(fetchMock)).toEqual({
      itens: [
        { estruturaItemId: 7, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, destinoSetorId: null, quantidade: 4 },
        { estruturaItemId: 8, origem: { posicao: 'AguardandoColeta', setorId: 4, ordem: 2 }, destinoSetorId: 4, quantidade: 8 },
        { estruturaItemId: 2, origem: { posicao: 'AguardandoColeta', setorId: 5, ordem: 3 }, destinoSetorId: null, quantidade: 2 },
      ],
    })
    expect(getsDasTarefas()).toBe(2)
  })

  it('o Movimentador pode trocar o Setor de montagem sugerido', async () => {
    const { fetchMock } = montarFetch([TAREFAS])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar Parafuso'))
    fireEvent.change(screen.getByLabelText('Setor de montagem'), { target: { value: '6' } })
    fireEvent.click(screen.getByRole('button', { name: 'Entregar 1 item' }))

    await waitFor(() => expect(corpoDaEntrega(fetchMock)).toMatchObject({ itens: [{ destinoSetorId: 6 }] }))
  })

  it('sem sugestão, o Setor de montagem começa vazio e trava a entrega até ser escolhido', async () => {
    vi.stubGlobal('fetch', montarFetch([[{
      setorId: 4, setorNome: 'Solda',
      itens: [{ no: PARAFUSO, ordem: 2, quantidade: 10, destino: { ...DESTINO_MONTAGEM, sugestaoSetorId: null } }],
    }]]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar Parafuso'))

    expect(screen.getByLabelText('Setor de montagem')).toHaveProperty('value', '')
    expect(screen.getByText('Escolha o Setor de montagem.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Entregar 1 item' })).toHaveProperty('disabled', true)
    fireEvent.change(screen.getByLabelText('Setor de montagem'), { target: { value: '4' } })
    expect(screen.getByRole('button', { name: 'Entregar 1 item' })).toHaveProperty('disabled', false)
  })

  it('quantidade acima do pronto trava a entrega e diz o limite', async () => {
    vi.stubGlobal('fetch', montarFetch([TAREFAS]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '5' } })

    expect(screen.getByText('No máximo 4.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Entregar 1 item' })).toHaveProperty('disabled', true)
  })

  it('desmarcar tira o item da entrega', async () => {
    vi.stubGlobal('fetch', montarFetch([TAREFAS]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByLabelText('Levar SUP-01 — Suporte'))

    expect(screen.queryByLabelText('Quantidade')).toBeNull()
    expect(screen.getByRole('button', { name: 'Entregar' })).toHaveProperty('disabled', true)
  })

  it('item cujo pai não tem Roteiro aparece, mas não se marca', async () => {
    // Desvio D7 do plano 2.
    vi.stubGlobal('fetch', montarFetch([[{
      setorId: 4, setorNome: 'Solda',
      itens: [{ no: PARAFUSO, ordem: 2, quantidade: 10, destino: { ...DESTINO_MONTAGEM, sugestaoSetorId: null, setoresPossiveis: [], paiSemRoteiro: true } }],
    }]]).fetchMock)

    renderizar()

    expect(await screen.findByText('Destino: Montagem de Chassi — o pai não tem Roteiro')).toBeTruthy()
    expect(screen.getByLabelText('Levar Parafuso')).toHaveProperty('disabled', true)
    expect(screen.getByText('Peça ao PCP o Roteiro do pai antes de levar.')).toBeTruthy()
  })

  it('toque duplo em Entregar envia uma vez só', async () => {
    let resolver: (r: Response) => void = () => {}
    const fetchMock = vi.fn((url: string | URL) => {
      if (String(url) === '/api/tarefas') return Promise.resolve(respostaJson(TAREFAS))
      return new Promise<Response>((r) => { resolver = r })
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    const botao = screen.getByRole('button', { name: 'Entregar 1 item' })
    fireEvent.click(botao)
    fireEvent.click(botao)

    expect(fetchMock.mock.calls.filter((c) => String(c[0]) === '/api/entregas')).toHaveLength(1)
    await act(async () => { resolver(respostaJson([], 201)) })
  })

  it('409 mostra a frase do servidor, recarrega, e avisa do item marcado que saiu da lista', async () => {
    const { fetchMock, getsDasTarefas } = montarFetch([TAREFAS, TAREFAS.slice(1)], () => respostaJson(
      { erro: 'SaldoInsuficiente', mensagem: 'Só há 2 de Suporte aguardando coleta no Corte (passo 1).' }, 409))
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByLabelText('Levar Parafuso'))
    fireEvent.click(screen.getByRole('button', { name: 'Entregar 2 itens' }))

    expect(await screen.findByText('Só há 2 de Suporte aguardando coleta no Corte (passo 1).')).toBeTruthy()
    await waitFor(() => expect(getsDasTarefas()).toBe(2))
    expect(await screen.findByText(/Um item que você tinha marcado não está mais pronto/)).toBeTruthy()
    // O que continua pronto continua marcado.
    expect(screen.getByLabelText('Levar Parafuso')).toHaveProperty('checked', true)
    expect(screen.getByRole('button', { name: 'Entregar 1 item' })).toBeTruthy()
  })

  // Contraparte do '403 mostra a mensagem e não recarrega' de FilaDoSetorPage.test.tsx: aqui
  // recarregar SÓ no 409 (spec §8.3) é o que faz `getsDasTarefas()` ficar em 1 — mudar o `if
  // (ehConflito(e))` para incondicional deixaria este teste vermelho (achado Important #1 da
  // review da Task 6).
  it('403 mostra a mensagem e não recarrega', async () => {
    const { fetchMock, getsDasTarefas } = montarFetch([TAREFAS], () => respostaJson(
      { erro: 'Proibido', mensagem: 'Só o Movimentador pode registrar entregas.' }, 403))
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByRole('button', { name: 'Entregar 1 item' }))

    expect(await screen.findByText('Só o Movimentador pode registrar entregas.')).toBeTruthy()
    expect(getsDasTarefas()).toBe(1)
    // A seleção continua intacta: nem recarregou, nem limpou.
    expect(screen.getByLabelText('Levar SUP-01 — Suporte')).toHaveProperty('checked', true)
    expect(screen.getByLabelText('Quantidade')).toBeTruthy()
  })

  it('item marcado que fica sem Roteiro no meio do caminho pode ser desmarcado', async () => {
    // Achado Important #2 da review da Task 6: o Movimentador marca o Parafuso; o PCP tira o
    // Roteiro do pai (Chassi) antes da próxima atualização periódica; a tarefa continua vindo,
    // agora com `paiSemRoteiro: true`, sem sair da lista (ela continua pronta — só o destino
    // ficou inválido).
    vi.useFakeTimers()
    let gets = 0
    const fetchMock = vi.fn((url: string | URL) => {
      if (String(url) !== '/api/tarefas') return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
      gets += 1
      const semRoteiro = gets > 1
      const destinoParafuso = semRoteiro
        ? { ...DESTINO_MONTAGEM, sugestaoSetorId: null, setoresPossiveis: [], paiSemRoteiro: true }
        : DESTINO_MONTAGEM
      return Promise.resolve(respostaJson([{
        setorId: 4, setorNome: 'Solda',
        itens: [{ no: PARAFUSO, ordem: 2, quantidade: 10, destino: destinoParafuso }],
      }]))
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByLabelText('Levar Parafuso'))

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    const checkbox = screen.getByLabelText('Levar Parafuso')
    expect(checkbox).toHaveProperty('checked', true)
    // Continua marcável de desmarcar: travar só quem ainda NÃO marcou (`travaOMarcar`), não
    // quem já marcou e ficou bloqueado depois.
    expect(checkbox).toHaveProperty('disabled', false)
    // A mesma frase aparece duas vezes: no aviso sempre visível do item, e na dica do painel —
    // é esta segunda ocorrência que prova que `erroDaEscolha` passou a considerar `paiSemRoteiro`.
    expect(screen.getAllByText('Peça ao PCP o Roteiro do pai antes de levar.')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Entregar 1 item' })).toHaveProperty('disabled', true)

    fireEvent.click(checkbox)

    expect(screen.getAllByText('Peça ao PCP o Roteiro do pai antes de levar.')).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Entregar' })).toHaveProperty('disabled', true)
  })

  it('a atualização periódica preserva a seleção e o que foi digitado', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', montarFetch([TAREFAS]).fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '3' } })

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.getByLabelText('Levar SUP-01 — Suporte')).toHaveProperty('checked', true)
    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '3')
    expect(screen.queryByRole('alert')).toBeNull()
  })
  it.each(['Operador', 'Gestao'])('%s lê as tarefas, sem marcar nem entregar', async (quem) => {
    perfil = quem
    vi.stubGlobal('fetch', montarFetch([TAREFAS]).fetchMock)

    renderizar()

    expect(await screen.findByRole('list', { name: 'Prontos em Corte' })).toBeTruthy()
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.queryByRole('button', { name: /Entregar/ })).toBeNull()
  })
})
