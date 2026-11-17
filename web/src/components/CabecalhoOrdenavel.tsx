export type Direcao = 'asc' | 'desc'

export interface OrdemDeColuna<C extends string> {
  coluna: C
  direcao: Direcao
}

interface Props<C extends string> {
  coluna: C
  rotulo: string
  /** A ordem atual da tabela, de qualquer coluna; `null` é a ordem original. */
  ordem: OrdemDeColuna<C> | null
  aoMudar: (proxima: OrdemDeColuna<C> | null) => void
}

/**
 * A ordem que o clique em `coluna` pede: crescente, decrescente e de volta à original (`null`). Clicar numa coluna
 * que não é a ativa começa em crescente, venha a ativa de onde vier.
 */
export function proximaOrdem<C extends string>(coluna: C, ordem: OrdemDeColuna<C> | null): OrdemDeColuna<C> | null {
  if (!ordem || ordem.coluna !== coluna) return { coluna, direcao: 'asc' }
  return ordem.direcao === 'asc' ? { coluna, direcao: 'desc' } : null
}

const ARIA_SORT = { asc: 'ascending', desc: 'descending' } as const
const SETA = { asc: '▲', desc: '▼' } as const

/**
 * O `<th>` de uma coluna que ordena a tabela. Controlado: recebe a ordem atual e devolve a próxima por `aoMudar`,
 * sem ordenar nada. O `aria-sort` mora no `<th>`; o `<button>` é desta primitiva, como o da `Botao`.
 */
export function CabecalhoOrdenavel<C extends string>({ coluna, rotulo, ordem, aoMudar }: Props<C>) {
  const ativa = ordem?.coluna === coluna ? ordem.direcao : null
  return (
    <th scope="col" aria-sort={ativa ? ARIA_SORT[ativa] : 'none'} className="px-3 py-2 text-left font-medium text-tinta">
      <button
        type="button"
        onClick={() => aoMudar(proximaOrdem(coluna, ordem))}
        className="inline-flex min-h-11 items-center gap-1 rounded-lg font-medium text-tinta hover:text-acao-forte focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
      >
        {rotulo}
        <span aria-hidden="true" className="text-xs text-tinta-fraca">
          {ativa ? SETA[ativa] : '↕'}
        </span>
      </button>
    </th>
  )
}
