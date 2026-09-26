import type { NoDaEstrutura } from '../api/estrutura'
import { Botao } from '../components/Botao'
import { EditorDeRoteiroDoNo } from './EditorDeRoteiroDoNo'
import { HistoricoDoNo } from './HistoricoDoNo'
import { usePermissoesDaExecucao } from './usePermissoesDaExecucao'

interface Props {
  no: NoDaEstrutura
  aoFechar: () => void
  /** O Roteiro ou o livro do nó mudou — a tela recarrega a árvore e as posições. */
  aoMudar: () => void
}

/**
 * O detalhe de um nó da árvore (spec da Fase 3 §6.3): o Roteiro dele, com o editor para o PCP, e o
 * histórico, com o estorno. Leitura de todo perfil; as escritas, conforme `usePermissoesDaExecucao`.
 *
 * No topo da tela, como o painel de acrescentar/editar da `AgrupamentoDetalhePage` — o mesmo lugar e
 * a mesma exclusividade mútua, para a tela ter um só painel aberto por vez.
 */
export function PainelDoNo({ no, aoFechar, aoMudar }: Props) {
  const { editarRoteiro, podeEstornar } = usePermissoesDaExecucao()

  return (
    <section
      aria-label={`Detalhes de ${no.descricao}`}
      className="flex flex-col gap-5 rounded-lg border border-borda bg-superficie p-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-medium text-tinta">{`Detalhes de ${no.descricao}`}</h2>
          <p className="text-xs text-tinta-fraca">{`${no.codigoDoComponente ?? 'Ad-hoc'} (Id ${no.id})`}</p>
        </div>
        <Botao variante="secundario" onClick={aoFechar}>Fechar</Botao>
      </div>
      <EditorDeRoteiroDoNo noId={no.id} podeEditar={editarRoteiro} aoSalvar={aoMudar} />
      <HistoricoDoNo noId={no.id} podeEstornar={podeEstornar} aoEstornar={aoMudar} />
    </section>
  )
}
