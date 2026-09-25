import { useAuth } from '../auth/AuthContext'

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
 * O que a sessão pode fazer na execução, num lugar só — as telas perguntam aqui, e não a
 * `usePodeEscrever` direto, por um motivo de ORDEM DE MERGE: as chaves `apontamento`, `entrega`,
 * `roteiro` e `estorno` de `permissoes.ts` nascem na Task 10 do plano 2 (backend), e a guarda
 * `permissoesEspelhamOBackend.test.ts` só as aceita junto dos controllers delas. Até a Task 10
 * DESTE plano, que roda depois do merge do plano 2, as três ações ficam liberadas para todo
 * autenticado (desvio D2 deste plano) — e o 403 do backend continua sendo a fronteira real, que
 * toda tela da execução já traduz.
 */
export function usePermissoesDaExecucao(): PermissoesDaExecucao {
  const { estado } = useAuth()
  if (estado.status !== 'autenticado') return NENHUMA
  const { id, perfil } = estado.usuario
  return {
    apontar: true,
    entregar: true,
    editarRoteiro: true,
    podeEstornar: (autorId) => autorId === id || ESTORNAM_REGISTRO_ALHEIO.includes(perfil),
  }
}
