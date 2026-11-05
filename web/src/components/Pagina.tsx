import { useEffect, useRef, type ReactNode } from 'react'

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

const NOME_DO_SISTEMA = 'Rastru'

function tituloDaAba(texto: string): string {
  const limpo = texto.replace(/\s+/g, ' ').trim()
  return limpo ? `${limpo} · ${NOME_DO_SISTEMA}` : NOME_DO_SISTEMA
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
 *
 * **Escreve o título da aba** ("{título} · Rastru"; sem texto, só "Rastru"), e a tela não escreve
 * `document.title`. O `titulo` é `ReactNode` e seis telas passam um elemento (um `span` focável com o
 * nome do registro ou da tela), então o texto vem do `textContent` do `<h1>` já renderizado, lido a
 * cada render: acompanha o que chega depois da carga (o número do Pedido). Ao desmontar, devolve
 * "Rastru". `LoginPage` e `TelaCarregando` não usam `Pagina` e ficam com o `<title>` do `index.html`.
 */
export function Pagina({ titulo, acao, largura = 'padrao', children }: Props) {
  const h1 = useRef<HTMLHeadingElement>(null)

  // Sem lista de dependências de propósito: o texto do <h1> pode mudar sem o `titulo` mudar de
  // identidade (um elemento novo a cada render), e só se escreve quando o valor difere.
  useEffect(() => {
    const novo = tituloDaAba(h1.current?.textContent ?? '')
    if (document.title !== novo) document.title = novo
  })

  // Separado para a limpeza rodar só ao desmontar, e a aba não piscar "Rastru" a cada render.
  useEffect(
    () => () => {
      document.title = NOME_DO_SISTEMA
    },
    [],
  )

  return (
    <div className={`mx-auto w-full ${MAX_W[largura]} px-4 py-8 sm:px-6 flex flex-col gap-6`}>
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 ref={h1} className="text-2xl font-semibold tracking-tight text-tinta">{titulo}</h1>
        {acao}
      </header>
      {children}
    </div>
  )
}
