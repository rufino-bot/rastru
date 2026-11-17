import { usePodeEscrever } from '../auth/usePermissao'

export interface PermissoesDaEmbalagem {
  /** Marcar e desmarcar o "Embalado": a Qualidade, o PCP e o Administrador. */
  marcar: boolean
}

/**
 * O que a sessão pode fazer com o "Embalado", num lugar só: espelha UMA chave de `permissoes.ts` (`embalagem`), que
 * espelha o `[Authorize(Roles)]` da rota. Gating vai na ação, não no link (`CLAUDE.md`, Interface): quem não pode,
 * lê. O 403 do backend continua sendo a fronteira real.
 */
export function usePermissoesDaEmbalagem(): PermissoesDaEmbalagem {
  return { marcar: usePodeEscrever('embalagem') }
}
