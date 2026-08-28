import type { TomDePilula } from '../components/Pilula'

/**
 * Os cinco status do `CK_Pedido_Status` (`specs/02-modelo-de-dados.sql:170-171`), NA ORDEM DO
 * DDL — que é a ordem do fluxo, e é a ordem em que a Home apresenta o resumo.
 *
 * Módulo compartilhado, e não cópia: a partir da Fase 1E a `HomePage` e a `PedidosPage` precisam
 * do mesmo mapa. Duas cópias divergiriam no dia em que o domínio ganhar um sexto status.
 */
export const STATUS_DO_PEDIDO = [
  'Aberto', 'EmProducao', 'AguardandoExpedicao', 'Concluido', 'Cancelado',
] as const

/** Status que tiram o Pedido da fila de "está parado": ele acabou, de um jeito ou de outro. */
export const ENCERRADOS = ['Concluido', 'Cancelado'] as const

/**
 * `Concluido` é o único que ganha tom positivo; `Cancelado`, o negativo. Os intermediários ficam
 * neutros — verde e vermelho são reservados a estado que exige decisão, e "em produção" não exige
 * nenhuma (`CLAUDE.md`, seção Interface).
 *
 * Recebe `string` e não o tipo estreito de propósito: o `status` do `PedidoDto` vem da API como
 * `string`, e um valor inesperado tem de cair em `neutro`, nunca numa cor de estado por acidente.
 */
export function tomDoStatus(status: string): TomDePilula {
  if (status === 'Concluido') return 'positivo'
  if (status === 'Cancelado') return 'negativo'
  return 'neutro'
}
