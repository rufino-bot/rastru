import { useAuth } from '../auth/AuthContext'
import { podeEscrever } from '../auth/permissoes'

export interface PermissoesDaReposicao {
  /** Abrir o Pedido de Retrabalho: a Qualidade, o PCP e o Administrador. */
  abrir: boolean
  /** "Usar a sobra" e a prévia dele: o PCP e o Administrador. */
  reaproveitar: boolean
  /** Registrar a reclamação do cliente: a Qualidade, o PCP e o Administrador. */
  reclamar: boolean
  /** "Não repor" e "Desfazer": o PCP e o Administrador. */
  naoRepor: boolean
  /** Estornar uma reclamação: a autora dela, ou PCP/Administrador. */
  podeEstornarReclamacao: (autorId: number) => boolean
}

const NENHUMA: PermissoesDaReposicao = {
  abrir: false, reaproveitar: false, reclamar: false, naoRepor: false, podeEstornarReclamacao: () => false,
}

/** Espelho, NA TELA, de quem pode estornar o registro de outra pessoa; o backend decide (403 `Proibido`). */
const ESTORNAM_REGISTRO_ALHEIO: readonly string[] = ['PCP', 'Administrador']

/**
 * O que a sessão pode fazer na reposição, num lugar só — espelha as chaves `retrabalho`, `reaproveitamento`,
 * `reclamacao` e `dispensa` de `permissoes.ts`, que espelham os quatro controllers. Gating vai na ação, não no link
 * (`CLAUDE.md`, Interface). Tela e painel da reposição não chamam `usePodeEscrever` direto.
 */
export function usePermissoesDaReposicao(): PermissoesDaReposicao {
  const { estado } = useAuth()
  if (estado.status !== 'autenticado') return NENHUMA
  const { id, perfil } = estado.usuario
  const reclamar = podeEscrever(perfil, 'reclamacao')
  return {
    abrir: podeEscrever(perfil, 'retrabalho'),
    reaproveitar: podeEscrever(perfil, 'reaproveitamento'),
    reclamar,
    naoRepor: podeEscrever(perfil, 'dispensa'),
    podeEstornarReclamacao: (autorId) => reclamar && (autorId === id || ESTORNAM_REGISTRO_ALHEIO.includes(perfil)),
  }
}
