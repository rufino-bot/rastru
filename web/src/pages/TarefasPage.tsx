import { useEffect, useId, useRef, useState } from 'react'
import {
  listarTarefas, entregar, ehConflito,
  type ItemDaEntrega, type ItemDeTarefa, type TarefasDoSetorDto,
} from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { INTERVALO_DA_EXECUCAO_MS, useCargaPeriodica } from '../hooks/useCargaPeriodica'
import { caminhoDoNo, descreverDestino, formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'
import { usePermissoesDaExecucao } from '../execucao/usePermissoesDaExecucao'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { ItemComAcao } from '../components/ItemComAcao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { Botao } from '../components/Botao'

/** O que o Movimentador marcou para levar, por item. `quantidade` é o TEXTO do campo. */
interface Escolha {
  quantidade: string
  /** Só no destino `Montagem`; começa na sugestão (spec §6.2). */
  destinoSetorId: number | null
}

const chaveDoItem = (setorId: number, item: ItemDeTarefa) => `${item.no.id}:${setorId}:${item.ordem}`

const SAIU_DA_LISTA = 'Um item que você tinha marcado não está mais pronto: outra pessoa o moveu. Confira a seleção.'

/** Recusa local de um item marcado — o mesmo que o backend recusaria, antes da rede. */
function erroDaEscolha(item: ItemDeTarefa, escolha: Escolha): string | null {
  const leitura = lerQuantidade(escolha.quantidade, item.quantidade)
  if (leitura.erro) return leitura.erro
  if (item.destino.tipo === 'Montagem' && escolha.destinoSetorId === null) return 'Escolha o Setor de montagem.'
  return null
}

/**
 * `/tarefas` — os "Item pronto" (regra 23), agrupados pelo Setor de origem, cada um com o destino
 * calculado (spec §6.2). O Movimentador marca vários, ajusta quantidades e toca **Entregar**: UMA
 * requisição com a lista, tudo ou nada (spec §4.3).
 *
 * A seleção sobrevive à atualização periódica: um item que continua pronto continua marcado, com
 * o que foi digitado. O que deixou de estar pronto sai da seleção, com aviso — entregar algo que
 * outra pessoa já levou só produziria um 409.
 */
export function TarefasPage() {
  const { dados: grupos, carregando, erro, recarregar } = useCargaPeriodica(
    listarTarefas, 'tarefas', INTERVALO_DA_EXECUCAO_MS, 'Não foi possível carregar as tarefas.',
  )
  const { entregar: podeEntregar } = usePermissoesDaExecucao()

  const [escolhas, setEscolhas] = useState<Record<string, Escolha>>({})
  const [erroDaEntrega, setErroDaEntrega] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const enviandoRef = useRef(false)

  useEffect(() => {
    if (grupos === null) return
    const existentes = new Set(grupos.flatMap((g) => g.itens.map((i) => chaveDoItem(g.setorId, i))))
    const sobreviventes = Object.entries(escolhas).filter(([chave]) => existentes.has(chave))
    if (sobreviventes.length === Object.keys(escolhas).length) return
    setEscolhas(Object.fromEntries(sobreviventes))
    setAviso(SAIU_DA_LISTA)
  }, [grupos, escolhas])

  function alternar(chave: string, item: ItemDeTarefa, marcado: boolean) {
    setAviso(null)
    setEscolhas((atuais) => {
      if (!marcado) {
        const { [chave]: _removida, ...resto } = atuais
        return resto
      }
      const destinoSetorId = item.destino.tipo === 'Montagem' ? item.destino.sugestaoSetorId : null
      return { ...atuais, [chave]: { quantidade: quantidadeParaCampo(item.quantidade), destinoSetorId } }
    })
  }

  function mudar(chave: string, parcial: Partial<Escolha>) {
    setEscolhas((atuais) => ({ ...atuais, [chave]: { ...atuais[chave], ...parcial } }))
  }

  const marcados = (grupos ?? []).flatMap((g) => g.itens
    .filter((i) => escolhas[chaveDoItem(g.setorId, i)] !== undefined)
    .map((i) => ({ grupo: g, item: i, escolha: escolhas[chaveDoItem(g.setorId, i)] })))
  const algumInvalido = marcados.some(({ item, escolha }) => erroDaEscolha(item, escolha) !== null)

  async function enviar() {
    if (marcados.length === 0 || algumInvalido || enviandoRef.current) return
    const itens: ItemDaEntrega[] = marcados.map(({ grupo, item, escolha }) => ({
      estruturaItemId: item.no.id,
      origem: { posicao: 'AguardandoColeta', setorId: grupo.setorId, ordem: item.ordem },
      destinoSetorId: item.destino.tipo === 'Montagem' ? escolha.destinoSetorId : null,
      quantidade: lerQuantidade(escolha.quantidade, item.quantidade).valor!,
    }))
    enviandoRef.current = true
    setEnviando(true)
    setErroDaEntrega(null)
    setAviso(null)
    try {
      await entregar(itens)
      setEscolhas({})
      await recarregar()
    } catch (e) {
      setErroDaEntrega(mensagemDeErro(e, 'Não foi possível registrar a entrega.'))
      if (ehConflito(e)) await recarregar()
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  const vazia = grupos !== null && grupos.length === 0

  return (
    <Pagina titulo="Tarefas">
      <BannerDeErro mensagem={erro} />
      <BannerDeErro mensagem={aviso} />
      {carregando && <EstadoCarregando />}
      {vazia && (
        <EstadoVazio
          titulo="Nenhum item pronto para levar agora"
          descricao="Quando um Setor terminar algo que precisa ir a outro lugar, aparece aqui."
        />
      )}
      {grupos?.map((g) => (
        <GrupoDeTarefas
          key={g.setorId}
          grupo={g}
          podeEntregar={podeEntregar}
          escolhas={escolhas}
          aoAlternar={alternar}
          aoMudar={mudar}
        />
      ))}
      {podeEntregar && grupos !== null && grupos.length > 0 && (
        <div className="flex flex-col gap-3">
          <BannerDeErro mensagem={erroDaEntrega} />
          <Botao
            onClick={enviar}
            carregando={enviando}
            rotuloCarregando="Entregando…"
            disabled={marcados.length === 0 || algumInvalido}
            className="self-start"
          >
            {marcados.length === 0 ? 'Entregar' : `Entregar ${marcados.length} ${marcados.length === 1 ? 'item' : 'itens'}`}
          </Botao>
        </div>
      )}
    </Pagina>
  )
}

function GrupoDeTarefas({ grupo, podeEntregar, escolhas, aoAlternar, aoMudar }: {
  grupo: TarefasDoSetorDto
  podeEntregar: boolean
  escolhas: Record<string, Escolha>
  aoAlternar: (chave: string, item: ItemDeTarefa, marcado: boolean) => void
  aoMudar: (chave: string, parcial: Partial<Escolha>) => void
}) {
  const id = useId()
  return (
    <section aria-labelledby={id} className="flex flex-col gap-3">
      <h2 id={id} className="text-lg font-medium text-tinta">{`Em ${grupo.setorNome}`}</h2>
      <ListaDeCadastro rotulo={`Prontos em ${grupo.setorNome}`}>
        {grupo.itens.map((item) => {
          const chave = chaveDoItem(grupo.setorId, item)
          const escolha = escolhas[chave]
          // Pai sem Roteiro: a entrega seria recusada (`PaiSemRoteiro`); o item aparece para o
          // Movimentador saber que existe (desvio D7 do plano 2), mas não se marca.
          const bloqueado = item.destino.paiSemRoteiro
          return (
            <ItemComAcao
              key={chave}
              acao={podeEntregar && (
                <label className="flex items-center gap-2 text-sm text-tinta">
                  <input
                    type="checkbox"
                    checked={escolha !== undefined}
                    disabled={bloqueado}
                    onChange={(e) => aoAlternar(chave, item, e.target.checked)}
                    aria-label={`Levar ${rotuloDoNo(item.no)}`}
                    className="size-5 accent-acao"
                  />
                  Levar
                </label>
              )}
              painel={escolha && <EscolhaDoItem item={item} escolha={escolha} aoMudar={(p) => aoMudar(chave, p)} />}
            >
              <span className="font-medium text-tinta">{rotuloDoNo(item.no)}</span>
              <span className="text-xs text-tinta-fraca">{caminhoDoNo(item.no)}</span>
              <span className="text-sm text-tinta">{`${formatarQuantidade(item.quantidade)} pronto(s) · passo ${item.ordem}`}</span>
              <span className="text-sm text-tinta">{`Destino: ${descreverDestino(item.destino, item.no)}`}</span>
              {bloqueado && (
                <span className="text-xs text-tinta-fraca">Peça ao PCP o Roteiro do pai antes de levar.</span>
              )}
            </ItemComAcao>
          )
        })}
      </ListaDeCadastro>
    </section>
  )
}

function EscolhaDoItem({ item, escolha, aoMudar }: {
  item: ItemDeTarefa
  escolha: Escolha
  aoMudar: (parcial: Partial<Escolha>) => void
}) {
  const erro = erroDaEscolha(item, escolha)
  return (
    <div className="flex flex-col gap-3 border-t border-borda pt-3">
      {item.destino.tipo === 'Montagem' && (
        // `<select>` simples: poucos Setores (os do Roteiro do pai), não um catálogo paginado (spec §6.2).
        <Campo rotulo="Setor de montagem">
          {(id) => (
            <select
              id={id}
              value={escolha.destinoSetorId ?? ''}
              onChange={(e) => aoMudar({ destinoSetorId: e.target.value ? Number(e.target.value) : null })}
              className={CLASSES_DE_CONTROLE}
            >
              <option value="">Escolha o Setor</option>
              {item.destino.setoresPossiveis.map((s) => <option key={s.id} value={s.id}>{s.nome}</option>)}
            </select>
          )}
        </Campo>
      )}
      <Campo rotulo="Quantidade" dica={erro ?? `Pronto(s): ${formatarQuantidade(item.quantidade)}`}>
        {(id, idDaDica) => (
          <input
            id={id}
            type="text"
            inputMode="decimal"
            autoComplete="off"
            value={escolha.quantidade}
            onChange={(e) => aoMudar({ quantidade: e.target.value })}
            aria-describedby={idDaDica}
            aria-invalid={erro !== null}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>
    </div>
  )
}
