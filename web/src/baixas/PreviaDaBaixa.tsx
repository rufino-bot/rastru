import type { LinhaDaBaixaDto, PreviaDaBaixaDto } from '../api/baixas'
import { formatarQuantidade, rotuloDoLocal, rotuloDoNo } from '../execucao/formatacao'

interface Props {
  previa: PreviaDaBaixaDto
}

function origemDaLinha(l: LinhaDaBaixaDto): string {
  return rotuloDoLocal({
    posicao: l.origemPosicao, setorId: l.origemSetorId, setorNome: l.origemSetorNome, ordem: l.origemOrdem,
  })
}

/**
 * O bloco "O que acontece" (C14 da spec da Fase 5B-1): o que o servidor diz que a baixa gravaria — as linhas que
 * saem de produção, inclusive os filhos que saem junto (C6) —, e o que vira sobra. Só texto neutro: perder não é
 * estado de negócio de aprovado nem de reprovado (seção 6 da spec). A trava no topo não mora aqui: é a pergunta do
 * painel (D13, D14), e ele a mostra como caixa.
 */
export function PreviaDaBaixa({ previa }: Props) {
  return (
    <section aria-label="O que acontece" className="flex flex-col gap-2 rounded-lg border border-borda p-3">
      <h3 className="text-sm font-medium text-tinta">O que acontece</h3>
      {previa.linhas.length === 0 ? (
        <p className="text-sm text-tinta-fraca">Nada sai de produção com esses valores.</p>
      ) : (
        <ul aria-label="Saem de produção" className="flex flex-col gap-1 text-sm text-tinta">
          {previa.linhas.map((l, i) => (
            <li key={`${l.estruturaItemId}-${i}`}>
              {`${formatarQuantidade(l.quantidade)} de ${rotuloDoNo({ codigoDoComponente: l.codigo, descricao: l.descricao })}, ${origemDaLinha(l)}`}
            </li>
          ))}
        </ul>
      )}
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
