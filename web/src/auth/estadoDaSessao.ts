import type { UsuarioDto } from '../api/tipos'

export type EstadoSessao =
  | { status: 'carregando' }
  | { status: 'autenticado'; usuario: UsuarioDto }
  | { status: 'anonimo'; motivo?: 'sessao-expirada' }

// Mapeamento puro do resultado do init-refresh -> estado. Testavel sem React.
// null vira anonimo SEM motivo: nao ter sessao no boot e diferente de "sessao expirou no uso".
export function estadoDaSessao(usuario: UsuarioDto | null): EstadoSessao {
  return usuario ? { status: 'autenticado', usuario } : { status: 'anonimo' }
}
