import { useCallback, useEffect, useRef, useState } from 'react'
import type { NoDaEstrutura } from '../api/estrutura'
import type { SaldoDto } from '../api/execucao'
import { Botao } from '../components/Botao'
import { EditorDeRoteiroDoNo } from './EditorDeRoteiroDoNo'
import { HistoricoDoNo } from './HistoricoDoNo'
import { usePermissoesDaExecucao } from './usePermissoesDaExecucao'
import { listarBaixasDoNo, type BaixaDto } from '../api/baixas'
import { HistoricoDeBaixas } from '../baixas/HistoricoDeBaixas'
import { PainelDeBaixa } from '../baixas/PainelDeBaixa'
import { saldosParaBaixa } from '../baixas/posicoesDaBaixa'
import { usePermissoesDaBaixa } from '../baixas/usePermissoesDaBaixa'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { HistoricoDeSeparacoes } from '../separacao/HistoricoDeSeparacoes'
import { MateriaisDoNo } from '../separacao/MateriaisDoNo'
import { usePermissoesDaSeparacao } from '../separacao/usePermissoesDaSeparacao'

interface Props {
  no: NoDaEstrutura
  aoFechar: () => void
  /** O Roteiro ou o livro do nó mudou — a tela recarrega a árvore e as posições. */
  aoMudar: () => void
  /** Onde está cada unidade do nó (`GET /agrupamentos/{id}/posicoes`); vazio enquanto a tela ainda não as tem. */
  saldos: SaldoDto[]
  /**
   * O nome do Pedido ou do Agrupamento que a tela conhece, para a frase do descarte na seção "Baixas"; sem ele a
   * seção diz o Id.
   */
  nomeDoEscopo?: (escopo: BaixaDto['escopo']) => string | null
}

/**
 * O detalhe de um nó da árvore (spec da Fase 3 §6.3): o Roteiro dele, com o editor para o PCP, e o
 * histórico, com o estorno. Leitura de todo perfil; as escritas, conforme `usePermissoesDaExecucao`.
 * Depois do histórico do livro, os materiais do nó e o histórico de separação (spec da Fase 4, seção
 * 6.2), com as escritas conforme `usePermissoesDaSeparacao`.
 *
 * O "Dar baixa" (D9 da spec da Fase 5B-1) é um botão do cabeçalho que abre o `PainelDeBaixa`, conforme
 * `usePermissoesDaBaixa`. Só existe para quem pode e com algo em produção de onde tirar (C4): nó sem nada ali não
 * tem baixa a dar. Enquanto o painel está aberto o botão some, como nas telas de cadastro.
 *
 * No topo da tela, como o painel de acrescentar/editar da `AgrupamentoDetalhePage` — o mesmo lugar e
 * a mesma exclusividade mútua, para a tela ter um só painel aberto por vez.
 */
export function PainelDoNo({ no, aoFechar, aoMudar, saldos, nomeDoEscopo }: Props) {
  const { editarRoteiro, podeEstornar } = usePermissoesDaExecucao()
  const { editarMateriais, podeEstornarSeparacao } = usePermissoesDaSeparacao()
  // Os materiais e as separações dependem uns dos outros: um estorno muda o separado, e um `PUT` do
  // planejado pode tirar do plano um material com separação. Cada um remonta o OUTRO pelo `key`, e
  // não a si mesmo: remontar quem escreveu apagaria o que ele acabou de mostrar (a frase do 409
  // `JaEstornada`, que vem junto com o aviso de recarga). Nenhum dos dois mexe na árvore nem nas
  // posições, então nenhum chama `aoMudar`.
  const [versaoDosMateriais, setVersaoDosMateriais] = useState(0)
  const [versaoDasSeparacoes, setVersaoDasSeparacoes] = useState(0)
  // Uma baixa dada pelo painel recarrega a seção "Baixas"; o estorno de dentro dela não mexe nessa versão.
  const [versaoDasBaixas, setVersaoDasBaixas] = useState(0)
  const carregarBaixas = useCallback(() => listarBaixasDoNo(no.id), [no.id])
  const tituloRef = useRef<HTMLHeadingElement>(null)
  const { darBaixa } = usePermissoesDaBaixa()
  const [baixaAberta, setBaixaAberta] = useState(false)
  const botaoDaBaixa = useRef<HTMLButtonElement>(null)
  const podeDarBaixa = darBaixa && saldosParaBaixa(saldos).length > 0
  // O botão some com o painel aberto, e o foco do painel sai do DOM com ele: volta ao botão, ou ao título quando a
  // baixa tirou tudo o que havia em produção e o botão não voltou com a recarga.
  useDevolverFoco(baixaAberta, () => botaoDaBaixa.current ?? tituloRef.current)
  // O foco volta ao botão no fechamento, com os saldos de antes; a recarga que vem depois pode zerá-los (o "Tudo") e
  // o botão sai do DOM com o foco nele. Quando isso acontece, o foco vai ao título do nó, e não ao `<body>`.
  const podiaDarBaixa = useRef(podeDarBaixa)
  useEffect(() => {
    const sumiu = podiaDarBaixa.current && !podeDarBaixa
    podiaDarBaixa.current = podeDarBaixa
    if (sumiu && (document.activeElement === null || document.activeElement === document.body)) tituloRef.current?.focus()
  }, [podeDarBaixa])

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
        <div className="flex flex-wrap items-center gap-2">
          {podeDarBaixa && !baixaAberta && (
            <Botao ref={botaoDaBaixa} onClick={() => setBaixaAberta(true)}>Dar baixa</Botao>
          )}
          <Botao variante="secundario" onClick={aoFechar}>Fechar</Botao>
        </div>
      </div>
      {baixaAberta && podeDarBaixa && (
        <PainelDeBaixa
          no={{ id: no.id, descricao: no.descricao, ehPeca: no.nivelHierarquico === 'Peca' }}
          saldos={saldos}
          aoConcluir={() => { setVersaoDasBaixas((v) => v + 1); aoMudar() }}
          aoFechar={() => setBaixaAberta(false)}
        />
      )}
      <EditorDeRoteiroDoNo noId={no.id} podeEditar={editarRoteiro} aoSalvar={aoMudar} />
      <HistoricoDoNo noId={no.id} podeEstornar={podeEstornar} aoEstornar={aoMudar} />
      <HistoricoDeBaixas
        carregar={carregarBaixas}
        versao={versaoDasBaixas}
        aoEstornar={aoMudar}
        nomeDoEscopo={nomeDoEscopo}
      />
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
