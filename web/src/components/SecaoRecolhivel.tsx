import type { ReactNode } from 'react'

interface Props {
  titulo: string
  contagem: number
  /**
   * Controla o aberto/fechado de fora. Sem ela, a seção cuida de si (nasce recolhida). Com ela, a tela decide, por
   * exemplo para abrir a seção que ganhou item marcado, e o que o usuário abriu sobrevive às recargas.
   */
  aberta?: boolean
  /** O usuário abriu ou fechou a seção. Só faz sentido junto com `aberta`. */
  aoAlternar?: (aberta: boolean) => void
  children: ReactNode
}

/**
 * Seção informativa que nasce recolhida, com a contagem no título: "Kits incompletos (23)". Existe para a lista
 * que pode crescer muito e não pede ação (D7 da spec da Fase 3B). `<details>` nativo: abre no teclado e no leitor
 * de tela sem código, e o `<summary>` é o nome do controle. Não é para seção com ação: o que se faz não fica
 * escondido.
 *
 * Exceção pedida pelo usuário (2026-11-10): os grupos "Em {Setor}" das Tarefas, que pedem ação, nascem recolhidos
 * porque ali o Movimentador procura pela peça pelo Setor. Eles usam a forma controlada (`aberta` e `aoAlternar`),
 * porque a tela abre o grupo que ganhou item marcado e preserva o que o usuário abriu nas recargas.
 *
 * Tem moldura no padrão dos cartões da tela (as classes da caixa do `ItemComAcao`): sem ela, o título solto no
 * fim da tela parecia um texto qualquer, e não uma seção que abre. O `<summary>` e o conteúdo ficam dentro dela.
 */
export function SecaoRecolhivel({ titulo, contagem, aberta, aoAlternar, children }: Props) {
  return (
    <details
      className="rounded-lg border border-borda bg-superficie px-4 py-3"
      open={aberta}
      // O clique no resumo muda o `open` do DOM antes do React saber; o evento devolve essa mudança à tela.
      onToggle={aoAlternar && ((e) => aoAlternar(e.currentTarget.open))}
    >
      <summary
        className={
          'cursor-pointer rounded text-lg font-medium text-tinta ' +
          'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao'
        }
      >
        {`${titulo} (${contagem})`}
      </summary>
      <div className="mt-3 flex flex-col gap-3">{children}</div>
    </details>
  )
}
