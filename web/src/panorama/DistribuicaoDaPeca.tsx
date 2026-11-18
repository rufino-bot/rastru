import type { SegmentoDto, SetorResumoDto } from '../api/panorama'
import { formatarQuantidade, quantidadeConcordada } from '../execucao/formatacao'

interface Props {
  segmentos: SegmentoDto[]
  /** Os Setores onde há Itens da Peça, sem número (o detalhe está na página do Agrupamento). */
  itensEm: SetorResumoDto[]
}

const CLASSES_DO_SEGMENTO = 'rounded-md border border-borda bg-fundo px-2 py-0.5 text-sm text-tinta'
/** As saídas já saíram da fábrica: o traço tracejado as separa do que ainda está lá dentro. */
const CLASSES_DA_SAIDA = 'rounded-md border border-dashed border-borda px-2 py-0.5 text-sm text-tinta-fraca'

function textoDoSegmento(s: SegmentoDto): string {
  const q = formatarQuantidade(s.quantidade)
  switch (s.tipo) {
    case 'AIniciar': return `${q} a iniciar`
    case 'Passo': {
      const base = `${q} ${s.setorNome}`
      return s.aguardandoColeta > 0 ? `${base}, ${formatarQuantidade(s.aguardandoColeta)} aguardando coleta` : base
    }
    case 'Conserto': return `${q} em conserto (${s.setorNome})`
    case 'Expedicao': return `${q} Expedição`
    case 'Expedido': return quantidadeConcordada(s.quantidade, 'expedida', 'expedidas')
    case 'Perdido': return quantidadeConcordada(s.quantidade, 'baixada', 'baixadas')
  }
}

/**
 * O "Onde está" da Peça: os segmentos como o servidor os manda, já na ordem do Roteiro. Tudo neutro — posição não é
 * estado de negócio (C6). Componente próprio e não o `ResumoDePosicoes`, porque a ordem, o "Expedição" somado e o
 * traço das saídas são do servidor e deste painel.
 */
export function DistribuicaoDaPeca({ segmentos, itensEm }: Props) {
  return (
    <ul aria-label="Onde está" className="flex flex-wrap items-center gap-1.5">
      {segmentos.map((s, i) => (
        <li
          key={`${s.tipo}-${s.ordem ?? ''}-${s.setorId ?? ''}-${i}`}
          className={s.tipo === 'Expedido' || s.tipo === 'Perdido' ? CLASSES_DA_SAIDA : CLASSES_DO_SEGMENTO}
        >
          {textoDoSegmento(s)}
        </li>
      ))}
      {itensEm.length > 0 && (
        <li className="text-sm text-tinta-fraca">{`Itens em: ${itensEm.map((s) => s.nome).join(', ')}`}</li>
      )}
    </ul>
  )
}
