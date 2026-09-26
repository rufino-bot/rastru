import { useEffect, useId, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  obterFila, iniciar, terminar, montar, entregar, ehConflito,
  type FilaDoSetorDto, type GrupoAguardandoMontagem, type LinhaDeSobra, type NoResumoDto,
} from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { INTERVALO_DA_EXECUCAO_MS, useCargaPeriodica } from '../hooks/useCargaPeriodica'
import { caminhoDoNo, descreverDestino, formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { lembrarSetor } from '../execucao/setorLembrado'
import { usePermissoesDaExecucao } from '../execucao/usePermissoesDaExecucao'
import { FormularioDeQuantidade } from '../execucao/FormularioDeQuantidade'
import { FormularioDeRedirecionamento } from '../execucao/FormularioDeRedirecionamento'
import type { EstadoDaEscolhaDeSetor } from './FilaPage'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { ItemComAcao } from '../components/ItemComAcao'
import { Botao } from '../components/Botao'

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

/** `/fila/:setorId` — a fila de um Setor (spec §6.1). */
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
 * por tela é o que cabe num celular, e é o que deixa "Cancelar" e "Quantidade" sem ambiguidade.
 */
const chaveDeIniciar = (noId: number, ordem: number) => `iniciar:${noId}:${ordem}`
const chaveDeTerminar = (noId: number, ordem: number) => `terminar:${noId}:${ordem}`
const chaveDeMontar = (paiId: number) => `montar:${paiId}`
const chaveDeLevar = (paiId: number, filhoId: number) => `levar:${paiId}:${filhoId}`

/** Toda ação que a fila de agora ainda oferece — a que sumiu não pode continuar aberta. */
function chavesDaFila(fila: FilaDoSetorDto): Set<string> {
  const chaves = new Set<string>()
  for (const l of fila.aIniciar) chaves.add(chaveDeIniciar(l.no.id, l.ordem))
  for (const l of fila.emTrabalho) chaves.add(chaveDeTerminar(l.no.id, l.ordem))
  for (const g of fila.aguardandoMontagem) {
    if (g.daParaMontar > 0) chaves.add(chaveDeMontar(g.pai.id))
    for (const f of g.filhos) if (f.presente > 0) chaves.add(chaveDeLevar(g.pai.id, f.no.id))
  }
  return chaves
}

const SAIU_DA_FILA = 'O item que você estava registrando não está mais nesta fila: outra pessoa o moveu.'

function FilaDoSetor({ setorId }: { setorId: number }) {
  const { dados: fila, carregando, erro, recarregar } = useCargaPeriodica(
    () => obterFila(setorId), setorId, INTERVALO_DA_EXECUCAO_MS, 'Não foi possível carregar a fila.',
  )
  const [aberta, setAberta] = useState<string | null>(null)
  const [erroDaAcao, setErroDaAcao] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)

  // Lembra só o Setor cuja fila CARREGOU: um Id digitado na barra que dá 404 não vira lembrança.
  useEffect(() => { if (fila?.setorId === setorId) lembrarSetor(setorId) }, [fila, setorId])

  // Review Focus 3: a atualização (periódica, ou a recarga depois de um 409) tirou da fila a linha
  // cujo formulário está aberto. O formulário fecha, e o aviso — com a recusa do servidor, se foi
  // ela — sobe para o topo da tela, porque o painel onde ele estava deixou de existir.
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
   * Toda escrita passa por aqui: sucesso fecha o formulário e recarrega na hora (spec §6.2); recusa
   * mostra a mensagem no próprio formulário e, se for 409, recarrega também — o 409 quase sempre
   * quer dizer que a tela ficou velha (spec §8.3). O `throw` devolve a recusa ao formulário, que só
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

  const titulo = fila ? `Fila — ${fila.setorNome}` : 'Fila do Setor'

  return (
    <Pagina titulo={titulo} acao={<TrocarDeSetor />}>
      {/* Com a fila já na tela, este banner é de uma ATUALIZAÇÃO que falhou: a lista abaixo é a da
          última carga boa (a decisão "falha de atualização mantém os dados" de `useCargaPeriodica`). */}
      <BannerDeErro mensagem={erro} />
      <BannerDeErro mensagem={aviso} />
      {carregando && <EstadoCarregando />}
      {fila && (
        <SecoesDaFila
          fila={fila}
          acoes={{ aberta, erroDaAcao, abrir, fechar, registrar, setorId }}
        />
      )}
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
}

function SecoesDaFila({ fila, acoes }: { fila: FilaDoSetorDto; acoes: AcoesDaFila }) {
  const { apontar, entregar: podeEntregar } = usePermissoesDaExecucao()
  const { aberta, erroDaAcao, abrir, fechar, registrar, setorId } = acoes

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

  const vazia = fila.aIniciar.length === 0 && fila.emTrabalho.length === 0
    && fila.aguardandoColeta.length === 0 && fila.aguardandoMontagem.length === 0 && fila.sobra.length === 0
  if (vazia) {
    return (
      <EstadoVazio
        titulo="Nada neste Setor agora"
        descricao="O que chegar para iniciar, trabalhar, coletar ou montar aqui aparece nesta tela."
      />
    )
  }

  return (
    <>
      {fila.aIniciar.length > 0 && (
        <Secao titulo="A iniciar aqui">
          {fila.aIniciar.map((l) => (
            <ItemComAcao
              key={`${l.no.id}-${l.ordem}`}
              acao={apontar && botao(chaveDeIniciar(l.no.id, l.ordem), 'Iniciar', l.no)}
              painel={painel(chaveDeIniciar(l.no.id, l.ordem), (
                <FormularioDeQuantidade
                  rotulo="Iniciar"
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
        </Secao>
      )}
      {fila.emTrabalho.length > 0 && (
        <Secao titulo="Em trabalho">
          {fila.emTrabalho.map((l) => (
            <ItemComAcao
              key={`${l.no.id}-${l.ordem}`}
              acao={apontar && botao(chaveDeTerminar(l.no.id, l.ordem), 'Terminar', l.no)}
              painel={painel(chaveDeTerminar(l.no.id, l.ordem), (
                <FormularioDeQuantidade
                  rotulo="Terminar"
                  maximo={l.quantidade}
                  aoConfirmar={(q) => registrar(() => terminar(l.no.id, { setorId, ordem: l.ordem, quantidade: q }))}
                  aoCancelar={fechar}
                />
              ))}
            >
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} em trabalho · passo ${l.ordem}`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {fila.aguardandoColeta.length > 0 && (
        <Secao titulo="Aguardando coleta">
          {fila.aguardandoColeta.map((l) => (
            <ItemComAcao key={`${l.no.id}-${l.ordem}`}>
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} aguardando coleta · passo ${l.ordem}`}</Detalhe>
              <Detalhe>{`Destino: ${descreverDestino(l.destino, l.no)}`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {fila.aguardandoMontagem.length > 0 && (
        <Secao titulo="Aguardando montagem">
          {fila.aguardandoMontagem.map((g) => {
            const chaveDoFilhoAberto = g.filhos
              .map((f) => chaveDeLevar(g.pai.id, f.no.id))
              .find((c) => c === aberta)
            const filhoAberto = g.filhos.find((f) => chaveDeLevar(g.pai.id, f.no.id) === aberta)
            return (
              <ItemComAcao
                key={g.pai.id}
                // "Dá para montar 0" não oferece Montar: o backend recusaria qualquer N.
                acao={apontar && g.daParaMontar > 0 && botao(chaveDeMontar(g.pai.id), 'Montar', g.pai)}
                painel={
                  painel(chaveDeMontar(g.pai.id), (
                    <FormularioDeQuantidade
                      rotulo="Montar"
                      maximo={g.daParaMontar}
                      aoConfirmar={(q) => registrar(() => montar(g.pai.id, { setorId, quantidade: q }))}
                      aoCancelar={fechar}
                    />
                  ))
                  ?? (chaveDoFilhoAberto && filhoAberto && painel(chaveDoFilhoAberto, (
                    <FormularioDeRedirecionamento
                      pai={g.pai}
                      setorAtualId={setorId}
                      maximo={filhoAberto.presente}
                      aoConfirmar={(destinoSetorId, q) => registrar(() => entregar([{
                        estruturaItemId: filhoAberto.no.id,
                        origem: { posicao: 'AguardandoMontagem', setorId, ordem: null },
                        destinoSetorId,
                        quantidade: q,
                      }]))}
                      aoCancelar={fechar}
                    />
                  )))
                }
              >
                <GrupoDeMontagem
                  grupo={g}
                  acaoDoFilho={(f) => (podeEntregar && f.presente > 0
                    ? botao(chaveDeLevar(g.pai.id, f.no.id), 'Levar para outro Setor', f.no)
                    : undefined)}
                />
              </ItemComAcao>
            )
          })}
        </Secao>
      )}
      {fila.sobra.length > 0 && (
        <Secao titulo="Sobra">
          {fila.sobra.map((s) => (
            <ItemComAcao key={`${s.no.id}-${s.origem}-${s.ordem ?? ''}`}>
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

function CabecalhoDoNo({ no }: { no: NoResumoDto }) {
  return (
    <>
      <span className="font-medium text-tinta">{rotuloDoNo(no)}</span>
      <span className="text-xs text-tinta-fraca">{caminhoDoNo(no)}</span>
    </>
  )
}

function Detalhe({ children }: { children: ReactNode }) {
  return <span className="text-sm text-tinta">{children}</span>
}

/**
 * "Dá para montar N; falta X de Y para a próxima" (spec §7.6). O "falta" é por filho, e só existe
 * enquanto há próxima unidade a montar — a API manda `null` quando não há.
 */
function GrupoDeMontagem({ grupo, acaoDoFilho }: {
  grupo: GrupoAguardandoMontagem
  acaoDoFilho: (filho: GrupoAguardandoMontagem['filhos'][number]) => ReactNode
}) {
  return (
    <>
      <CabecalhoDoNo no={grupo.pai} />
      <Detalhe>
        {`Dá para montar ${formatarQuantidade(grupo.daParaMontar)}; falta montar ${formatarQuantidade(grupo.faltaMontar)}.`}
      </Detalhe>
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
 * A sobra (spec §7.5, regra 30) é só informada: o descarte é registrado na Fase 5, pelo ator da
 * perda. Quando o filho aguarda montagem em mais de um Setor, o texto diz que não dá para saber em
 * qual está a unidade a mais, em vez de escolher um por conta própria.
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
