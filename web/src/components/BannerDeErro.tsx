interface Props {
  /** Aceita `null` para o chamador passar o estado direto, sem `{erro && …}` em sete telas. */
  mensagem: string | null
  /**
   * Itens que acompanham a mensagem, um `<li>` cada (a mensagem vira o título da lista). Serve ao
   * erro que traz várias causas de uma vez — o arquivo do BOM com uma linha ruim por problema.
   * Ausente ou vazio, o banner é só a mensagem.
   */
  linhas?: string[]
}

const CLASSES = 'rounded-lg border border-negativo bg-negativo-fundo px-4 py-3 text-negativo-texto'

export function BannerDeErro({ mensagem, linhas }: Props) {
  if (!mensagem) return null

  if (linhas && linhas.length > 0) {
    return (
      <div role="alert" className={`${CLASSES} flex flex-col gap-2`}>
        <p>{mensagem}</p>
        <ul className="list-disc pl-5">
          {linhas.map((l, i) => <li key={i}>{l}</li>)}
        </ul>
      </div>
    )
  }

  return (
    <p role="alert" className={CLASSES}>
      {mensagem}
    </p>
  )
}
