import { useRef, useState } from 'react'
import { formatarData, formatarDataHora } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { registrarEmbalagem, type PecaDoPanoramaDto } from '../api/panorama'
import { CampoDeCaixa } from '../components/CampoDeCaixa'
import { Pilula } from '../components/Pilula'
import { formatarQuantidade } from '../execucao/formatacao'
import { usePermissoesDaEmbalagem } from './usePermissoesDaEmbalagem'

interface Props {
  peca: PecaDoPanoramaDto
  /** A marca foi gravada, ou a recusa disse que a tela estava velha: a tela recarrega. */
  aoGravar: () => void
}

/**
 * O "Embalado" da Peça. Quem pode marcar vê a caixa: travada com "faltam N em produção" enquanto a Peça não pode
 * ser marcada (e não vale uma marca antiga), e marcada com quem e quando. Quem só lê vê a pílula neutra e a data.
 * O `ref` segura o segundo toque no mesmo quadro, antes de o React desabilitar a caixa; a recusa do servidor fica
 * ao lado da caixa e não derruba a tela.
 */
export function CelulaDeEmbalagem({ peca, aoGravar }: Props) {
  const { marcar } = usePermissoesDaEmbalagem()
  const { vale, podeMarcar, emProducao, ultima } = peca.embalagem
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const enviandoRef = useRef(false)

  async function gravar(embalado: boolean) {
    if (enviandoRef.current) return
    enviandoRef.current = true
    setEnviando(true)
    setErro(null)
    try {
      await registrarEmbalagem(peca.estruturaItemId, embalado)
      aoGravar()
    } catch (falha) {
      setErro(mensagemDeErro(falha, 'Não foi possível gravar o "Embalado".'))
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  if (!marcar) {
    if (!vale || !ultima) return null
    return (
      <div className="flex flex-wrap items-center gap-1.5">
        <Pilula>Embalado</Pilula>
        <span className="text-sm text-tinta-fraca">{formatarData(ultima.dataHora.split('T')[0])}</span>
      </div>
    )
  }

  const travada = !podeMarcar && !vale
  const dica = vale && ultima
    ? `${ultima.usuario} · ${formatarDataHora(ultima.dataHora)}`
    : travada && emProducao > 0 ? `faltam ${formatarQuantidade(emProducao)} em produção` : undefined

  return (
    <div className="flex flex-col gap-1">
      <CampoDeCaixa
        marcado={vale}
        aoMudar={(m) => void gravar(m)}
        desabilitado={travada || enviando}
        dica={dica}
      >
        Embalado
      </CampoDeCaixa>
      {erro && <p role="alert" className="text-sm text-negativo-texto">{erro}</p>}
    </div>
  )
}
