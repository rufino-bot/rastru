interface Props {
  /** Nome acessível da caixa ("Marcar Suporte", "Levar Suporte"): é o que o leitor de tela anuncia e o teste acha. A caixa não escreve texto visível ao lado dele. */
  nome: string
  marcada: boolean
  aoMudar: (marcada: boolean) => void
  desabilitada?: boolean
}

/**
 * A caixa que põe uma linha de lista no lote (a Fila do Setor, as Tarefas, a Separação e a Expedição).
 *
 * O `<label>` é a área de toque: pelo menos 44 x 44 px, invisível, e o toque em qualquer ponto dela marca e
 * desmarca. A caixa em si tem 20 px; no celular, acertar só ela é difícil, e sem o texto "Marcar" ao lado (que
 * servia de área de toque) a área precisa ser declarada. Estas telas ficam abertas o dia inteiro no Android da
 * fábrica. A caixa nunca escreve texto visível ao lado: nem o "Marcar" nem o "Levar" (conferência da Fase 5A,
 * 2026-11-03), porque o texto repetido em cada linha só atrapalha; o nome acessível fica no `aria-label`.
 */
export function CaixaDeLote({ nome, marcada, aoMudar, desabilitada }: Props) {
  return (
    <label className="flex min-h-11 min-w-11 cursor-pointer items-center justify-center gap-2 text-sm text-tinta">
      <input
        type="checkbox"
        checked={marcada}
        disabled={desabilitada}
        onChange={(e) => aoMudar(e.target.checked)}
        aria-label={nome}
        className="size-5 accent-acao"
      />
    </label>
  )
}
