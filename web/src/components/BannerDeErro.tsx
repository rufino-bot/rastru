interface Props {
  /** Aceita `null` para o chamador passar o estado direto, sem `{erro && …}` em sete telas. */
  mensagem: string | null
}

export function BannerDeErro({ mensagem }: Props) {
  if (!mensagem) return null

  return (
    <p
      role="alert"
      className="rounded-lg border border-negativo bg-negativo-fundo px-4 py-3 text-negativo-texto"
    >
      {mensagem}
    </p>
  )
}
