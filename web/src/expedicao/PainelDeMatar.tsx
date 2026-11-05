import type { PecaNaExpedicaoDto } from '../api/expedicao'
import type { SaldoDto } from '../api/execucao'
import { PainelDeBaixa } from '../baixas/PainelDeBaixa'
import { rotuloDoNo } from '../execucao/formatacao'

interface Props {
  peca: PecaNaExpedicaoDto
  aoConcluir: () => void
  aoFechar: () => void
}

/**
 * "Matar" as não conformes que aguardam decisão (D6 da spec da Fase 5B-1): uma baixa de parte por `MortaEmProcesso`,
 * com a origem e o teto decididos pelo cartão, sem escolha de motivo, de forma nem de posição. O painel é o
 * `PainelDeBaixa`, que guarda a prévia do servidor, o enviar e o 409.
 */
export function PainelDeMatar({ peca, aoConcluir, aoFechar }: Props) {
  const naoConforme = peca.posicoes.naoConforme
  // O cartão não tem os saldos do nó, só a contagem da Peça: o único que a baixa usa é este.
  const saldos: SaldoDto[] = [{ posicao: 'NaoConforme', setorId: null, setorNome: null, ordem: null, quantidade: naoConforme }]
  return (
    <PainelDeBaixa
      titulo={`Matar não conformes de ${rotuloDoNo(peca)}`}
      aviso="Vira perda (morreu num processo) e sai do expedível. Refazer é um Pedido de Retrabalho, aberto à parte."
      no={{ id: peca.id, descricao: rotuloDoNo(peca), ehPeca: true }}
      saldos={saldos}
      motivoFixo="MortaEmProcesso"
      origemFixa={{ posicao: 'NaoConforme', setorId: null, ordem: null }}
      maximo={naoConforme}
      aoConcluir={aoConcluir}
      aoFechar={aoFechar}
    />
  )
}
