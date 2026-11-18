import type { PecaDoPanoramaDto } from '../api/panorama'
import { formatarData } from '../api/cadastros'
import { Pilula } from '../components/Pilula'
import { ROTULOS_DA_AFERICAO } from '../expedicao/filtroDaExpedicao'

/**
 * A data de entrega do Pedido e, quando houver, o atraso e a pausa dele (C6: `atraso` e `atencao`). A pílula da pausa
 * diz só "Pausado": o motivo, que pode ser comprido, vem abaixo dela em texto neutro, cortado em duas linhas, com o
 * texto inteiro no `title`.
 */
export function PrazoDaPeca({ pedido }: { pedido: PecaDoPanoramaDto['pedido'] }) {
  const motivo = pedido.pausa?.motivo
  return (
    <div className="flex flex-col gap-1">
      <div className="flex flex-wrap items-center gap-1.5">
        <span>{formatarData(pedido.dataEntrega)}</span>
        {pedido.atrasado && <Pilula tom="atraso">Atrasado</Pilula>}
        {pedido.pausa && <Pilula tom="atencao">Pausado</Pilula>}
      </div>
      {motivo && <span title={motivo} className="line-clamp-2 text-sm text-tinta-fraca">{motivo}</span>}
    </div>
  )
}

/**
 * A aferição da Peça: "Conforme" é aprovação e "Não conforme" é reprovação; "Aferição pendente" e "Exige relatório"
 * ficam neutras. A Peça sem relatório não leva nada (o mesmo que o cartão da expedição).
 */
export function AfericaoDaPeca({ afericao }: { afericao: PecaDoPanoramaDto['afericao'] }) {
  if (afericao.situacao === 'Conforme') return <Pilula tom="positivo">{ROTULOS_DA_AFERICAO.Conforme}</Pilula>
  if (afericao.situacao === 'NaoConforme') return <Pilula tom="negativo">{ROTULOS_DA_AFERICAO.NaoConforme}</Pilula>
  if (afericao.situacao === 'Pendente') return <Pilula>{ROTULOS_DA_AFERICAO.Pendente}</Pilula>
  return afericao.exigeRelatorio ? <Pilula>Exige relatório</Pilula> : null
}
