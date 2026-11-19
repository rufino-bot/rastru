import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { buscarAndamento } from './andamento'
import { inicializar, _resetParaTeste } from './client'
import { respostaJson } from '../testes/api'

describe('andamento', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })
  afterEach(() => { vi.unstubAllGlobals() })

  it('buscarAndamento faz GET em /pedidos/7/andamento e devolve o corpo', async () => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({ pecas: [] }))
    vi.stubGlobal('fetch', fetchMock)

    const a = await buscarAndamento(7)

    expect(a).toEqual({ pecas: [] })
    expect(fetchMock.mock.calls[0][0]).toBe('/api/pedidos/7/andamento')
  })
})
