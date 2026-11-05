// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { PainelDeBaixa } from './PainelDeBaixa'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import type { Posicao, SaldoDto } from '../api/execucao'
import type { PreviaDaBaixaDto } from '../api/baixas'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const BAIXA = '/api/estrutura/7/baixas'
const PREVIA = '/api/estrutura/7/baixas/previa'

const saldo = (posicao: Posicao, quantidade: number, setorId: number | null = null, ordem: number | null = null): SaldoDto =>
  ({ posicao, quantidade, setorId, setorNome: setorId === null ? null : 'Corte', ordem })

const SALDOS = [
  saldo('AIniciar', 4), saldo('NoSetor', 6, 1, 1), saldo('AguardandoLiberacao', 2), saldo('Montado', 3),
]
const ITEM = { id: 7, descricao: 'Suporte', ehPeca: false }

function previa(parcial: Partial<PreviaDaBaixaDto> = {}): PreviaDaBaixaDto {
  return {
    linhas: [
      { estruturaItemId: 7, descricao: 'Suporte', codigo: 'SUP-01', origemPosicao: 'AIniciar', origemSetorId: null, origemSetorNome: null, origemOrdem: null, quantidade: 3 },
      { estruturaItemId: 8, descricao: 'Parafuso', codigo: null, origemPosicao: 'NoSetor', origemSetorId: 1, origemSetorNome: 'Corte', origemOrdem: 1, quantidade: 6 },
    ],
    viramSobra: [{ estruturaItemId: 8, descricao: 'Parafuso', quantidade: 2 }],
    travaNoTopo: null,
    ...parcial,
  }
}
const TRAVA = { pecaId: 2, descricao: 'Chassi', quantidade: 2, aIniciar: 2 }

function chamadas(fetchMock: ReturnType<typeof vi.fn>, rota: string): unknown[] {
  return fetchMock.mock.calls
    .filter((c) => String(c[0]) === rota)
    .map((c) => JSON.parse((c[1] as RequestInit).body as string))
}

function renderizar(props: Partial<Parameters<typeof PainelDeBaixa>[0]> = {}) {
  const aoFechar = vi.fn()
  const aoConcluir = vi.fn()
  render(<PainelDeBaixa no={ITEM} saldos={SALDOS} aoFechar={aoFechar} aoConcluir={aoConcluir} {...props} />)
  return { aoFechar, aoConcluir }
}

const formulario = () => screen.getByRole('form', { name: 'Dar baixa' })
const enviar = (nome: string | RegExp = /^Dar baixa/) =>
  within(formulario()).getByRole('button', { name: nome }) as HTMLButtonElement
const campo = (nome: string) => screen.getByRole('textbox', { name: nome }) as HTMLInputElement
const escolher = (nome: string) => fireEvent.click(screen.getByRole('radio', { name: nome }))
const digitar = (nome: string, valor: string) => fireEvent.change(campo(nome), { target: { value: valor } })
const esperarPrevia = () => screen.findByRole('region', { name: 'O que acontece' }, { timeout: 2000 })

