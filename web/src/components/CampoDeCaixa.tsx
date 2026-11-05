import { useId, type ReactNode } from 'react'

interface Props {
  marcado: boolean
  aoMudar: (marcado: boolean) => void
  desabilitado?: boolean
  /** Uma frase de apoio abaixo do texto; fica ligada à caixa por `aria-describedby`. */
  dica?: string
  /** O texto da caixa: é também o nome acessível dela. */
  children: ReactNode
}

/**
 * Uma caixa de marcar com o texto ao lado, dentro de um formulário: "Dar baixa também delas". É o `Campo` das
 * escolhas de sim ou não. Diferente da `CaixaDeLote`, que põe uma linha de lista no lote e tem só o nome
 * acessível, aqui o texto é visível e faz parte da pergunta.
 */
export function CampoDeCaixa({ marcado, aoMudar, desabilitado = false, dica, children }: Props) {
  const idDaDica = useId()
  return (
    <div className="flex flex-col gap-1">
      <label className="flex items-start gap-2 text-sm text-tinta">
        <input
          type="checkbox"
          checked={marcado}
          onChange={(e) => aoMudar(e.target.checked)}
          disabled={desabilitado}
          aria-describedby={dica ? idDaDica : undefined}
          className="mt-0.5 size-4 accent-acao"
        />
        <span>{children}</span>
      </label>
      {dica && <p id={idDaDica} className="pl-6 text-sm text-tinta-fraca">{dica}</p>}
    </div>
  )
}
