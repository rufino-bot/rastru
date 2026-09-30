import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  obterFila, iniciar, terminar, entregar, estornar, ehConflito,
  type Estornavel, type FilaDoSetorDto, type GrupoAguardandoMontagem, type LinhaDeSobra, type NoResumoDto,
} from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { INTERVALO_DA_EXECUCAO_MS, useCargaPeriodica } from '../hooks/useCargaPeriodica'
import {
  caminhoDoNo, descreverDestino, formatarQuantidade, mensagemDoEstorno, rotuloDaAcao, rotuloDoNo,
} from '../execucao/formatacao'
import { lembrarSetor } from '../execucao/setorLembrado'
import { chaveDeIniciar, chaveDeIniciarPai, chaveDeTerminar } from '../execucao/loteDaFila'
import { CHAVES_DA_DEMANDA, facetasDaFila, filtrarFila } from '../execucao/filtroDaDemanda'
import { useSelecaoNaUrl } from '../hooks/useSelecaoNaUrl'
import { usePermissoesDaExecucao } from '../execucao/usePermissoesDaExecucao'
import { FormularioDeQuantidade } from '../execucao/FormularioDeQuantidade'
import { ListaDeEstornaveis } from '../execucao/ListaDeEstornaveis'
import type { EstadoDaEscolhaDeSetor } from './FilaPage'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { FiltroDeDemanda } from '../components/FiltroDeDemanda'
import { ItemComAcao } from '../components/ItemComAcao'
import { Botao } from '../components/Botao'
import { Confirmacao } from '../components/Confirmacao'
import { Pilula } from '../components/Pilula'

const ESCOLHER: EstadoDaEscolhaDeSetor = { escolher: true }

function TrocarDeSetor() {
  return (
    <Link
      to="/fila"
      state={ESCOLHER}
      className="text-sm font-medium rounded underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      Trocar de Setor
    </Link>
  )
}

/** `/fila/:setorId` — a fila de um Setor (spec da Fase 3, §6.1, com a emenda da Fase 3D, §6.1). */
export function FilaDoSetorPage() {
  const { setorId } = useParams<{ setorId: string }>()
  const id = Number(setorId)
  if (!Number.isInteger(id) || id <= 0) {
    return (
      <Pagina titulo="Fila do Setor" acao={<TrocarDeSetor />}>
        <BannerDeErro mensagem="Este Setor não existe." />
      </Pagina>
    )
  }
  // `key`: trocar de Setor desmonta a fila anterior inteira — formulário aberto incluído.
  return <FilaDoSetor key={id} setorId={id} />
}

/**
 * A ação aberta, pela CHAVE da linha que a abriu. Uma por vez: um formulário de quantidade aberto
 * por tela é o que cabe num celular, e é o que deixa "Cancelar" e "Quantidade" sem ambiguidade. As
 * chaves de iniciar, terminar e iniciar o pai vivem em `loteDaFila`, porque são também as do lote.
 */
const chaveDeLevar = (paiId: number, filhoId: number) => `levar:${paiId}:${filhoId}`
const chaveDeEstornar = (secao: string, noId: number, ordem: number | null) => `estornar:${secao}:${noId}:${ordem ?? ''}`

/** Toda ação que a fila de agora ainda oferece — a que sumiu não pode continuar aberta. */
function chavesDaFila(fila: FilaDoSetorDto): Set<string> {
  const chaves = new Set<string>()
  // A pausa tira o Iniciar da linha (e do pai): a chave dele não existe mais, e o formulário aberto fecha.
  for (const l of fila.aIniciar) if (l.no.pausa === null) chaves.add(chaveDeIniciar(l.no.id, l.ordem))
  for (const l of fila.emTrabalho) chaves.add(chaveDeTerminar(l.no.id, l.ordem))
  for (const g of fila.aguardandoMontagem) {
    if (g.iniciaAqui && g.daParaMontar > 0 && g.pai.pausa === null) chaves.add(chaveDeIniciarPai(g.pai.id))
    if (!g.iniciaAqui && g.primeiroPassoDoPai !== null) {
      for (const f of g.filhos) if (f.presente > 0) chaves.add(chaveDeLevar(g.pai.id, f.no.id))
    }
  }
  // A lista curta do estorno só existe com mais de um registro; com um só, o botão vai à confirmação.
  const comLista = (secao: string, noId: number, ordem: number | null, estornaveis: Estornavel[]) => {
    if (estornaveis.length > 1) chaves.add(chaveDeEstornar(secao, noId, ordem))
  }
  for (const l of fila.emTrabalho) comLista('em-trabalho', l.no.id, l.ordem, l.estornaveis)
  for (const l of fila.aguardandoColeta) comLista('coleta', l.no.id, l.ordem, l.estornaveis)
  for (const s of fila.sobra) comLista('sobra', s.no.id, s.ordem, s.estornaveis)
  return chaves
}

