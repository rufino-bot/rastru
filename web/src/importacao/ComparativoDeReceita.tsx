import type { LinhaDoComparativoDto } from '../api/importacao'

const TEXTO_DA_SITUACAO: Record<LinhaDoComparativoDto['situacao'], string> = {
  Igual: 'Igual',
  QuantidadeMuda: 'Quantidade diferente',
  Entra: 'Só no BOM',
  Sai: 'Só no catálogo',
}

function quantidade(q: number | null): string {
  return q === null ? '—' : q.toLocaleString('pt-BR', { maximumFractionDigits: 4 })
}

/**
 * Os filhos diretos de um Componente nos dois lados — a receita que o catálogo tem hoje e a que o BOM
 * traz —, uma linha por filho. É só leitura: a escolha entre as duas receitas fica no painel.
 */
export function ComparativoDeReceita({ linhas }: { linhas: LinhaDoComparativoDto[] }) {
  if (linhas.length === 0) {
    return <p className="text-sm text-tinta-fraca">Nenhum filho de nenhum dos dois lados.</p>
  }
  return (
    <div className="overflow-x-auto">
      <table aria-label="Comparativo da receita" className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-borda text-tinta-fraca">
            <th scope="col" className="py-1.5 pr-3 font-medium">Filho</th>
            <th scope="col" className="py-1.5 pr-3 text-right font-medium">Catálogo hoje</th>
            <th scope="col" className="py-1.5 pr-3 text-right font-medium">BOM</th>
            <th scope="col" className="py-1.5 font-medium">Situação</th>
          </tr>
        </thead>
        <tbody>
          {linhas.map((l) => (
            <tr key={l.codigo} className="border-b border-borda last:border-b-0">
              <td className="py-1.5 pr-3 text-tinta">
                <span className="font-mono">{l.codigo}</span>
                {' '}
                {l.descricao}
              </td>
              <td className="py-1.5 pr-3 text-right text-tinta">{quantidade(l.noCatalogo)}</td>
              <td className="py-1.5 pr-3 text-right text-tinta">{quantidade(l.noBom)}</td>
              <td className="py-1.5 text-tinta">{TEXTO_DA_SITUACAO[l.situacao]}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
