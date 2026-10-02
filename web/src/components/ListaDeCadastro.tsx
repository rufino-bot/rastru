import type { ReactNode } from 'react'

/**
 * O `<ul>` das quatro listas de cadastro. NÃO recebe os itens por prop: as quatro mostram campos
 * diferentes, e abstrair isso viraria seis render props sem ganho. O que a primitiva guarda é o
 * que não varia — semântica de lista e espaçamento.
 *
 * O estado vazio fica com a TELA (`EstadoVazio`): a spec §9 exige distinguir "nenhum resultado
 * para a busca" de "catálogo vazio" e de "erro de rede", e daqui não dá para saber qual é.
 */
export function ListaDeCadastro({ children, rotulo }: { children: ReactNode; rotulo?: string }) {
  return (
    <ul aria-label={rotulo} className="flex flex-col gap-2">
      {children}
    </ul>
  )
}

export function ItemDeCadastro({
  ativo = true,
  acao,
  children,
}: {
  /** Ausente = ativo. `Agrupamento` não tem coluna `Ativo` e usa o item sem a prop. */
  ativo?: boolean
  acao?: ReactNode
  children: ReactNode
}) {
  return (
    <li className="relative flex items-center justify-between gap-3 rounded-lg border border-borda bg-superficie px-4 py-3">
      <span className={ativo ? 'text-tinta' : 'text-tinta-fraca'}>
        <span className={ativo ? undefined : 'line-through'}>{children}</span>
        {/* O traço é visual e não chega ao leitor de tela; sem este texto, ativo e inativo soam
            idênticos para quem não vê a lista.

            IRMÃO do nome riscado, e não filho: `text-decoration` de um ancestral é pintada ATRAVÉS
            dos descendentes em fluxo e um descendente NÃO consegue desligá-la (CSS Text Decoration
            L3). A primeira versão deste plano punha o rótulo dentro do `line-through` com
            `no-underline` para tentar salvá-lo — classe que não faz nada e que encena uma decisão.
            Como irmão, o rótulo nunca é riscado por construção, sem depender de truque de cascata. */}
        {!ativo && <span className="ml-2">(inativo)</span>}
      </span>
      {/* A `acao` vai num wrapper posicionado (decisão 12 da spec da 1F). Motivo: quando o
          `children` traz um `<Link>` que estende a área clicável ao cartão — o padrão da
          `LinhaDePedido`, um pseudo-elemento absoluto que cobre o `<li>` inteiro —, esse overlay
          engole a `acao` que dividir o item com ele. MEDIDO em Chrome na Fase 1D: clicar no centro
          do botão devolvia o link, não a ação. Por isso duas telas tinham restringido o link ao
          texto. Com o wrapper, o cartão inteiro é do link e o botão continua sendo do botão, sem
          cada tela repetir o conserto.

          O wrapper é posicionado e SEM índice de empilhamento próprio. Entre elementos
          posicionados sem índice, quem vem depois na ordem da árvore pinta por cima: o wrapper
          vem depois do link, então fica acima do overlay. E fica abaixo de qualquer coisa COM
          índice, como a lista aberta do `SeletorComBusca`. Com índice igual ao dela, o empate se
          resolvia pela ordem da árvore, e na `ComponenteDetalhePage` o seletor de "Componente
          filho" abre para baixo, sobre a lista de Materiais: os botões "Remover" pintavam por cima
          das opções e tomavam o clique. Medido no Chromium com `document.elementFromPoint` no
          centro do botão, nas três formas: com índice, o botão ganha da lista; sem índice, a lista
          ganha do botão e o botão ganha do overlay; sem posição, o overlay ganha do botão.

          **jsdom não calcula layout**: a suíte só afirma a estrutura e as classes do wrapper
          (testes `a acao fica num wrapper posicionado acima do conteudo` e `sem acao nao cria
          wrapper`). Que o clique no centro do botão chega ao botão é conferido no navegador.

          Sem `acao` não há wrapper: uma caixa vazia posicionada sobre o cartão seria alvo de
          clique que não é de ninguém.

          Ao editar este bloco, descreva as classes de empilhamento, não as escreva: o scanner do
          Tailwind lê o fonte inteiro, comentário incluído, e uma classe citada aqui planta a regra
          dela no CSS de produção mesmo sem elemento nenhum que a use (16,80 → 16,82 kB, medido
          quando aconteceu). */}
      {acao ? <div className="relative">{acao}</div> : null}
    </li>
  )
}
