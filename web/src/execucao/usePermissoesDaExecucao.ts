import { useAuth } from '../auth/AuthContext'
import { podeEscrever } from '../auth/permissoes'

export interface PermissoesDaExecucao {
  /** Iniciar, terminar e montar — o Operador, no chão de fábrica. */
  apontar: boolean
  /** Levar o que aguarda coleta, e redirecionar o que aguarda montagem — o Movimentador. */
  entregar: boolean
  /** Editar o Roteiro de um nó — o PCP. */
  editarRoteiro: boolean
  /** Estornar um registro: o autor dele, ou PCP/Administrador (spec §4.5). */
  podeEstornar: (autorId: number) => boolean
}

const NENHUMA: PermissoesDaExecucao = {
  apontar: false, entregar: false, editarRoteiro: false, podeEstornar: () => false,
}

/**
 * Espelho, NA TELA, de quem pode estornar registro de outra pessoa. O backend decide
 * (`EhPcpOuAdministrador`, 403 `Proibido`); aqui só se esconde o botão de quem receberia o 403.
 */
const ESTORNAM_REGISTRO_ALHEIO: readonly string[] = ['PCP', 'Administrador']

/**
 * O que a sessão pode fazer na execução, num lugar só — cada ação espelha UMA chave de
 * `permissoes.ts`, que espelha UM controller (desvio D1 do plano 2 da Fase 3). Gating vai na ação,
 * não no link (`CLAUDE.md`, Interface): quem não pode, lê.
 *
 * Isto é conveniência de interface: o 403 do backend continua sendo a fronteira real, e toda tela
 * da execução o traduz. No estorno o backend decide ainda mais fino — o autor, ou PCP/Administrador
 * —, e a tela repete a regra só para não oferecer o botão a quem receberia o 403 `Proibido`.
 */
export function usePermissoesDaExecucao(): PermissoesDaExecucao {
  const { estado } = useAuth()
  if (estado.status !== 'autenticado') return NENHUMA
  const { id, perfil } = estado.usuario
  const estorna = podeEscrever(perfil, 'estorno')
  return {
    apontar: podeEscrever(perfil, 'apontamento'),
    entregar: podeEscrever(perfil, 'entrega'),
    editarRoteiro: podeEscrever(perfil, 'roteiro'),
    podeEstornar: (autorId) => estorna && (autorId === id || ESTORNAM_REGISTRO_ALHEIO.includes(perfil)),
  }
}
