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
 * Resposta binária pronta para `vi.stubGlobal('fetch', ...)`. Existe porque `respostaJson` não
 * serve ao sólido: o viewer consome `arrayBuffer()`, e um corpo JSON faria o loader receber texto.
 *
 * O `Content-Disposition` é opcional porque só o teste de download o afirma — os demais só olham
 * os bytes. O corpo vai passado direto como `Uint8Array`, e não empacotado num `Blob`: o `Blob` do
 * jsdom não implementa `stream()`, e o `undici` do Node 22 reconhece o objeto como blob-like (pelo
 * `arrayBuffer()` e pelo `Symbol.toStringTag`) e chama `stream()` nele — o construtor de `Response`
 * lançava `TypeError` dentro do stub de `fetch`, o que tornava a suíte dependente da versão do Node
 * (vermelha no 22, verde no 24). Um `Uint8Array` é `BufferSource`: o `undici` o consome direto pelo
 * buffer, sem passar por esse ramo.
 *
 * `Uint8Array<ArrayBuffer>`, e não `Uint8Array` liso: sem o parâmetro de tipo, o `lib.dom` daqui
 * infere `Uint8Array<ArrayBufferLike>` (que inclui `SharedArrayBuffer`), e o `BodyInit` de
 * `Response` só aceita a variante apoiada em `ArrayBuffer`. `new Uint8Array(n)`, como os chamadores
 * usam, já produz essa variante — o parâmetro só precisa deixar de alargar o tipo.
 */
export function respostaBinaria(
  bytes: Uint8Array<ArrayBuffer>,
  nomeDoArquivo?: string,
  status = 200,
): Response {
  const headers: Record<string, string> = { 'Content-Type': 'application/octet-stream' }
  if (nomeDoArquivo) headers['Content-Disposition'] = `attachment; filename="${nomeDoArquivo}"`
  return new Response(bytes, { status, headers })
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
