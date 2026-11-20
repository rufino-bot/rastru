import type { BlocoDaTrilhaDto, SituacaoDoBloco } from '../api/andamento'
import { textoDoExpedido } from './textoDoBloco'

const SITUACAO: Record<SituacaoDoBloco, string> = { Passado: 'já passou', Atual: 'tem peça agora', Futuro: 'ainda não chegou' }

const PONTO: Record<SituacaoDoBloco, string> = {
  Passado: 'bg-tinta-fraca',
  Atual: 'bg-tinta ring-2 ring-tinta ring-offset-2 ring-offset-superficie',
  Futuro: 'border border-borda-campo bg-superficie',
}

interface Ponto {
  chave: string
  ordem: number | null
  nome: string
  situacao: SituacaoDoBloco
  /** Substitui o texto da situação quando ela não diz tudo (a Expedição que já despachou peças). */
  texto?: string
}

/**
 * A linha do Roteiro de um nó: um ponto por passo e a Expedição por último, na largura inteira do cartão. O rótulo
 * encolhe pela largura do próprio ponto (container query): o nome inteiro com 7 rem ou mais, o número e o nome abaixo
 * disso, só o número abaixo de 4,5 rem — o ponto atual mantém o nome. Montagem, conserto e "a iniciar" não são passos
 * do Roteiro e ficam só na lista de baixo. Sem nenhum passo não há linha.
 */
export function LinhaDoRoteiro({ trilha, expedido }: { trilha: BlocoDaTrilhaDto[]; expedido: number }) {
  const passos = trilha.filter((b) => b.tipo === 'Passo')
  if (passos.length === 0) return null
  const pontos: Ponto[] = passos.map((b) => ({ chave: b.chave, ordem: b.ordem, nome: b.setor ?? 'Passo', situacao: b.situacao }))
  // Com peça no local de expedição a Expedição é o ponto atual; sem ela, passada se já saiu algo, futura se não.
  const naExpedicao = trilha.some((b) => b.tipo === 'Expedicao')
  pontos.push(naExpedicao
    ? { chave: 'Expedicao', ordem: null, nome: 'Expedição', situacao: 'Atual' }
    : expedido > 0
      ? { chave: 'Expedicao', ordem: null, nome: 'Expedição', situacao: 'Passado', texto: textoDoExpedido(expedido) }
      : { chave: 'Expedicao', ordem: null, nome: 'Expedição', situacao: 'Futuro' })
  return (
    <ol aria-label="Roteiro" className="flex w-full">
      {pontos.map((p, i) => {
        const rotulo = `${p.ordem !== null ? `${p.ordem} · ` : ''}${p.nome}: ${p.texto ?? SITUACAO[p.situacao]}`
        const atual = p.situacao === 'Atual'
        const ligacao = `absolute top-1.5 h-px bg-borda-campo ${i === 0 ? 'left-1/2' : 'left-0'} ${i === pontos.length - 1 ? 'right-1/2' : 'right-0'}`
        return (
          <li key={p.chave} aria-label={rotulo} title={rotulo} className="@container relative flex min-w-0 flex-1 flex-col items-center gap-2 px-1">
            <span aria-hidden="true" className={ligacao} />
            <span aria-hidden="true" className={`relative z-10 h-3 w-3 shrink-0 rounded-full ${PONTO[p.situacao]}`} />
            <span aria-hidden="true" className={`flex w-full min-w-0 justify-center gap-1 text-xs ${atual ? 'font-medium text-tinta' : 'text-tinta-fraca'}`}>
              {p.ordem !== null && <span className="hidden shrink-0 @max-[7rem]:inline">{p.ordem}</span>}
              <span className={`truncate ${atual || p.ordem === null ? '' : '@max-[4.5rem]:hidden'}`}>{p.nome}</span>
            </span>
          </li>
        )
      })}
    </ol>
  )
}
