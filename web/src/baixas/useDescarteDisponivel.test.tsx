// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { renderHook, cleanup, act } from '@testing-library/react'
import { useDescarteDisponivel } from './useDescarteDisponivel'
import { ErroDeApi } from '../api/erros'
import type { PreviaDaBaixaDto } from '../api/baixas'

afterEach(cleanup)

const COM_ALGO: PreviaDaBaixaDto = {
  linhas: [{ estruturaItemId: 1, descricao: 'X', codigo: null, origemPosicao: 'AIniciar', origemSetorId: null, origemSetorNome: null, origemOrdem: null, quantidade: 1 }],
  viramSobra: [], travaNoTopo: null,
}
const VAZIA: PreviaDaBaixaDto = { linhas: [], viramSobra: [], travaNoTopo: null }

interface Pedido { resolver: (p: PreviaDaBaixaDto) => void; rejeitar: (e: unknown) => void; sinal: AbortSignal }

function montar() {
  const pedidos: Pedido[] = []
  const prever = (sinal: AbortSignal) => new Promise<PreviaDaBaixaDto>((resolver, rejeitar) => { pedidos.push({ resolver, rejeitar, sinal }) })
  return { pedidos, prever }
}

describe('useDescarteDisponivel', () => {
  it('com linhas fica disponível; vazia, não', async () => {
    const { pedidos, prever } = montar()
    const { result, rerender } = renderHook(({ v }) => useDescarteDisponivel(prever, true, 7, v), { initialProps: { v: 0 } })
    expect(result.current).toEqual({ disponivel: false, carregando: true })
    await act(async () => pedidos[0].resolver(COM_ALGO))
    expect(result.current).toEqual({ disponivel: true, carregando: false })

    rerender({ v: 1 })
    await act(async () => pedidos[1].resolver(VAZIA))
    expect(result.current.disponivel).toBe(false)
  })

  it('NadaEmProducao esconde; outra recusa 409 de negócio mantém; rede e 500 escondem', async () => {
    const { pedidos, prever } = montar()
    const { result, rerender } = renderHook(({ v }) => useDescarteDisponivel(prever, true, 7, v), { initialProps: { v: 0 } })
    await act(async () => pedidos[0].rejeitar(new ErroDeApi(409, 'F', 'sem nada', 'NadaEmProducao')))
    expect(result.current.disponivel).toBe(false)

    rerender({ v: 1 })
    await act(async () => pedidos[1].rejeitar(new ErroDeApi(409, 'F', 'Peças: CH-1', 'LiberacaoEmAberto')))
    expect(result.current).toEqual({ disponivel: true, carregando: false })

    rerender({ v: 2 })
    await act(async () => pedidos[2].rejeitar(new ErroDeApi(500, 'F')))
    expect(result.current.disponivel).toBe(false)

    rerender({ v: 3 })
    await act(async () => pedidos[3].rejeitar(new TypeError('rede')))
    expect(result.current.disponivel).toBe(false)
  })

  it('a resposta atrasada de uma consulta antiga não vence a de agora', async () => {
    const { pedidos, prever } = montar()
    const { result, rerender } = renderHook(({ v }) => useDescarteDisponivel(prever, true, 7, v), { initialProps: { v: 0 } })
    rerender({ v: 1 })
    expect(pedidos).toHaveLength(2)

    await act(async () => pedidos[1].resolver(VAZIA))
    await act(async () => pedidos[0].resolver(COM_ALGO))

    expect(result.current).toEqual({ disponivel: false, carregando: false })
  })

  it('a consulta velha é abortada', () => {
    const { pedidos, prever } = montar()
    const { rerender, unmount } = renderHook(({ v }) => useDescarteDisponivel(prever, true, 7, v), { initialProps: { v: 0 } })
    expect(pedidos[0].sinal.aborted).toBe(false)
    rerender({ v: 1 })
    expect(pedidos[0].sinal.aborted).toBe(true)
    unmount()
    expect(pedidos[1].sinal.aborted).toBe(true)
  })

  it('trocar o escopo sem remontar consulta de novo', async () => {
    const { pedidos, prever } = montar()
    const { result, rerender } = renderHook(({ id }) => useDescarteDisponivel(prever, true, id, 0), { initialProps: { id: 7 } })
    await act(async () => pedidos[0].resolver(COM_ALGO))
    rerender({ id: 8 })
    expect(pedidos).toHaveLength(2)
    expect(result.current.carregando).toBe(true)
  })

  it('sem permissão não consulta nada', () => {
    const { pedidos, prever } = montar()
    const { result } = renderHook(() => useDescarteDisponivel(prever, false, 7, 0))
    expect(pedidos).toHaveLength(0)
    expect(result.current).toEqual({ disponivel: false, carregando: false })
  })
})