/** Nenhuma seção, nenhuma linha: a fila realmente vazia (não a que o filtro esvaziou). */
function estaVazia(fila: FilaDoSetorDto): boolean {
  return fila.aIniciar.length === 0 && fila.emTrabalho.length === 0
    && fila.aguardandoColeta.length === 0 && fila.aguardandoMontagem.length === 0 && fila.sobra.length === 0
}

const SAIU_DA_FILA = 'O item que você estava registrando não está mais nesta fila: outra pessoa o moveu.'

function FilaDoSetor({ setorId }: { setorId: number }) {
  const { dados: fila, carregando, erro, recarregar } = useCargaPeriodica(
    () => obterFila(setorId), setorId, INTERVALO_DA_EXECUCAO_MS, 'Não foi possível carregar a fila.',
  )
  const { selecao, mudarSelecao, limpar } = useSelecaoNaUrl(CHAVES_DA_DEMANDA)
  const [aberta, setAberta] = useState<string | null>(null)
  const [erroDaAcao, setErroDaAcao] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [aConfirmar, setAConfirmar] = useState<Estornavel | null>(null)
  const [estornando, setEstornando] = useState(false)
  // O toque no mesmo quadro, antes do React redesenhar o botão desabilitado — mesmo padrão de
  // `FormularioDeQuantidade` e do histórico do nó.
  const estornandoRef = useRef(false)

  // Lembra só o Setor cuja fila CARREGOU: um Id digitado na barra que dá 404 não vira lembrança.
  useEffect(() => { if (fila?.setorId === setorId) lembrarSetor(setorId) }, [fila, setorId])

  // O filtro só muda o que se DESENHA. O aviso `SAIU_DA_FILA` e as chaves de ação (`chavesDaFila`)
  // valem para a resposta inteira: esconder uma linha pelo filtro não é outra pessoa tê-la movido.
  const filtrada = useMemo(() => (fila ? filtrarFila(fila, selecao) : null), [fila, selecao])
  const facetas = useMemo(() => (fila ? facetasDaFila(fila, selecao) : []), [fila, selecao])

  // A atualização (periódica, ou a recarga depois de um 409) tirou da fila a linha cujo formulário
  // está aberto. O formulário fecha, e o aviso — com a recusa do servidor, se foi ela — sobe para o
  // topo da tela, porque o painel onde ele estava deixou de existir.
  useEffect(() => {
    if (fila === null || aberta === null || chavesDaFila(fila).has(aberta)) return
    setAviso(erroDaAcao ?? SAIU_DA_FILA)
    setErroDaAcao(null)
    setAberta(null)
  }, [fila, aberta, erroDaAcao])

  function abrir(chave: string) {
    setAberta(chave)
    setErroDaAcao(null)
    setAviso(null)
  }

  function fechar() {
    setAberta(null)
    setErroDaAcao(null)
  }

  /**
   * Toda escrita passa por aqui: sucesso fecha o formulário e recarrega na hora; recusa mostra a
   * mensagem no próprio formulário e, se for 409, recarrega também — o 409 quase sempre quer dizer
   * que a tela ficou velha (spec da Fase 3, §8.3). O `throw` devolve a recusa ao formulário, que só
   * destrava o botão.
   */
  async function registrar(fazer: () => Promise<unknown>) {
    setErroDaAcao(null)
    try {
      await fazer()
      setAberta(null)
      await recarregar()
    } catch (e) {
      setErroDaAcao(mensagemDeErro(e, 'Não foi possível registrar.'))
      if (ehConflito(e)) await recarregar()
      throw e
    }
  }

  /** Com um registro só, direto à confirmação; com vários, a lista curta abre na linha. */
  function pedirEstorno(chave: string, estornaveis: Estornavel[]) {
    setAviso(null)
    if (estornaveis.length === 1) setAConfirmar(estornaveis[0])
    else abrir(chave)
  }

  /**
   * O estorno não tem formulário na linha (a confirmação é um diálogo), então a recusa sobe como aviso
   * no topo. Sucesso e 409 recarregam: dois registros que cabem no saldo um a um podem não caber
   * juntos, e o segundo estorno é recusado com a fila já velha. O contador de Tarefas reconta porque
   * toda escrita da execução avisa que o livro mudou.
   */
  async function confirmarEstorno() {
    if (!aConfirmar || estornandoRef.current) return
    estornandoRef.current = true
    setEstornando(true)
    const alvo = aConfirmar
    setAConfirmar(null)
    setAviso(null)
    try {
      await estornar(alvo)
      setAberta(null)
      await recarregar()
    } catch (e) {
      setAberta(null)
      setAviso(mensagemDeErro(e, 'Não foi possível estornar.'))
      if (ehConflito(e)) await recarregar()
    } finally {
      estornandoRef.current = false
      setEstornando(false)
    }
  }

  const titulo = fila ? `Fila — ${fila.setorNome}` : 'Fila do Setor'

  return (
    <Pagina titulo={titulo} acao={<TrocarDeSetor />}>
      {/* Com a fila já na tela, este banner é de uma ATUALIZAÇÃO que falhou: a lista abaixo é a da
          última carga boa (a decisão "falha de atualização mantém os dados" de `useCargaPeriodica`). */}
      <BannerDeErro mensagem={erro} />
      <BannerDeErro mensagem={aviso} />
      {/* Só com dado de verdade: a fila realmente vazia não tem o que filtrar. */}
      {fila && !estaVazia(fila) && <FiltroDeDemanda facetas={facetas} selecao={selecao} aoMudar={mudarSelecao} />}
      {carregando && <EstadoCarregando />}
      {fila && filtrada && (
        <SecoesDaFila
          fila={filtrada}
          completa={fila}
          aoLimparFiltros={limpar}
          acoes={{
            aberta, erroDaAcao, abrir, fechar, registrar, setorId,
            pedirEstorno, escolherEstorno: setAConfirmar, estornando,
          }}
        />
      )}
      <Confirmacao
        aberto={aConfirmar !== null}
        mensagem={aConfirmar && mensagemDoEstorno(aConfirmar)}
        rotuloConfirmar="Estornar"
        // Estorno é correção, não destruição: nada some do livro. Por isso `primario`, não `perigo`.
        varianteConfirmar="primario"
        aoConfirmar={confirmarEstorno}
        aoCancelar={() => setAConfirmar(null)}
      />
    </Pagina>
  )
}

