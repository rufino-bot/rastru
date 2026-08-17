/**
 * Indicador de "carregando", extraído das seis telas que buscam dados (I3 da review de branch da
 * Fase 1D: a mesma linha, caractere a caractere, em Setores/Materiais/Pedidos/PedidoDetalhe/
 * Componentes/Home, e a única forma com ≥3 consumidores que não tinha virado primitiva).
 *
 * `role="status"` no molde do `EstadoVazio` — o indicador de carregando não tinha nenhum papel
 * ARIA antes desta extração, então quem usa leitor de tela era avisado do vazio e do erro, mas
 * nunca do carregando.
 */
export function EstadoCarregando() {
  return (
    <p role="status" className="text-tinta-fraca">
      Carregando…
    </p>
  )
}
