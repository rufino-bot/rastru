import { useId, type ReactNode } from 'react'

interface Props {
  rotulo: string
  /**
   * Recebe o `id` gerado — e, quando há dica, o id dela — e devolve o controle:
   * `{(id, idDaDica) => <input id={id} aria-describedby={idDaDica} … />}`.
   * O segundo parâmetro pode ser ignorado em campo sem dica.
   */
  children: (id: string, idDaDica?: string) => ReactNode
  dica?: string
}

/**
 * Rótulo + controle, ligados por `id` explícito.
 *
 * O padrão é *render prop* em vez de `<Campo type="text" …>` porque as telas usam `input` e
 * `select`, e o `select` de `ComponentesPage` tem lista fechada com `<option>` próprios. Uma
 * primitiva que tentasse abstrair os dois viraria um repasse de props sem valor. O que a
 * primitiva realmente entrega é a **ligação acessível**, e é isso que ela guarda.
 *
 * Classes do controle ficam com o chamador — via `CLASSES_DE_CONTROLE`, exportada abaixo — para
 * que ele possa acrescentar `font-mono` num campo de código sem lutar contra a primitiva.
 */
export function Campo({ rotulo, children, dica }: Props) {
  const id = useId()
  const idDaDica = `${id}-dica`

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium text-tinta">
        {rotulo}
      </label>
      {/* O `aria-describedby` vai no CONTROLE, não num wrapper: leitor de tela associa a descrição
          ao campo focado, e num `<div>` externo ele nunca é anunciado. Por isso a render prop
          recebe os dois ids. Quando não há dica o segundo é `undefined`, e React omite o atributo
          em vez de apontar para um id inexistente — apontar para o vazio é pior que não apontar. */}
      {children(id, dica ? idDaDica : undefined)}
      {dica && <p id={idDaDica} className="text-sm text-tinta-fraca">{dica}</p>}
    </div>
  )
}

/** Aparência única de todo input/select do sistema. Use com `className={CLASSES_DE_CONTROLE}`. */
export const CLASSES_DE_CONTROLE =
  'w-full rounded-lg border border-borda-campo bg-superficie px-3 py-2.5 text-tinta ' +
  'placeholder:text-tinta-fraca ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao'
