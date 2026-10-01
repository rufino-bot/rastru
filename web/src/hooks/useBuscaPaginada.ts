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
  /** Só presente quando o chamador passou `filtros` ao hook. */
  filtros?: Record<string, string[]>
}

export interface OpcoesDeBuscaPaginada<T> {
  buscar: (filtro: FiltroDeBusca) => Promise<PaginaDeBusca<T>>
  tamanhoInicial?: number
  atrasoDoDebounce?: number
  /** Busca e página da PRIMEIRA consulta (lidas da URL, por exemplo). Só valem na montagem. */
  inicial?: { busca?: string; pagina?: number }
  /**
   * Filtros de fora do hook (facetas). Comparados por VALOR, não por referência: um objeto novo com
   * o mesmo conteúdo a cada render não recarrega; conteúdo diferente volta à página 1 e recarrega.
   */
  filtros?: Record<string, string[]>
  /**
   * Chamado quando a consulta (busca já debounced, página) muda — para quem a guarda na URL. Não é
   * chamado na montagem: nada mudou ainda.
   */
  aoMudarConsulta?: (consulta: { busca: string; pagina: number }) => void
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
  /**
   * Devolve a consulta ao padrão — busca (campo e consultada) vazia, página 1, sem inativos — e
   * recarrega, mesmo que ela já estivesse no padrão. O tamanho da página e os `filtros` não são
   * tocados: o primeiro é preferência de exibição, os segundos são de fora do hook.
   */
  voltarAoInicio(): void
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
 * 4. **Reset de filtro** — mudar busca, tamanho, inativos ou `filtros` volta para a página 1.
 *
 * `voltarAoInicio` FORÇA a recarga. Um contador (`geracao`) nas dependências de `carregar` faz isso:
 * se a consulta já estava no padrão (página 1, sem busca, sem inativos), nenhum estado muda, e sem
 * o contador salvar um item novo nesse caso não buscaria nada — o item não apareceria. Campo e
 * consulta vão juntos a `''`, então o efeito do debounce os vê iguais e descarta o atraso que
 * estivesse pendente; as quatro trocas de estado e o contador caem num render só, e saem numa
 * requisição só (decisão D4 do plano da 1F).
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
  inicial,
  filtros,
  aoMudarConsulta,
}: OpcoesDeBuscaPaginada<T>): BuscaPaginada<T> {
  const [textoDaBusca, setTextoDaBusca] = useState(inicial?.busca ?? '')
  const [busca, setBusca] = useState(inicial?.busca ?? '')
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [pagina, setPagina] = useState(inicial?.pagina ?? 1)
  const [tamanho, setTamanho] = useState(tamanhoInicial)
  const [itens, setItens] = useState<T[]>([])
  const [total, setTotal] = useState(0)
  // `true` na montagem: a primeira carga já está a caminho quando o primeiro render acontece.
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<unknown>(null)
  // Só gatilho de recarga: não entra no `FiltroDeBusca`.
  const [geracao, setGeracao] = useState(0)

  const sequenciaRef = useRef(0)

  // A função de busca vive num ref para que um chamador que passe uma lambda nova a cada render
  // não vire laço infinito de requisições: com ela nas dependências de `carregar`, uma lambda
  // inline dispararia carga -> render -> lambda nova -> carga. O ref quebra o ciclo, e o
  // exhaustive-deps não cobra refs.
  const buscarRef = useRef(buscar)
  useEffect(() => { buscarRef.current = buscar })

  // Os filtros entram por VALOR: a chave serializada (com as chaves em ordem) é o que `carregar`
  // e o reset de página comparam, e não a identidade do objeto — quem chama monta um literal novo
  // a cada render.
  const chaveDosFiltros = filtros === undefined ? undefined : serializarFiltros(filtros)

  // Reset de página quando os filtros mudam, feito DURANTE o render: o React descarta este render
  // e refaz com a página 1 antes de qualquer efeito. Num efeito, a carga sairia primeiro com o
  // filtro novo e a página velha, e uma segunda carga corrigiria — duas requisições por mudança.
  const [chaveAnterior, setChaveAnterior] = useState(chaveDosFiltros)
  if (chaveAnterior !== chaveDosFiltros) {
    setChaveAnterior(chaveDosFiltros)
    setPagina(1)
  }

  const aoMudarConsultaRef = useRef(aoMudarConsulta)
  useEffect(() => { aoMudarConsultaRef.current = aoMudarConsulta })

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
      const resposta = await buscarRef.current({
        busca, incluirInativos, pagina, tamanho,
        filtros: chaveDosFiltros === undefined ? undefined : lerFiltros(chaveDosFiltros),
      })
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
    // `geracao` não é lido acima: está aqui para que `voltarAoInicio` troque a identidade de
    // `carregar` e o efeito rode de novo, mesmo com todo o resto igual.
  }, [busca, incluirInativos, pagina, tamanho, chaveDosFiltros, geracao])

  useEffect(() => { carregar() }, [carregar])

  const totalDePaginas = Math.max(1, Math.ceil(total / tamanho))

  // Clamp: só depois de a carga assentar, para não recuar a página no meio de uma requisição cujo
  // `total` ainda é o antigo.
  useEffect(() => {
    if (!carregando && pagina > totalDePaginas) setPagina(totalDePaginas)
  }, [carregando, pagina, totalDePaginas])

  // Avisa a consulta que mudou, não a que nasceu: a montagem não é mudança. Comparar com a última
  // avisada (e não pular "a primeira execução") também aguenta o efeito rodar duas vezes em StrictMode.
  const ultimaConsultaAvisada = useRef({ busca, pagina })
  useEffect(() => {
    const ultima = ultimaConsultaAvisada.current
    if (ultima.busca === busca && ultima.pagina === pagina) return
    ultimaConsultaAvisada.current = { busca, pagina }
    aoMudarConsultaRef.current?.({ busca, pagina })
  }, [busca, pagina])

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

  const voltarAoInicio = useCallback(() => {
    setTextoDaBusca('')
    setBusca('')
    setPagina(1)
    setIncluirInativos(false)
    setGeracao((g) => g + 1)
  }, [])

  return {
    itens, total, totalDePaginas,
    textoDaBusca, busca,
    incluirInativos, pagina, tamanho,
    carregando, erro,
    mudarBusca, mudarInativos, mudarTamanho, irParaPagina, voltarAoInicio,
    recarregar: carregar,
  }
}

function serializarFiltros(filtros: Record<string, string[]>): string {
  const entradas = Object.entries(filtros).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
  return JSON.stringify(entradas)
}

function lerFiltros(chave: string): Record<string, string[]> {
  return Object.fromEntries(JSON.parse(chave) as [string, string[]][])
}
