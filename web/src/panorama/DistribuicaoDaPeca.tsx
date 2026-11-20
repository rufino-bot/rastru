import type { SegmentoDto, SetorResumoDto } from '../api/panorama'
import { formatarQuantidade, quantidadeConcordada } from '../execucao/formatacao'
import { idadeDesde } from './idade'

interface Props {
  segmentos: SegmentoDto[]
  /** Os Setores onde há Itens da Peça, sem número (o detalhe está na página do Agrupamento). */
  itensEm: SetorResumoDto[]
  /** O `agora` do servidor, contra o qual se calcula cada idade (C12). */
  agora: string
}

const CLASSES_DO_SEGMENTO = 'rounded-md border border-borda bg-fundo px-2 py-0.5 text-sm text-tinta'
/** As saídas já saíram da fábrica: o traço tracejado as separa do que ainda está lá dentro. */
const CLASSES_DA_SAIDA = 'rounded-md border border-dashed border-borda px-2 py-0.5 text-sm text-tinta-fraca'

function textoDoSegmento(s: SegmentoDto, agora: string): string {
  const q = formatarQuantidade(s.quantidade)
  const idade = s.desde ? ` · ${idadeDesde(s.desde, agora)}` : ''
  switch (s.tipo) {
    case 'AIniciar': return `${q} a iniciar${idade}`
    case 'Passo': {
      const base = `${q} ${s.setorNome}${idade}`
      if (s.aguardandoColeta <= 0) return base
      const coleta = s.aguardandoColetaDesde ? ` · ${idadeDesde(s.aguardandoColetaDesde, agora)}` : ''
      return `${base}, coleta ${formatarQuantidade(s.aguardandoColeta)}${coleta}`
    }
    case 'Conserto': return `${q} em conserto (${s.setorNome})${idade}`
    case 'Expedicao': return `${q} Expedição${idade}`
    case 'Expedido': return quantidadeConcordada(s.quantidade, 'expedida', 'expedidas')
    case 'Perdido': return quantidadeConcordada(s.quantidade, 'baixada', 'baixadas')
  }
}

/**
 * O "Onde está" da Peça: os segmentos como o servidor os manda, já na ordem do Roteiro. Tudo neutro — posição não é
 * estado de negócio (C6). Componente próprio e não o `ResumoDePosicoes`, porque a ordem, o "Expedição" somado e o
 * traço das saídas são do servidor e deste painel.
 */
export function DistribuicaoDaPeca({ segmentos, itensEm, agora }: Props) {
  return (
    <ul aria-label="Onde está" className="flex flex-wrap items-center gap-1.5">
      {segmentos.map((s, i) => (
        <li
          key={`${s.tipo}-${s.ordem ?? ''}-${s.setorId ?? ''}-${i}`}
          className={s.tipo === 'Expedido' || s.tipo === 'Perdido' ? CLASSES_DA_SAIDA : CLASSES_DO_SEGMENTO}
        >
          {textoDoSegmento(s, agora)}
        </li>
      ))}
      {itensEm.length > 0 && (
        <li className="text-sm text-tinta-fraca">{`Itens em: ${itensEm.map((s) => s.nome).join(', ')}`}</li>
      )}
    </ul>
  )
}
