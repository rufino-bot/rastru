// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, cleanup, act, waitFor } from '@testing-library/react'
import { useCargaPeriodica } from './useCargaPeriodica'

afterEach(() => { cleanup(); vi.useRealTimers() })

/** Promessa controlada de fora — para decidir a ORDEM em que as respostas chegam. */
function adiada<T>() {
  let resolver!: (v: T) => void
  let rejeitar!: (e: unknown) => void
  const promessa = new Promise<T>((res, rej) => { resolver = res; rejeitar = rej })
  return { promessa, resolver, rejeitar }
}

describe('useCargaPeriodica', () => {
  it('começa carregando e entrega os dados', async () => {
    const { result } = renderHook(() => useCargaPeriodica(() => Promise.resolve(['a']), 1, null, 'falhou'))

    expect(result.current.carregando).toBe(true)
    await waitFor(() => expect(result.current.dados).toEqual(['a']))
    expect(result.current.carregando).toBe(false)
    expect(result.current.erro).toBeNull()
  })

  it('carga inicial que falha: sem dados, com a mensagem traduzida', async () => {
    const { result } = renderHook(() => useCargaPeriodica(() => Promise.reject(new Error('x')), 1, null, 'Não foi possível carregar a fila.'))

    await waitFor(() => expect(result.current.erro).toBe('Não foi possível carregar a fila.'))
    expect(result.current.dados).toBeNull()
    expect(result.current.carregando).toBe(false)
  })

  it('busca de novo a cada intervalo, sem voltar a "carregando"', async () => {
    vi.useFakeTimers()
    let n = 0
    const buscar = vi.fn(() => Promise.resolve(++n))
    const { result } = renderHook(() => useCargaPeriodica(buscar, 1, 30_000, 'falhou'))
    await act(async () => { await Promise.resolve() })
    expect(result.current.dados).toBe(1)

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(buscar).toHaveBeenCalledTimes(2)
    expect(result.current.dados).toBe(2)
    expect(result.current.carregando).toBe(false)
  })

  it('atualização que falha mantém os dados e mostra o erro', async () => {
    const buscar = vi.fn().mockResolvedValueOnce('fila velha').mockRejectedValueOnce(new TypeError('Failed to fetch'))
    const { result } = renderHook(() => useCargaPeriodica(buscar, 1, null, 'falhou'))
    await waitFor(() => expect(result.current.dados).toBe('fila velha'))

    await act(async () => { await result.current.recarregar() })

    expect(result.current.dados).toBe('fila velha')
    expect(result.current.erro).toBe('Sem conexão com o servidor. Verifique a rede e tente de novo.')
  })

  it('a atualização seguinte que dá certo apaga o erro', async () => {
    const buscar = vi.fn()
      .mockResolvedValueOnce('a').mockRejectedValueOnce(new Error('x')).mockResolvedValueOnce('b')
    const { result } = renderHook(() => useCargaPeriodica(buscar, 1, null, 'falhou'))
    await waitFor(() => expect(result.current.dados).toBe('a'))

    await act(async () => { await result.current.recarregar() })
    expect(result.current.erro).toBe('falhou')
    await act(async () => { await result.current.recarregar() })

    expect(result.current.erro).toBeNull()
    expect(result.current.dados).toBe('b')
  })

  it('resposta atrasada da chave anterior não sobrescreve a da chave nova', async () => {
    // Review Focus 4: trocar de `/fila/1` para `/fila/2` com a resposta do Setor 1 ainda em voo.
    const doSetor1 = adiada<string>()
    const doSetor2 = adiada<string>()
    const buscar = vi.fn((setor: number) => (setor === 1 ? doSetor1.promessa : doSetor2.promessa))
    const { result, rerender } = renderHook(
      ({ setor }) => useCargaPeriodica(() => buscar(setor), setor, null, 'falhou'),
      { initialProps: { setor: 1 } },
    )

    rerender({ setor: 2 })
    await act(async () => { doSetor2.resolver('fila do 2') })
    await act(async () => { doSetor1.resolver('fila do 1') })

    expect(result.current.dados).toBe('fila do 2')
  })

  it('chave nova zera os dados e volta a "carregando"', async () => {
    const segunda = adiada<string>()
    const buscar = vi.fn().mockResolvedValueOnce('fila do 1').mockReturnValueOnce(segunda.promessa)
    const { result, rerender } = renderHook(
      ({ setor }) => useCargaPeriodica(buscar, setor, null, 'falhou'),
      { initialProps: { setor: 1 } },
    )
    await waitFor(() => expect(result.current.dados).toBe('fila do 1'))

    rerender({ setor: 2 })

    expect(result.current.dados).toBeNull()
    expect(result.current.carregando).toBe(true)
    await act(async () => { segunda.resolver('fila do 2') })
    expect(result.current.dados).toBe('fila do 2')
  })

  it('desmontar para a atualização periódica', async () => {
    vi.useFakeTimers()
    const buscar = vi.fn(() => Promise.resolve(1))
    const { unmount } = renderHook(() => useCargaPeriodica(buscar, 1, 30_000, 'falhou'))
    await act(async () => { await Promise.resolve() })

    unmount()
    await vi.advanceTimersByTimeAsync(90_000)

    expect(buscar).toHaveBeenCalledTimes(1)
  })
})
