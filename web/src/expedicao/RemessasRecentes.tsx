import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { estornarRemessa, listarRemessasRecentes, type RemessaDto } from '../api/expedicao'
import { formatarDataHora } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { Pilula } from '../components/Pilula'
import { formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { useEscritaDoPainel } from './escritaDoPainel'
import { usePermissoesDaExpedicao } from './usePermissoesDaExpedicao'

// Somas em décimos de milésimo inteiros, como em `tiradaDaRemessa`: 0,1 + 0,2 tem de dar 0,3.
const ESCALA = 10_000

interface PecaDaRemessa {
  /** Uma Peça é uma linha só por Pedido; o `pecaId` já é único por Pedido. */
  pecaId: number
  rotulo: string
  quantidade: number
}

/**
 * Uma linha por Peça: a remessa grava uma linha `Expedicao` por posição de origem (C10 da spec da Fase 5A), e quem
 * a lê quer saber quanto de cada Peça saiu, não de onde.
 */
function pecasDaRemessa(r: RemessaDto): PecaDaRemessa[] {
  const porPeca = new Map<number, { rotulo: string; unidades: number }>()
  for (const l of r.linhas) {
    const rotulo = `${rotuloDoNo({ codigoDoComponente: l.codigoDoComponente, descricao: l.pecaDescricao })} (${l.pedidoNumero})`
    const atual = porPeca.get(l.pecaId)
    porPeca.set(l.pecaId, { rotulo, unidades: (atual?.unidades ?? 0) + Math.round(l.quantidade * ESCALA) })
  }
  return [...porPeca].map(([pecaId, { rotulo, unidades }]) => ({ pecaId, rotulo, quantidade: unidades / ESCALA }))
}

/** Os Pedidos que o estorno reabre: os de linha com `pedidoConcluido`, cada um uma vez (C20). */
function pedidosQueReabrem(r: RemessaDto): string[] {
  const numeros = new Map<number, string>()
  for (const l of r.linhas) if (l.pedidoConcluido) numeros.set(l.pedidoId, l.pedidoNumero)
  return [...numeros.values()]
}

const quando = (r: RemessaDto) => formatarDataHora(r.dataHora)

interface PainelProps {
  remessa: RemessaDto
  aoFechar: () => void
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega. */
  aoConcluir: () => void
}

/**
 * "Estornar remessa" (C20 da spec da Fase 5A): antes de confirmar, todas as Peças que ela desfaz e os Pedidos que
 * reabrem. A remessa se estorna inteira, pela rota dela; a genérica da movimentação a recusa.
 */
function PainelDeEstornoDaRemessa({ remessa, aoFechar, aoConcluir }: PainelProps) {
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível estornar a remessa.')
  const reabre = pedidosQueReabrem(remessa)

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    escrever(() => estornarRemessa(remessa.id))
  }

  return (
    <PainelDeEscrita
      titulo="Estornar remessa"
      subtitulo={`${quando(remessa)}, de ${remessa.usuarioNome}`}
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      <ul aria-label="Peças que a remessa desfaz" className="flex flex-col gap-0.5">
        {pecasDaRemessa(remessa).map((p) => (
          <li key={p.pecaId} className="text-sm text-tinta">{`${p.rotulo} · ${formatarQuantidade(p.quantidade)}`}</li>
        ))}
      </ul>
      {reabre.map((numero) => (
        <p key={numero} className="text-sm font-medium text-tinta">{`Reabre o Pedido ${numero}.`}</p>
      ))}
      <BannerDeErro mensagem={erro} />
      <Botao type="submit" carregando={enviando} rotuloCarregando="Estornando…" className="self-start">Estornar</Botao>
    </PainelDeEscrita>
  )
}

interface Props {
  /** Uma remessa foi estornada (ou o servidor disse que já estava): a tela recarrega a lista de Peças. */
  aoEstornar: () => void
  /**
   * Muda quando a tela gravou outra remessa (o "Expedir" de um cartão ou do lote): a lista já carregada recarrega.
   * A primeira leitura é a da montagem.
   */
  versao?: number
}

