// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, act, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { TarefasPage } from './TarefasPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import { INTERVALO_DA_EXECUCAO_MS } from '../hooks/useCargaPeriodica'
import { SUPORTE, PARAFUSO, CHASSI, DESTINO_MONTAGEM, destino, no } from '../testes/execucao'
import type { FilhoDoKitDto, KitDto, TarefasDoSetorDto, TarefasDto } from '../api/execucao'

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

function tarefas(grupos: TarefasDoSetorDto[], montaveis: KitDto[] = [], incompletos: KitDto[] = []): TarefasDto {
  return { grupos, kitsMontaveis: montaveis, kitsIncompletos: incompletos }
}

function filhoDoKit(parcial: Partial<FilhoDoKitDto> = {}): FilhoDoKitDto {
  return {
    no: no({ id: 31, descricao: 'Longarina', codigoDoComponente: 'LG-01', paiId: 30, paiDescricao: 'Quadro', agrupamentoTipo: 'Kit' }),
    quantidadePorPai: 4, origem: { id: 1, nome: 'Corte' }, ordem: 1, pronto: 8, jaNoDestino: false,
    ...parcial,
  }
}

/**
 * Kit Quadro (id 30): Longarina (razão 4, 8 prontas no Corte, passo 1) e Travessa (razão 1, 2 prontas na Dobra,
 * passo 2). Dá para levar 2 conjuntos, para a Solda.
 */
const QUADRO = no({
  id: 30, descricao: 'Quadro', codigoDoComponente: 'QD-01', paiId: null, paiDescricao: null, agrupamentoTipo: 'Kit',
})
const KIT_QUADRO: KitDto = {
  pai: QUADRO,
  destino: { id: 4, nome: 'Solda' },
  conjuntos: 2,
  filhos: [
    filhoDoKit(),
    filhoDoKit({
      no: no({ id: 32, descricao: 'Travessa', codigoDoComponente: 'TV-01', paiId: 30, paiDescricao: 'Quadro', agrupamentoTipo: 'Kit' }),
      quantidadePorPai: 1, origem: { id: 3, nome: 'Dobra' }, ordem: 2, pronto: 2,
    }),
  ],
}

