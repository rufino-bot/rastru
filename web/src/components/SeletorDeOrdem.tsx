import { Campo, CLASSES_DE_CONTROLE } from './Campo'

export interface OpcaoDeOrdem<T extends string> {
  valor: T
  /** O texto que a pessoa lê, com a direção dentro dele: "Código (A→Z)" (decisão 10 da spec da 1F). */
  rotulo: string
}

interface Props<T extends string> {
  opcoes: readonly OpcaoDeOrdem<T>[]
  valor: T
  aoMudar: (valor: T) => void
}

/**
 * "Ordenar por": um `<select>` com a lista fechada de ordens de uma tela. É só a escolha — quem
 * ordena é o servidor (Componentes e Pedidos, pela `ordem` da API) ou `ordenarCadastro` (Setores e
 * Materiais, no cliente). `aoMudar` recebe o VALOR da opção, nunca o rótulo.
 */
export function SeletorDeOrdem<T extends string>({ opcoes, valor, aoMudar }: Props<T>) {
  return (
    <Campo rotulo="Ordenar por">
      {(id) => (
        <select
          id={id}
          value={valor}
          onChange={(e) => aoMudar(e.target.value as T)}
          className={CLASSES_DE_CONTROLE}
        >
          {opcoes.map((o) => <option key={o.valor} value={o.valor}>{o.rotulo}</option>)}
        </select>
      )}
    </Campo>
  )
}
