import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { estornarBaixa, ROTULO_DO_MOTIVO, type BaixaDto } from '../api/baixas'
import { desfazerNaoRepor, naoReporBaixa } from '../api/retrabalho'
import { formatarDataHora } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { ehConflito } from '../api/execucao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Confirmacao } from '../components/Confirmacao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { formatarQuantidade, rotuloDoSaldo } from '../execucao/formatacao'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import type { PortaDoRetrabalho } from '../retrabalho/PainelDeAbertura'
import { ReposicaoDaLinha } from '../retrabalho/ReposicaoDaLinha'
import { usePermissoesDaBaixa } from './usePermissoesDaBaixa'

// Somas em décimos de milésimo inteiros, como a `RemessasRecentes`: 0,1 + 0,2 tem de dar 0,3.
const ESCALA = 10_000

/**
 * O resumo das linhas de uma baixa: por nó, o total e de onde saiu ("4: 3 a iniciar, 1 em Usinagem (passo 2)").
 * Com mais de um nó (a cascata de C6 e o "Descartar tudo"), cada grupo nomeia o nó: "4 de Eixo: …; e 6 de Chapa".
 * Num nó só que não é o `noId` da tela (a baixa de um filho, listada no painel do pai), ele também é nomeado.
 */
function resumoDasLinhas(b: BaixaDto, noId?: number): string {
  const porNo = new Map<number, { descricao: string; unidades: number; origens: string[] }>()
  for (const l of b.linhas) {
    const g = porNo.get(l.estruturaItemId) ?? { descricao: l.descricao, unidades: 0, origens: [] }
    g.unidades += Math.round(l.quantidade * ESCALA)
    g.origens.push(
      rotuloDoSaldo({
        posicao: l.origemPosicao, setorId: l.origemSetorId, setorNome: l.origemSetorNome, ordem: l.origemOrdem,
        quantidade: l.quantidade,
      }),
    )
    porNo.set(l.estruturaItemId, g)
  }
  const grupos = [...porNo.values()].map((g) => ({ ...g, total: formatarQuantidade(g.unidades / ESCALA) }))
  const umSoEDesteNo = grupos.length === 1 && (noId === undefined || b.linhas[0].estruturaItemId === noId)
  if (umSoEDesteNo) return `${grupos[0].total}: ${grupos[0].origens.join(', ')}`
  const frases = grupos.map((g) => `${g.total} de ${g.descricao}: ${g.origens.join(', ')}`)
  return frases.length < 2 ? frases.join('') : `${frases.slice(0, -1).join('; ')}; e ${frases[frases.length - 1]}`
}

/** "Descartar tudo do Pedido P-2291": só o escopo que não é um nó; o do nó é lido nas linhas. */
function frasesDoEscopo(b: BaixaDto, nomeDoEscopo?: Props['nomeDoEscopo']): string | null {
  if (b.escopo.tipo === 'No') return null
  const nome = nomeDoEscopo?.(b.escopo) ?? `(Id ${b.escopo.id})`
  return `Descartar tudo do ${b.escopo.tipo === 'Pedido' ? 'Pedido' : 'Agrupamento'} ${nome}`
}

interface Props {
  /** Mais recente primeiro: `listarBaixasDoNo` ou `listarBaixasDoPedido`. */
  carregar: () => Promise<BaixaDto[]>
  /** Muda quando a tela gravou uma baixa: a lista já carregada recarrega. A primeira leitura é a da montagem. */
  versao: number
  /**
   * A baixa mudou (estornada, ou o servidor disse que já estava; "Não repor" gravado ou desfeito): a tela recarrega o
   * que depende dela, o saldo e o "a repor" do cabeçalho.
   */
  aoEstornar: () => void
  /** O nome de quem a tela já conhece (o número do Pedido, o código do Agrupamento); sem ele, o Id. */
  nomeDoEscopo?: (escopo: BaixaDto['escopo']) => string | null
  /** O nó cujo painel mostra a seção: a baixa de outro nó (um descendente) é lida com o nome dele. */
  noId?: number
  /** O nível do título: `h3` dentro do painel do nó, `h2` onde as seções da tela são `h2`. */
  nivel?: 'h2' | 'h3'
  /** Abre o Retrabalho de uma baixa a repor; só a tela do Pedido o passa, e sem ele a linha não oferece o botão. */
  aoAbrirRetrabalho?: (porta: PortaDoRetrabalho) => void
}

/**
 * A seção "Baixas" (C7, C27 da spec da Fase 5B-1): cada baixa com o motivo, o resumo do que saiu e de onde, o escopo
 * quando é o descarte de um Pedido ou Agrupamento, e o autor. "Estornar" desfaz a baixa inteira, sob o `estornavel` do
 * servidor e `usePermissoesDaBaixa().podeEstornar`. "Estornada" é pílula neutra: desfazer não é reprovar.
 *
 * Um 409 (`JaEstornado`) é tela velha: a frase do servidor fica na seção e a lista recarrega. Quando a escrita tira o
 * "Estornar", o foco vai ao título da seção.
 */
