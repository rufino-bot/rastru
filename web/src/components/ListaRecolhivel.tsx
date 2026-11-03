import { useState, type ReactNode } from 'react'

interface Props {
  /** O nome do resumo. Sem ele, "Filhos", o uso de origem. */
  rotulo?: string
  /**
   * Quantos itens a lista tem: aparece no resumo, "Filhos (37)". Sem ela, o resumo é só o rótulo: a lista que
   * carrega ao abrir ainda não sabe quantos são.
   */
  contagem?: number
  /** Algum item da lista tem ação própria. Com ação, a lista abre: o que se faz não fica escondido. */
  temAcao: boolean
  /** A lista abriu, pelo usuário ou pela ação que apareceu. Serve a quem só carrega o conteúdo quando ele é visto. */
  aoAbrir?: () => void
  children: ReactNode
}

/**
 * A lista de filhos dentro de um cartão, que nasce recolhida e mostra só a contagem: uma Peça pode ter dezenas
 * de filhos, e a lista inteira empurrava o resto da tela (emendas de 2026-10-11 das seções 5.1 e 5.2 da spec da
 * Fase 3B). É o disclosure de DENTRO do cartão: sem moldura e com texto pequeno. A seção de página, com título
 * grande e moldura própria, é a `SecaoRecolhivel`. `<details>` nativo: abre no teclado e no leitor de tela sem
 * código, e o `<summary>` é o nome do controle.
 *
 * O rótulo é "Filhos" por padrão; o histórico da Peça na expedição (Fase 5A) a usa como "Histórico", sem contagem
 * até a primeira leitura (ele só busca ao abrir, pelo `aoAbrir`) e com ela depois.
 *
 * Exceção: se algum item tem ação, a lista nasce aberta, e continua recolhível. Se a ação aparece depois, com a
 * lista fechada (a Fila recarrega sozinha), a lista abre — só na passagem de falso para verdadeiro, para o
 * usuário que a fechou de propósito não ser contrariado a cada recarga.
 */
export function ListaRecolhivel({ rotulo = 'Filhos', contagem, temAcao, aoAbrir, children }: Props) {
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
      onToggle={(e) => {
        const abriu = e.currentTarget.open
        setAberta(abriu)
        if (abriu) aoAbrir?.()
      }}
    >
      <summary
        className={
          'cursor-pointer rounded text-sm text-tinta-fraca ' +
          'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao'
        }
      >
        {contagem === undefined ? rotulo : `${rotulo} (${contagem})`}
      </summary>
      <div className="mt-1">{children}</div>
    </details>
  )
}