/**
 * "Remessas recentes" (C20 da spec da Fase 5A, seção 6.1): as dos últimos 30 dias, com as Peças e as quantidades de
 * cada uma. É daqui que se estorna uma remessa, inclusive a que concluiu o Pedido e tirou a Peça da lista; o
 * histórico do cartão a mostra, mas não a estorna.
 *
 * "Estornar remessa" fica sob `usePermissoesDaExpedicao().podeEstornar` e sob o `estornavel` do servidor (que nega o
 * Pedido cancelado, C13): o autor, ou PCP e Administrador. Um painel aberto por vez. Ao fechar, o foco volta ao botão
 * da remessa depois da recarga; se ela deixou de ser estornável, cai no título da seção.
 *
 * A seção carrega a própria lista, e a falha dela não derruba a tela: o banner fica aqui.
 */
export function RemessasRecentes({ aoEstornar, versao = 0 }: Props) {
  const { podeEstornar } = usePermissoesDaExpedicao()
  const [remessas, setRemessas] = useState<RemessaDto[] | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [recarregando, setRecarregando] = useState(false)
  const sequencia = useRef(0)

  const [aberta, setAberta] = useState<number | null>(null)
  const ultimaAberta = useRef<number | null>(null)
  const botoes = useRef(new Map<number, HTMLButtonElement>())
  const titulo = useRef<HTMLSpanElement>(null)

  // A resposta que chega depois de uma mais nova é descartada: duas recargas seguidas não terminam na velha.
  const carregar = useCallback(async () => {
    const minha = ++sequencia.current
    setRecarregando(true)
    try {
      const lidas = await listarRemessasRecentes()
      if (minha !== sequencia.current) return
      setRemessas(lidas)
      setErro(null)
    } catch (e) {
      if (minha !== sequencia.current) return
      setErro(mensagemDeErro(e, 'Não foi possível carregar as remessas.'))
    } finally {
      if (minha === sequencia.current) setRecarregando(false)
    }
  }, [])

  // Só a versão dispara a recarga: a primeira leitura é a da montagem.
  const versaoLida = useRef<number | null>(null)
  useEffect(() => {
    if (versaoLida.current === versao) return
    versaoLida.current = versao
    carregar()
  }, [versao, carregar])

  useDevolverFoco(
    aberta !== null,
    () => (ultimaAberta.current !== null ? botoes.current.get(ultimaAberta.current) : undefined) ?? titulo.current,
    !recarregando,
  )

  function abrir(id: number) {
    ultimaAberta.current = id
    setAberta(id)
  }

  // Fecha só se o painel desta remessa ainda for o aberto.
  const fechar = (id: number) => () => setAberta((atual) => (atual === id ? null : atual))

  function concluir() {
    carregar()
    aoEstornar()
  }

  return (
    <section aria-labelledby="remessas-recentes" className="flex flex-col gap-3">
      <h2 id="remessas-recentes" className="text-lg font-medium text-tinta">
        <span ref={titulo} tabIndex={-1}>Remessas recentes</span>
      </h2>
      <BannerDeErro mensagem={erro} />
      {remessas === null && erro === null && <EstadoCarregando />}
      {remessas !== null && remessas.length === 0 && <EstadoVazio titulo="Nenhuma remessa nos últimos 30 dias." />}
      {remessas !== null && remessas.length > 0 && (
        <ListaDeCadastro rotulo="Remessas recentes">
          {remessas.map((r) => (
            <ItemComAcao
              key={r.id}
              acao={r.estornavel && podeEstornar(r.usuarioId) && aberta !== r.id && (
                <Botao
                  ref={(el) => {
                    if (el) botoes.current.set(r.id, el)
                    else botoes.current.delete(r.id)
                  }}
                  variante="secundario"
                  aria-label={`Estornar remessa de ${quando(r)}, de ${r.usuarioNome}`}
                  onClick={() => abrir(r.id)}
                >
                  Estornar remessa
                </Botao>
              )}
              painel={aberta === r.id && (
                <PainelDeEstornoDaRemessa remessa={r} aoFechar={fechar(r.id)} aoConcluir={concluir} />
              )}
            >
              <span className="flex flex-wrap items-center gap-2 text-sm font-medium text-tinta">
                {`${quando(r)} · ${r.usuarioNome}`}
                {r.estornada && <Pilula>estornada</Pilula>}
              </span>
              <ul aria-label="Peças da remessa" className="flex flex-col gap-0.5">
                {pecasDaRemessa(r).map((p) => (
                  <li key={p.pecaId} className="text-sm text-tinta">
                    {`${p.rotulo} · ${formatarQuantidade(p.quantidade)}`}
                  </li>
                ))}
              </ul>
            </ItemComAcao>
          ))}
        </ListaDeCadastro>
      )}
    </section>
  )
}