function montarFetch(respostas: TarefasDto[], entrega: () => Response = () => respostaJson([], 201)) {
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

function renderizar(caminho = '/') {
  return render(<MemoryRouter initialEntries={[caminho]}><TarefasPage /></MemoryRouter>)
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
    vi.stubGlobal('fetch', montarFetch([tarefas([])]).fetchMock)

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
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS)]).fetchMock)

    renderizar()

    const corte = await screen.findByRole('list', { name: 'Prontos em Corte' })
    expect(within(corte).getByText('4 pronto(s) · passo 1')).toBeTruthy()
    expect(within(corte).getByText('Destino: Dobra (passo 2)')).toBeTruthy()
    expect(within(screen.getByRole('list', { name: 'Prontos em Solda' }))
      .getByText('Destino: Montagem de Chassi em Solda')).toBeTruthy()
    expect(within(screen.getByRole('list', { name: 'Prontos em Pintura' }))
      .getByText('Destino: Local de expedição')).toBeTruthy()
  })

  it('item de Pedido pausado mostra a pilula "Pausado" e continua marcavel (a pausa recusa so o Iniciar)', async () => {
    const pausa = { desde: '2026-09-28T10:14:00-03:00', porUsuarioNome: 'PCP', motivo: 'PED-9 urgente' }
    vi.stubGlobal('fetch', montarFetch([tarefas([
      { setorId: 1, setorNome: 'Corte', itens: [
        { no: { ...SUPORTE, pausa }, ordem: 1, quantidade: 4, destino: destino() },
        { no: PARAFUSO, ordem: 1, quantidade: 6, destino: destino() },
      ] },
    ])]).fetchMock)

    renderizar()

    const pausado = (await screen.findByText('SUP-01 — Suporte')).closest('li')!
    expect(within(pausado).getByText('Pausado')).toBeTruthy()
    const classesDaPilula = within(pausado).getByText('Pausado').className.split(/\s+/)
    expect(classesDaPilula).toContain('bg-atencao-fundo')
    expect(classesDaPilula).toContain('text-atencao-texto')
    expect(classesDaPilula.some((c) => /negativo-|positivo-/.test(c))).toBe(false)
    const livre = screen.getByText('Parafuso').closest('li')!
    expect(within(livre).queryByText('Pausado')).toBeNull()
    const caixa = screen.getByRole('checkbox', { name: 'Levar SUP-01 — Suporte' }) as HTMLInputElement
    expect(caixa.disabled).toBe(false)
    fireEvent.click(caixa)
    expect(caixa.checked).toBe(true)
  })

  it('entrega vários itens numa requisição só, sem escolher Setor, e recarrega', async () => {
    const { fetchMock, getsDasTarefas } = montarFetch([tarefas(TAREFAS), tarefas([])])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByLabelText('Levar Parafuso'))
    fireEvent.click(screen.getByLabelText('Levar CH-01 — Chassi'))
    // O destino de montagem vem calculado: nenhum item pede escolha de Setor.
    expect(screen.queryByLabelText('Setor de montagem')).toBeNull()
    const quantidades = screen.getAllByLabelText('Quantidade')
    expect(quantidades.map((q) => (q as HTMLInputElement).value)).toEqual(['4', '10', '2'])
    fireEvent.change(quantidades[1], { target: { value: '8' } })
    fireEvent.click(screen.getByRole('button', { name: 'Entregar 3 itens' }))

    expect(await screen.findByText('Nenhum item pronto para levar agora')).toBeTruthy()
    expect(corpoDaEntrega(fetchMock)).toEqual({
      itens: [
        { estruturaItemId: 7, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 4 },
        { estruturaItemId: 8, origem: { posicao: 'AguardandoColeta', setorId: 4, ordem: 2 }, quantidade: 8 },
        { estruturaItemId: 2, origem: { posicao: 'AguardandoColeta', setorId: 5, ordem: 3 }, quantidade: 2 },
      ],
    })
    expect(getsDasTarefas()).toBe(2)
  })

  it('quantidade acima do pronto trava a entrega e diz o limite', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS)]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '5' } })

    expect(screen.getByText('No máximo 4.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Entregar 1 item' })).toHaveProperty('disabled', true)
  })

  it('desmarcar tira o item da entrega', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS)]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByLabelText('Levar SUP-01 — Suporte'))

    expect(screen.queryByLabelText('Quantidade')).toBeNull()
    expect(screen.getByRole('button', { name: 'Entregar' })).toHaveProperty('disabled', true)
  })

  it('item cujo pai não tem Roteiro aparece, mas não se marca', async () => {
    // Desvio D7 do plano 2.
    vi.stubGlobal('fetch', montarFetch([tarefas([{
      setorId: 4, setorNome: 'Solda',
      itens: [{ no: PARAFUSO, ordem: 2, quantidade: 10, destino: { ...DESTINO_MONTAGEM, setorId: null, setorNome: null, paiSemRoteiro: true } }],
    }])]).fetchMock)

    renderizar()

    expect(await screen.findByText('Destino: Montagem de Chassi — o pai não tem Roteiro')).toBeTruthy()
    expect(screen.getByLabelText('Levar Parafuso')).toHaveProperty('disabled', true)
    expect(screen.getByText('Peça ao PCP o Roteiro do pai antes de levar.')).toBeTruthy()
  })

  it('toque duplo em Entregar envia uma vez só', async () => {
    let resolver: (r: Response) => void = () => {}
    const fetchMock = vi.fn((url: string | URL) => {
      if (String(url) === '/api/tarefas') return Promise.resolve(respostaJson(tarefas(TAREFAS)))
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
    const { fetchMock, getsDasTarefas } = montarFetch([tarefas(TAREFAS), tarefas(TAREFAS.slice(1))], () => respostaJson(
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
    const { fetchMock, getsDasTarefas } = montarFetch([tarefas(TAREFAS)], () => respostaJson(
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
        ? { ...DESTINO_MONTAGEM, setorId: null, setorNome: null, paiSemRoteiro: true }
        : DESTINO_MONTAGEM
      return Promise.resolve(respostaJson(tarefas([{
        setorId: 4, setorNome: 'Solda',
        itens: [{ no: PARAFUSO, ordem: 2, quantidade: 10, destino: destinoParafuso }],
      }])))
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
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS)]).fetchMock)

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
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS)]).fetchMock)

    renderizar()

    expect(await screen.findByRole('list', { name: 'Prontos em Corte' })).toBeTruthy()
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.queryByRole('button', { name: /Entregar/ })).toBeNull()
  })
})

