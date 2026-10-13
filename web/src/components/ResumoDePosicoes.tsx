import type { SaldoDto } from '../api/execucao'
import { quantidadeConcordada, rotuloDoSaldo } from '../execucao/formatacao'
import { Pilula } from './Pilula'

interface Props {
  saldos: SaldoDto[]
  /** Número nos nós com filhos (0 inclusive), `null` nos sem filhos — como vem da API. */
  totalMontado: number | null
}

/**
 * Onde está cada unidade de um nó, em pílulas (spec da Fase 3 §6.3 — o critério de pronto da fase é
 * poder ver isso nó a nó). A ordem é a da API (`AIniciar`, `NoSetor`, …, por passo); a primitiva
 * não reordena.
 *
 * Tom NEUTRO em todas, inclusive "aguardando coleta": verde e vermelho são reservados a estado
 * (aprovado/reprovado, perda, erro) e uma posição no livro não é nenhum deles (spec §6.4).
 *
 * `totalMontado` é o que já foi montado a partir dos filhos ("3 montados a partir dos filhos"), e não
 * uma posição: é conta do próprio nó, que as pílulas de posição não somam. Aparece no singular abaixo
 * de 2. O 0 não aparece — "0 montados" num pai que ninguém começou a montar seria ruído.
 */
export function ResumoDePosicoes({ saldos, totalMontado }: Props) {
  const mostraMontado = totalMontado !== null && totalMontado > 0
  if (saldos.length === 0 && !mostraMontado) return null

  return (
    <ul aria-label="Onde está" className="flex flex-wrap gap-1.5">
      {saldos.map((s) => (
        <li key={`${s.posicao}-${s.setorId ?? ''}-${s.ordem ?? ''}`}>
          <Pilula>{rotuloDoSaldo(s)}</Pilula>
        </li>
      ))}
      {mostraMontado && (
        <li>
          <Pilula>{`${quantidadeConcordada(totalMontado, 'montado', 'montados')} a partir dos filhos`}</Pilula>
        </li>
      )}
    </ul>
  )
}