export function HistoricoDeBaixas({ carregar, versao, aoEstornar, nomeDoEscopo, noId, nivel = 'h3', aoAbrirRetrabalho }: Props) {
  const { podeEstornar } = usePermissoesDaBaixa()
  const idDoTitulo = useId()
  const [baixas, setBaixas] = useState<BaixaDto[] | null>(null)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [recarregando, setRecarregando] = useState(false)
  const [aConfirmar, setAConfirmar] = useState<BaixaDto | null>(null)
  const [erroDoEstorno, setErroDoEstorno] = useState<string | null>(null)
  const [estornando, setEstornando] = useState(false)
  const enviandoRef = useRef(false)
  const sequencia = useRef(0)
  const carregarRef = useRef(carregar)
  carregarRef.current = carregar
  const titulo = useRef<HTMLSpanElement>(null)
  const Titulo = nivel
  const botoes = useRef(new Map<number, HTMLButtonElement>())
  const ultimaAberta = useRef<number | null>(null)

  // A resposta que chega depois de uma mais nova é descartada.
  const recarregar = useCallback(async () => {
    const minha = ++sequencia.current
    setRecarregando(true)
    try {
      const lidas = await carregarRef.current()
      if (minha !== sequencia.current) return
      setBaixas(lidas)
      setErroDeCarga(null)
    } catch (e) {
      if (minha !== sequencia.current) return
      setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar as baixas.'))
    } finally {
      if (minha === sequencia.current) setRecarregando(false)
    }
  }, [])

  const versaoLida = useRef<number | null>(null)
  useEffect(() => {
    if (versaoLida.current === versao) return
    versaoLida.current = versao
    recarregar()
  }, [versao, recarregar])

  useDevolverFoco(
    aConfirmar !== null || estornando,
    () => (ultimaAberta.current !== null ? botoes.current.get(ultimaAberta.current) : undefined) ?? titulo.current,
    !recarregando && !estornando,
  )

  async function confirmar() {
    if (!aConfirmar || enviandoRef.current) return
    enviandoRef.current = true
    setEstornando(true)
    const alvo = aConfirmar
    setAConfirmar(null)
    setErroDoEstorno(null)
    try {
      await estornarBaixa(alvo.id)
      await recarregar()
      aoEstornar()
    } catch (e) {
      setErroDoEstorno(mensagemDeErro(e, 'Não foi possível estornar a baixa.'))
      if (ehConflito(e)) {
        await recarregar()
        aoEstornar()
      }
    } finally {
      enviandoRef.current = false
      setEstornando(false)
    }
  }

  async function aoMudarAReposicaoDe() {
    await recarregar()
    aoEstornar()
  }

  function descricaoDa(b: BaixaDto): string {
    return `${ROTULO_DO_MOTIVO[b.motivo]}, ${resumoDasLinhas(b, noId)}, em ${formatarDataHora(b.dataHora)}`
  }

  return (
    <section aria-labelledby={idDoTitulo} className="flex flex-col gap-3">
      <Titulo id={idDoTitulo} className={nivel === 'h2' ? 'text-lg font-medium text-tinta' : 'font-medium text-tinta'}>
        <span ref={titulo} tabIndex={-1}>Baixas</span>
      </Titulo>
      <BannerDeErro mensagem={erroDeCarga} />
      <BannerDeErro mensagem={erroDoEstorno} />
      {baixas === null && erroDeCarga === null && <EstadoCarregando />}
      {baixas !== null && baixas.length === 0 && <EstadoVazio titulo="Nenhuma baixa." />}
      {baixas !== null && baixas.length > 0 && (
        <ListaDeCadastro rotulo="Baixas, da mais recente à mais antiga">
          {baixas.map((b) => {
            const escopo = frasesDoEscopo(b, nomeDoEscopo)
            return (
              <ItemComAcao
                key={b.id}
                acao={b.estornavel && podeEstornar(b.usuarioId) && (
                  <Botao
                    ref={(el) => {
                      if (el) botoes.current.set(b.id, el)
                      else botoes.current.delete(b.id)
                    }}
                    variante="secundario"
                    aria-label={`Estornar a baixa: ${descricaoDa(b)}`}
                    onClick={() => { ultimaAberta.current = b.id; setAConfirmar(b) }}
                    disabled={estornando}
                  >
                    Estornar
                  </Botao>
                )}
              >
                <span className="flex flex-wrap items-center gap-2 text-sm font-medium text-tinta">
                  {ROTULO_DO_MOTIVO[b.motivo]}
                  {b.estornada && <Pilula>Estornada</Pilula>}
                </span>
                {escopo && <span className="text-sm text-tinta">{escopo}</span>}
                <span className="text-sm text-tinta">{resumoDasLinhas(b, noId)}</span>
                {b.observacao && <span className="text-xs text-tinta-fraca">{b.observacao}</span>}
                <span className="text-xs text-tinta-fraca">{`${b.usuarioNome} · ${formatarDataHora(b.dataHora)}`}</span>
                <ReposicaoDaLinha
                  nome={`a baixa de ${ROTULO_DO_MOTIVO[b.motivo]}`}
                  aRepor={b.aRepor}
                  dispensa={b.dispensa}
                  pedidoRetrabalho={b.pedidoRetrabalho}
                  reaproveitada={b.motivo === 'Reaproveitado'}
                  naoRepor={(j) => naoReporBaixa(b.id, j)}
                  desfazer={desfazerNaoRepor}
                  aoMudar={aoMudarAReposicaoDe}
                  aoAbrirRetrabalho={aoAbrirRetrabalho && (() => aoAbrirRetrabalho({ tipo: 'Baixa', baixa: b }))}
                  pronto={!recarregando}
                />
              </ItemComAcao>
            )
          })}
        </ListaDeCadastro>
      )}
      <Confirmacao
        aberto={aConfirmar !== null}
        mensagem={aConfirmar && `Estornar a baixa de ${descricaoDa(aConfirmar)}? Devolve tudo o que ela tirou; o estorno fica no histórico.`}
        rotuloConfirmar="Estornar"
        // Estorno é correção, não destruição: nada some do livro. Por isso `primario`, não `perigo`.
        varianteConfirmar="primario"
        aoConfirmar={confirmar}
        aoCancelar={() => setAConfirmar(null)}
      />
    </section>
  )
}
