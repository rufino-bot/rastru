import { Link } from 'react-router-dom'
import { formatarDataHora, type PedidoDto } from '../api/cadastros'
import { Pilula } from '../components/Pilula'
import { tomDoStatus } from './statusDoPedido'

/**
 * Uma linha de Pedido: número, cliente, status e data de abertura, com o item inteiro como alvo do
 * clique.
 *
 * Extraída na Fase 1E para servir de segundo consumidor a uma seção "abertos há mais tempo" que
 * outra task desta mesma fase ainda vai acrescentar à Home, mostrando o MESMO item de Pedido que a
 * `PedidosPage` já mostra — dois consumidores do mesmo markup é o que faz primitiva. Antes disso
 * era código inline de uma tela só, e extrair teria sido abstração sem segundo caso.
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
    // dele, o overlay vaza para o ancestral posicionado mais próximo e cobre o que não devia. E
    // vale a armadilha m6 documentada no próprio `ItemDeCadastro`: overlay e `acao` no mesmo item
    // colidem, e jsdom não pega — a conferência é no navegador.
    <Link
      to={`/pedidos/${pedido.id}`}
      className="flex flex-col gap-1 after:absolute after:inset-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      <span className="font-medium">
        <span className="font-mono">{pedido.numero}</span> — {pedido.cliente}
      </span>
      <span className="flex items-center gap-2 text-sm text-tinta-fraca">
        <Pilula tom={tomDoStatus(pedido.status)}>{pedido.status}</Pilula>
        aberto em {formatarDataHora(pedido.dataAbertura)}
      </span>
    </Link>
  )
}
