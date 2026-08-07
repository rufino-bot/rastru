import { vi } from 'vitest'

/**
 * Resposta JSON pronta para `vi.stubGlobal('fetch', ...)`.
 *
 * O corpo NUNCA é `''`, mesmo em resposta de erro: um corpo vazio faz `.json()` lançar sozinho, e
 * um `rejects.toThrow()` sem argumento passa pelo parse falho em vez de passar pela guarda que se
 * queria provar (adendo F6 — custou três mutações vivas na Fase 1A).
 */
export function respostaJson(corpo: unknown, status = 200): Response {
  return new Response(JSON.stringify(corpo), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/**
 * Mock de `fetch` roteado por caminho. A chave é o caminho COM o prefixo `/api` — é isso que o
 * `rota()` de `client.ts` monta, e escrever a chave sem o prefixo é o erro que faz o teste falhar
 * com "fetch não esperado" em vez de com a asserção.
 *
 * A query string é descartada na comparação: o teste declara `/api/componentes`, não
 * `/api/componentes?busca=&pagina=1&tamanho=20`. Quando a URL completa importa (prova de filtro),
 * asserte sobre `fetchMock.mock.calls[n][0]`, que guarda a URL inteira.
 *
 * Rota não declarada REJEITA com mensagem nomeando a URL, em vez de devolver `undefined` — o erro
 * aponta a rota que faltou declarar, em vez de um `undefined` genérico rio abaixo.
 */
export function fetchPorRota(mapa: Record<string, () => Response | Promise<Response>>) {
  // O `init` é declarado mas não usado: o roteamento é só por caminho. Ele existe na assinatura
  // porque é o 2º argumento do `fetch` real, e é nele que teste de prova de método/corpo olha
  // (`fetchMock.mock.calls[n][1]`). Sem declará-lo, `mock.calls` vira tupla de UM elemento e o
  // acesso ao índice 1 não compila — `tsc -b` reprova, embora a suíte passe.
  return vi.fn((url: string | URL, _init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    const entrada = mapa[caminho]
    if (!entrada) return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    return Promise.resolve(entrada())
  })
}
