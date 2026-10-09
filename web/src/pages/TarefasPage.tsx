import { useEffect, useId, useMemo, useRef, useState } from 'react'
import {
  listarTarefas, entregar, ehConflito,
  type ItemDaEntrega, type ItemDeTarefa, type KitDto, type TarefasDoSetorDto,
} from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { INTERVALO_DA_EXECUCAO_MS, useCargaPeriodica } from '../hooks/useCargaPeriodica'
import { caminhoDoNo, descreverDestino, formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'
import { lerConjuntos, quantidadeDoConjunto } from '../execucao/conjuntos'
import { CartaoDeKitIncompleto, CartaoDeKitMontavel } from '../execucao/CartaoDeKit'
import { CHAVES_DA_DEMANDA, facetasDasTarefas, filtrarKits, filtrarTarefas } from '../execucao/filtroDaDemanda'
import { useSelecaoNaUrl } from '../hooks/useSelecaoNaUrl'
import { usePermissoesDaExecucao } from '../execucao/usePermissoesDaExecucao'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { ItemComAcao } from '../components/ItemComAcao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { Botao } from '../components/Botao'
import { Pilula } from '../components/Pilula'
import { FiltroDeDemanda } from '../components/FiltroDeDemanda'
import { SecaoRecolhivel } from '../components/SecaoRecolhivel'

/** O que o Movimentador marcou para levar, por item. `quantidade` é o TEXTO do campo. */
interface Escolha {
  quantidade: string
}

const chaveDoItem = (setorId: number, item: ItemDeTarefa) => `${item.no.id}:${setorId}:${item.ordem}`

const SAIU_DA_LISTA = 'Um item que você tinha marcado não está mais pronto: outra pessoa o moveu. Confira a seleção.'

const SEM_ROTEIRO_NO_PAI = 'Peça ao PCP o Roteiro do pai antes de levar.'

/**
 * Recusa local de um item marcado — o mesmo que o backend recusaria, antes da rede.
 *
 * `paiSemRoteiro` entra AQUI, e não só no aviso sob o item (achado Important #2 da review da
 * Task 6): um item pode ficar marcado e só DEPOIS perder o Roteiro do pai — a atualização
 * periódica devolve o mesmo item, agora bloqueado, sem tirá-lo da seleção (ele continua pronto,
 * só o destino ficou inválido). Sem esta linha, `algumInvalido` não via o bloqueio, Entregar
 * continuava liberado, e a entrega ia para o servidor só para voltar em 409 — sem o usuário
 * conseguir desmarcar, porque o checkbox também estava desabilitado (ver `bloqueado &&
 * escolha === undefined` em `GrupoDeTarefas`).
 */
function erroDaEscolha(item: ItemDeTarefa, escolha: Escolha): string | null {
  if (item.destino.paiSemRoteiro) return SEM_ROTEIRO_NO_PAI
  const leitura = lerQuantidade(escolha.quantidade, item.quantidade)
  if (leitura.erro) return leitura.erro
  return null
}

/** "Entregar 2 Kits e 3 itens": Kits e itens contados separados (spec da Fase 3B, seção 5.1). */
function rotuloDoEntregar(kits: number, itens: number): string {
  const k = kits === 1 ? '1 Kit' : `${kits} Kits`
  const i = itens === 1 ? '1 item' : `${itens} itens`
  if (kits > 0 && itens > 0) return `Entregar ${k} e ${i}`
  if (kits > 0) return `Entregar ${k}`
  if (itens > 0) return `Entregar ${i}`
  return 'Entregar'
}

/**
 * `/tarefas` — os "Item pronto" (regra 23), agrupados pelo Setor de origem, cada um com o destino
 * calculado (spec §6.2). O Movimentador marca vários, ajusta quantidades e toca **Entregar**: UMA
 * requisição com a lista, tudo ou nada (spec §4.3).
 *
 * Os Kits vêm em cartões próprios, um por pai, e entram na mesma entrega: marcar um Kit manda todos
 * os filhos dele, cada um com N conjuntos × a razão. Os filhos de Kit não aparecem nos grupos por
 * Setor (regra 23; D7 da spec da Fase 3B).
 *
 * A seleção sobrevive à atualização periódica: um item que continua pronto continua marcado, com
 * o que foi digitado, e um Kit que continua montável também. O que deixou de estar pronto, ou o Kit
 * que deixou de ser montável, sai da seleção, com aviso — entregar algo que outra pessoa já levou só
 * produziria um 409.
 *
 * O filtro de Material e Pedido só muda o que se DESENHA. A seleção e o aviso de "saiu da lista"
 * valem para a resposta inteira: o item ou Kit marcado que o filtro esconde continua marcado e vai
 * na entrega, e a página avisa quantos estão ocultos.
 *
 * O destino de montagem vem calculado — o primeiro passo do pai (spec da Fase 3D, §2.2) —, então o
 * Movimentador não escolhe Setor.
 */
export function TarefasPage() {
  const { dados, carregando, erro, recarregar } = useCargaPeriodica(
    listarTarefas, 'tarefas', INTERVALO_DA_EXECUCAO_MS, 'Não foi possível carregar as tarefas.',
  )
  const grupos = dados?.grupos ?? null
  const montaveis = useMemo(() => dados?.kitsMontaveis ?? [], [dados])
  const incompletos = useMemo(() => dados?.kitsIncompletos ?? [], [dados])
  const { entregar: podeEntregar } = usePermissoesDaExecucao()
  const { selecao, mudarSelecao, limpar } = useSelecaoNaUrl(CHAVES_DA_DEMANDA)
  const idDosKits = useId()

  const [escolhas, setEscolhas] = useState<Record<string, Escolha>>({})
  // O texto do campo Conjuntos de cada Kit marcado, por `pai.id`.
  const [conjuntos, setConjuntos] = useState<Record<number, string>>({})
  const [erroDaEntrega, setErroDaEntrega] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const enviandoRef = useRef(false)

  // Itens e Kits pela mesma regra: sai da seleção o que não está mais na resposta (o Kit, se não está
  // mais entre os montáveis), e um aviso só cobre os dois.
  useEffect(() => {
    if (dados === null) return
    const existentes = new Set(dados.grupos.flatMap((g) => g.itens.map((i) => chaveDoItem(g.setorId, i))))
    const itensQueFicam = Object.entries(escolhas).filter(([chave]) => existentes.has(chave))
    const kitsExistentes = new Set(dados.kitsMontaveis.map((k) => k.pai.id))
    const kitsQueFicam = Object.entries(conjuntos).filter(([paiId]) => kitsExistentes.has(Number(paiId)))
    const saiuItem = itensQueFicam.length !== Object.keys(escolhas).length
    const saiuKit = kitsQueFicam.length !== Object.keys(conjuntos).length
    if (!saiuItem && !saiuKit) return
    if (saiuItem) setEscolhas(Object.fromEntries(itensQueFicam))
    if (saiuKit) setConjuntos(Object.fromEntries(kitsQueFicam))
    setAviso(SAIU_DA_LISTA)
  }, [dados, escolhas, conjuntos])

  function alternar(chave: string, item: ItemDeTarefa, marcado: boolean) {
    setAviso(null)
    setEscolhas((atuais) => {
      if (!marcado) {
        const { [chave]: _removida, ...resto } = atuais
        return resto
      }
      return { ...atuais, [chave]: { quantidade: quantidadeParaCampo(item.quantidade) } }
    })
  }

  /** Marcar o Kit põe no campo o N máximo; desmarcar tira o texto. */
  function alternarKit(kit: KitDto, marcado: boolean) {
    setAviso(null)
    setConjuntos((atuais) => {
      if (!marcado) {
        const { [kit.pai.id]: _removido, ...resto } = atuais
        return resto
      }
      return { ...atuais, [kit.pai.id]: String(kit.conjuntos) }
    })
  }

  function mudarConjuntos(kit: KitDto, texto: string) {
    setConjuntos((atuais) => ({ ...atuais, [kit.pai.id]: texto }))
  }

  /**
   * "Marcar todos" / "Desmarcar todos" (desvio D10 do plano 2 dos filtros): age só sobre o que a
   * tela mostra depois do filtro e que se pode marcar, em todos os grupos e nos Kits montáveis.
   * Marcar não reescreve o texto de quem já estava marcado (quantidade ou conjuntos); desmarcar tira
   * só os visíveis, e o marcado oculto pelo filtro fica.
   */
  function alternarTodos() {
    setAviso(null)
    setEscolhas((atuais) => {
      if (todosMarcados) {
        const resto = { ...atuais }
        for (const { chave } of marcaveis) delete resto[chave]
        return resto
      }
      const novas = { ...atuais }
      for (const { chave, item } of marcaveis) {
        if (novas[chave] === undefined) novas[chave] = { quantidade: quantidadeParaCampo(item.quantidade) }
      }
      return novas
    })
    setConjuntos((atuais) => {
      if (todosMarcados) {
        const resto = { ...atuais }
        for (const kit of montaveisVisiveis) delete resto[kit.pai.id]
        return resto
      }
      const novos = { ...atuais }
      for (const kit of montaveisVisiveis) {
        if (novos[kit.pai.id] === undefined) novos[kit.pai.id] = String(kit.conjuntos)
      }
      return novos
    })
  }

  function mudar(chave: string, parcial: Partial<Escolha>) {
    setEscolhas((atuais) => ({ ...atuais, [chave]: { ...atuais[chave], ...parcial } }))
  }

  const marcados = (grupos ?? []).flatMap((g) => g.itens
    .filter((i) => escolhas[chaveDoItem(g.setorId, i)] !== undefined)
    .map((i) => ({ grupo: g, item: i, escolha: escolhas[chaveDoItem(g.setorId, i)] })))
  // O N de cada Kit marcado; `null` enquanto o texto não é um inteiro de 1 ao máximo que o servidor mandou.
  const kitsMarcados = montaveis
    .filter((k) => conjuntos[k.pai.id] !== undefined)
    .map((k) => ({ kit: k, n: lerConjuntos(conjuntos[k.pai.id], k.conjuntos).valor }))
  const visiveis = useMemo(() => (grupos ? filtrarTarefas(grupos, selecao) : []), [grupos, selecao])
  const montaveisVisiveis = useMemo(() => filtrarKits(montaveis, selecao), [montaveis, selecao])
  const incompletosVisiveis = useMemo(() => filtrarKits(incompletos, selecao), [incompletos, selecao])
  const facetas = useMemo(() => (dados ? facetasDasTarefas(dados, selecao) : []), [dados, selecao])
  const chavesVisiveis = new Set(visiveis.flatMap((g) => g.itens.map((i) => chaveDoItem(g.setorId, i))))
  const kitsVisiveis = new Set(montaveisVisiveis.map((k) => k.pai.id))
  const ocultos = marcados.filter(({ grupo, item }) => !chavesVisiveis.has(chaveDoItem(grupo.setorId, item))).length
    + kitsMarcados.filter(({ kit }) => !kitsVisiveis.has(kit.pai.id)).length
  // O que "Marcar todos" alcança: os visíveis depois do filtro, menos o pai sem Roteiro (que não se
  // marca), e os Kits montáveis visíveis.
  const marcaveis = visiveis.flatMap((g) => g.itens
    .filter((i) => !i.destino.paiSemRoteiro)
    .map((i) => ({ chave: chaveDoItem(g.setorId, i), item: i })))
  const haMarcavel = marcaveis.length + montaveisVisiveis.length > 0
  const todosMarcados = haMarcavel
    && marcaveis.every(({ chave }) => escolhas[chave] !== undefined)
    && montaveisVisiveis.every((k) => conjuntos[k.pai.id] !== undefined)
  const algumInvalido = marcados.some(({ item, escolha }) => erroDaEscolha(item, escolha) !== null)
    || kitsMarcados.some(({ n }) => n === null)
  const totalMarcado = marcados.length + kitsMarcados.length

  async function enviar() {
    if (totalMarcado === 0 || algumInvalido || enviandoRef.current) return
    const soltos: ItemDaEntrega[] = marcados.map(({ grupo, item, escolha }) => ({
      estruturaItemId: item.no.id,
      origem: { posicao: 'AguardandoColeta', setorId: grupo.setorId, ordem: item.ordem },
      quantidade: lerQuantidade(escolha.quantidade, item.quantidade).valor!,
    }))
    // Um item por filho, todos com o mesmo N (regra 25). Kit montável tem `origem` em todo filho:
    // `conjuntos >= 1` exige pronto em todos, e pronto exige Roteiro.
    const doKit: ItemDaEntrega[] = kitsMarcados.flatMap(({ kit, n }) => kit.filhos.map((f) => ({
      estruturaItemId: f.no.id,
      origem: { posicao: 'AguardandoColeta', setorId: f.origem!.id, ordem: f.ordem! },
      quantidade: quantidadeDoConjunto(n!, f.quantidadePorPai),
    })))
    enviandoRef.current = true
    setEnviando(true)
    setErroDaEntrega(null)
    setAviso(null)
    try {
      await entregar([...soltos, ...doKit])
      setEscolhas({})
      setConjuntos({})
      await recarregar()
    } catch (e) {
      setErroDaEntrega(mensagemDeErro(e, 'Não foi possível registrar a entrega.'))
      if (ehConflito(e)) await recarregar()
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  // "Tarefa" é item solto ou Kit montável; o incompleto é informativo e não tira a tela do vazio.
  const haTarefa = grupos !== null && (grupos.length > 0 || montaveis.length > 0)
  const vazia = grupos !== null && !haTarefa
  const vaziaPeloFiltro = haTarefa && visiveis.length === 0 && montaveisVisiveis.length === 0

  return (
    <Pagina titulo="Tarefas">
      <BannerDeErro mensagem={erro} />
      <BannerDeErro mensagem={aviso} />
      {/* Só com dado de verdade: sem nenhuma tarefa nem Kit não há o que filtrar. */}
      {(haTarefa || incompletos.length > 0) && (
        <FiltroDeDemanda facetas={facetas} selecao={selecao} aoMudar={mudarSelecao} />
      )}
      {podeEntregar && haMarcavel && (
        <Botao variante="secundario" onClick={alternarTodos} className="self-start">
          {todosMarcados ? 'Desmarcar todos' : 'Marcar todos'}
        </Botao>
      )}
      {carregando && <EstadoCarregando />}
      {vaziaPeloFiltro && (
        <EstadoVazio
          titulo="Nada para levar com esses filtros"
          descricao="Nenhum item pronto combina com o que está marcado."
          acao={<Botao variante="secundario" onClick={limpar}>Limpar filtros</Botao>}
        />
      )}
      {vazia && (
        <EstadoVazio
          titulo="Nenhum item pronto para levar agora"
          descricao="Quando um Setor terminar algo que precisa ir a outro lugar, aparece aqui."
        />
      )}
      {montaveisVisiveis.length > 0 && (
        <section aria-labelledby={idDosKits} className="flex flex-col gap-3">
          <h2 id={idDosKits} className="text-lg font-medium text-tinta">Kits montáveis</h2>
          <ListaDeCadastro rotulo="Kits montáveis">
            {montaveisVisiveis.map((kit) => (
              <CartaoDeKitMontavel
                key={kit.pai.id}
                kit={kit}
                podeEntregar={podeEntregar}
                conjuntos={conjuntos[kit.pai.id]}
                aoAlternar={(marcado) => alternarKit(kit, marcado)}
                aoMudar={(texto) => mudarConjuntos(kit, texto)}
              />
            ))}
          </ListaDeCadastro>
        </section>
      )}
      {visiveis.map((g) => (
        <GrupoDeTarefas
          key={g.setorId}
          grupo={g}
          podeEntregar={podeEntregar}
          escolhas={escolhas}
          aoAlternar={alternar}
          aoMudar={mudar}
        />
      ))}
      {podeEntregar && haTarefa && (
        <div className="flex flex-col gap-3">
          <BannerDeErro mensagem={erroDaEntrega} />
          <Botao
            onClick={enviar}
            carregando={enviando}
            rotuloCarregando="Entregando…"
            disabled={totalMarcado === 0 || algumInvalido}
            className="self-start"
          >
            {rotuloDoEntregar(kitsMarcados.length, marcados.length)}
          </Botao>
          {ocultos > 0 && (
            <p className="text-sm text-tinta-fraca">
              {ocultos === 1 ? '1 marcado oculto pelo filtro' : `${ocultos} marcados ocultos pelo filtro`}
            </p>
          )}
        </div>
      )}
      {/* Some quando o filtro esconde todos, como o grupo por Setor que fica sem item. */}
      {incompletosVisiveis.length > 0 && (
        <SecaoRecolhivel titulo="Kits incompletos" contagem={incompletosVisiveis.length}>
          <ListaDeCadastro rotulo="Kits incompletos">
            {incompletosVisiveis.map((kit) => <CartaoDeKitIncompleto key={kit.pai.id} kit={kit} />)}
          </ListaDeCadastro>
        </SecaoRecolhivel>
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
          // Só trava o checkbox de quem ainda NÃO marcou. Um item já marcado que fica sem
          // Roteiro no meio do caminho (achado Important #2 da review da Task 6) continua
          // desmarcável — travá-lo junto prenderia a seleção sem saída, porque `erroDaEscolha`
          // (acima) já barra o Entregar enquanto ele estiver marcado.
          const travaOMarcar = bloqueado && escolha === undefined
          return (
            <ItemComAcao
              key={chave}
              acao={podeEntregar && (
                <label className="flex items-center gap-2 text-sm text-tinta">
                  <input
                    type="checkbox"
                    checked={escolha !== undefined}
                    disabled={travaOMarcar}
                    onChange={(e) => aoAlternar(chave, item, e.target.checked)}
                    aria-label={`Levar ${rotuloDoNo(item.no)}`}
                    className="size-5 accent-acao"
                  />
                  Levar
                </label>
              )}
              painel={escolha && <EscolhaDoItem item={item} escolha={escolha} aoMudar={(p) => aoMudar(chave, p)} />}
            >
              <span className="flex flex-wrap items-center gap-2 font-medium text-tinta">
                {rotuloDoNo(item.no)}
                {item.no.pausa && <Pilula tom="atencao">Pausado</Pilula>}
              </span>
              <span className="text-xs text-tinta-fraca">{caminhoDoNo(item.no)}</span>
              <span className="text-sm text-tinta">{`${formatarQuantidade(item.quantidade)} pronto(s) · passo ${item.ordem}`}</span>
              <span className="text-sm text-tinta">{`Destino: ${descreverDestino(item.destino, item.no)}`}</span>
              {bloqueado && (
                <span className="text-xs text-tinta-fraca">{SEM_ROTEIRO_NO_PAI}</span>
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
