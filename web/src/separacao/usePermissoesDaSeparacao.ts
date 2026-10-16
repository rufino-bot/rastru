import { useAuth } from '../auth/AuthContext'
import { podeEscrever } from '../auth/permissoes'

export interface PermissoesDaSeparacao {
  /** Registrar o que se levou ao nó — o Separador. */
  separar: boolean
  /** Editar os materiais planejados do nó — o PCP. */
  editarMateriais: boolean
  /** Estornar uma separação: o autor dela, ou PCP/Administrador. */
  podeEstornarSeparacao: (autorId: number) => boolean
}

const NENHUMA: PermissoesDaSeparacao = {
  separar: false, editarMateriais: false, podeEstornarSeparacao: () => false,
}

/**
 * Espelho, NA TELA, de quem pode estornar a separação de outra pessoa. O backend decide
 * (`EhPcpOuAdministrador`, 403 `Proibido`); aqui só se esconde o botão de quem receberia o 403. É a mesma
 * lista de `usePermissoesDaExecucao`.
 */
const ESTORNAM_REGISTRO_ALHEIO: readonly string[] = ['PCP', 'Administrador']

/**
 * O que a sessão pode fazer na separação de material, num lugar só — cada ação espelha UMA chave de
 * `permissoes.ts`, que espelha UM controller. Gating vai na ação, não no link (`CLAUDE.md`, Interface):
 * quem não pode, lê.
 *
 * Isto é conveniência de interface: o 403 do backend continua sendo a fronteira real. No estorno o
 * backend decide ainda mais fino — o autor, ou PCP/Administrador —, e a tela repete a regra só para não
 * oferecer o botão a quem receberia o 403 `Proibido`.
 */
export function usePermissoesDaSeparacao(): PermissoesDaSeparacao {
  const { estado } = useAuth()
  if (estado.status !== 'autenticado') return NENHUMA
  const { id, perfil } = estado.usuario
  const estorna = podeEscrever(perfil, 'estornoDeSeparacao')
  return {
    separar: podeEscrever(perfil, 'separacao'),
    editarMateriais: podeEscrever(perfil, 'materiaisDoNo'),
    podeEstornarSeparacao: (autorId) =>
      estorna && (autorId === id || ESTORNAM_REGISTRO_ALHEIO.includes(perfil)),
  }
}
