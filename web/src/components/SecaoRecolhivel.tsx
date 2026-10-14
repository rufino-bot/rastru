import type { ReactNode } from 'react'

interface Props {
  titulo: string
  contagem: number
  children: ReactNode
}

/**
 * Seção informativa que nasce recolhida, com a contagem no título: "Kits incompletos (23)". Existe para a lista
 * que pode crescer muito e não pede ação (D7 da spec da Fase 3B). `<details>` nativo: abre no teclado e no leitor
 * de tela sem código, e o `<summary>` é o nome do controle. Não é para seção com ação: o que se faz não fica
 * escondido.
 *
 * Tem moldura no padrão dos cartões da tela (as classes da caixa do `ItemComAcao`): sem ela, o título solto no
 * fim da tela parecia um texto qualquer, e não uma seção que abre. O `<summary>` e o conteúdo ficam dentro dela.
 */
export function SecaoRecolhivel({ titulo, contagem, children }: Props) {
  return (
    <details className="rounded-lg border border-borda bg-superficie px-4 py-3">
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
