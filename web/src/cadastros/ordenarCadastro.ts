/**
 * Ordena a lista de um cadastro que chega inteira no cliente (Setores e Materiais). `texto` nulo é
 * "Mais recentes": `id` decrescente (o `Id` é `IDENTITY`). Senão, `localeCompare` em pt-BR — o `É`
 * de "Ébano" fica entre "Delta" e "Faia", e não depois de "Z", como numa comparação de code points
 * — e `id` decrescente no empate. Devolve um array NOVO: nunca ordena o estado no lugar.
 */
export function ordenarCadastro<T extends { id: number }>(
  itens: readonly T[],
  texto: ((item: T) => string) | null,
): T[] {
  return [...itens].sort((a, b) => {
    if (texto !== null) {
      const porTexto = texto(a).localeCompare(texto(b), 'pt-BR')
      if (porTexto !== 0) return porTexto
    }
    return b.id - a.id
  })
}
