import type { ReactNode } from 'react'

interface Props {
  /**
   * Texto, ou texto com link (o Pedido no título do Agrupamento, C2 da spec da Fase 3B). O `<h1>` é um
   * só; quem passa o link não escreve outro cabeçalho.
   */
  titulo: ReactNode
  /** Ação principal da tela, alinhada ao título (ex.: "Novo pedido"). */
  acao?: ReactNode
  /**
   * `padrao` (768px) para toda tela de lista e cadastro; `ampla` (1280px) só para a tela que põe duas
   * colunas lado a lado em tela de PC, como a conferência da importação (a árvore e o painel do
   * Componente). A largura vem daqui, e não de um container da tela.
   */
  largura?: 'padrao' | 'ampla'
  children: ReactNode
}

const MAX_W: Record<NonNullable<Props['largura']>, string> = {
  padrao: 'max-w-3xl',
  ampla: 'max-w-7xl',
}

/**
 * Substitui as SEIS cópias do container antigo: altura mínima de tela, respiro de página, largura
 * contida e centralizada, coluna com espaçamento.
 *
 * A largura contida aqui é 768px, mais generosa que os 448px do container antigo: a spec §7
 * registra que busca + filtro + seletor de tamanho + paginação não cabem nos 448px. A altura
 * mínima de tela sai daqui e vai para o `AppShell` — as duas coisas juntas produzem rolagem
 * permanente de alguns pixels.
 *
 * O respiro é generoso de propósito (direção "sóbria e espaçada", spec §3). O custo foi medido e
 * aceito: na mesma altura de tela, ~3 itens onde a densa mostraria ~6.
 *
 * **O landmark `main` não é daqui — é do `AppShell`.** A partir da Task 7 toda tela interna
 * renderiza dentro do shell, e dois `main` aninhados são não conformes no HTML e tiram do leitor
 * de tela o atalho "ir para o conteúdo", que é a razão de o landmark existir. Custo aceito: uma
 * `Pagina` renderizada FORA do shell deixa de ser landmark — hoje nenhuma tela faz isso, e a
 * `LoginPage` (Task 12) não usa `Pagina`.
 */
export function Pagina({ titulo, acao, largura = 'padrao', children }: Props) {
  return (
    <div className={`mx-auto w-full ${MAX_W[largura]} px-4 py-8 sm:px-6 flex flex-col gap-6`}>
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight text-tinta">{titulo}</h1>
        {acao}
      </header>
      {children}
    </div>
  )
}
