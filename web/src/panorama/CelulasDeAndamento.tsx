import type { PecaDoPanoramaDto } from '../api/panorama'
import { duracaoLegivel, msDesde } from './idade'

/**
 * O "Parado há" da Peça: a duração em negrito, vazia sem `paradoDesde`. Neutro, como a idade (C9): ficar parado
 * não é estado de negócio.
 */
export function ParadoHa({ peca, agora }: { peca: PecaDoPanoramaDto; agora: string }) {
  if (!peca.paradoDesde) return null
  return <strong className="font-bold text-tinta">{duracaoLegivel(msDesde(peca.paradoDesde, agora))}</strong>
}

/** As complicações da Peça (C8), o `texto` de cada uma em sua linha; nada sem nenhuma. Neutro (C9). */
export function ComplicacoesDaPeca({ peca }: { peca: PecaDoPanoramaDto }) {
  if (peca.complicacoes.length === 0) return null
  return (
    <ul aria-label="Complicações" className="flex flex-col gap-0.5 text-sm text-tinta">
      {peca.complicacoes.map((c) => <li key={c.codigo}>{c.texto}</li>)}
    </ul>
  )
}
