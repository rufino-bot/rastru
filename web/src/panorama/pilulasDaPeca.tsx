import type { PecaDoPanoramaDto } from '../api/panorama'
import { formatarData } from '../api/cadastros'
import { Pilula } from '../components/Pilula'
import { ROTULOS_DA_AFERICAO } from '../expedicao/filtroDaExpedicao'

/** A data de entrega do Pedido e, quando houver, o atraso e a pausa dele (C6: `atraso` e `atencao`). */
export function PrazoDaPeca({ pedido }: { pedido: PecaDoPanoramaDto['pedido'] }) {
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <span>{formatarData(pedido.dataEntrega)}</span>
      {pedido.atrasado && <Pilula tom="atraso">Atrasado</Pilula>}
      {pedido.pausa && <Pilula tom="atencao">{pedido.pausa.motivo ? `Pausado: ${pedido.pausa.motivo}` : 'Pausado'}</Pilula>}
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
