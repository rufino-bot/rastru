import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import type { SecaoDoLote } from './loteDaFila'

interface Props {
  secao: SecaoDoLote
  marcados: number
  /** Marcados que o filtro esconde: vão no lote assim mesmo (desvio D8). */
  ocultos: number
  /** Algum marcado com quantidade inválida ou bloqueado: o botão não envia. */
  invalido: boolean
  enviando: boolean
  erro: string | null
  aoEnviar: () => void
  aoLimpar: () => void
}

/**
 * A barra fixa do lote da fila (desvios D11 e D12 do plano 2 dos filtros). O verbo é o puro —
 * "Iniciar 5 itens", sem a atividade do Setor que o botão da linha usa —, e só usa tinta e
 * superfície: marcar linhas não é estado de negócio, então nenhuma cor de estado aparece fora do
 * banner da recusa.
 */
export function BarraDoLote({ secao, marcados, ocultos, invalido, enviando, erro, aoEnviar, aoLimpar }: Props) {
  const verbo = secao === 'emTrabalho' ? 'Terminar' : 'Iniciar'
  return (
    <div className="sticky bottom-0 flex flex-col gap-3 border-t border-borda bg-superficie py-3">
      <BannerDeErro mensagem={erro} />
      <div className="flex flex-wrap items-center gap-3">
        <Botao onClick={aoEnviar} carregando={enviando} rotuloCarregando="Registrando…" disabled={invalido}>
          {`${verbo} ${marcados} ${marcados === 1 ? 'item' : 'itens'}`}
        </Botao>
        <Botao variante="secundario" onClick={aoLimpar} disabled={enviando}>Limpar seleção</Botao>
      </div>
      {ocultos > 0 && (
        <p className="text-sm text-tinta-fraca">
          {ocultos === 1 ? '1 marcado oculto pelo filtro' : `${ocultos} marcados ocultos pelo filtro`}
        </p>
      )}
    </div>
  )
}
