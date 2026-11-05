import { useEffect, useRef, useState } from 'react'
import type { PreviaDaBaixaDto } from '../api/baixas'
import { ErroDeApi } from '../api/erros'

export interface DescarteDisponivel {
  /** Há algo em produção no escopo: a prévia voltou com linhas, ou o servidor recusou com um 409 de negócio. */
  disponivel: boolean
  /** A consulta de agora ainda não respondeu. Enquanto for verdade, `disponivel` é o da anterior. */
  carregando: boolean
}

/** Uma recusa de negócio do servidor (409 com código) que não é `NadaEmProducao`: há algo em produção a descartar. */
function haAlgoMesmoRecusado(erro: unknown): boolean {
  return erro instanceof ErroDeApi && erro.status === 409 && erro.codigo !== undefined && erro.codigo !== 'NadaEmProducao'
}

/**
 * Decide se o "Descartar tudo" aparece só com algo em produção no escopo, e quem diz
 * isso é a prévia do servidor — `NadaEmProducao` (409) ou `linhas` vazias escondem o botão. Outra recusa de negócio
 * (409 com código, como `LiberacaoEmAberto`, C15) mantém o botão: há algo em produção, e o painel mostra a frase do
 * servidor. Falha de rede ou 500 esconde o botão, sem quebrar a página.
 *
 * Uma consulta por `versao` (a da montagem e uma a cada vez que a tela grava ou estorna), nunca uma por render.
 * Vence a última consulta enviada, não a última a responder, e a anterior é abortada. `habilitado` falso (quem não
 * pode dar baixa) não consulta nada.
 */
export function useDescarteDisponivel(
  prever: (sinal: AbortSignal) => Promise<PreviaDaBaixaDto>,
  habilitado: boolean,
  /** O Pedido ou Agrupamento: trocar de escopo sem remontar a tela consulta de novo. */
  escopoId: number,
  versao: number,
): DescarteDisponivel {
  const [resposta, setResposta] = useState<{ chave: string; disponivel: boolean } | null>(null)
  const chave = `${escopoId}:${versao}`
  const preverRef = useRef(prever)
  useEffect(() => { preverRef.current = prever })

  useEffect(() => {
    if (!habilitado) return
    const controle = new AbortController()
    let vivo = true
    preverRef.current(controle.signal).then(
      (p) => { if (vivo) setResposta({ chave, disponivel: p.linhas.length > 0 }) },
      (e: unknown) => { if (vivo) setResposta({ chave, disponivel: haAlgoMesmoRecusado(e) }) },
    )
    return () => { vivo = false; controle.abort() }
  }, [habilitado, chave])

  if (!habilitado) return { disponivel: false, carregando: false }
  return { disponivel: resposta?.disponivel ?? false, carregando: resposta?.chave !== chave }
}
