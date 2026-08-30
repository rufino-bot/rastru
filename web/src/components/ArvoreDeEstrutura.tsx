import { useState } from 'react'
import type { NoDaEstrutura } from '../api/estrutura'
import { Botao } from './Botao'
import { Pilula } from './Pilula'

interface Props {
  nos: NoDaEstrutura[]
  podeEscrever: boolean
  onAcrescentarFilho?: (paiId: number) => void
  onEditar?: (no: NoDaEstrutura) => void
  onExcluir?: (no: NoDaEstrutura) => void
}

const RECUO_BASE_PX = 12
const RECUO_POR_NIVEL_PX = 20

/**
 * A árvore de `EstruturaItem` de um Agrupamento: lista indentada, uma coluna — layout escolhido
 * pelo dono do projeto em 2026-08-29 contra árvore-com-painel-de-detalhe e cartões aninhados (a
 * única das três que não precisa de layout alternativo no celular; o painel exigiria gaveta ou
 * segunda tela, e os cartões apertam já no terceiro nível).
 *
 * Um nó por linha; o recuo à esquerda cresce com o nível e carrega a hierarquia. Os FILHOS ficam
 * sempre visíveis (é o que faz "três níveis, três linhas" valer sem exigir clique nenhum);
 * materiais e roteiro, ao contrário, abrem embaixo da própria linha só quando o nó é expandido —
 * é conteúdo auxiliar, não estrutura.
 *
 * Só constrói a lista a partir do que recebe; não busca dado, não sabe de rota, não sabe de
 * Agrupamento. Isso é da tela (Task 8).
 */
export function ArvoreDeEstrutura({ nos, podeEscrever, onAcrescentarFilho, onEditar, onExcluir }: Props) {
  return (
    <ul aria-label="Estrutura da peça" className="flex flex-col gap-1">
      {nos.map((no) => (
        <LinhaDoNo
          key={no.id}
          no={no}
          nivel={0}
          podeEscrever={podeEscrever}
          onAcrescentarFilho={onAcrescentarFilho}
          onEditar={onEditar}
          onExcluir={onExcluir}
        />
      ))}
    </ul>
  )
}

function LinhaDoNo({
  no,
  nivel,
  podeEscrever,
  onAcrescentarFilho,
  onEditar,
  onExcluir,
}: {
  no: NoDaEstrutura
  nivel: number
  podeEscrever: boolean
  onAcrescentarFilho?: (paiId: number) => void
  onEditar?: (no: NoDaEstrutura) => void
  onExcluir?: (no: NoDaEstrutura) => void
}) {
  const [expandido, setExpandido] = useState(false)
  const temDetalhe = no.materiais.length > 0 || no.roteiro.length > 0
  // Ad-hoc (sem Componente do catálogo) distingue-se por RÓTULO textual — a pílula "Ad-hoc" — e
  // pela ausência do código do componente, nunca por verde/vermelho: essas duas cores são
  // reservadas a estado (aprovado/reprovado), e a Fase 1E achou exatamente esse defeito com a
  // suíte inteira verde.
  const ehAdHoc = no.componenteId === null
  // Ordenado por `Ordem`, não deduplicado por `setorId`: setor repetido é retorno ao mesmo setor
  // (regra 21), não duplicata — o backend preserva a Ordem por isso, e apagar a repetição aqui
  // destruiria a informação que ela carrega.
  const roteiroOrdenado = [...no.roteiro].sort((a, b) => a.ordem - b.ordem)

  return (
    <li>
      <div
        data-testid={`linha-no-${no.id}`}
        className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-borda bg-superficie py-2 pr-3"
        style={{ paddingLeft: `${RECUO_BASE_PX + nivel * RECUO_POR_NIVEL_PX}px` }}
      >
        <div className="flex flex-wrap items-center gap-2">
          {temDetalhe && (
            <button
              type="button"
              onClick={() => setExpandido((v) => !v)}
              aria-expanded={expandido}
              aria-label={`${expandido ? 'Recolher' : 'Expandir'} ${no.descricao}`}
              className="text-tinta-fraca"
            >
              {expandido ? '▾' : '▸'}
            </button>
          )}
          {no.codigoDoComponente && (
            <span className="font-mono text-sm text-tinta-fraca">{no.codigoDoComponente}</span>
          )}
          <span className="text-tinta">{no.descricao}</span>
          {ehAdHoc && <Pilula tom="neutro">Ad-hoc</Pilula>}
          <span className="text-sm text-tinta-fraca">{`Qtd: ${no.quantidade}`}</span>
        </div>

        {podeEscrever && (
          <div className="flex flex-wrap items-center gap-2">
            {onAcrescentarFilho && (
              <Botao variante="secundario" onClick={() => onAcrescentarFilho(no.id)}>
                Acrescentar filho
              </Botao>
            )}
            {onEditar && (
              <Botao variante="secundario" onClick={() => onEditar(no)}>
                Editar
              </Botao>
            )}
            {onExcluir && (
              <Botao variante="perigo" onClick={() => onExcluir(no)}>
                Excluir
              </Botao>
            )}
          </div>
        )}
      </div>

      {expandido && (
        <div
          className="flex flex-col gap-2 border-l border-borda py-2 pl-4 text-sm"
          style={{ marginLeft: `${RECUO_BASE_PX + nivel * RECUO_POR_NIVEL_PX}px` }}
        >
          {no.materiais.length > 0 && (
            <div>
              <p className="font-semibold text-tinta-fraca">Materiais</p>
              <ul className="flex flex-col gap-1">
                {no.materiais.map((m) => (
                  <li key={m.materialId} className="text-tinta">
                    {`${m.nome} — ${m.quantidade}`}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {no.roteiro.length > 0 && (
            <div>
              <p className="font-semibold text-tinta-fraca">Roteiro</p>
              <ol className="flex flex-col gap-1">
                {roteiroOrdenado.map((p) => (
                  <li key={`${p.ordem}-${p.setorId}`} data-testid="passo-do-roteiro" className="text-tinta">
                    {`${p.ordem}. ${p.nome}`}
                  </li>
                ))}
              </ol>
            </div>
          )}
        </div>
      )}

      {no.filhos.length > 0 && (
        <ul>
          {no.filhos.map((filho) => (
            <LinhaDoNo
              key={filho.id}
              no={filho}
              nivel={nivel + 1}
              podeEscrever={podeEscrever}
              onAcrescentarFilho={onAcrescentarFilho}
              onEditar={onEditar}
              onExcluir={onExcluir}
            />
          ))}
        </ul>
      )}
    </li>
  )
}
