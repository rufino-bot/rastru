import { formatarData, formatarDataHora } from '../api/cadastros'

/**
 * O prazo de entrega e a data de abertura de um Pedido, em dois blocos que não quebram por dentro.
 *
 * Devolve um fragmento, e os dois `<span>` são filhos diretos do `flex flex-wrap` de quem o usa: o
 * `gap` do contêiner separa os blocos, e, quando a linha não cabe, o segundo desce INTEIRO. Como
 * texto corrido solto no flex, o navegador quebrava no último espaço e a hora de "aberto em" descia
 * sozinha, longe da data. O `·` fica no fim do primeiro bloco, para não abrir a linha de baixo.
 */
export function DatasDoPedido({ dataEntrega, dataAbertura }: { dataEntrega: string; dataAbertura: string }) {
  return (
    <>
      <span className="whitespace-nowrap">entrega em {formatarData(dataEntrega)} ·</span>
      <span className="whitespace-nowrap">aberto em {formatarDataHora(dataAbertura)}</span>
    </>
  )
}
