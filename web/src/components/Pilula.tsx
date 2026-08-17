import type { ReactNode } from 'react'

export type TomDePilula = 'neutro' | 'positivo' | 'negativo'

// Os três tons são PARES DECLARADOS, medidos pela guarda da Task 4 — nenhum modificador de
// opacidade. `bg-positivo/10` e `bg-negativo/10` (a versão anterior) viravam
// `color-mix(in oklab, …)`, que não é declaração `--color-*` e escapava da guarda inteira; medido,
// os quatro casos reprovavam AA (4,32 / 4,11 / 4,13 / 3,95 contra os 4,5 exigidos).
//
// `positivo-texto` e não `positivo`, e `negativo-texto` e não `negativo`: os pares medidos são
// esses. NÃO repita aqui a justificativa de que "o verde cheio reprova como texto sobre claro" —
// ela é falsa: `#166534` sobre branco dá 7,130, MEDIDO. Razão de contraste é simétrica.
const POR_TOM: Record<TomDePilula, string> = {
  neutro: 'bg-acao-fundo text-acao',
  positivo: 'bg-positivo-fundo text-positivo-texto',
  negativo: 'bg-negativo-fundo text-negativo-texto',
}

/**
 * Rótulo curto de categoria ou estado (tipo do componente, tipo do agrupamento, status do pedido).
 *
 * O tom `neutro` usa a MESMA tinta do botão primário sobre fundo tingido — não é engano: é a mesma
 * cor em dois contextos. Verde e vermelho ficam reservados a estado de verdade.
 */
export function Pilula({ children, tom = 'neutro' }: { children: ReactNode; tom?: TomDePilula }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${POR_TOM[tom]}`}>
      {children}
    </span>
  )
}
