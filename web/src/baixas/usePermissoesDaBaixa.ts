import { useAuth } from '../auth/AuthContext'
import { podeEscrever } from '../auth/permissoes'

export interface PermissoesDaBaixa {
  /** Dar baixa num nó, matar, descartar e "Descartar tudo": a Qualidade, o PCP e o Administrador. */
  darBaixa: boolean
  /** Estornar uma baixa: a autora dela, ou PCP/Administrador. */
  podeEstornar: (autorId: number) => boolean
}

const NENHUMA: PermissoesDaBaixa = { darBaixa: false, podeEstornar: () => false }

/**
 * Espelho, NA TELA, de quem pode estornar a baixa de outra pessoa. O backend decide (`EhPcpOuAdministrador`, 403
 * `Proibido`); aqui só se esconde o botão de quem receberia o 403. É a mesma lista de `usePermissoesDaExpedicao`.
 */
const ESTORNAM_REGISTRO_ALHEIO: readonly string[] = ['PCP', 'Administrador']

/**
 * O que a sessão pode fazer na baixa, num lugar só — espelha a chave `baixa` de `permissoes.ts`, que espelha
 * `BaixaController`. Gating vai na ação, não no link (`CLAUDE.md`, Interface). O estorno usa a mesma chave: quem dá
 * baixa estorna, e o backend ainda compara o autor.
 */
export function usePermissoesDaBaixa(): PermissoesDaBaixa {
  const { estado } = useAuth()
  if (estado.status !== 'autenticado') return NENHUMA
  const { id, perfil } = estado.usuario
  const da = podeEscrever(perfil, 'baixa')
  return {
    darBaixa: da,
    podeEstornar: (autorId) => da && (autorId === id || ESTORNAM_REGISTRO_ALHEIO.includes(perfil)),
  }
}
