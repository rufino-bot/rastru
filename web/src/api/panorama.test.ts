import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { buscarPanorama, registrarEmbalagem } from './panorama'
import { inicializar, _resetParaTeste } from './client'
import { ErroDeApi } from './erros'
import { respostaJson } from '../testes/api'

describe('panorama', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('buscarPanorama faz GET em /panorama e devolve o corpo', async () => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({ pecas: [] }))
    vi.stubGlobal('fetch', fetchMock)

    const panorama = await buscarPanorama()

    expect(panorama).toEqual({ pecas: [] })
    expect(fetchMock.mock.calls[0][0]).toBe('/api/panorama')
    expect((fetchMock.mock.calls[0][1] as RequestInit | undefined)?.method).toBeUndefined()
  })

  it.each([true, false])('registrarEmbalagem(%s) faz POST com o corpo { embalado }', async (embalado) => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(registrarEmbalagem(42, embalado)).resolves.toBeUndefined()

    const [caminho, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(caminho).toBe('/api/estrutura/42/embalagem')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ embalado })
  })

  it('registrarEmbalagem lança ErroDeApi com o código e a frase do servidor', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      respostaJson({ erro: 'NaoPodeEmbalar', mensagem: 'Ainda há unidades em produção.' }, 409),
    ))

    const falha = await registrarEmbalagem(42, true).catch((e: unknown) => e)

    expect(falha).toBeInstanceOf(ErroDeApi)
    expect((falha as ErroDeApi).codigo).toBe('NaoPodeEmbalar')
    expect((falha as ErroDeApi).detalhe).toBe('Ainda há unidades em produção.')
  })
})
