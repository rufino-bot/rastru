import type { ReactNode } from 'react'

interface Props {
  /** Botões da linha, à direita no desktop e embaixo do conteúdo no celular. */
  acao?: ReactNode
  /** O que abre DENTRO da linha, embaixo dela — o formulário de quantidade de uma ação. */
  painel?: ReactNode
  children: ReactNode
}

/**
 * Item de lista da execução (fila do Setor e Tarefas): conteúdo de várias linhas, ações, e um painel
 * que abre embaixo sem sair do item. Vai dentro de `ListaDeCadastro`, que continua dando o `<ul>`.
 *
 * Por que não `ItemDeCadastro` (spec §6.4 manda conferir antes de criar): ele é uma linha só, com
 * o conteúdo dentro de um `<span>` e a noção de ativo/inativo riscado — e o painel de quantidade
 * não tem onde morar nele sem virar a primitiva de dentro para fora, nas telas que já a usam.
 */
export function ItemComAcao({ acao, painel, children }: Props) {
  return (
    <li className="flex flex-col gap-3 rounded-lg border border-borda bg-superficie px-4 py-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-1">{children}</div>
        {acao && <div className="flex flex-wrap gap-2">{acao}</div>}
      </div>
      {painel}
    </li>
  )
}
