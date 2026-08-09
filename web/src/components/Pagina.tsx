import type { ReactNode } from 'react'

interface Props {
  titulo: string
  /** Ação principal da tela, alinhada ao título (ex.: "Novo pedido"). */
  acao?: ReactNode
  children: ReactNode
}

/**
 * Substitui as SEIS cópias de `min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4`.
 *
 * `max-w-3xl` (768px) e não `max-w-md` (448px): a spec §7 registra que busca + filtro + seletor de
 * tamanho + paginação não cabem em 448px. `min-h-screen` sai daqui e vai para o `AppShell` — as
 * duas coisas juntas produzem rolagem permanente de alguns pixels.
 *
 * O respiro é generoso de propósito (direção "sóbria e espaçada", spec §3). O custo foi medido e
 * aceito: na mesma altura de tela, ~3 itens onde a densa mostraria ~6.
 */
export function Pagina({ titulo, acao, children }: Props) {
  return (
    <main className="mx-auto w-full max-w-3xl px-4 py-8 sm:px-6 flex flex-col gap-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight text-tinta">{titulo}</h1>
        {acao}
      </header>
      {children}
    </main>
  )
}
