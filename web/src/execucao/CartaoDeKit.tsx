import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { ItemComAcao } from '../components/ItemComAcao'
import { Pilula } from '../components/Pilula'
import type { FilhoDoKitDto, KitDto } from '../api/execucao'
import { caminhoDoNo, formatarQuantidade, rotuloDoNo } from './formatacao'
import { lerConjuntos, quantidadeDoConjunto } from './conjuntos'

/** A origem de um filho no cartão: onde ele aguarda coleta, ou "já está" no Setor do pai (D2 da spec da Fase 3B). */
function origemDoFilho(f: FilhoDoKitDto, destino: string): string {
  if (f.jaNoDestino) return `já está em ${destino}`
  if (f.origem) return `de ${f.origem.nome}, passo ${f.ordem}`
  return 'sem Roteiro'
}

/**
 * Um Kit montável nas Tarefas (regra 23; spec da Fase 3B, seção 5.1). O Movimentador escolhe QUANTOS conjuntos,
 * nunca quanto de cada filho: a tela não consegue compor um conjunto incompleto. `conjuntos` é o texto do campo,
 * `undefined` quando o Kit não está marcado. O máximo é o `conjuntos` que o servidor calculou.
 */
export function CartaoDeKitMontavel({ kit, podeEntregar, conjuntos, aoAlternar, aoMudar }: {
  kit: KitDto
  podeEntregar: boolean
  conjuntos: string | undefined
  aoAlternar: (marcado: boolean) => void
  aoMudar: (texto: string) => void
}) {
  const leitura = conjuntos === undefined ? null : lerConjuntos(conjuntos, kit.conjuntos)
  // Enquanto o texto não é um N válido, a lista mostra o máximo: o número a enviar é o do campo, e o Entregar trava.
  const n = leitura?.valor ?? kit.conjuntos
  return (
    <ItemComAcao
      acao={podeEntregar ? (
        <label className="flex items-center gap-2 text-sm text-tinta">
          <input
            type="checkbox"
            checked={conjuntos !== undefined}
            onChange={(e) => aoAlternar(e.target.checked)}
            aria-label={`Levar o Kit ${rotuloDoNo(kit.pai)}`}
            className="size-5 accent-acao"
          />
          Levar
        </label>
      ) : undefined}
      painel={conjuntos !== undefined ? (
        <div className="flex flex-col gap-3 border-t border-borda pt-3">
          <Campo rotulo="Conjuntos" dica={leitura?.erro ?? `Dá para levar: ${formatarQuantidade(kit.conjuntos)}`}>
            {(id, idDaDica) => (
              <input
                id={id}
                type="text"
                inputMode="numeric"
                autoComplete="off"
                value={conjuntos}
                onChange={(e) => aoMudar(e.target.value)}
                aria-describedby={idDaDica}
                aria-invalid={leitura?.erro != null}
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
        </div>
      ) : undefined}
    >
      <span className="flex flex-wrap items-center gap-2 font-medium text-tinta">
        {rotuloDoNo(kit.pai)}
        {kit.pai.pausa !== null && <Pilula tom="atencao">Pausado</Pilula>}
      </span>
      <span className="text-xs text-tinta-fraca">{caminhoDoNo(kit.pai)}</span>
      <span className="text-sm text-tinta">{`Destino: ${kit.destino.nome} (início de ${kit.pai.descricao})`}</span>
      <span className="text-sm text-tinta">{`Dá para levar ${formatarQuantidade(kit.conjuntos)} conjunto(s)`}</span>
      <ul aria-label={`Filhos do Kit ${kit.pai.descricao}`} className="flex flex-col gap-1 text-sm text-tinta-fraca">
        {kit.filhos.map((f) => (
          <li key={f.no.id}>
            {`${rotuloDoNo(f.no)}: ${formatarQuantidade(quantidadeDoConjunto(n, f.quantidadePorPai))} (${origemDoFilho(f, kit.destino.nome)})`}
          </li>
        ))}
      </ul>
    </ItemComAcao>
  )
}

/** Um Kit que ainda não fecha um conjunto: informativo, sem caixa de marcar (D7 da spec da Fase 3B). */
export function CartaoDeKitIncompleto({ kit }: { kit: KitDto }) {
  return (
    <ItemComAcao>
      <span className="font-medium text-tinta">{rotuloDoNo(kit.pai)}</span>
      <span className="text-xs text-tinta-fraca">{caminhoDoNo(kit.pai)}</span>
      <ul aria-label={`Filhos do Kit ${kit.pai.descricao}`} className="flex flex-col gap-1 text-sm text-tinta-fraca">
        {kit.filhos.map((f) => {
          const falta = Math.max(0, quantidadeDoConjunto(1, f.quantidadePorPai - f.pronto))
          return (
            <li key={f.no.id}>
              {f.pronto > 0
                ? `${rotuloDoNo(f.no)}: pronto ${formatarQuantidade(f.pronto)}${falta > 0 ? `, falta ${formatarQuantidade(falta)} para 1 conjunto` : ''}`
                : `${rotuloDoNo(f.no)}: nenhum pronto, falta ${formatarQuantidade(f.quantidadePorPai)} para 1 conjunto`}
            </li>
          )
        })}
      </ul>
    </ItemComAcao>
  )
}
