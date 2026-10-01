import { useEffect, useRef } from 'react'

/**
 * Devolve o foco a quem abriu um painel quando ele fecha.
 *
 * O botão que abre um `PainelDeEscrita` some enquanto o painel está aberto (decisão D5 do plano da
 * 1F), e o controle focado no fechamento (o `Cancelar`, o submit, um campo) sai do DOM com o painel.
 * Sem isto o foco cai no `<body>`, e quem navega por teclado ou leitor de tela volta ao topo da
 * página sem aviso.
 *
 * Três regras, cada uma com motivo:
 * - **Só na transição de aberto para fechado**, nunca na montagem: abrir a tela não rouba foco.
 * - **Só se o foco se perdeu** (está no `<body>`). Fechar um painel porque outro abriu (o de Peça
 *   e o do nó, no Agrupamento) não pode tirar o foco que o painel novo acabou de dar ao campo dele;
 *   nem tirar o de quem clicou em outra coisa enquanto esperava.
 * - **Espera `pronto`**: o alvo pode estar fora do DOM no momento do fechamento (o "Editar" de um
 *   item some enquanto a lista recarrega). O pedido fica pendente até `pronto`, e reabrir o painel
 *   antes disso o descarta.
 *
 * `alvo` é lido quando o foco é devolvido, não quando o painel fecha: é o que permite à tela cair
 * num botão de reserva se o original não voltou com a recarga.
 */
export function useDevolverFoco(
  aberto: boolean,
  alvo: () => HTMLElement | null | undefined,
  pronto = true,
): void {
  const estavaAberto = useRef(aberto)
  const pendente = useRef(false)

  // Sem lista de dependências de propósito: o pedido pendente é atendido no primeiro commit em que
  // `pronto` é verdade, qualquer que seja o estado que mudou para chegar lá.
  useEffect(() => {
    if (aberto) pendente.current = false
    else if (estavaAberto.current) pendente.current = true
    estavaAberto.current = aberto

    if (!pendente.current || !pronto) return
    pendente.current = false
    const focado = document.activeElement
    if (focado !== null && focado !== document.body) return
    alvo()?.focus()
  })
}
