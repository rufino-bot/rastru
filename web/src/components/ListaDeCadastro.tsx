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
      {acao}
    </li>
  )
}
