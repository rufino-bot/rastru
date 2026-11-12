import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { formatarDataHora } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import {
  desfazerNaoRepor, estornarReclamacao, naoReporReclamacao, type ReclamacaoDto,
} from '../api/retrabalho'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Confirmacao } from '../components/Confirmacao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { formatarQuantidade } from '../execucao/formatacao'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import type { PortaDoRetrabalho } from './PainelDeAbertura'
import { ReposicaoDaLinha } from './ReposicaoDaLinha'
import { usePermissoesDaReposicao } from './usePermissoesDaReposicao'

interface Props {
  /** Mais recente primeiro: `listarReclamacoes` do Pedido. */
  carregar: () => Promise<ReclamacaoDto[]>
  /** Muda quando a tela gravou algo que altera a reposição: a lista recarrega. A primeira leitura é a da montagem. */
  versao: number
  /** Uma reclamação mudou (estornada, dispensada ou a dispensa desfeita): a tela recarrega o cabeçalho e o que depende. */
  aoMudar: () => void
  /** Abre o Retrabalho de uma reclamação a repor. */
  aoAbrirRetrabalho?: (porta: PortaDoRetrabalho) => void
}

/**
 * A seção "Reclamações" do Pedido original: cada reclamação com a Peça, a quantidade, o que o cliente disse e o autor,
 * a reposição da linha (`ReposicaoDaLinha`) e o "Estornar", sob `estornavel` do servidor e
 * `usePermissoesDaReposicao().podeEstornarReclamacao`. Registrar mora no `acao` da página, não aqui. "Estornada" é
 * pílula neutra.
 */
export function SecaoDeReclamacoes({ carregar, versao, aoMudar, aoAbrirRetrabalho }: Props) {
  const { podeEstornarReclamacao } = usePermissoesDaReposicao()
  const idDoTitulo = useId()
  const [lista, setLista] = useState<ReclamacaoDto[] | null>(null)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [recarregando, setRecarregando] = useState(false)
  const [aConfirmar, setAConfirmar] = useState<ReclamacaoDto | null>(null)
  const [erroDoEstorno, setErroDoEstorno] = useState<string | null>(null)
  const [estornando, setEstornando] = useState(false)
  const estornandoRef = useRef(false)
  const sequencia = useRef(0)
  const carregarRef = useRef(carregar)
  carregarRef.current = carregar
  const titulo = useRef<HTMLSpanElement>(null)
  const botoes = useRef(new Map<number, HTMLButtonElement>())
  const ultimaAberta = useRef<number | null>(null)

  // A resposta que chega depois de uma mais nova é descartada.
  const recarregar = useCallback(async () => {
    const minha = ++sequencia.current
    setRecarregando(true)
    setErroDeCarga(null)
    try {
      const lidas = await carregarRef.current()
      if (minha !== sequencia.current) return
      setLista(lidas)
    } catch (e) {
      if (minha !== sequencia.current) return
      setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar as reclamações.'))
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
    if (!aConfirmar || estornandoRef.current) return
    estornandoRef.current = true
    setEstornando(true)
    const alvo = aConfirmar
    setAConfirmar(null)
    setErroDoEstorno(null)
    try {
      await estornarReclamacao(alvo.id)
      await recarregar()
      aoMudar()
    } catch (e) {
      setErroDoEstorno(mensagemDeErro(e, 'Não foi possível estornar a reclamação.'))
    } finally {
      estornandoRef.current = false
      setEstornando(false)
    }
  }

  async function aoMudarUma() {
    await recarregar()
    aoMudar()
  }

  return (
    <section aria-labelledby={idDoTitulo} className="flex flex-col gap-3">
      <h2 id={idDoTitulo} className="text-lg font-medium text-tinta">
        <span ref={titulo} tabIndex={-1}>Reclamações</span>
      </h2>
      <BannerDeErro mensagem={erroDeCarga} />
      {erroDeCarga !== null && (
        <Botao variante="secundario" className="self-start" onClick={recarregar}>Tentar de novo</Botao>
      )}
      <BannerDeErro mensagem={erroDoEstorno} />
      {lista === null && erroDeCarga === null && <EstadoCarregando />}
      {lista !== null && lista.length === 0 && <EstadoVazio titulo="Nenhuma reclamação" />}
      {lista !== null && lista.length > 0 && (
        <ListaDeCadastro rotulo="Reclamações, da mais recente à mais antiga">
          {lista.map((r) => {
            const nome = `a reclamação de ${r.descricao}`
            return (
              <ReposicaoDaLinha
                key={r.id}
                nome={nome}
                aRepor={r.aRepor}
                dispensa={r.dispensa}
                pedidoRetrabalho={r.pedidoRetrabalho}
                reaproveitada={false}
                naoRepor={(j) => naoReporReclamacao(r.id, j)}
                desfazer={desfazerNaoRepor}
                aoMudar={aoMudarUma}
                aoAbrirRetrabalho={aoAbrirRetrabalho && (() => aoAbrirRetrabalho({ tipo: 'Reclamacao', reclamacao: r }))}
                pronto={!recarregando}
              >
                {({ reposicao, painel }) => (
                  <ItemComAcao
                    painel={painel}
                    acao={r.estornavel && podeEstornarReclamacao(r.usuarioId) && (
                      <Botao
                        ref={(el) => {
                          if (el) botoes.current.set(r.id, el)
                          else botoes.current.delete(r.id)
                        }}
                        variante="secundario"
                        aria-label={`Estornar ${nome}`}
                        onClick={() => { ultimaAberta.current = r.id; setAConfirmar(r) }}
                        disabled={estornando}
                      >
                        Estornar
                      </Botao>
                    )}
                  >
                    <span className="flex flex-wrap items-center gap-2 text-sm font-medium text-tinta">
                      {`${formatarQuantidade(r.quantidade)} de ${r.descricao}${r.codigo ? ` (${r.codigo})` : ''}`}
                      {r.estornada && <Pilula>Estornada</Pilula>}
                    </span>
                    <span className="text-sm text-tinta">{r.texto}</span>
                    <span className="text-xs text-tinta-fraca">{`${r.usuarioNome} · ${formatarDataHora(r.dataHora)}`}</span>
                    {reposicao}
                  </ItemComAcao>
                )}
              </ReposicaoDaLinha>
            )
          })}
        </ListaDeCadastro>
      )}
      <Confirmacao
        aberto={aConfirmar !== null}
        mensagem={aConfirmar && `Estornar a reclamação de ${aConfirmar.descricao}? Ela deixa de contar como a repor; o estorno fica no histórico.`}
        rotuloConfirmar="Estornar"
        varianteConfirmar="primario"
        aoConfirmar={confirmar}
        aoCancelar={() => setAConfirmar(null)}
      />
    </section>
  )
}
