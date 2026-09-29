import { useRef, useState, type FormEvent } from 'react'
import { formatarDataHora, type PedidoDto } from '../api/cadastros'
import { pausarPedido, retomarPedido, ehConflito } from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Botao } from '../components/Botao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { ENCERRADOS } from './statusDoPedido'

interface Props {
  pedido: PedidoDto
  /** A pausa mudou (ou a tela ficou velha): o chamador recarrega o Pedido. */
  aoMudar: () => Promise<void>
}

/**
 * Pausar e retomar um Pedido (spec da Fase 3D, §2.5), com o aviso da pausa aberta. Todo perfil vê o
 * aviso; os botões são do PCP e da Gestão (`usePodeEscrever('pausa')`), e o 403 do backend continua
 * sendo a fronteira real. A pausa recusa só o Iniciar — o aviso diz isso, para ninguém achar que o
 * Pedido inteiro travou.
 */
export function ControleDePausa({ pedido, aoMudar }: Props) {
  const podePausar = usePodeEscrever('pausa')
  const [pedindoMotivo, setPedindoMotivo] = useState(false)
  const [motivo, setMotivo] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  // O toque no mesmo quadro, antes do React redesenhar o botão desabilitado — e o `submit` do
  // formulário, que o `disabled` do botão não barra (Enter no campo).
  const enviandoRef = useRef(false)

  const encerrado = (ENCERRADOS as readonly string[]).includes(pedido.status)

  async function executar(acao: () => Promise<unknown>) {
    if (enviandoRef.current) return
    enviandoRef.current = true
    setEnviando(true)
    setErro(null)
    try {
      await acao()
      setPedindoMotivo(false)
      setMotivo('')
      await aoMudar()
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar a pausa do pedido.'))
      if (ehConflito(e)) await aoMudar()
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  function confirmarPausa(e: FormEvent) {
    e.preventDefault()
    const texto = motivo.trim()
    void executar(() => pausarPedido(pedido.id, texto === '' ? null : texto))
  }

  return (
    <div className="flex flex-col gap-3">
      {pedido.pausa && (
        <p className="text-sm text-tinta">
          {`Pausado desde ${formatarDataHora(pedido.pausa.desde)} por ${pedido.pausa.porUsuarioNome}`}
          {pedido.pausa.motivo && ` — ${pedido.pausa.motivo}`}
          {'. Nada dele começa até ser retomado; o que já está em trabalho continua.'}
        </p>
      )}
      <BannerDeErro mensagem={erro} />
      {podePausar && pedido.pausa && (
        <Botao
          variante="secundario"
          onClick={() => executar(() => retomarPedido(pedido.id))}
          carregando={enviando}
          rotuloCarregando="Retomando…"
          className="self-start"
        >
          Retomar
        </Botao>
      )}
      {podePausar && !pedido.pausa && !encerrado && !pedindoMotivo && (
        <Botao variante="secundario" onClick={() => setPedindoMotivo(true)} className="self-start">
          Pausar
        </Botao>
      )}
      {podePausar && !pedido.pausa && !encerrado && pedindoMotivo && (
        <form onSubmit={confirmarPausa} className="flex flex-col gap-3 border-t border-borda pt-3">
          <Campo rotulo="Motivo (opcional)">
            {(id) => (
              <input
                id={id}
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
                maxLength={200}
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
          <div className="flex flex-wrap gap-2">
            <Botao type="submit" carregando={enviando} rotuloCarregando="Pausando…">Confirmar pausa</Botao>
            <Botao variante="secundario" onClick={() => { setPedindoMotivo(false); setMotivo('') }}>Cancelar</Botao>
          </div>
        </form>
      )}
    </div>
  )
}
