import { useId } from 'react'
import type { LinhaDaBaixaDto, PreviaDaBaixaDto } from '../api/baixas'
import { formatarQuantidade, rotuloDoLocal, rotuloDoNo } from '../execucao/formatacao'

interface Props {
  previa: PreviaDaBaixaDto
  /** O nó da baixa: a linha com este `estruturaItemId` é do próprio nó, as outras saem junto. */
  noId: number
}

function origemDaLinha(l: LinhaDaBaixaDto): string {
  return rotuloDoLocal({
    posicao: l.origemPosicao, setorId: l.origemSetorId, setorNome: l.origemSetorNome, ordem: l.origemOrdem,
  })
}

function LinhaDeSaida({ linha }: { linha: LinhaDaBaixaDto }) {
  return (
    <li>
      {`${formatarQuantidade(linha.quantidade)} de ${rotuloDoNo({ codigoDoComponente: linha.codigo, descricao: linha.descricao })}, ${origemDaLinha(linha)}`}
    </li>
  )
}

function ListaDeSaidas({ titulo, linhas }: { titulo: string; linhas: LinhaDaBaixaDto[] }) {
  const idDoTitulo = useId()
  if (linhas.length === 0) return null
  return (
    <>
      <p id={idDoTitulo} className="text-sm text-tinta-fraca">{titulo}</p>
      <ul aria-labelledby={idDoTitulo} className="flex flex-col gap-1 text-sm text-tinta">
        {linhas.map((l, i) => <LinhaDeSaida key={`${l.estruturaItemId}-${i}`} linha={l} />)}
      </ul>
    </>
  )
}

/**
 * O bloco "O que acontece" (C14 da spec da Fase 5B-1): o que o servidor diz que a baixa gravaria, em duas listas com
 * título visível — "Saem de produção:", as linhas do próprio nó (`estruturaItemId` igual ao `noId`), e "Saem junto:",
 * as dos outros nós (a cascata dos filhos C6 e, com a trava, a Peça do topo) —, mais o que vira sobra, que não muda.
 * Cada lista só aparece com linha. Só texto neutro: perder não é estado de negócio de aprovado nem de reprovado
 * (seção 6 da spec). A trava no topo não mora aqui: é a pergunta do painel (D13, D14), e ele a mostra como caixa.
 */
export function PreviaDaBaixa({ previa, noId }: Props) {
  const doNo = previa.linhas.filter((l) => l.estruturaItemId === noId)
  const junto = previa.linhas.filter((l) => l.estruturaItemId !== noId)
  return (
    <section aria-label="O que acontece" className="flex flex-col gap-2 rounded-lg border border-borda p-3">
      <h3 className="text-sm font-medium text-tinta">O que acontece</h3>
      {previa.linhas.length === 0 && <p className="text-sm text-tinta-fraca">Nada sai de produção com esses valores.</p>}
      <ListaDeSaidas titulo="Saem de produção:" linhas={doNo} />
      <ListaDeSaidas titulo="Saem junto:" linhas={junto} />
      {previa.viramSobra.length > 0 && (
        <>
          <p className="text-sm text-tinta-fraca">Ficam onde estão e viram sobra:</p>
          <ul aria-label="Viram sobra" className="flex flex-col gap-1 text-sm text-tinta">
            {previa.viramSobra.map((s) => (
              <li key={s.estruturaItemId}>{`${formatarQuantidade(s.quantidade)} de ${s.descricao}`}</li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}
