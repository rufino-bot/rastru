import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { mensagemDeErro } from '../api/erros'
import { buscarAndamento, type AndamentoDoPedidoDto } from '../api/andamento'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { duracaoLegivel, msDesde } from '../panorama/idade'
import { TrilhaDoNo } from './TrilhaDoNo'

interface Props {
  pedidoId: number
  /** Muda quando a tela gravou algo que altera o andamento: a seção recarrega. A primeira leitura é a da montagem. */
  versao: number
}

function Numero({ children }: { children: string }) {
  return <p className="rounded-lg border border-borda bg-superficie p-3 text-sm text-tinta">{children}</p>
}

/**
 * "Onde está travando" (Fase 6A): os três números do Pedido e a trilha de cada Peça em aberto. Tudo neutro (C9). A
 * idade se calcula contra o `agora` do servidor (C12). A tela só monta a seção para Pedido aberto.
 */
export function SecaoOndeEstaTravando({ pedidoId, versao }: Props) {
  const idDoTitulo = useId()
  const [dados, setDados] = useState<AndamentoDoPedidoDto | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const sequencia = useRef(0)

  // A resposta que chega depois de uma mais nova é descartada.
  const recarregar = useCallback(async () => {
    const minha = ++sequencia.current
    setErro(null)
    try {
      const lido = await buscarAndamento(pedidoId)
      if (minha === sequencia.current) setDados(lido)
    } catch (e) {
      if (minha === sequencia.current) setErro(mensagemDeErro(e, 'Não foi possível carregar o andamento do pedido.'))
    }
  }, [pedidoId])

  const versaoLida = useRef<number | null>(null)
  useEffect(() => {
    if (versaoLida.current === versao) return
    versaoLida.current = versao
    recarregar()
  }, [versao, recarregar])

  return (
    <section aria-labelledby={idDoTitulo} className="flex flex-col gap-3">
      <h2 id={idDoTitulo} className="text-lg font-medium text-tinta">Onde está travando</h2>
      <BannerDeErro mensagem={erro} />
      {erro !== null && <Botao variante="secundario" className="self-start" onClick={recarregar}>Tentar de novo</Botao>}
      {dados === null && erro === null && <EstadoCarregando />}
      {dados !== null && erro === null && (
        <>
          <div className="grid gap-2 sm:grid-cols-3">
            <Numero>{`Aberto há ${duracaoLegivel(msDesde(dados.abertura, dados.agora))}`}</Numero>
            {dados.inicioReal ? (
              <Numero>{`Esperou para começar ${duracaoLegivel(msDesde(dados.abertura, dados.inicioReal))}`}</Numero>
            ) : (
              <Numero>Ainda não começou</Numero>
            )}
            {dados.inicioReal && (
              <div className="rounded-lg border border-borda bg-superficie p-3 text-sm text-tinta">
                <p>{`Em produção há ${duracaoLegivel(msDesde(dados.inicioReal, dados.agora))}`}</p>
                {dados.pausadoDepoisDoInicioSegundos > 0 && (
                  <p className="text-tinta-fraca">{`dos quais ${duracaoLegivel(dados.pausadoDepoisDoInicioSegundos * 1000)} pausado`}</p>
                )}
              </div>
            )}
          </div>
          {dados.pecas.length === 0 ? (
            <EstadoVazio titulo="Nada parado neste Pedido." />
          ) : (
            <div className="flex flex-col gap-4">
              {dados.pecas.map((p) => (
                <div key={p.estruturaItemId} className="rounded-lg border border-borda bg-superficie p-3">
                  <TrilhaDoNo no={p} agora={dados.agora} maisParado={dados.maisParado} nivel="Peca" />
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </section>
  )
}