describe('TarefasPage — filtro de Material e Pedido', () => {
  beforeEach(() => {
    perfil = 'Movimentador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  const CHAPA_3 = { id: 3, codigo: 'CH-300', descricao: 'Chapa SAE 1020 3,00 mm' }
  const CHAPA_6 = { id: 6, codigo: 'CH-600', descricao: 'Chapa SAE 1020 6,00 mm' }
  const DO_PEDIDO_1 = no({ materiais: [CHAPA_3] })
  const DO_PEDIDO_2 = no({
    id: 21, descricao: 'Tampa', codigoDoComponente: 'TP-01', pedidoId: 2, pedidoNumero: 'PED-2026-02',
    pedidoCliente: 'Beta Máquinas', paiId: null, paiDescricao: null, materiais: [CHAPA_6],
  })
  const item = (n: ReturnType<typeof no>) => ({ no: n, ordem: 1, quantidade: 4, destino: destino() })

  // Corte tem um item de cada Pedido; Solda só do Pedido 2.
  const DOIS_PEDIDOS: TarefasDoSetorDto[] = [
    { setorId: 1, setorNome: 'Corte', itens: [item(DO_PEDIDO_1), item(DO_PEDIDO_2)] },
    { setorId: 4, setorNome: 'Solda', itens: [item(no({ ...DO_PEDIDO_2, id: 22, descricao: 'Trava', codigoDoComponente: 'TR-01' }))] },
  ]

  const abrirFiltro = () => fireEvent.click(screen.getByRole('button', { name: /^Filtrar/ }))

  it('filtra as tarefas por material e pedido e some o grupo vazio', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(DOIS_PEDIDOS)]).fetchMock)

    renderizar()
    await screen.findByRole('list', { name: 'Prontos em Solda' })
    abrirFiltro()
    fireEvent.click(screen.getByRole('checkbox', { name: 'PED-2026-01 · Metalúrgica Alfa' }))

    const corte = screen.getByRole('list', { name: 'Prontos em Corte' })
    expect(within(corte).getByText('SUP-01 — Suporte')).toBeTruthy()
    expect(within(corte).queryByText('TP-01 — Tampa')).toBeNull()
    expect(screen.queryByRole('list', { name: 'Prontos em Solda' })).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Em Solda' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Remover filtro Pedido PED-2026-01' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Chapa SAE 1020 6,00 mm' }))

    expect(within(screen.getByRole('list', { name: 'Prontos em Corte' })).queryByText('SUP-01 — Suporte')).toBeNull()
    expect(within(screen.getByRole('list', { name: 'Prontos em Corte' })).getByText('TP-01 — Tampa')).toBeTruthy()
    expect(screen.getByRole('list', { name: 'Prontos em Solda' })).toBeTruthy()
  })

  it('a selecao da URL filtra as tarefas na primeira carga', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(DOIS_PEDIDOS)]).fetchMock)

    renderizar('/?material=3')

    const corte = await screen.findByRole('list', { name: 'Prontos em Corte' })
    expect(within(corte).getByText('SUP-01 — Suporte')).toBeTruthy()
    expect(within(corte).queryByText('TP-01 — Tampa')).toBeNull()
    expect(screen.queryByRole('list', { name: 'Prontos em Solda' })).toBeNull()
  })

  // O Quadro (Pedido 1) com a Longarina de CHAPA_6: o pai não tem Material, o filho pronto tem.
  const QUADRO_CH6: KitDto = {
    ...KIT_QUADRO,
    filhos: [{ ...KIT_QUADRO.filhos[0], no: { ...KIT_QUADRO.filhos[0].no, materiais: [CHAPA_6] } }, KIT_QUADRO.filhos[1]],
  }
  const CAIXA_SEM_MATERIAL: KitDto = {
    ...KIT_QUADRO,
    pai: no({ id: 60, descricao: 'Caixa', codigoDoComponente: 'CX-01', paiId: null, paiDescricao: null, agrupamentoTipo: 'Kit' }),
  }

  it('filtro por Material do filho mostra o Kit', async () => {
    const incompleto = (k: KitDto): KitDto => ({ ...k, conjuntos: 0, filhos: k.filhos.map((f) => ({ ...f, pronto: 1 })) })
    vi.stubGlobal('fetch', montarFetch([tarefas(
      DOIS_PEDIDOS, [QUADRO_CH6, CAIXA_SEM_MATERIAL], [incompleto(QUADRO_CH6), incompleto(CAIXA_SEM_MATERIAL)],
    )]).fetchMock)

    renderizar('/?material=6')

    const kits = await screen.findByRole('list', { name: 'Kits montáveis' })
    expect(within(kits).getByText('QD-01 — Quadro')).toBeTruthy()
    expect(within(kits).queryByText('CX-01 — Caixa')).toBeNull()
    // A contagem da seção recolhida é a dos visíveis.
    const incompletos = screen.getByText('Kits incompletos (1)').closest('details')!
    expect(within(incompletos).getByText('QD-01 — Quadro')).toBeTruthy()
    expect(within(incompletos).queryByText('CX-01 — Caixa')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Remover filtro Chapa SAE 1020 6,00 mm' }))

    expect(within(screen.getByRole('list', { name: 'Kits montáveis' })).getByText('CX-01 — Caixa')).toBeTruthy()
    expect(screen.getByText('Kits incompletos (2)')).toBeTruthy()
  })

  it('o filtro que esconde todos os Kits incompletos esconde a seção', async () => {
    const incompleto: KitDto = { ...CAIXA_SEM_MATERIAL, conjuntos: 0 }
    vi.stubGlobal('fetch', montarFetch([tarefas(DOIS_PEDIDOS, [], [incompleto])]).fetchMock)

    renderizar('/?material=6')

    await screen.findByRole('list', { name: 'Prontos em Solda' })
    expect(screen.queryByText(/Kits incompletos/)).toBeNull()
  })

  it('Kit marcado e oculto pelo filtro continua na entrega e conta como oculto', async () => {
    const { fetchMock } = montarFetch([tarefas(DOIS_PEDIDOS, [KIT_QUADRO]), tarefas([])])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    fireEvent.click(screen.getByLabelText('Levar TP-01 — Tampa'))
    abrirFiltro()
    fireEvent.click(screen.getByRole('checkbox', { name: 'PED-2026-02 · Beta Máquinas' }))

    expect(screen.queryByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' })).toBeNull()
    expect(screen.queryByRole('heading', { name: 'Kits montáveis' })).toBeNull()
    expect(screen.getByText('1 marcado oculto pelo filtro')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Entregar 1 Kit e 1 item' }))

    await waitFor(() => expect(fetchMock.mock.calls.some((c) => String(c[0]) === '/api/entregas')).toBe(true))
    expect(corpoDaEntrega(fetchMock)).toEqual({ itens: [
      { estruturaItemId: 21, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 4 },
      { estruturaItemId: 31, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 8 },
      { estruturaItemId: 32, origem: { posicao: 'AguardandoColeta', setorId: 3, ordem: 2 }, quantidade: 2 },
    ] })
  })

  it('Marcar todos respeita o filtro também nos Kits', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(DOIS_PEDIDOS, [KIT_QUADRO])]).fetchMock)

    renderizar('/?pedido=2')
    fireEvent.click(await screen.findByRole('button', { name: 'Marcar todos' }))

    expect(screen.getByRole('button', { name: 'Entregar 2 itens' })).toBeTruthy()
    expect(screen.queryByText(/oculto/)).toBeNull()
  })

  it('o filtro que esconde todos os grupos mas mostra um Kit não é o vazio do filtro', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas([DOIS_PEDIDOS[1]], [KIT_QUADRO])]).fetchMock)

    renderizar('/?pedido=1')

    expect(await screen.findByRole('list', { name: 'Kits montáveis' })).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Prontos em Solda' })).toBeNull()
    expect(screen.queryByText('Nada para levar com esses filtros')).toBeNull()
  })

  it('o filtro que esconde o único Kit montável mostra o vazio do filtro', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas([], [KIT_QUADRO])]).fetchMock)

    renderizar('/?pedido=2')

    expect(await screen.findByText('Nada para levar com esses filtros')).toBeTruthy()
    expect(screen.queryByText('Nenhum item pronto para levar agora')).toBeNull()
  })

  it('todos os grupos vazios pelo filtro mostram o vazio do filtro', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas([DOIS_PEDIDOS[1]])]).fetchMock)

    renderizar('/?pedido=1')

    expect(await screen.findByText('Nada para levar com esses filtros')).toBeTruthy()
    expect(screen.queryByText('Nenhum item pronto para levar agora')).toBeNull()
    expect(screen.queryByRole('list', { name: 'Prontos em Solda' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }))

    expect(await screen.findByRole('list', { name: 'Prontos em Solda' })).toBeTruthy()
    expect(screen.queryByText('Nada para levar com esses filtros')).toBeNull()
  })

  it('sem tarefa nenhuma nao mostra o filtro, so o vazio de sempre', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas([])]).fetchMock)

    renderizar('/?pedido=1')

    expect(await screen.findByText('Nenhum item pronto para levar agora')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Filtrar/ })).toBeNull()
    expect(screen.queryByText('Nada para levar com esses filtros')).toBeNull()
  })

  it('item marcado e oculto pelo filtro continua na entrega', async () => {
    const { fetchMock } = montarFetch([tarefas(DOIS_PEDIDOS), tarefas([])])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByLabelText('Levar TP-01 — Tampa'))
    expect(screen.queryByText(/oculto/)).toBeNull()

    abrirFiltro()
    fireEvent.click(screen.getByRole('checkbox', { name: 'PED-2026-01 · Metalúrgica Alfa' }))

    expect(screen.queryByLabelText('Levar TP-01 — Tampa')).toBeNull()
    expect(screen.getByRole('button', { name: 'Entregar 2 itens' })).toBeTruthy()
    expect(screen.getByText('1 marcado oculto pelo filtro')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Entregar 2 itens' }))

    await waitFor(() => expect(fetchMock.mock.calls.some((c) => String(c[0]) === '/api/entregas')).toBe(true))
    expect(corpoDaEntrega(fetchMock)).toEqual({ itens: [
      { estruturaItemId: 7, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 4 },
      { estruturaItemId: 21, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 4 },
    ] })
  })

  it('varios marcados ocultos vao no plural', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(DOIS_PEDIDOS)]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar TP-01 — Tampa'))
    fireEvent.click(screen.getByLabelText('Levar TR-01 — Trava'))
    abrirFiltro()
    fireEvent.click(screen.getByRole('checkbox', { name: 'PED-2026-01 · Metalúrgica Alfa' }))

    expect(screen.getByText('2 marcados ocultos pelo filtro')).toBeTruthy()
  })

  it('filtrar nao tira item da selecao nem mostra o aviso de saiu da lista', async () => {
    // Timers falsos: a limpeza da seleção só reavalia quando a resposta muda, então é a
    // atualização periódica com o filtro ativo que prova que ela olha a resposta INTEIRA.
    vi.useFakeTimers()
    const { fetchMock, getsDasTarefas } = montarFetch([tarefas(DOIS_PEDIDOS)])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByLabelText('Levar TP-01 — Tampa'))
    abrirFiltro()
    fireEvent.click(screen.getByRole('checkbox', { name: 'PED-2026-01 · Metalúrgica Alfa' }))

    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('button', { name: 'Entregar 1 item' })).toBeTruthy()

    // A mesma resposta volta na atualização periódica, com o filtro ainda ativo: o item marcado
    // continua na resposta, só escondido, então segue marcado, contado como oculto e sem aviso.
    const getsAntes = getsDasTarefas()
    await act(async () => { await vi.advanceTimersByTimeAsync(INTERVALO_DA_EXECUCAO_MS) })
    // Sem esta afirmação o teste passaria também se a atualização nunca acontecesse.
    expect(getsDasTarefas()).toBeGreaterThan(getsAntes)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('button', { name: 'Entregar 1 item' })).toBeTruthy()
    expect(screen.getByText('1 marcado oculto pelo filtro')).toBeTruthy()

    // Tirar o filtro devolve o item ainda marcado, com o que já estava digitado.
    fireEvent.click(screen.getByRole('button', { name: 'Remover filtro Pedido PED-2026-01' }))
    expect((screen.getByLabelText('Levar TP-01 — Tampa') as HTMLInputElement).checked).toBe(true)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByText(/oculto/)).toBeNull()
  })

  describe('Marcar todos', () => {
    const SEM_ROTEIRO = { ...DESTINO_MONTAGEM, setorId: null, setorNome: null, paiSemRoteiro: true }
    const marcado = (nome: string) => (screen.getByLabelText(nome) as HTMLInputElement).checked

    it('Marcar todos marca os itens visiveis de todos os grupos e pula o pai sem Roteiro', async () => {
      vi.stubGlobal('fetch', montarFetch([tarefas([
        TAREFAS[0],
        { setorId: 4, setorNome: 'Solda', itens: [{ no: PARAFUSO, ordem: 2, quantidade: 10, destino: SEM_ROTEIRO }] },
        TAREFAS[2],
      ])]).fetchMock)

      renderizar()
      fireEvent.click(await screen.findByRole('button', { name: 'Marcar todos' }))

      expect(marcado('Levar SUP-01 — Suporte')).toBe(true)
      expect(marcado('Levar CH-01 — Chassi')).toBe(true)
      expect(marcado('Levar Parafuso')).toBe(false)
      expect(screen.getAllByLabelText('Quantidade').map((q) => (q as HTMLInputElement).value)).toEqual(['4', '2'])
      expect(screen.getByRole('button', { name: 'Entregar 2 itens' })).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Desmarcar todos' })).toBeTruthy()
    })

    it('Marcar todos respeita o filtro', async () => {
      vi.stubGlobal('fetch', montarFetch([tarefas(DOIS_PEDIDOS)]).fetchMock)

      renderizar('/?pedido=2')
      fireEvent.click(await screen.findByRole('button', { name: 'Marcar todos' }))

      expect(marcado('Levar TP-01 — Tampa')).toBe(true)
      expect(marcado('Levar TR-01 — Trava')).toBe(true)
      expect(screen.queryByLabelText('Levar SUP-01 — Suporte')).toBeNull()
      expect(screen.getByRole('button', { name: 'Entregar 2 itens' })).toBeTruthy()
      // Nada oculto: o item do outro Pedido não foi marcado por baixo do filtro.
      expect(screen.queryByText(/oculto/)).toBeNull()
    })

    it('Marcar todos preserva a quantidade ja digitada', async () => {
      vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS)]).fetchMock)

      renderizar()
      fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
      fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '3' } })
      fireEvent.click(screen.getByRole('button', { name: 'Marcar todos' }))

      expect(screen.getAllByLabelText('Quantidade').map((q) => (q as HTMLInputElement).value)).toEqual(['3', '10', '2'])
      expect(screen.getByRole('button', { name: 'Entregar 3 itens' })).toBeTruthy()
    })

    it('Desmarcar todos desmarca so os visiveis', async () => {
      vi.stubGlobal('fetch', montarFetch([tarefas(DOIS_PEDIDOS)]).fetchMock)

      renderizar()
      fireEvent.click(await screen.findByLabelText('Levar TP-01 — Tampa'))
      abrirFiltro()
      fireEvent.click(screen.getByRole('checkbox', { name: 'PED-2026-01 · Metalúrgica Alfa' }))
      fireEvent.click(screen.getByRole('button', { name: 'Marcar todos' }))

      expect(marcado('Levar SUP-01 — Suporte')).toBe(true)
      expect(screen.getByRole('button', { name: 'Entregar 2 itens' })).toBeTruthy()

      fireEvent.click(screen.getByRole('button', { name: 'Desmarcar todos' }))

      expect(marcado('Levar SUP-01 — Suporte')).toBe(false)
      expect(screen.getByRole('button', { name: 'Entregar 1 item' })).toBeTruthy()
      expect(screen.getByText('1 marcado oculto pelo filtro')).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Marcar todos' })).toBeTruthy()
    })

    it('sem item visivel nao bloqueado nao ha Marcar todos', async () => {
      vi.stubGlobal('fetch', montarFetch([tarefas([
        { setorId: 4, setorNome: 'Solda', itens: [{ no: PARAFUSO, ordem: 2, quantidade: 10, destino: SEM_ROTEIRO }] },
      ])]).fetchMock)

      renderizar()

      await screen.findByRole('list', { name: 'Prontos em Solda' })
      expect(screen.queryByRole('button', { name: /^(Marcar|Desmarcar) todos$/ })).toBeNull()
    })

    it('sem permissao de entregar nao ha Marcar todos', async () => {
      perfil = 'Operador'
      vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS)]).fetchMock)

      renderizar()

      await screen.findByRole('list', { name: 'Prontos em Corte' })
      expect(screen.queryByRole('button', { name: /^(Marcar|Desmarcar) todos$/ })).toBeNull()
    })
  })
})

