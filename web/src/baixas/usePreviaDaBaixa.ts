import { useEffect, useRef, useState } from 'react'
import { preverBaixa, type NovaBaixa, type PreviaDaBaixaDto } from '../api/baixas'

export interface PreviaConsultada {
  /** O que a última consulta que respondeu devolveu; `null` sem pedido, enquanto a primeira não voltou e na recusa. */
  previa: PreviaDaBaixaDto | null
  /** A recusa do servidor ao pedido de agora, crua; `mensagemDeErro` a traduz. `null` até o pedido mudar de novo. */
  erro: unknown
  /** Há um pedido de agora sem resposta (no debounce ou em voo). */
  carregando: boolean
}

const ATRASO_DO_DEBOUNCE = 300

interface Resposta {
  chave: string
  previa: PreviaDaBaixaDto | null
  erro: unknown
}

/**
 * A prévia do servidor para o painel de baixa (C14 da spec da Fase 5B-1): o que a baixa gravaria, sem gravar. A
 * conta percorre a árvore, e por isso é do servidor e não uma cópia no front.
 *
 * Mesmas propriedades do `useBuscaPaginada`: **debounce** de 300 ms (digitar "12" faz uma consulta só) e
 * **cancelamento por sequência** — vence a última consulta ENVIADA, não a última a responder, então uma resposta
 * atrasada de um pedido antigo nunca sobrescreve a do pedido de agora. O `AbortSignal` corta a requisição velha na
 * rede, mas quem garante a regra é a sequência: o sinal só é honrado por quem o lê.
 *
 * `pedido` `null` (campos incompletos) não consulta e apaga a prévia. Enquanto o pedido de agora não respondeu, a
 * `previa` ainda é a da última resposta, para o painel não piscar a cada tecla; quem a usa para decidir (o enviar)
 * olha `carregando`. A comparação do pedido é por valor, porque o painel monta um objeto novo a cada render.
 */
export function usePreviaDaBaixa(noId: number, pedido: NovaBaixa | null): PreviaConsultada {
  const [resposta, setResposta] = useState<Resposta | null>(null)
  const sequencia = useRef(0)
  const chave = pedido === null ? null : JSON.stringify([noId, pedido])
  // O efeito lê o pedido pela chave: ela é o valor, e o objeto muda de identidade a cada render.
  const pedidoRef = useRef(pedido)
  useEffect(() => { pedidoRef.current = pedido })

  useEffect(() => {
    sequencia.current += 1
    const minha = sequencia.current
    if (chave === null) return

    const controle = new AbortController()
    const cronometro = setTimeout(() => {
      const corpo = pedidoRef.current
      if (corpo === null) return
      preverBaixa(noId, corpo, controle.signal).then(
        (previa) => { if (minha === sequencia.current) setResposta({ chave, previa, erro: null }) },
        (erro: unknown) => { if (minha === sequencia.current) setResposta({ chave, previa: null, erro }) },
      )
    }, ATRASO_DO_DEBOUNCE)

    return () => {
      clearTimeout(cronometro)
      controle.abort()
    }
  }, [noId, chave])

  if (chave === null || resposta === null) {
    return { previa: null, erro: null, carregando: chave !== null }
  }
  const atual = resposta.chave === chave
  return {
    previa: resposta.previa,
    erro: atual ? resposta.erro : null,
    carregando: !atual,
  }
}
