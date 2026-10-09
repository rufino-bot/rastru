import { Link } from 'react-router-dom'
import type { PedidoDto } from '../api/cadastros'
import { Pilula } from '../components/Pilula'
import { DatasDoPedido } from './DatasDoPedido'
import { rotuloDoStatus, tomDoStatus } from './statusDoPedido'

/**
 * Uma linha de Pedido: número, cliente, status, prazo de entrega e data de abertura, com o item
 * inteiro como alvo do clique.
 *
 * Hoje tem dois consumidores, e é isso que a faz primitiva: a `PedidosPage` (a lista inteira) e a
 * seção "Prazos de entrega" da `HomePage` (os cinco mais urgentes). Ambas mostram o MESMO item
 * de Pedido.
 *
 * A extração veio ANTES do segundo consumidor existir, e de propósito: em `7dbb61b` só a
 * `PedidosPage` a adotava, e a seção da Home só nasceu em `7cd144f`. Foi decisão de plano
 * (`6020833`) — extrair primeiro para que a seção nascesse consumindo a primitiva, em vez de
 * duplicar o markup e alguém ter de desduplicar depois.
 *
 * **Não traz o `<li>`**: quem o traz é o `ItemDeCadastro`, que guarda o que não varia (semântica de
 * lista, borda e espaçamento). Esta primitiva é o CONTEÚDO dele.
 */
export function LinhaDePedido({ pedido }: { pedido: PedidoDto }) {
  return (
    // O item inteiro é o alvo do clique, e não só o número: numa tela de bancada com tablet, alvo
    // pequeno erra. `after:absolute after:inset-0` estende a área clicável ao cartão sem aninhar
    // elementos interativos.
    //
    // ⚠️ DEPENDE de o ancestral ser posicionado — hoje é o `<li>` do `ItemDeCadastro`. Usada fora
    // dele, o overlay vaza para o ancestral posicionado mais próximo e cobre o que não devia. A
    // `acao` do item, se houver, vai num wrapper empilhado acima do overlay pelo próprio
    // `ItemDeCadastro` (ver o comentário dele); jsdom não calcula layout, então a conferência
    // do clique no botão é no navegador.
    <Link
      to={`/pedidos/${pedido.id}`}
      className="flex flex-col gap-1 after:absolute after:inset-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      <span className="font-medium">
        <span className="font-mono">{pedido.numero}</span> — {pedido.cliente}
      </span>
      <span className="flex flex-wrap items-center gap-2 text-sm text-tinta-fraca">
        <Pilula tom={tomDoStatus(pedido.status)}>{rotuloDoStatus(pedido.status)}</Pilula>
        {pedido.pausa && <Pilula tom="atencao">Pausado</Pilula>}
        {pedido.atrasado && <Pilula tom="atraso">Atrasado</Pilula>}
        <DatasDoPedido dataEntrega={pedido.dataEntrega} dataAbertura={pedido.dataAbertura} />
      </span>
    </Link>
  )
}