describe('PainelDeBaixa', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('abre com os três motivos, nenhum marcado, e sem poder enviar', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))

    renderizar()

    const motivos = within(screen.getByRole('group', { name: 'Motivo' })).getAllByRole('radio')
    expect(motivos.map((r) => (r as HTMLInputElement).labels?.[0].textContent)).toEqual([
      'Sumiu no armazém', 'Morreu num processo', 'Descarte',
    ])
    expect(motivos.every((r) => !(r as HTMLInputElement).checked)).toBe(true)
    expect(screen.getByRole('radio', { name: 'Tudo o que está em produção' })).toBeTruthy()
    expect(screen.getByRole('radio', { name: 'Uma parte' })).toBeTruthy()
    expect(enviar().disabled).toBe(true)
  })

  it('em "Uma parte" dá um campo por posição de origem com saldo e explica a que aguarda liberação', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar()

    escolher('Uma parte')

    expect(campo('4 a iniciar')).toBeTruthy()
    expect(campo('6 em Corte (passo 1)')).toBeTruthy()
    expect(screen.getAllByRole('textbox', { name: /^\d/ })).toHaveLength(2)
    // Montado nunca é origem; AguardandoLiberacao aparece sem campo e com o porquê.
    expect(screen.queryByText(/montados no pai/)).toBeNull()
    expect(screen.getByText(/2 aguardando liberação.*aguarda a resposta da Gestão; estorne o pedido de liberação para dar baixa/)).toBeTruthy()
  })

  it('mostra a prévia do servidor com as linhas, os filhos que saem junto e os que viram sobra', async () => {
    const fetchMock = fetchPorRota({ [PREVIA]: () => respostaJson(previa()) })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()
    escolher('Sumiu no armazém')
    escolher('Uma parte')

    digitar('4 a iniciar', '3')

    const bloco = await esperarPrevia()
    expect(within(bloco).getByText('3 de SUP-01 — Suporte, a iniciar')).toBeTruthy()
    expect(within(bloco).getByText('6 de Parafuso, Corte (passo 1)')).toBeTruthy()
    expect(within(bloco).getByText('2 de Parafuso')).toBeTruthy()
    expect(chamadas(fetchMock, PREVIA)).toEqual([{
      motivo: 'PerdaArmazem', observacao: null, tudo: false, incluirTopo: true,
      origens: [{ posicao: 'AIniciar', setorId: null, ordem: null, quantidade: 3 }],
    }])
  })

  it('quantidade acima do saldo mostra o erro no campo e não consulta nem deixa enviar', async () => {
    const fetchMock = fetchPorRota({ [PREVIA]: () => respostaJson(previa()) })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()
    escolher('Sumiu no armazém')
    escolher('Uma parte')

    digitar('4 a iniciar', '5')

    expect(screen.getByText('No máximo 4.')).toBeTruthy()
    await new Promise((r) => setTimeout(r, 450))
    expect(chamadas(fetchMock, PREVIA)).toEqual([])
    expect(enviar().disabled).toBe(true)
  })

  it('o enviar diz a soma, manda o corpo do contrato, fecha e avisa quem abriu', async () => {
    const fetchMock = fetchPorRota({
      [PREVIA]: () => respostaJson(previa()),
      [BAIXA]: () => respostaJson({ id: 90 }, 201),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar, aoConcluir } = renderizar()
    escolher('Morreu num processo')
    escolher('Uma parte')
    digitar('4 a iniciar', '1,5')
    digitar('6 em Corte (passo 1)', '2')
    fireEvent.change(screen.getByRole('textbox', { name: /Observação/ }), { target: { value: '  caiu da prateleira ' } })
    await esperarPrevia()

    await waitFor(() => expect(enviar('Dar baixa em 3,5').disabled).toBe(false))
    fireEvent.click(enviar('Dar baixa em 3,5'))

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(chamadas(fetchMock, BAIXA)).toEqual([{
      motivo: 'MortaEmProcesso', observacao: 'caiu da prateleira', tudo: false, incluirTopo: false,
      origens: [
        { posicao: 'AIniciar', setorId: null, ordem: null, quantidade: 1.5 },
        { posicao: 'NoSetor', setorId: 1, ordem: 1, quantidade: 2 },
      ],
    }])
  })

  it('"Tudo o que está em produção" manda tudo, sem origens, e o enviar soma os saldos que saem', async () => {
    const fetchMock = fetchPorRota({
      [PREVIA]: () => respostaJson(previa()),
      [BAIXA]: () => respostaJson({ id: 90 }, 201),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()
    escolher('Descarte')
    escolher('Tudo o que está em produção')
    await esperarPrevia()

    // 4 a iniciar + 6 no Setor; os que aguardam liberação não entram na soma.
    await waitFor(() => expect(enviar('Dar baixa em 10').disabled).toBe(false))
    fireEvent.click(enviar('Dar baixa em 10'))

    await waitFor(() => expect(aoFechar).toHaveBeenCalled())
    expect(chamadas(fetchMock, BAIXA)).toEqual([{ motivo: 'Descarte', observacao: null, tudo: true, incluirTopo: false }])
  })

  it('com travaNoTopo em "Uma parte" a caixa vem marcada e o corpo leva incluirTopo', async () => {
    const fetchMock = fetchPorRota({
      [PREVIA]: () => respostaJson(previa({ travaNoTopo: TRAVA })),
      [BAIXA]: () => respostaJson({ id: 90 }, 201),
    })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()
    escolher('Sumiu no armazém')
    escolher('Uma parte')
    digitar('4 a iniciar', '1')
    await esperarPrevia()

    const caixa = await screen.findByRole('checkbox', { name: 'Isso impede montar 2 Chassi. Dar baixa também delas' })
    expect((caixa as HTMLInputElement).checked).toBe(true)
    await waitFor(() => expect(enviar('Dar baixa em 1').disabled).toBe(false))
    fireEvent.click(enviar('Dar baixa em 1'))

    await waitFor(() => expect(chamadas(fetchMock, BAIXA)).toHaveLength(1))
    expect((chamadas(fetchMock, BAIXA)[0] as { incluirTopo: boolean }).incluirTopo).toBe(true)
  })

  it('desmarcar a caixa consulta de novo sem o topo e a caixa não some', async () => {
    const fetchMock = fetchPorRota({ [PREVIA]: () => respostaJson(previa({ travaNoTopo: TRAVA })) })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()
    escolher('Sumiu no armazém')
    escolher('Uma parte')
    digitar('4 a iniciar', '1')
    const caixa = await screen.findByRole('checkbox', { name: /Isso impede montar/ })

    fireEvent.click(caixa)

    await waitFor(() => expect(chamadas(fetchMock, PREVIA)).toHaveLength(2), { timeout: 2000 })
    expect((chamadas(fetchMock, PREVIA)[1] as { incluirTopo: boolean }).incluirTopo).toBe(false)
    expect((screen.getByRole('checkbox', { name: /Isso impede montar/ }) as HTMLInputElement).checked).toBe(false)
  })

  it('em "Tudo" com motivo de perda a caixa também aparece (D14)', async () => {
    const fetchMock = fetchPorRota({ [PREVIA]: () => respostaJson(previa({ travaNoTopo: TRAVA })) })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()
    escolher('Morreu num processo')
    escolher('Tudo o que está em produção')

    const caixa = await screen.findByRole('checkbox', { name: 'Isso impede montar 2 Chassi. Dar baixa também delas' })

    expect((caixa as HTMLInputElement).checked).toBe(true)
    expect((chamadas(fetchMock, PREVIA)[0] as { incluirTopo: boolean }).incluirTopo).toBe(true)
  })

  it('em "Tudo" com Descarte não há caixa nem pedido de topo', async () => {
    const fetchMock = fetchPorRota({ [PREVIA]: () => respostaJson(previa({ travaNoTopo: TRAVA })) })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()
    escolher('Descarte')
    escolher('Tudo o que está em produção')

    await esperarPrevia()

    expect(screen.queryByRole('checkbox')).toBeNull()
    expect((chamadas(fetchMock, PREVIA)[0] as { incluirTopo: boolean }).incluirTopo).toBe(false)
  })

  it('na Peça não há caixa nem pedido de topo', async () => {
    const fetchMock = fetchPorRota({ [PREVIA]: () => respostaJson(previa({ travaNoTopo: TRAVA })) })
    vi.stubGlobal('fetch', fetchMock)
    renderizar({ no: { ...ITEM, ehPeca: true } })
    escolher('Sumiu no armazém')
    escolher('Tudo o que está em produção')

    await esperarPrevia()

    expect(screen.queryByRole('checkbox')).toBeNull()
    expect((chamadas(fetchMock, PREVIA)[0] as { incluirTopo: boolean }).incluirTopo).toBe(false)
  })

  it('a prévia que o servidor recusa mostra a frase e trava o enviar até uma prévia passar', async () => {
    const frase = 'Só há 4 a iniciar de Suporte.'
    let recusar = true
    const fetchMock = fetchPorRota({
      [PREVIA]: () => (recusar
        ? respostaJson({ erro: 'SaldoInsuficiente', mensagem: frase }, 409)
        : respostaJson(previa())),
    })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()
    escolher('Sumiu no armazém')
    escolher('Uma parte')
    digitar('4 a iniciar', '4')

    expect(await screen.findByText(frase, undefined, { timeout: 2000 })).toBeTruthy()
    expect(enviar('Dar baixa em 4').disabled).toBe(true)

    recusar = false
    digitar('4 a iniciar', '3')
    await esperarPrevia()
    expect(screen.queryByText(frase)).toBeNull()
    await waitFor(() => expect(enviar('Dar baixa em 3').disabled).toBe(false))
  })

  it('enquanto a prévia do pedido de agora não voltou o enviar fica travado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ [PREVIA]: () => respostaJson(previa()) }))
    renderizar()
    escolher('Sumiu no armazém')
    escolher('Uma parte')

    digitar('4 a iniciar', '3')

    expect(enviar('Dar baixa em 3').disabled).toBe(true)
    await esperarPrevia()
    await waitFor(() => expect(enviar('Dar baixa em 3').disabled).toBe(false))
  })

  it('o erro da gravação fica no painel com o digitado, e o 409 avisa quem abriu para recarregar', async () => {
    const frase = 'Só há 1 a iniciar de Suporte.'
    vi.stubGlobal('fetch', fetchPorRota({
      [PREVIA]: () => respostaJson(previa()),
      [BAIXA]: () => respostaJson({ erro: 'SaldoInsuficiente', mensagem: frase }, 409),
    }))
    const { aoFechar, aoConcluir } = renderizar()
    escolher('Sumiu no armazém')
    escolher('Uma parte')
    digitar('4 a iniciar', '3')
    await esperarPrevia()
    await waitFor(() => expect(enviar('Dar baixa em 3').disabled).toBe(false))

    fireEvent.click(enviar('Dar baixa em 3'))

    expect(await screen.findByText(frase)).toBeTruthy()
    expect(aoFechar).not.toHaveBeenCalled()
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(campo('4 a iniciar').value).toBe('3')
  })

  it('Cancelar fecha sem gravar', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(aoFechar).toHaveBeenCalledTimes(1)
    expect(aoConcluir).not.toHaveBeenCalled()
  })

  it('o "Matar": motivo, origem e quantidade fixos, sem escolhas, e o enviar diz "Matar"', async () => {
    const matar = '/api/estrutura/7/baixas'
    const fetchMock = fetchPorRota({
      [PREVIA]: () => respostaJson(previa()),
      [matar]: () => respostaJson({ id: 91 }, 201),
    })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar({
      no: { ...ITEM, ehPeca: true },
      saldos: [saldo('NaoConforme', 5), saldo('AIniciar', 9)],
      titulo: 'Matar',
      motivoFixo: 'MortaEmProcesso',
      origemFixa: { posicao: 'NaoConforme', setorId: null, ordem: null },
      maximo: 3,
    })

    expect(screen.getByRole('form', { name: 'Matar' })).toBeTruthy()
    expect(screen.queryByRole('radio')).toBeNull()
    expect(campo('Quantidade').value).toBe('3')
    fireEvent.change(campo('Quantidade'), { target: { value: '2' } })
    await esperarPrevia()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Matar 2' })).toHaveProperty('disabled', false))
    fireEvent.click(screen.getByRole('button', { name: 'Matar 2' }))

    await waitFor(() => expect(aoFechar).toHaveBeenCalled())
    expect(chamadas(fetchMock, matar)).toEqual([{
      motivo: 'MortaEmProcesso', observacao: null, tudo: false, incluirTopo: false,
      origens: [{ posicao: 'NaoConforme', setorId: null, ordem: null, quantidade: 2 }],
    }])
  })

  it('o descarte da sobra: motivo Descarte fixo, quantidade limitada, e o enviar diz "Descartar"', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar({
      titulo: 'Descartar sobra',
      motivoFixo: 'Descarte',
      origemFixa: { posicao: 'NoSetor', setorId: 1, ordem: 1 },
      maximo: 2,
    })

    expect(campo('Quantidade').value).toBe('2')
    fireEvent.change(campo('Quantidade'), { target: { value: '3' } })
    expect(screen.getByText('No máximo 2.')).toBeTruthy()
    fireEvent.change(campo('Quantidade'), { target: { value: '1' } })
    expect(screen.getByRole('button', { name: 'Descartar 1' })).toBeTruthy()
  })
})
