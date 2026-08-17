import type { ReactNode } from 'react'

interface Props {
  titulo: string
  /** Ação principal da tela, alinhada ao título (ex.: "Novo pedido"). */
  acao?: ReactNode
  children: ReactNode
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
export function Pagina({ titulo, acao, children }: Props) {
  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-8 sm:px-6 flex flex-col gap-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight text-tinta">{titulo}</h1>
        {acao}
      </header>
      {children}
    </div>
  )
}
