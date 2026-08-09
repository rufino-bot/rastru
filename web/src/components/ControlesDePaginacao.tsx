import { Botao } from './Botao'

interface Props {
  pagina: number
  totalDePaginas: number
  /** Total sob o filtro, vindo do servidor — não `itens.length`. */
  total: number
  aoMudarPagina: (pagina: number) => void
}

/**
 * Anterior / posição / Próxima.
 *
 * `flex-wrap` não é detalhe: no celular Android da fábrica os três controles não cabem lado a
 * lado, e sem a quebra a página rola na horizontal — o que a spec §11 barra explicitamente.
 */
export function ControlesDePaginacao({ pagina, totalDePaginas, total, aoMudarPagina }: Props) {
  // Uma página só: os controles não informam nada e viram ruído.
  if (totalDePaginas <= 1) return null

  return (
    <nav aria-label="Paginação" className="flex flex-wrap items-center justify-between gap-3">
      <Botao variante="secundario" disabled={pagina <= 1} onClick={() => aoMudarPagina(pagina - 1)}>
        Anterior
      </Botao>
      <span className="text-sm text-tinta-fraca">
        Página {pagina} de {totalDePaginas} — {total} no total
      </span>
      <Botao
        variante="secundario"
        disabled={pagina >= totalDePaginas}
        onClick={() => aoMudarPagina(pagina + 1)}
      >
        Próxima
      </Botao>
    </nav>
  )
}
