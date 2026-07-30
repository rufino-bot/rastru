import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { listarSetores, criarSetor, definirAtivoSetor, ehConflito } from './cadastros'
import { inicializar, _resetParaTeste } from './client'

describe('cadastros', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('lista setores ativos por padrao', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([{ id: 1, nome: 'Solda', ativo: true }]), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const setores = await listarSetores(false)

    expect(setores).toEqual([{ id: 1, nome: 'Solda', ativo: true }])
    expect(fetchMock.mock.calls[0][0]).toBe('/setores?incluirInativos=false')
  })

  it('devolve o conflito quando o nome ja existe inativo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'nome', existeInativo: true, idExistente: 7 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarSetor('Solda')

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.existeInativo).toBe(true)
    expect(ehConflito(resultado) && resultado.idExistente).toBe(7)
  })

  // O ramo que decide NAO oferecer "Reativar o existente": o homonimo esta ativo, entao
  // reativar nao e opcao — a tela so avisa que o nome esta em uso.
  it('devolve o conflito com existeInativo falso quando o homonimo esta ativo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'nome', existeInativo: false, idExistente: 3 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarSetor('Solda')

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.existeInativo).toBe(false)
  })

  // A tela chama isto em dois lugares (inativar e reativar) e e a unica chamada com PATCH.
  // Assertar so "nao lancou" nao provaria nada: URL errada passaria igual.
  it('definirAtivoSetor manda PATCH na rota do id com o corpo do ativo', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await definirAtivoSetor(7, false)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/setores/7/ativo')
    expect(init.method).toBe('PATCH')
    expect(init.body).toBe(JSON.stringify({ ativo: false }))
  })

  it('lanca quando a resposta e erro nao tratado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 500 })))

    await expect(criarSetor('Solda')).rejects.toThrow()
  })
})