interface AcoesDaFila {
  aberta: string | null
  erroDaAcao: string | null
  abrir: (chave: string) => void
  fechar: () => void
  registrar: (fazer: () => Promise<unknown>) => Promise<void>
  setorId: number
  pedirEstorno: (chave: string, estornaveis: Estornavel[]) => void
  escolherEstorno: (e: Estornavel) => void
  estornando: boolean
}

/**
 * `fila` é a que o filtro deixou; `completa`, a resposta inteira. A seção que tinha linha e perdeu
 * todas pelo filtro continua na tela com o próprio vazio, para o operador ver ONDE o filtro cortou.
 */
function SecoesDaFila({ fila, completa, aoLimparFiltros, acoes }: {
  fila: FilaDoSetorDto
  completa: FilaDoSetorDto
  aoLimparFiltros: () => void
  acoes: AcoesDaFila
}) {
  const { apontar, entregar: podeEntregar, podeEstornar } = usePermissoesDaExecucao()
  const { aberta, erroDaAcao, abrir, fechar, registrar, setorId, pedirEstorno, escolherEstorno, estornando } = acoes
  const rotuloDeIniciar = rotuloDaAcao('Iniciar', fila.setorAtividade)
  const rotuloDeTerminar = rotuloDaAcao('Terminar', fila.setorAtividade)

  /** O painel de uma linha: a recusa do servidor, e o formulário embaixo dela. */
  function painel(chave: string, formulario: ReactNode) {
    if (aberta !== chave) return undefined
    return (
      <>
        <BannerDeErro mensagem={erroDaAcao} />
        {formulario}
      </>
    )
  }

  /** O botão que abre a ação; some enquanto ela está aberta (o formulário tem "Cancelar"). */
  function botao(chave: string, rotulo: string, no: NoResumoDto) {
    if (aberta === chave) return undefined
    return (
      <Botao variante="secundario" aria-label={`${rotulo} ${rotuloDoNo(no)}`} onClick={() => abrir(chave)}>
        {rotulo}
      </Botao>
    )
  }

  /**
   * "Estornar" da linha: só os registros que a sessão pode estornar (o servidor já filtrou por quem
   * lê; isto repete a regra para o 403 nunca ser o primeiro aviso). Some enquanto a lista curta
   * dela está aberta, como os outros botões. Não supõe nada sobre a quantidade do registro contra a
   * da linha: um Término maior que a tarefa aparece nela (desvio D4 do plano da Fase 3D).
   */
  function botaoDeEstorno(chave: string, estornaveis: Estornavel[], no: NoResumoDto) {
    const meus = estornaveis.filter((e) => podeEstornar(e.usuarioId))
    if (meus.length === 0 || aberta === chave) return undefined
    return (
      <Botao
        variante="secundario"
        aria-label={`Estornar ${rotuloDoNo(no)}`}
        disabled={estornando}
        onClick={() => pedirEstorno(chave, meus)}
      >
        Estornar
      </Botao>
    )
  }

  function listaDeEstorno(chave: string, estornaveis: Estornavel[]) {
    return painel(chave, (
      <ListaDeEstornaveis
        estornaveis={estornaveis.filter((e) => podeEstornar(e.usuarioId))}
        aoEscolher={escolherEstorno}
        aoCancelar={fechar}
      />
    ))
  }

  if (estaVazia(completa)) {
    return (
      <EstadoVazio
        titulo="Nada neste Setor agora"
        descricao="O que chegar para iniciar, trabalhar, coletar ou montar aqui aparece nesta tela."
      />
    )
  }
  if (estaVazia(fila)) {
    return (
      <EstadoVazio
        titulo="Nada nesta fila com esses filtros"
        descricao="Nenhuma linha da fila combina com o que está marcado."
        acao={<Botao variante="secundario" onClick={aoLimparFiltros}>Limpar filtros</Botao>}
      />
    )
  }

  return (
    <>
      {completa.emTrabalho.length > 0 && (
        <Secao titulo="Em trabalho">
          {fila.emTrabalho.length === 0 && <SemLinhaNoFiltro />}
          {fila.emTrabalho.map((l) => {
            const chaveEstornar = chaveDeEstornar('em-trabalho', l.no.id, l.ordem)
            const terminarAqui = apontar ? botao(chaveDeTerminar(l.no.id, l.ordem), rotuloDeTerminar, l.no) : undefined
            const estornarAqui = botaoDeEstorno(chaveEstornar, l.estornaveis, l.no)
            return (
              <ItemComAcao
                key={`${l.no.id}-${l.ordem}`}
                // Fragmento só com botão: `ItemComAcao` desenha o contêiner de ação para qualquer valor
                // verdadeiro, e um fragmento vazio é verdadeiro.
                acao={terminarAqui || estornarAqui ? <>{terminarAqui}{estornarAqui}</> : undefined}
                painel={
                  painel(chaveDeTerminar(l.no.id, l.ordem), (
                    <FormularioDeQuantidade
                      rotulo={rotuloDeTerminar}
                      maximo={l.quantidade}
                      aoConfirmar={(q) => registrar(() => terminar(l.no.id, { setorId, ordem: l.ordem, quantidade: q }))}
                      aoCancelar={fechar}
                    />
                  ))
                  ?? listaDeEstorno(chaveEstornar, l.estornaveis)
                }
              >
                <CabecalhoDoNo no={l.no} />
                <Detalhe>{`${formatarQuantidade(l.quantidade)} em trabalho · passo ${l.ordem}`}</Detalhe>
              </ItemComAcao>
            )
          })}
        </Secao>
      )}
      {completa.aIniciar.length > 0 && (
        <Secao titulo="A iniciar aqui">
          {fila.aIniciar.length === 0 && <SemLinhaNoFiltro />}
          {fila.aIniciar.filter((l) => l.no.pausa === null).map((l) => (
            <ItemComAcao
              key={`${l.no.id}-${l.ordem}`}
              acao={apontar && botao(chaveDeIniciar(l.no.id, l.ordem), rotuloDeIniciar, l.no)}
              painel={painel(chaveDeIniciar(l.no.id, l.ordem), (
                <FormularioDeQuantidade
                  rotulo={rotuloDeIniciar}
                  maximo={l.quantidade}
                  aoConfirmar={(q) => registrar(() => iniciar(l.no.id, { setorId, quantidade: q }))}
                  aoCancelar={fechar}
                />
              ))}
            >
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} a iniciar · passo ${l.ordem}`}</Detalhe>
            </ItemComAcao>
          ))}
          {/* O servidor manda os pausados no fim; a tela os separa por conta própria. Sem `acao`: a
              pausa recusa o Iniciar. O título é `aria-hidden` porque cada linha já diz "Pedido
              pausado" e traz a pílula — um `<li>` de título seria lido como item da lista. */}
          {fila.aIniciar.some((l) => l.no.pausa !== null) && (
            <li className="pt-2 text-sm font-medium text-tinta-fraca" aria-hidden="true">Pausados</li>
          )}
          {fila.aIniciar.filter((l) => l.no.pausa !== null).map((l) => (
            <ItemComAcao key={`${l.no.id}-${l.ordem}`}>
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} a iniciar · passo ${l.ordem} · Pedido pausado`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {completa.aguardandoMontagem.length > 0 && (
        <Secao titulo="Aguardando montagem">
          {fila.aguardandoMontagem.length === 0 && <SemLinhaNoFiltro />}
          {fila.aguardandoMontagem.map((g) => {
            const filhoAberto = g.filhos.find((f) => chaveDeLevar(g.pai.id, f.no.id) === aberta)
            const levarPara = g.primeiroPassoDoPai
            return (
              <ItemComAcao
                key={g.pai.id}
                // O pai começa aqui consumindo os filhos (spec da Fase 3D, §2.1). "Dá para iniciar 0"
                // não oferece o botão: o backend recusaria qualquer N.
                acao={apontar && g.iniciaAqui && g.daParaMontar > 0 && g.pai.pausa === null
                  && botao(chaveDeIniciarPai(g.pai.id), rotuloDeIniciar, g.pai)}
                painel={
                  painel(chaveDeIniciarPai(g.pai.id), (
                    <FormularioDeQuantidade
                      rotulo={rotuloDeIniciar}
                      maximo={g.daParaMontar}
                      aoConfirmar={(q) => registrar(() => iniciar(g.pai.id, { setorId, quantidade: q }))}
                      aoCancelar={fechar}
                    />
                  ))
                  ?? (filhoAberto && levarPara && painel(chaveDeLevar(g.pai.id, filhoAberto.no.id), (
                    <FormularioDeQuantidade
                      rotulo="Levar"
                      maximo={filhoAberto.presente}
                      aoConfirmar={(q) => registrar(() => entregar([{
                        estruturaItemId: filhoAberto.no.id,
                        origem: { posicao: 'AguardandoMontagem', setorId, ordem: null },
                        quantidade: q,
                      }]))}
                      aoCancelar={fechar}
                    />
                  )))
                }
              >
                <GrupoDeMontagem
                  grupo={g}
                  acaoDoFilho={(f) => (podeEntregar && !g.iniciaAqui && levarPara && f.presente > 0
                    ? botao(chaveDeLevar(g.pai.id, f.no.id), `Levar para ${levarPara.nome}`, f.no)
                    : undefined)}
                />
              </ItemComAcao>
            )
          })}
        </Secao>
      )}
      {completa.aguardandoColeta.length > 0 && (
        <Secao titulo="Aguardando coleta">
          {fila.aguardandoColeta.length === 0 && <SemLinhaNoFiltro />}
          {fila.aguardandoColeta.map((l) => (
            <ItemComAcao
              key={`${l.no.id}-${l.ordem}`}
              acao={botaoDeEstorno(chaveDeEstornar('coleta', l.no.id, l.ordem), l.estornaveis, l.no)}
              painel={listaDeEstorno(chaveDeEstornar('coleta', l.no.id, l.ordem), l.estornaveis)}
            >
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} aguardando coleta · passo ${l.ordem}`}</Detalhe>
              <Detalhe>{`Destino: ${descreverDestino(l.destino, l.no)}`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {completa.sobra.length > 0 && (
        <Secao titulo="Sobra">
          {fila.sobra.length === 0 && <SemLinhaNoFiltro />}
          {fila.sobra.map((s) => (
            <ItemComAcao
              key={`${s.no.id}-${s.origem}-${s.ordem ?? ''}`}
              acao={botaoDeEstorno(chaveDeEstornar('sobra', s.no.id, s.ordem), s.estornaveis, s.no)}
              painel={listaDeEstorno(chaveDeEstornar('sobra', s.no.id, s.ordem), s.estornaveis)}
            >
              <CabecalhoDoNo no={s.no} />
              <DetalheDaSobra sobra={s} />
            </ItemComAcao>
          ))}
        </Secao>
      )}
    </>
  )
}

function Secao({ titulo, children }: { titulo: string; children: ReactNode }) {
  const id = useId()
  return (
    <section aria-labelledby={id} className="flex flex-col gap-3">
      <h2 id={id} className="text-lg font-medium text-tinta">{titulo}</h2>
      <ListaDeCadastro rotulo={titulo}>{children}</ListaDeCadastro>
    </section>
  )
}

function SemLinhaNoFiltro() {
  return <ItemDeCadastro>Nada nesta seção com esses filtros.</ItemDeCadastro>
}

function CabecalhoDoNo({ no }: { no: NoResumoDto }) {
  return (
    <>
      <span className="flex flex-wrap items-center gap-2 font-medium text-tinta">
        {rotuloDoNo(no)}
        {no.pausa && <Pilula tom="atencao">Pausado</Pilula>}
      </span>
      <span className="text-xs text-tinta-fraca">{caminhoDoNo(no)}</span>
      {no.pausa?.motivo && <span className="text-xs text-tinta-fraca">{`Pausa: ${no.pausa.motivo}`}</span>}
    </>
  )
}

function Detalhe({ children }: { children: ReactNode }) {
  return <span className="text-sm text-tinta">{children}</span>
}

/**
 * "Dá para iniciar N; falta iniciar M" (spec da Fase 3, §7.6). O "falta" por filho é o que ele precisa
 * para a unidade N+1, e só existe enquanto há próxima unidade — a API manda `null` quando não há.
 * Fora do primeiro passo do pai, o card diz para onde levar os filhos (spec da Fase 3D, §4.4).
 */
function GrupoDeMontagem({ grupo, acaoDoFilho }: {
  grupo: GrupoAguardandoMontagem
  acaoDoFilho: (filho: GrupoAguardandoMontagem['filhos'][number]) => ReactNode
}) {
  return (
    <>
      <CabecalhoDoNo no={grupo.pai} />
      <Detalhe>
        {`Dá para iniciar ${formatarQuantidade(grupo.daParaMontar)}; falta iniciar ${formatarQuantidade(grupo.faltaMontar)}.`}
      </Detalhe>
      {!grupo.iniciaAqui && (
        <span className="text-xs text-tinta-fraca">
          {grupo.primeiroPassoDoPai
            ? `${grupo.pai.descricao} começa em ${grupo.primeiroPassoDoPai.nome}: leve os filhos para lá.`
            : `${grupo.pai.descricao} não tem Roteiro. Peça ao PCP para cadastrá-lo.`}
        </span>
      )}
      <ul aria-label={`Filhos de ${grupo.pai.descricao}`} className="flex flex-col gap-1 text-sm text-tinta-fraca">
        {grupo.filhos.map((f) => (
          <li key={f.no.id} className="flex flex-wrap items-center justify-between gap-2">
            <span>
              {`${rotuloDoNo(f.no)}: ${formatarQuantidade(f.presente)} aqui, ${formatarQuantidade(f.quantidadePorPai)} por unidade`}
              {f.faltaParaProxima !== null && f.faltaParaProxima > 0 && f.necessarioParaProxima !== null
                && ` — falta ${formatarQuantidade(f.faltaParaProxima)} de ${formatarQuantidade(f.necessarioParaProxima)} para a próxima`}
            </span>
            {acaoDoFilho(f)}
          </li>
        ))}
      </ul>
    </>
  )
}

/**
 * A sobra (spec da Fase 3, §7.5, regra 30) é informada aqui, e o descarte dela não: ele é registrado
 * na Fase 5, pelo ator da perda. O único botão da linha é o "Estornar" do registro que a produziu, e
 * ele vem do `ItemComAcao` que envolve este componente, quando há registro estornável. Quando o
 * filho aguarda montagem em mais de um Setor, o texto diz que não dá para saber em qual está a
 * unidade a mais, em vez de escolher um por conta própria.
 */
function DetalheDaSobra({ sobra }: { sobra: LinhaDeSobra }) {
  const q = formatarQuantidade(sobra.quantidade)
  if (sobra.origem === 'UltimoPasso') {
    return <Detalhe>{`${q} a mais no passo ${sobra.ordem}: o pai já tem o que precisa.`}</Detalhe>
  }
  return (
    <>
      <Detalhe>{`${q} a mais aguardando montagem do que o pai precisa.`}</Detalhe>
      {sobra.emMaisDeUmSetor && (
        <span className="text-xs text-tinta-fraca">
          Este item aguarda montagem em mais de um Setor; não dá para saber em qual está a unidade a mais.
        </span>
      )}
    </>
  )
}
