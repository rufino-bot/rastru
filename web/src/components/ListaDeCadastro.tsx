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
      {/* ⚠️ ARMADILHA CONHECIDA (m6 da review da Task 8): `acao` e overlay de link no MESMO item
          colidem. Se o `children` trouxer um `<Link>` que estende a área clicável ao cartão com
          `after:absolute after:inset-0` — o padrão da `PedidosPage` —, esse overlay cobre o `<li>`
          inteiro e ENGOLE a `acao`: clicar no centro do botão devolve o link, não a ação.

          MEDIDO em Chrome. **jsdom não calcula layout, então nenhum teste desta suíte pega isso** —
          o dia em que alguém combinar os dois, a suíte fica verde e a tela quebra.

          Hoje nenhuma tela combina (a `PedidosPage` tem o overlay e não tem `acao`; `SetoresPage` e
          `MateriaisPage` têm `acao` e não têm link), por isso não há conserto aplicado aqui: seria
          mexer em código sem sintoma e sem prova. Saída, quando precisar: pôr a `acao` num wrapper
          posicionado com `z-index` positivo, tirando-a de baixo do overlay — e conferir NO
          NAVEGADOR, não na suíte.

          E note que a classe utilitária NÃO está escrita por extenso acima: o scanner do Tailwind
          lê o fonte inteiro, comentário incluído. A primeira versão deste comentário citava a
          classe de empilhamento pelo nome e plantou a regra dela no CSS de produção — regra que
          elemento nenhum usa (16,80 → 16,82 kB, medido). A segunda versão citava o nome de novo,
          dentro do próprio aviso, e replantou. Ao editar este bloco: descreva a classe, não a
          escreva. */}
      {acao}
    </li>
  )
}
