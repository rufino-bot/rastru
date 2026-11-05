import { useEffect, useRef, useState } from 'react'
import type { PreviaDaBaixaDto } from '../api/baixas'

export interface DescarteDisponivel {
  /** Há algo em produção no escopo: a prévia voltou com linhas. Recusa, vazio ou falha de rede: `false`. */
  disponivel: boolean
  /** A consulta de agora ainda não respondeu. Enquanto for verdade, `disponivel` é o da anterior. */
  carregando: boolean
}

/**
 * Decide se o "Descartar tudo" aparece (Review Focus 4 da Fase 5B-1): só com algo em produção no escopo, e quem diz
 * isso é a prévia do servidor — `NadaEmProducao` (409) ou `linhas` vazias escondem o botão, e qualquer outra falha
 * também, sem quebrar a página.
 *
 * Uma consulta por `versao` (a da montagem e uma a cada vez que a tela grava ou estorna), nunca uma por render.
 * Vence a última consulta enviada, não a última a responder, e a anterior é abortada. `habilitado` falso (quem não
 * pode dar baixa) não consulta nada.
 */
export function useDescarteDisponivel(
  prever: (sinal: AbortSignal) => Promise<PreviaDaBaixaDto>,
  habilitado: boolean,
  versao: number,
): DescarteDisponivel {
  const [resposta, setResposta] = useState<{ versao: number; disponivel: boolean } | null>(null)
  const preverRef = useRef(prever)
  useEffect(() => { preverRef.current = prever })

  useEffect(() => {
    if (!habilitado) return
    const controle = new AbortController()
    let vivo = true
    preverRef.current(controle.signal).then(
      (p) => { if (vivo) setResposta({ versao, disponivel: p.linhas.length > 0 }) },
      () => { if (vivo) setResposta({ versao, disponivel: false }) },
    )
    return () => { vivo = false; controle.abort() }
  }, [habilitado, versao])

  if (!habilitado) return { disponivel: false, carregando: false }
  return { disponivel: resposta?.disponivel ?? false, carregando: resposta?.versao !== versao }
}
