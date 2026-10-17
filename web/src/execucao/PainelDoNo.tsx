import { useEffect, useRef, useState } from 'react'
import type { NoDaEstrutura } from '../api/estrutura'
import { Botao } from '../components/Botao'
import { EditorDeRoteiroDoNo } from './EditorDeRoteiroDoNo'
import { HistoricoDoNo } from './HistoricoDoNo'
import { usePermissoesDaExecucao } from './usePermissoesDaExecucao'
import { HistoricoDeSeparacoes } from '../separacao/HistoricoDeSeparacoes'
import { MateriaisDoNo } from '../separacao/MateriaisDoNo'
import { usePermissoesDaSeparacao } from '../separacao/usePermissoesDaSeparacao'

interface Props {
  no: NoDaEstrutura
  aoFechar: () => void
  /** O Roteiro ou o livro do nó mudou — a tela recarrega a árvore e as posições. */
  aoMudar: () => void
}

/**
 * O detalhe de um nó da árvore (spec da Fase 3 §6.3): o Roteiro dele, com o editor para o PCP, e o
 * histórico, com o estorno. Leitura de todo perfil; as escritas, conforme `usePermissoesDaExecucao`.
 * Depois do histórico do livro, os materiais do nó e o histórico de separação (spec da Fase 4, seção
 * 6.2), com as escritas conforme `usePermissoesDaSeparacao`.
 *
 * No topo da tela, como o painel de acrescentar/editar da `AgrupamentoDetalhePage` — o mesmo lugar e
 * a mesma exclusividade mútua, para a tela ter um só painel aberto por vez.
 */
export function PainelDoNo({ no, aoFechar, aoMudar }: Props) {
  const { editarRoteiro, podeEstornar } = usePermissoesDaExecucao()
  const { editarMateriais, podeEstornarSeparacao } = usePermissoesDaSeparacao()
  // Os materiais e as separações dependem uns dos outros: um estorno muda o separado, e um `PUT` do
  // planejado pode tirar do plano um material com separação. Cada um remonta o OUTRO pelo `key`, e
  // não a si mesmo: remontar quem escreveu apagaria o que ele acabou de mostrar (a frase do 409
  // `JaEstornada`, que vem junto com o aviso de recarga). Nenhum dos dois mexe na árvore nem nas
  // posições, então nenhum chama `aoMudar`.
  const [versaoDosMateriais, setVersaoDosMateriais] = useState(0)
  const [versaoDasSeparacoes, setVersaoDasSeparacoes] = useState(0)
  const tituloRef = useRef<HTMLHeadingElement>(null)

  // I3 da review de branch da Fase 3: o painel nasce no topo da tela (comentário acima), mas nada
  // levava o celular até lá nem avisava o leitor de tela — num toque em "Detalhes" do fim de uma
  // árvore longa em 360px, nada do que está visível muda. Focar o próprio título rola a página até
  // ele e o anuncia. Depende de `no.id` (e não só do mount) para o `key` do chamador continuar
  // sendo a única razão de remontar — se um dia esse `key` sair, o foco ainda acompanha a troca.
  useEffect(() => { tituloRef.current?.focus() }, [no.id])

  return (
    <section
      aria-label={`Detalhes de ${no.descricao}`}
      className="flex flex-col gap-5 rounded-lg border border-borda bg-superficie p-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 ref={tituloRef} tabIndex={-1} className="text-lg font-medium text-tinta">{`Detalhes de ${no.descricao}`}</h2>
          <p className="text-xs text-tinta-fraca">{`${no.codigoDoComponente ?? 'Ad-hoc'} (Id ${no.id})`}</p>
        </div>
        <Botao variante="secundario" onClick={aoFechar}>Fechar</Botao>
      </div>
      <EditorDeRoteiroDoNo noId={no.id} podeEditar={editarRoteiro} aoSalvar={aoMudar} />
      <HistoricoDoNo noId={no.id} podeEstornar={podeEstornar} aoEstornar={aoMudar} />
      <MateriaisDoNo
        key={`materiais-${versaoDosMateriais}`}
        noId={no.id}
        podeEditar={editarMateriais}
        aoSalvar={() => setVersaoDasSeparacoes((v) => v + 1)}
      />
      <HistoricoDeSeparacoes
        key={`separacoes-${versaoDasSeparacoes}`}
        noId={no.id}
        podeEstornar={podeEstornarSeparacao}
        aoEstornar={() => setVersaoDosMateriais((v) => v + 1)}
      />
    </section>
  )
}
