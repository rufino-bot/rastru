import type { ReactNode } from 'react'

/**
 * Estado vazio explícito. Hoje "nenhum resultado para a busca", "catálogo vazio" e "erro de rede"
 * renderizam a mesma lista vazia e muda — o usuário não tem como distinguir "não achei" de
 * "não perguntei" (spec §9).
 *
 * O texto vem da tela, porque só ela sabe qual dos três é.
 */
export function EstadoVazio({
  titulo,
  descricao,
  acao,
}: {
  titulo: string
  descricao?: string
  acao?: ReactNode
}) {
  return (
    <div
      role="status"
      className="flex flex-col items-center gap-2 rounded-lg border border-dashed border-borda bg-superficie px-6 py-12 text-center"
    >
      <p className="font-medium text-tinta">{titulo}</p>
      {descricao && <p className="text-sm text-tinta-fraca">{descricao}</p>}
      {acao && <div className="mt-2">{acao}</div>}
    </div>
  )
}
