import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import type { Selecao } from '../components/FiltroDeDemanda'

function lerValores(bruto: string | null): string[] {
  if (!bruto) return []
  const vistos = new Set<string>()
  for (const pedaco of bruto.split(',')) {
    const valor = pedaco.trim()
    if (valor) vistos.add(valor)
  }
  return [...vistos]
}

/**
 * A seleção de um `FiltroDeDemanda` na query string: `?material=3,5&pedido=42`, uma chave por
 * faceta. Pedaço vazio sai, espaço é aparado e repetido colapsa; chave sem valor não aparece na
 * seleção. Escrever usa `replace` — marcar uma caixa não cria entrada de histórico — e só toca nas
 * chaves do hook: `pagina`, `busca` e o que mais estiver na URL ficam como estão.
 */
export function useSelecaoNaUrl(chaves: readonly string[]) {
  const [params, setParams] = useSearchParams()
  const chavesJuntas = chaves.join('\u0000')
  const textoDaBusca = params.toString()

  // Recalcula só quando a query string ou o conjunto de chaves mudam: quem chama passa a seleção
  // adiante como dependência de efeito, e uma identidade nova a cada render viraria laço.
  const selecao = useMemo<Selecao>(() => {
    const lidos = new URLSearchParams(textoDaBusca)
    const resultado: Selecao = {}
    for (const chave of chavesJuntas ? chavesJuntas.split('\u0000') : []) {
      const valores = lerValores(lidos.get(chave))
      if (valores.length > 0) resultado[chave] = valores
    }
    return resultado
  }, [textoDaBusca, chavesJuntas])

  const mudarSelecao = useCallback(
    (nova: Selecao) => {
      setParams(
        (anterior) => {
          const proxima = new URLSearchParams(anterior)
          for (const chave of chavesJuntas ? chavesJuntas.split('\u0000') : []) {
            const valores = [...new Set((nova[chave] ?? []).map((v) => v.trim()).filter(Boolean))]
            if (valores.length > 0) proxima.set(chave, valores.join(','))
            else proxima.delete(chave)
          }
          return proxima
        },
        { replace: true },
      )
    },
    [setParams, chavesJuntas],
  )

  const limpar = useCallback(() => mudarSelecao({}), [mudarSelecao])

  return { selecao, mudarSelecao, limpar }
}