describe('TarefasPage — Kits', () => {
  beforeEach(() => {
    perfil = 'Movimentador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  /** Kit Caixa (id 60): um filho só, Tampa (razão 2, 2 prontas no Corte). Dá para levar 1 conjunto. */
  const KIT_CAIXA: KitDto = {
    pai: no({ id: 60, descricao: 'Caixa', codigoDoComponente: 'CX-01', paiId: null, paiDescricao: null, agrupamentoTipo: 'Kit' }),
    destino: { id: 4, nome: 'Solda' },
    conjuntos: 1,
    filhos: [filhoDoKit({
      no: no({ id: 61, descricao: 'Tampa', codigoDoComponente: 'TP-09', paiId: 60, paiDescricao: 'Caixa', agrupamentoTipo: 'Kit' }),
      quantidadePorPai: 2, pronto: 2,
    })],
  }

  /** O Quadro sem conjunto completo: 3 Longarinas prontas de 4, nenhuma Travessa. */
  const QUADRO_INCOMPLETO: KitDto = {
    ...KIT_QUADRO,
    conjuntos: 0,
    filhos: [{ ...KIT_QUADRO.filhos[0], pronto: 3 }, { ...KIT_QUADRO.filhos[1], pronto: 0, origem: null, ordem: null }],
  }

  const marcado = (nome: string) => (screen.getByLabelText(nome) as HTMLInputElement).checked
  const SAIU = /Um item que você tinha marcado não está mais pronto/

  it('Kit montável aparece em Kits montáveis, antes dos grupos por Setor', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS, [KIT_QUADRO])]).fetchMock)

    renderizar()

    const titulo = await screen.findByRole('heading', { name: 'Kits montáveis' })
    const lista = screen.getByRole('list', { name: 'Kits montáveis' })
    expect(within(lista).getByText('QD-01 — Quadro')).toBeTruthy()
    expect(within(lista).getByText('Dá para levar 2 conjunto(s)')).toBeTruthy()
    expect(within(lista).getByText('Destino: Solda (início de Quadro)')).toBeTruthy()
    // "Kits montáveis" vem primeiro: é a tarefa principal de quem trabalha com Kit (spec da Fase 3B, seção 5.1).
    const emCorte = screen.getByRole('heading', { name: 'Em Corte' })
    expect(titulo.compareDocumentPosition(emCorte) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('sem Kit montável não há a seção Kits montáveis', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS)]).fetchMock)

    renderizar()

    await screen.findByRole('list', { name: 'Prontos em Corte' })
    expect(screen.queryByRole('heading', { name: 'Kits montáveis' })).toBeNull()
    expect(screen.queryByText(/Kits incompletos/)).toBeNull()
  })

  it('marcar o Kit e entregar manda todos os filhos com N × razão', async () => {
    const { fetchMock, getsDasTarefas } = montarFetch([tarefas([], [KIT_QUADRO]), tarefas([])])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    // O campo já vem com o N máximo.
    expect((screen.getByLabelText('Conjuntos') as HTMLInputElement).value).toBe('2')
    fireEvent.change(screen.getByLabelText('Conjuntos'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Entregar 1 Kit' }))

    expect(await screen.findByText('Nenhum item pronto para levar agora')).toBeTruthy()
    // A entrega aceita esvazia a seleção ANTES da recarga: o Kit que a própria entrega levou não é "saiu da lista".
    expect(screen.queryByText(SAIU)).toBeNull()
    expect(corpoDaEntrega(fetchMock)).toEqual({
      itens: [
        { estruturaItemId: 31, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 4 },
        { estruturaItemId: 32, origem: { posicao: 'AguardandoColeta', setorId: 3, ordem: 2 }, quantidade: 1 },
      ],
    })
    expect(getsDasTarefas()).toBe(2)
  })

  it('Kit e item juntos numa entrega só', async () => {
    const { fetchMock } = montarFetch([tarefas(TAREFAS, [KIT_QUADRO]), tarefas([])])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    fireEvent.click(screen.getByRole('button', { name: 'Entregar 1 Kit e 1 item' }))

    await screen.findByText('Nenhum item pronto para levar agora')
    expect(corpoDaEntrega(fetchMock)).toEqual({
      itens: [
        { estruturaItemId: 7, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 4 },
        { estruturaItemId: 31, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 8 },
        { estruturaItemId: 32, origem: { posicao: 'AguardandoColeta', setorId: 3, ordem: 2 }, quantidade: 2 },
      ],
    })
  })

  it('o botão conta Kits e itens separados', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS, [KIT_QUADRO, KIT_CAIXA])]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    expect(screen.getByRole('button', { name: 'Entregar 1 Kit' })).toBeTruthy()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit CX-01 — Caixa' }))
    expect(screen.getByRole('button', { name: 'Entregar 2 Kits' })).toBeTruthy()
    fireEvent.click(screen.getByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByLabelText('Levar Parafuso'))
    fireEvent.click(screen.getByLabelText('Levar CH-01 — Chassi'))
    expect(screen.getByRole('button', { name: 'Entregar 2 Kits e 3 itens' })).toBeTruthy()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit CX-01 — Caixa' }))
    expect(screen.getByRole('button', { name: 'Entregar 3 itens' })).toBeTruthy()
  })

  it('Marcar todos marca os Kits montáveis, cada um com o N máximo', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS, [KIT_QUADRO, KIT_CAIXA])]).fetchMock)

    renderizar()
    // Todos os itens marcados à mão, os Kits não: ainda não está tudo marcado.
    fireEvent.click(await screen.findByLabelText('Levar SUP-01 — Suporte'))
    fireEvent.click(screen.getByLabelText('Levar Parafuso'))
    fireEvent.click(screen.getByLabelText('Levar CH-01 — Chassi'))
    expect(screen.getByRole('button', { name: 'Marcar todos' })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Marcar todos' }))

    expect(marcado('Levar o Kit QD-01 — Quadro')).toBe(true)
    expect(marcado('Levar o Kit CX-01 — Caixa')).toBe(true)
    expect(screen.getAllByLabelText('Conjuntos').map((c) => (c as HTMLInputElement).value)).toEqual(['2', '1'])
    expect(screen.getByRole('button', { name: 'Entregar 2 Kits e 3 itens' })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Desmarcar todos' }))

    expect(marcado('Levar o Kit QD-01 — Quadro')).toBe(false)
    expect(marcado('Levar o Kit CX-01 — Caixa')).toBe(false)
    expect(screen.getByRole('button', { name: 'Entregar' })).toBeTruthy()
  })

  it('Marcar todos preserva os conjuntos já digitados', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas([], [KIT_QUADRO, KIT_CAIXA])]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    fireEvent.change(screen.getByLabelText('Conjuntos'), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Marcar todos' }))

    // O Quadro continua com o 1 digitado (e não o máximo, 2); a Caixa entra com o máximo dela, 1.
    expect(screen.getAllByLabelText('Conjuntos').map((c) => (c as HTMLInputElement).value)).toEqual(['1', '1'])
    expect(screen.getByRole('button', { name: 'Entregar 2 Kits' })).toBeTruthy()
  })

  it('só com Kits montáveis há Marcar todos e Entregar, e não o vazio', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas([], [KIT_QUADRO])]).fetchMock)

    renderizar()

    fireEvent.click(await screen.findByRole('button', { name: 'Marcar todos' }))
    expect(screen.getByRole('button', { name: 'Entregar 1 Kit' })).toBeTruthy()
    expect(screen.queryByText('Nenhum item pronto para levar agora')).toBeNull()
  })

  it('Conjuntos inválido trava o Entregar', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas([], [KIT_QUADRO])]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    expect(screen.getByRole('button', { name: 'Entregar 1 Kit' })).toHaveProperty('disabled', false)

    fireEvent.change(screen.getByLabelText('Conjuntos'), { target: { value: '1,5' } })
    expect(screen.getByRole('button', { name: 'Entregar 1 Kit' })).toHaveProperty('disabled', true)

    fireEvent.change(screen.getByLabelText('Conjuntos'), { target: { value: '3' } })
    expect(screen.getByText('No máximo 2.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Entregar 1 Kit' })).toHaveProperty('disabled', true)

    fireEvent.change(screen.getByLabelText('Conjuntos'), { target: { value: '2' } })
    expect(screen.getByRole('button', { name: 'Entregar 1 Kit' })).toHaveProperty('disabled', false)
  })

  it('Kits incompletos vem recolhida com a contagem, depois dos grupos', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas(TAREFAS, [], [QUADRO_INCOMPLETO])]).fetchMock)

    renderizar()

    const resumo = await screen.findByText('Kits incompletos (1)')
    const secao = resumo.closest('details')!
    expect(secao.open).toBe(false)
    expect(within(secao).getByText('QD-01 — Quadro')).toBeTruthy()
    expect(within(secao).queryByRole('checkbox')).toBeNull()
    const emPintura = screen.getByRole('heading', { name: 'Em Pintura' })
    expect(emPintura.compareDocumentPosition(resumo) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    // O incompleto não é tarefa: não entra no Marcar todos nem no Entregar.
    fireEvent.click(screen.getByRole('button', { name: 'Marcar todos' }))
    expect(screen.getByRole('button', { name: 'Entregar 3 itens' })).toBeTruthy()
  })

  it('só Kits incompletos: mostra o vazio e a seção recolhida', async () => {
    vi.stubGlobal('fetch', montarFetch([tarefas([], [], [QUADRO_INCOMPLETO])]).fetchMock)

    renderizar()

    expect(await screen.findByText('Nenhum item pronto para levar agora')).toBeTruthy()
    expect(screen.getByText('Kits incompletos (1)').closest('details')?.open).toBe(false)
    expect(screen.queryByRole('button', { name: /Entregar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^(Marcar|Desmarcar) todos$/ })).toBeNull()
  })

  it('Kit que sumiu na recarga sai da seleção com aviso', async () => {
    vi.useFakeTimers()
    const { fetchMock, getsDasTarefas } = montarFetch([tarefas(TAREFAS, [KIT_QUADRO]), tarefas(TAREFAS)])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    expect(screen.getByRole('button', { name: 'Entregar 1 Kit' })).toBeTruthy()
    expect(screen.queryByText(SAIU)).toBeNull()

    await act(async () => { await vi.advanceTimersByTimeAsync(INTERVALO_DA_EXECUCAO_MS) })

    expect(getsDasTarefas()).toBe(2)
    expect(screen.getByText(SAIU)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Entregar' })).toHaveProperty('disabled', true)
  })

  it('Kit que virou incompleto na recarga também sai da seleção com aviso', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', montarFetch([tarefas([], [KIT_QUADRO]), tarefas([], [], [QUADRO_INCOMPLETO])]).fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))

    await act(async () => { await vi.advanceTimersByTimeAsync(INTERVALO_DA_EXECUCAO_MS) })

    expect(screen.getByText(SAIU)).toBeTruthy()
    expect(screen.getByText('Kits incompletos (1)')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Entregar/ })).toBeNull()
  })

  it('a recarga preserva o Kit marcado e os conjuntos digitados, sem aviso', async () => {
    vi.useFakeTimers()
    const { fetchMock, getsDasTarefas } = montarFetch([tarefas(TAREFAS, [KIT_QUADRO])])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    fireEvent.change(screen.getByLabelText('Conjuntos'), { target: { value: '1' } })

    await act(async () => { await vi.advanceTimersByTimeAsync(INTERVALO_DA_EXECUCAO_MS) })

    expect(getsDasTarefas()).toBe(2)
    expect(marcado('Levar o Kit QD-01 — Quadro')).toBe(true)
    expect(screen.getByLabelText('Conjuntos')).toHaveProperty('value', '1')
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('Kit que continua montável com menos conjuntos fica marcado, e o N acima do novo máximo trava o Entregar', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', montarFetch([tarefas([], [KIT_QUADRO]), tarefas([], [{ ...KIT_QUADRO, conjuntos: 1 }])]).fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    expect(screen.getByLabelText('Conjuntos')).toHaveProperty('value', '2')

    await act(async () => { await vi.advanceTimersByTimeAsync(INTERVALO_DA_EXECUCAO_MS) })

    expect(marcado('Levar o Kit QD-01 — Quadro')).toBe(true)
    expect(screen.getByText('No máximo 1.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Entregar 1 Kit' })).toHaveProperty('disabled', true)
    expect(screen.queryByText(SAIU)).toBeNull()
  })

  it('409 da entrega do Kit mostra a frase, recarrega, e o N acima do novo máximo fica marcado e trava', async () => {
    // AlemDoQueOPaiPrecisa: outra pessoa já levou um conjunto; na recarga o Kit dá para levar só 1.
    const { fetchMock, getsDasTarefas } = montarFetch(
      [tarefas([], [KIT_QUADRO]), tarefas([], [{ ...KIT_QUADRO, conjuntos: 1 }])],
      () => respostaJson({ erro: 'AlemDoQueOPaiPrecisa', mensagem: 'Quadro só precisa de 1 conjunto.' }, 409),
    )
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Levar o Kit QD-01 — Quadro' }))
    fireEvent.click(screen.getByRole('button', { name: 'Entregar 1 Kit' }))

    expect(await screen.findByText('Quadro só precisa de 1 conjunto.')).toBeTruthy()
    await waitFor(() => expect(getsDasTarefas()).toBe(2))
    expect(await screen.findByText('No máximo 1.')).toBeTruthy()
    expect(marcado('Levar o Kit QD-01 — Quadro')).toBe(true)
    expect(screen.getByRole('button', { name: 'Entregar 1 Kit' })).toHaveProperty('disabled', true)
    expect(screen.queryByText(SAIU)).toBeNull()
  })

  it('sem permissão de entregar, o Kit aparece sem caixa', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', montarFetch([tarefas([], [KIT_QUADRO])]).fetchMock)

    renderizar()

    expect(await screen.findByRole('list', { name: 'Kits montáveis' })).toBeTruthy()
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.queryByRole('button', { name: /Entregar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^(Marcar|Desmarcar) todos$/ })).toBeNull()
  })
})
