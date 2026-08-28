import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  listarFilhosPadrao, salvarFilhosPadrao,
  listarMateriaisPadrao, salvarMateriaisPadrao,
  listarRoteiroPadrao, salvarRoteiroPadrao,
} from './receitaPadrao'
import { inicializar, _resetParaTeste } from './client'
import { respostaJson } from '../testes/api'

// `apiFetch` exige `inicializar()` — sem isto toda chamada estoura "client nao inicializado", e o
// teste falha pelo bootstrap em vez de pela asserção.
beforeEach(() => {
  _resetParaTeste()
  inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
})

afterEach(() => { vi.unstubAllGlobals() })

/**
 * O `_init` é declarado na assinatura do dublê porque é o 2º argumento do `fetch` real: sem ele,
 * `mock.calls[n]` vira tupla de UM elemento e `calls[0][1]` não compila (`tsc -b` reprova mesmo
 * com a suíte verde). Mesmo motivo registrado em `src/testes/api.ts`.
 */
function dubleDeFetch() {
  const duble = vi.fn((_url: string | URL, _init?: RequestInit) =>
    Promise.resolve(respostaJson([])),
  )
  vi.stubGlobal('fetch', duble)
  return duble
}

/** O corpo enviado, já desserializado. `body` é `BodyInit | null | undefined` para o TypeScript. */
function corpoEnviado(duble: ReturnType<typeof dubleDeFetch>): unknown {
  return JSON.parse(String(duble.mock.calls[0][1]?.body))
}

describe('receitaPadrao', () => {
  it('lê os filhos-padrão pela rota do sub-recurso', async () => {
    const duble = dubleDeFetch()

    await listarFilhosPadrao(7)

    expect(String(duble.mock.calls[0][0])).toContain('/api/componentes/7/filhos-padrao')
  })

  it('grava os filhos-padrão como POST com o corpo em { linhas }', async () => {
    const duble = dubleDeFetch()

    await salvarFilhosPadrao(7, [{ componenteFilhoId: 3, quantidadePadrao: 2 }])

    expect(String(duble.mock.calls[0][0])).toContain('/api/componentes/7/filhos-padrao')
    expect(duble.mock.calls[0][1]?.method).toBe('POST')
    // O envelope { linhas } NÃO é decorativo: corpo sem ele é 400 no backend, de propósito.
    expect(corpoEnviado(duble)).toEqual({
      linhas: [{ componenteFilhoId: 3, quantidadePadrao: 2 }],
    })
  })

  it('grava lista vazia sem inventar atalho — é o comando de apagar', async () => {
    const duble = dubleDeFetch()

    await salvarFilhosPadrao(7, [])

    expect(corpoEnviado(duble)).toEqual({ linhas: [] })
  })

  it('lê os materiais-padrão pela rota do sub-recurso', async () => {
    const duble = dubleDeFetch()

    await listarMateriaisPadrao(7)

    expect(String(duble.mock.calls[0][0])).toContain('/api/componentes/7/materiais-padrao')
  })

  it('grava os materiais-padrão como POST em { linhas }', async () => {
    const duble = dubleDeFetch()

    await salvarMateriaisPadrao(7, [{ materialId: 5, quantidadePadrao: 1.5 }])

    expect(String(duble.mock.calls[0][0])).toContain('/api/componentes/7/materiais-padrao')
    expect(duble.mock.calls[0][1]?.method).toBe('POST')
    expect(corpoEnviado(duble)).toEqual({
      linhas: [{ materialId: 5, quantidadePadrao: 1.5 }],
    })
  })

  it('lê o roteiro-padrão pela rota do sub-recurso', async () => {
    const duble = dubleDeFetch()

    await listarRoteiroPadrao(7)

    expect(String(duble.mock.calls[0][0])).toContain('/api/componentes/7/roteiro-padrao')
  })

  /** O corpo do roteiro NÃO leva `ordem`: quem numera é o servidor, pela posição do array. */
  it('grava o roteiro mandando só setorId, na ordem do array', async () => {
    const duble = dubleDeFetch()

    await salvarRoteiroPadrao(7, [{ setorId: 20 }, { setorId: 21 }])

    expect(String(duble.mock.calls[0][0])).toContain('/api/componentes/7/roteiro-padrao')
    expect(duble.mock.calls[0][1]?.method).toBe('POST')
    expect(corpoEnviado(duble)).toEqual({
      linhas: [{ setorId: 20 }, { setorId: 21 }],
    })
  })

  it('erro de rede vira ErroDeApi com o status', async () => {
    // `respostaJson` e não `new Response(null, …)`: corpo vazio faz `.json()` lançar sozinho, e a
    // rejeição passaria a ser do parse em vez da guarda que se quer provar (adendo F6).
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaJson({ erro: 'x' }, 500))))

    await expect(listarFilhosPadrao(7)).rejects.toMatchObject({ status: 500 })
  })

  /**
   * Achado da Task 12, medido no navegador: o `POST` de ciclo devolve 400 com
   * `{"erro":"Esta receita criaria um ciclo: ..."}` e a mensagem nunca chegava ao usuário, porque
   * este cliente não lia o corpo. A spec §1.3 exige que chegue.
   */
  it('carrega no erro a mensagem que o servidor mandou no corpo', async () => {
    const ciclo = 'Esta receita criaria um ciclo: MT-1010 -> MT-1000 -> MT-1010.'
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaJson({ erro: ciclo }, 400))))

    await expect(salvarFilhosPadrao(2, [{ componenteFilhoId: 1, quantidadePadrao: 1 }]))
      .rejects.toMatchObject({ status: 400, detalhe: ciclo })
  })

  /**
   * Um erro ao LER a explicação não pode substituir o erro original: sem esta guarda, um corpo que
   * não é JSON faria a tela mostrar o `SyntaxError` do parse no lugar de "não foi possível salvar".
   */
  it('corpo sem o campo esperado nao vira detalhe, e o status sobrevive', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('nao sou json', { status: 400 }))))

    await expect(salvarFilhosPadrao(2, [])).rejects.toMatchObject({ status: 400, detalhe: undefined })
  })
})
