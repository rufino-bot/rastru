import { useCallback, useEffect, useRef, useState } from 'react'

export interface PaginaDeBusca<T> {
  itens: T[]
  total: number
}

export interface FiltroDeBusca {
  busca: string
  incluirInativos: boolean
  pagina: number
  tamanho: number
}

export interface OpcoesDeBuscaPaginada<T> {
  buscar: (filtro: FiltroDeBusca) => Promise<PaginaDeBusca<T>>
  tamanhoInicial?: number
  atrasoDoDebounce?: number
}

export interface BuscaPaginada<T> {
  itens: T[]
  total: number
  totalDePaginas: number
  /** O que está no campo AGORA. Atualiza a cada tecla. */
  textoDaBusca: string
  /** O que foi realmente consultado. Atrasa `atrasoDoDebounce` em relação ao campo. */
  busca: string
  incluirInativos: boolean
  pagina: number
  tamanho: number
  carregando: boolean
  /** O erro cru. Quem traduz para texto de tela é `mensagemDeErro` — o hook não conhece domínio. */
  erro: unknown
  mudarBusca(valor: string): void
  mudarInativos(valor: boolean): void
  mudarTamanho(valor: number): void
  irParaPagina(valor: number): void
  recarregar(): Promise<void>
}

/**
 * Busca paginada com as quatro propriedades que a Fase 1B resolveu à mão na `ComponentesPage` — e,
 * segundo a review da Task 6, resolveu errado ou sem prova:
 *
 * 1. **Debounce** — digitar "SUP" faz UMA requisição, não três.
 * 2. **Cancelamento por sequência** — vence a última requisição ENVIADA, não a última a RESPONDER.
 *    Sem isto o campo mostra "SUP" e a lista mostra o resultado de "SU".
 * 3. **Clamp de página** — se o total encolhe e a página atual deixa de existir, recua em vez de
 *    mostrar lista vazia com cara de bug.
 * 4. **Reset de filtro** — mudar busca, tamanho ou inativos volta para a página 1.
 *
 * Não usa `AbortController`: abortar exigiria que cada função de listagem aceitasse um
 * `AbortSignal`, ou seja, mudar a assinatura pública de `cadastros.ts` e o arquivo de teste de 679
 * linhas dele. A guarda de sequência entrega a mesma propriedade observável — o efeito da resposta
 * obsoleta é descartado — ao custo de a requisição obsoleta ainda trafegar. Já adjudicado duas
 * vezes neste projeto; não reabrir sem medição nova.
 */
export function useBuscaPaginada<T>({
  buscar,
  tamanhoInicial = 20,
  atrasoDoDebounce = 300,
}: OpcoesDeBuscaPaginada<T>): BuscaPaginada<T> {
  const [textoDaBusca, setTextoDaBusca] = useState('')
  const [busca, setBusca] = useState('')
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [pagina, setPagina] = useState(1)
  const [tamanho, setTamanho] = useState(tamanhoInicial)
  const [itens, setItens] = useState<T[]>([])
  const [total, setTotal] = useState(0)
  // `true` na montagem: a primeira carga já está a caminho quando o primeiro render acontece.
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<unknown>(null)

  const sequenciaRef = useRef(0)

  // A função de busca vive num ref para que um chamador que passe uma lambda nova a cada render
  // não vire laço infinito de requisições: com ela nas dependências de `carregar`, uma lambda
  // inline dispararia carga -> render -> lambda nova -> carga. O ref quebra o ciclo, e o
  // exhaustive-deps não cobra refs.
  const buscarRef = useRef(buscar)
  useEffect(() => { buscarRef.current = buscar })

  // Debounce: o campo (`textoDaBusca`) anda na hora; a consulta (`busca`) espera o silêncio.
  // O reset de página vive AQUI, e não em `mudarBusca`, para acontecer junto com a consulta nova —
  // resetar a cada tecla dispararia uma carga por tecla, que é o que o debounce impede.
  useEffect(() => {
    if (textoDaBusca === busca) return
    const timer = setTimeout(() => {
      setPagina(1)
      setBusca(textoDaBusca)
    }, atrasoDoDebounce)
    return () => clearTimeout(timer)
  }, [textoDaBusca, busca, atrasoDoDebounce])

  const carregar = useCallback(async () => {
    const minhaSequencia = ++sequenciaRef.current
    setCarregando(true)
    try {
      const resposta = await buscarRef.current({ busca, incluirInativos, pagina, tamanho })
      if (minhaSequencia !== sequenciaRef.current) return
      setItens(resposta.itens)
      setTotal(resposta.total)
      setErro(null)
    } catch (e) {
      if (minhaSequencia !== sequenciaRef.current) return
      setErro(e)
    } finally {
      // A guarda cobre os QUATRO efeitos pós-`await`, não só os dois óbvios: sem ela a resposta
      // obsoleta apagaria o "carregando" da requisição que ainda está em voo.
      if (minhaSequencia === sequenciaRef.current) setCarregando(false)
    }
  }, [busca, incluirInativos, pagina, tamanho])

  useEffect(() => { carregar() }, [carregar])

  const totalDePaginas = Math.max(1, Math.ceil(total / tamanho))

  // Clamp: só depois de a carga assentar, para não recuar a página no meio de uma requisição cujo
  // `total` ainda é o antigo.
  useEffect(() => {
    if (!carregando && pagina > totalDePaginas) setPagina(totalDePaginas)
  }, [carregando, pagina, totalDePaginas])

  const mudarBusca = useCallback((valor: string) => { setTextoDaBusca(valor) }, [])

  const mudarInativos = useCallback((valor: boolean) => {
    setPagina(1)
    setIncluirInativos(valor)
  }, [])

  const mudarTamanho = useCallback((valor: number) => {
    setPagina(1)
    setTamanho(valor)
  }, [])

  const irParaPagina = useCallback((valor: number) => { setPagina(valor) }, [])

  return {
    itens, total, totalDePaginas,
    textoDaBusca, busca,
    incluirInativos, pagina, tamanho,
    carregando, erro,
    mudarBusca, mudarInativos, mudarTamanho, irParaPagina,
    recarregar: carregar,
  }
}
