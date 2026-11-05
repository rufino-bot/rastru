// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest'
import { renderHook, act, cleanup } from '@testing-library/react'
import { usePreviaDaBaixa } from './usePreviaDaBaixa'
import { preverBaixa, type NovaBaixa, type PreviaDaBaixaDto } from '../api/baixas'
import { ErroDeApi } from '../api/erros'

vi.mock('../api/baixas', () => ({ preverBaixa: vi.fn() }))

const preverMock = vi.mocked(preverBaixa)

const UMA: NovaBaixa = { motivo: 'PerdaArmazem', tudo: false, incluirTopo: false, origens: [{ posicao: 'AIniciar', quantidade: 1 }] }
const DUAS: NovaBaixa = { ...UMA, origens: [{ posicao: 'AIniciar', quantidade: 2 }] }

function previa(quantidade: number): PreviaDaBaixaDto {
  return {
    linhas: [{
      estruturaItemId: 7, descricao: 'Suporte', codigo: 'SUP-01', origemPosicao: 'AIniciar',
      origemSetorId: null, origemSetorNome: null, origemOrdem: null, quantidade,
    }],
    viramSobra: [], travaNoTopo: null,
  }
}

function adiavel<T>() {
  let resolver!: (v: T) => void
  let rejeitar!: (e: unknown) => void
  const promessa = new Promise<T>((res, rej) => { resolver = res; rejeitar = rej })
  return { promessa, resolver, rejeitar }
}

beforeEach(() => { vi.useFakeTimers(); preverMock.mockReset() })
afterEach(() => { cleanup(); vi.useRealTimers() })

describe('usePreviaDaBaixa', () => {
  it('pedido nulo não consulta e não carrega', async () => {
    const { result } = renderHook(() => usePreviaDaBaixa(7, null))

    await act(async () => { await vi.advanceTimersByTimeAsync(1000) })

    expect(preverMock).not.toHaveBeenCalled()
    expect(result.current).toEqual({ previa: null, erro: null, carregando: false })
  })

  it('espera 300 ms de quietude e faz uma consulta só quando o pedido muda depressa', async () => {
    preverMock.mockResolvedValue(previa(2))
    const { result, rerender } = renderHook(({ p }) => usePreviaDaBaixa(7, p), { initialProps: { p: UMA } })
    expect(result.current.carregando).toBe(true)

    await act(async () => { await vi.advanceTimersByTimeAsync(200) })
    rerender({ p: DUAS })
    await act(async () => { await vi.advanceTimersByTimeAsync(200) })
    expect(preverMock).not.toHaveBeenCalled()

    await act(async () => { await vi.advanceTimersByTimeAsync(100) })

    expect(preverMock).toHaveBeenCalledTimes(1)
    expect(preverMock.mock.calls[0][0]).toBe(7)
    expect(preverMock.mock.calls[0][1]).toEqual(DUAS)
    expect(result.current.previa).toEqual(previa(2))
    expect(result.current.carregando).toBe(false)
  })

  it('a primeira consulta que responde depois da segunda não sobrescreve a segunda', async () => {
    const primeira = adiavel<PreviaDaBaixaDto>()
    const segunda = adiavel<PreviaDaBaixaDto>()
    preverMock.mockReturnValueOnce(primeira.promessa).mockReturnValueOnce(segunda.promessa)
    const { result, rerender } = renderHook(({ p }) => usePreviaDaBaixa(7, p), { initialProps: { p: UMA } })

    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    rerender({ p: DUAS })
    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    expect(preverMock).toHaveBeenCalledTimes(2)

    await act(async () => { segunda.resolver(previa(2)) })
    await act(async () => { primeira.resolver(previa(1)) })

    expect(result.current.previa).toEqual(previa(2))
    expect(result.current.carregando).toBe(false)
  })

  it('cancela a consulta anterior pelo sinal quando o pedido muda', async () => {
    preverMock.mockReturnValue(new Promise(() => {}))
    const { rerender } = renderHook(({ p }) => usePreviaDaBaixa(7, p), { initialProps: { p: UMA } })
    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    const sinal = preverMock.mock.calls[0][2] as AbortSignal

    rerender({ p: DUAS })

    expect(sinal.aborted).toBe(true)
  })

  it('a recusa do servidor vira erro e some quando o pedido muda', async () => {
    const recusa = new ErroDeApi(409, 'Falha', 'Só há 1 em produção.', 'SaldoInsuficiente')
    preverMock.mockRejectedValueOnce(recusa).mockResolvedValueOnce(previa(1))
    const { result, rerender } = renderHook(({ p }) => usePreviaDaBaixa(7, p), { initialProps: { p: DUAS } })

    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    expect(result.current.erro).toBe(recusa)
    expect(result.current.previa).toBeNull()

    rerender({ p: UMA })
    expect(result.current.erro).toBeNull()
    expect(result.current.carregando).toBe(true)

    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    expect(result.current.erro).toBeNull()
    expect(result.current.previa).toEqual(previa(1))
  })

  it('voltar a nulo apaga a prévia', async () => {
    preverMock.mockResolvedValue(previa(1))
    const { result, rerender } = renderHook(
      ({ p }: { p: NovaBaixa | null }) => usePreviaDaBaixa(7, p), { initialProps: { p: UMA as NovaBaixa | null } })
    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    expect(result.current.previa).not.toBeNull()

    rerender({ p: null })

    expect(result.current).toEqual({ previa: null, erro: null, carregando: false })
  })

  it('voltar ao mesmo valor depois de um pedido incompleto não reaproveita a resposta velha', async () => {
    preverMock.mockResolvedValue(previa(1))
    const { result, rerender } = renderHook(
      ({ p }: { p: NovaBaixa | null }) => usePreviaDaBaixa(7, p), { initialProps: { p: UMA as NovaBaixa | null } })
    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    expect(result.current.previa).not.toBeNull()

    rerender({ p: null })
    rerender({ p: UMA })

    expect(result.current.previa).toBeNull()
    expect(result.current.carregando).toBe(true)
    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    expect(preverMock).toHaveBeenCalledTimes(2)
    expect(result.current.carregando).toBe(false)
  })

  it('mudar o contexto (os saldos) consulta de novo com o mesmo pedido', async () => {
    preverMock.mockResolvedValue(previa(1))
    const { result, rerender } = renderHook(
      ({ c }) => usePreviaDaBaixa(7, UMA, c), { initialProps: { c: [{ q: 4 }] } })
    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    expect(result.current.carregando).toBe(false)

    rerender({ c: [{ q: 1 }] })

    expect(result.current.carregando).toBe(true)
    await act(async () => { await vi.advanceTimersByTimeAsync(300) })
    expect(preverMock).toHaveBeenCalledTimes(2)
    expect(result.current.carregando).toBe(false)
  })
})
