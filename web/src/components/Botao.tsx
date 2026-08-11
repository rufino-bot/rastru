import type { ButtonHTMLAttributes, ReactNode } from 'react'

export type VarianteDeBotao = 'primario' | 'secundario' | 'perigo'

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variante?: VarianteDeBotao
  /** Mutação em voo: desabilita e, se houver, troca o rótulo. */
  carregando?: boolean
  rotuloCarregando?: string
  children: ReactNode
}

// `focus-visible` (e não `focus`): o anel aparece na navegação por teclado e não no clique de
// mouse. Critério de aceite da spec §11.
const BASE =
  'inline-flex items-center justify-center rounded-lg transition-colors ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao ' +
  'disabled:opacity-50 disabled:cursor-not-allowed'

// O peso visual é o que compensa a ação ser monocromática com o chrome (spec §3): o primário é
// maior e mais denso; o secundário é contorno neutro. Dois botões com o mesmo peso na mesma tela
// devolvem ao usuário a decisão que o desenho deveria ter tomado por ele.
const POR_VARIANTE: Record<VarianteDeBotao, string> = {
  primario: 'bg-acao text-superficie px-5 py-2.5 font-semibold hover:bg-acao-forte',
  secundario: 'border border-borda-campo text-tinta px-4 py-2 hover:bg-acao-fundo',
  perigo: 'bg-negativo text-superficie px-5 py-2.5 font-semibold hover:brightness-90',
}

export function Botao({
  variante = 'primario',
  carregando = false,
  rotuloCarregando,
  className = '',
  disabled,
  children,
  ...resto
}: Props) {
  return (
    <button
      // `<button>` sem `type` dentro de `<form>` é `submit` por especificação — um "Cancelar"
      // herdaria isso e submeteria o formulário. O default seguro é `button`; quem submete pede.
      type="button"
      {...resto}
      disabled={disabled || carregando}
      // `className` do chamador vai por ÚLTIMO no atributo `class`, mas isso NÃO dá a ele a
      // última palavra: entre regras de mesma especificidade CSS, quem ganha é a ordem de
      // EMISSÃO no stylesheet, não a ordem no atributo. MEDIDO contra o CSS construído
      // (`npm run build`, `grep -o` em `dist/assets/*.css`): `.bg-acao{` vem antes de
      // `.bg-superficie{`, e `.px-2{` antes de `.px-4{` antes de `.px-5{` — nesta ordem de
      // emissão real do Tailwind v4 aqui. Logo `<Botao className="px-4">` sobre o primário
      // (`px-5`) PERDE, em silêncio. `className` serve para o que NÃO compete com base/variante
      // (posicionamento, margem); sobrepor peso visual pede prop nova, não classe concorrente.
      className={`${BASE} ${POR_VARIANTE[variante]} ${className}`}
    >
      {carregando && rotuloCarregando ? rotuloCarregando : children}
    </button>
  )
}
