import { useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { formatarDataHora } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import type { DispensaDto, PedidoRetrabalhoResumoDto } from '../api/retrabalho'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Pilula } from '../components/Pilula'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { PainelDeNaoRepor } from './PainelDeNaoRepor'
import { usePermissoesDaReposicao } from './usePermissoesDaReposicao'

interface Props {
  /** Como a linha se chama para o leitor de tela: "a baixa de Eixo". Entra no nome dos botões. */
  nome: string
  /** Unidades de Peça por repor (C1); lido do servidor, nunca calculado aqui. */
  aRepor: number
  dispensa: DispensaDto | null
  pedidoRetrabalho: PedidoRetrabalhoResumoDto | null
  /** A baixa `Reaproveitado`: o vínculo diz "Foi para", e não "Reposta por". */
  reaproveitada: boolean
  /** Grava o "não repor" desta linha; rejeita na falha. */
  naoRepor: (justificativa: string) => Promise<unknown>
  /** Desfaz o "não repor" pelo Id da dispensa; rejeita na falha. */
  desfazer: (dispensaId: number) => Promise<void>
  /** O servidor mudou: quem usa a linha recarrega a lista e o que depende dela (o cabeçalho). */
  aoMudar: () => void
  /** A lista ainda está recarregando depois de uma escrita: o foco só volta quando ela terminou. */
  pronto?: boolean
}

/**
 * O que a reposição acrescenta a uma linha de baixa ou de reclamação: a pílula "A repor" (`atencao`), o vínculo
 * ("Reposta por", "Foi para", "Não repor: justificativa") e as ações "Não repor" e "Desfazer", sob
 * `usePermissoesDaReposicao`. A linha só lê `aRepor`, `dispensa` e `pedidoRetrabalho` do servidor.
 *
 * O botão que abre o Retrabalho mora nesta mesma fila de ações, ao lado do "Não repor".
 */
export function ReposicaoDaLinha({
  nome, aRepor, dispensa, pedidoRetrabalho, reaproveitada, naoRepor, desfazer, aoMudar, pronto = true,
}: Props) {
  const { naoRepor: podeNaoRepor } = usePermissoesDaReposicao()
  const [aberto, setAberto] = useState(false)
  const [desfazendo, setDesfazendo] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const desfazendoRef = useRef(false)
  const botaoNaoRepor = useRef<HTMLButtonElement>(null)
  const botaoDesfazer = useRef<HTMLButtonElement>(null)
  const estado = useRef<HTMLSpanElement>(null)

  // O "Não repor" some com a baixa dispensada: o foco vai ao "Desfazer" que aparece, ou ao estado da linha.
  useDevolverFoco(aberto, () => botaoNaoRepor.current ?? botaoDesfazer.current ?? estado.current, pronto)

  async function desfazerAgora() {
    if (!dispensa || desfazendoRef.current) return
    desfazendoRef.current = true
    setDesfazendo(true)
    setErro(null)
    try {
      await desfazer(dispensa.id)
      aoMudar()
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível desfazer o "não repor".'))
    } finally {
      desfazendoRef.current = false
      setDesfazendo(false)
    }
  }

  const aReporAgora = aRepor > 0
  if (!aReporAgora && !dispensa && !pedidoRetrabalho) return null

  return (
    <div className="flex flex-col gap-2">
      <span ref={estado} tabIndex={-1} className="flex flex-wrap items-center gap-2 text-sm text-tinta">
        {aReporAgora && <Pilula tom="atencao">A repor</Pilula>}
        {pedidoRetrabalho && (
          <span>
            {reaproveitada ? 'Foi para ' : 'Reposta por '}
            <Link to={`/pedidos/${pedidoRetrabalho.id}`} className="font-mono font-medium underline">{pedidoRetrabalho.numero}</Link>
          </span>
        )}
        {dispensa && <span>{`Não repor: ${dispensa.justificativa}`}</span>}
      </span>
      {dispensa && (
        <span className="text-xs text-tinta-fraca">{`${dispensa.usuarioNome} · ${formatarDataHora(dispensa.dataHora)}`}</span>
      )}
      <div className="flex flex-wrap items-center gap-2">
        {aReporAgora && podeNaoRepor && !aberto && (
          <Botao ref={botaoNaoRepor} variante="secundario" aria-label={`Não repor ${nome}`} onClick={() => setAberto(true)}>
            Não repor
          </Botao>
        )}
        {dispensa && podeNaoRepor && (
          <Botao
            ref={botaoDesfazer} variante="secundario" aria-label={`Desfazer o "não repor" de ${nome}`}
            onClick={desfazerAgora} carregando={desfazendo} rotuloCarregando="Desfazendo…"
          >
            Desfazer
          </Botao>
        )}
      </div>
      <BannerDeErro mensagem={erro} />
      {podeNaoRepor && aberto && (
        <PainelDeNaoRepor subtitulo={nome} naoRepor={naoRepor} aoConcluir={aoMudar} aoFechar={() => setAberto(false)} />
      )}
    </div>
  )
}
