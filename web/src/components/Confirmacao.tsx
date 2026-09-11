import type { ReactNode } from 'react'
import { Botao, type VarianteDeBotao } from './Botao'

interface Props {
  /** `false` não renderiza nada — mesmo padrão de "estado controlado pelo chamador" de `BannerDeErro`. */
  aberto: boolean
  mensagem: ReactNode
  rotuloConfirmar: string
  rotuloCancelar?: string
  /**
   * Variante do botão de confirmar. Parâmetro, não fixo em `perigo`: os dois consumidores de hoje
   * confirmam exclusão (ação destrutiva — `perigo` é a variante de `Botao` para isso, uma
   * convenção de peso visual, não a cor de ESTADO de negócio que a Fase 1D reserva a
   * aprovado/reprovado), mas a primitiva não amarra a decisão a exclusão.
   */
  varianteConfirmar?: VarianteDeBotao
  aoConfirmar: () => void
  aoCancelar: () => void
}

/**
 * Diálogo modal de confirmação: a pausa deliberada antes de uma ação sem volta.
 *
 * Extraída para a Task 8b da Fase 2 (exclusão de nó da estrutura, com aviso de que a subárvore vai
 * junto) do modal que `PedidoDetalhePage` já tinha escrito à mão para a exclusão de Agrupamento
 * (Fase 1A/1D) — dois consumidores é o que torna a primitiva certa a nomear agora, e não mais uma
 * cópia colada na tela seguinte. `PedidoDetalhePage` continua com a cópia original: migrá-la para
 * esta primitiva é código morto de escopo (não pedido por esta task, e mexer numa tela já revisada
 * sem motivo de negócio é risco sem retorno).
 *
 * Ordem e peso visual são deliberados, não estética (mesmo raciocínio do modal original): o botão
 * de confirmar vem primeiro e à esquerda, "Cancelar" por último e à direita — onde cai o polegar
 * num tablet —, com `autoFocus`, para quem navega por teclado sem ler não cair direto no botão de
 * ação.
 */
export function Confirmacao({
  aberto,
  mensagem,
  rotuloConfirmar,
  rotuloCancelar = 'Cancelar',
  varianteConfirmar = 'perigo',
  aoConfirmar,
  aoCancelar,
}: Props) {
  if (!aberto) return null

  return (
    <div className="fixed inset-0 z-10 flex items-center justify-center bg-tinta/50 p-4">
      <div
        role="dialog"
        aria-modal="true"
        className="flex w-full max-w-sm flex-col gap-4 rounded-lg bg-superficie p-5 shadow-lg"
      >
        <p className="text-tinta">{mensagem}</p>
        <div className="flex flex-wrap justify-end gap-2">
          <Botao variante={varianteConfirmar} onClick={aoConfirmar}>{rotuloConfirmar}</Botao>
          <Botao variante="secundario" onClick={aoCancelar} autoFocus>{rotuloCancelar}</Botao>
        </div>
      </div>
    </div>
  )
}
