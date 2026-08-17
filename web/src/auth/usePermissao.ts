import { useAuth } from './AuthContext'
import { podeEscrever, type Recurso } from './permissoes'

/**
 * `podeEscrever` ligado à sessão. Sessão não autenticada devolve `false` — as telas de cadastro só
 * existem dentro do `ProtectedRoute`, então na prática este caso é a montagem de teste. A guarda
 * fica assim mesmo: sem ela, o caminho anônimo lança TypeError em vez de negar.
 */
export function usePodeEscrever(recurso: Recurso): boolean {
  const { estado } = useAuth()
  return estado.status === 'autenticado' && podeEscrever(estado.usuario.perfil, recurso)
}
