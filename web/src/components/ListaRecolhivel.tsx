import { useState, type ReactNode } from 'react'

interface Props {
  /** Quantos itens a lista tem: aparece no resumo, "Filhos (37)". */
  contagem: number
  /** Algum item da lista tem ação própria. Com ação, a lista abre: o que se faz não fica escondido. */
  temAcao: boolean
  children: ReactNode
}

/**
 * A lista de filhos dentro de um cartão, que nasce recolhida e mostra só a contagem: uma Peça pode ter dezenas
 * de filhos, e a lista inteira empurrava o resto da tela (ajuste A1 da conferência da Fase 3B). É o disclosure de
 * DENTRO do cartão: sem moldura e com texto pequeno. A seção de página, com título grande e moldura própria, é a
 * `SecaoRecolhivel`. `<details>` nativo: abre no teclado e no leitor de tela sem código, e o `<summary>` é o nome
 * do controle.
 *
 * Exceção: se algum item tem ação, a lista nasce aberta, e continua recolhível. Se a ação aparece depois, com a
 * lista fechada (a Fila recarrega sozinha), a lista abre — só na passagem de falso para verdadeiro, para o
 * usuário que a fechou de propósito não ser contrariado a cada recarga.
 */
export function ListaRecolhivel({ contagem, temAcao, children }: Props) {
  const [aberta, setAberta] = useState(temAcao)
  const [temAcaoAnterior, setTemAcaoAnterior] = useState(temAcao)
  if (temAcao !== temAcaoAnterior) {
    setTemAcaoAnterior(temAcao)
    if (temAcao) setAberta(true)
  }
  return (
    <details
      open={aberta}
      // O clique no resumo muda o `open` do DOM antes do React saber; o evento devolve essa mudança ao estado.
      onToggle={(e) => setAberta(e.currentTarget.open)}
    >
      <summary
        className={
          'cursor-pointer rounded text-sm text-tinta-fraca ' +
          'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao'
        }
      >
        {`Filhos (${contagem})`}
      </summary>
      <div className="mt-1">{children}</div>
    </details>
  )
}
