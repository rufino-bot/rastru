import { useAuth } from '../auth/AuthContext'
import { podeEscrever } from '../auth/permissoes'

export interface PermissoesDaExpedicao {
  /** Aferir, contar, mandar para conserto, pedir liberação e expedir — a Qualidade. */
  registrar: boolean
  /** Responder ao pedido de liberação — a Gestão. */
  responder: boolean
  /** Estornar um registro da expedição: o autor dele, ou PCP/Administrador. */
  podeEstornar: (autorId: number) => boolean
}

const NENHUMA: PermissoesDaExpedicao = {
  registrar: false, responder: false, podeEstornar: () => false,
}

/**
 * Espelho, NA TELA, de quem pode estornar o registro de outra pessoa. O backend decide
 * (`EhPcpOuAdministrador`, 403 `Proibido`); aqui só se esconde o botão de quem receberia o 403. É a mesma lista de
 * `usePermissoesDaExecucao` e de `usePermissoesDaSeparacao`.
 */
const ESTORNAM_REGISTRO_ALHEIO: readonly string[] = ['PCP', 'Administrador']

/**
 * O que a sessão pode fazer na expedição, num lugar só — cada ação espelha UMA chave de `permissoes.ts`, que espelha
 * UM controller. Gating vai na ação, não no link (`CLAUDE.md`, Interface): quem não pode, lê.
 *
 * Isto é conveniência de interface: o 403 do backend continua sendo a fronteira real. No estorno o backend decide
 * ainda mais fino — o autor, ou PCP/Administrador —, e a tela repete a regra só para não oferecer o botão a quem
 * receberia o 403 `Proibido`.
 */
export function usePermissoesDaExpedicao(): PermissoesDaExpedicao {
  const { estado } = useAuth()
  if (estado.status !== 'autenticado') return NENHUMA
  const { id, perfil } = estado.usuario
  const estorna = podeEscrever(perfil, 'estornoDaExpedicao')
  return {
    registrar: podeEscrever(perfil, 'expedicao'),
    responder: podeEscrever(perfil, 'liberacao'),
    podeEstornar: (autorId) => estorna && (autorId === id || ESTORNAM_REGISTRO_ALHEIO.includes(perfil)),
  }
}
