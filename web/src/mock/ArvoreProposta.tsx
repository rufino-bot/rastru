// MOCK DESCARTÁVEL — não commitar. Proposta para a ArvoreDeEstrutura no celular:
// (1) ações de escrita saem da linha e vão para um "⋯" que abre embaixo dela;
// (2) recuo de 12 px por nível, com teto, e linha-guia vertical ligando irmãos ao pai.
import { useState } from 'react'
import type { NoDaEstrutura } from '../api/estrutura'
import type { PosicoesDoNoDto } from '../api/execucao'
import { Botao } from '../components/Botao'
import { Pilula } from '../components/Pilula'
import { ResumoDePosicoes } from '../components/ResumoDePosicoes'

interface Props {
  nos: NoDaEstrutura[]
  posicoes?: ReadonlyMap<number, PosicoesDoNoDto>
  podeEscrever: boolean
  onAcrescentarFilho?: (paiId: number) => void
  onEditar?: (no: NoDaEstrutura) => void
  onExcluir?: (no: NoDaEstrutura) => void
  onDetalhe?: (no: NoDaEstrutura) => void
}

/** A partir deste nível o recuo para de crescer; o nível passa a ser dito por texto. */
const NIVEL_MAXIMO_RECUADO = 5

const FOCO = 'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao'

export function ArvoreProposta(props: Props) {
  return (
    <ul aria-label="Estrutura do agrupamento" className="flex flex-col gap-1">
      {props.nos.map((no) => (
        <LinhaDoNo key={no.id} {...props} no={no} nivel={0} />
      ))}
    </ul>
  )
}

function LinhaDoNo({
  no, nivel, posicoes, podeEscrever, onAcrescentarFilho, onEditar, onExcluir, onDetalhe,
}: Omit<Props, 'nos'> & { no: NoDaEstrutura; nivel: number }) {
  const [expandido, setExpandido] = useState(false)
  const [acoesAbertas, setAcoesAbertas] = useState(false)
  const temDetalhe = no.materiais.length > 0 || no.roteiro.length > 0
  const ehAdHoc = no.componenteId === null
  const roteiroOrdenado = [...no.roteiro].sort((a, b) => a.ordem - b.ordem)
  const temAcaoDeEscrita = podeEscrever && (onAcrescentarFilho || onEditar || onExcluir)
  const posicao = posicoes?.get(no.id)
  const alemDoTeto = nivel > NIVEL_MAXIMO_RECUADO
  // Os filhos só recuam (e ganham linha-guia) enquanto o PRÓPRIO filho ainda estiver dentro do teto.
  const filhosRecuam = nivel + 1 <= NIVEL_MAXIMO_RECUADO

  return (
    <li className="flex flex-col gap-1">
      <div
        data-testid={`linha-no-${no.id}`}
        className="flex flex-col gap-2 rounded-lg border border-borda bg-superficie px-2 py-2"
      >
        {/* Coluna fixa para o alternador: o ▸ nunca fica sozinho numa linha. */}
        <div className="grid grid-cols-[1.5rem_1fr_auto] items-center gap-x-1 gap-y-1">
          <div>
            {temDetalhe && (
              <button
                type="button"
                onClick={() => setExpandido((v) => !v)}
                aria-expanded={expandido}
                aria-label={`${expandido ? 'Recolher' : 'Expandir'} ${no.descricao}`}
                className={`inline-flex min-h-6 min-w-6 items-center justify-center rounded text-tinta-fraca ${FOCO}`}
              >
                {expandido ? '▾' : '▸'}
              </button>
            )}
          </div>

          {/* Linha 1, ao lado das ações: nível (além do teto) + código; no ad-hoc, a descrição. */}
          <div className="flex min-w-0 flex-wrap items-center gap-x-2">
            {alemDoTeto && <span className="text-xs text-tinta-fraca">{`nível ${nivel + 1}`}</span>}
            {no.codigoDoComponente ? (
              <span className="font-mono text-sm text-tinta-fraca">{no.codigoDoComponente}</span>
            ) : (
              <span className="text-tinta">{no.descricao}</span>
            )}
          </div>

          <div className="flex items-center gap-1">
            {onDetalhe && (
              <button
                type="button"
                onClick={() => onDetalhe(no)}
                className={`rounded px-2 py-1 text-sm text-acao underline-offset-2 hover:underline ${FOCO}`}
              >
                Detalhes
              </button>
            )}
            {temAcaoDeEscrita && (
              <button
                type="button"
                onClick={() => setAcoesAbertas((v) => !v)}
                aria-expanded={acoesAbertas}
                aria-label={`Ações de ${no.descricao}`}
                className={`inline-flex min-h-8 min-w-8 items-center justify-center rounded text-lg text-tinta-fraca ${FOCO}`}
              >
                ⋯
              </button>
            )}
          </div>

          {/* Linha 2: a largura toda, sem disputar espaço com os botões. */}
          <div className="col-span-2 col-start-2 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
            {no.codigoDoComponente && <span className="text-tinta">{no.descricao}</span>}
            {ehAdHoc && <Pilula tom="neutro">Ad-hoc</Pilula>}
            {no.requerRelatorioDimensional && <Pilula tom="neutro">Requer relatório dimensional</Pilula>}
            {no.semRoteiro && <Pilula tom="neutro">Sem Roteiro</Pilula>}
            <span className="text-sm text-tinta-fraca">{`Qtd: ${no.quantidade}`}</span>
            {no.quantidadePorPai !== null && (
              <span className="text-sm text-tinta-fraca">{`Por pai: ${no.quantidadePorPai}`}</span>
            )}
          </div>
        </div>

        {acoesAbertas && temAcaoDeEscrita && (
          <div data-testid={`acoes-do-no-${no.id}`} className="flex flex-wrap gap-2 pl-7">
            {onAcrescentarFilho && (
              <Botao variante="secundario" onClick={() => onAcrescentarFilho(no.id)}>Acrescentar filho</Botao>
            )}
            {onEditar && <Botao variante="secundario" onClick={() => onEditar(no)}>Editar</Botao>}
            {onExcluir && <Botao variante="perigo" onClick={() => onExcluir(no)}>Excluir</Botao>}
          </div>
        )}

        {posicao && (
          <div className="pl-7">
            <ResumoDePosicoes saldos={posicao.saldos} totalMontado={posicao.totalMontado} />
          </div>
        )}

        {expandido && (
          <div className="ml-7 flex flex-col gap-2 border-l border-borda py-1 pl-3 text-sm">
            {no.materiais.length > 0 && (
              <div>
                <p className="font-semibold text-tinta-fraca">Materiais</p>
                <ul className="flex flex-col gap-1">
                  {no.materiais.map((m) => (
                    <li key={m.materialId} className="text-tinta">{`${m.nome} — ${m.quantidade}`}</li>
                  ))}
                </ul>
              </div>
            )}
            {no.roteiro.length > 0 && (
              <div>
                <p className="font-semibold text-tinta-fraca">Roteiro</p>
                <ol className="flex flex-col gap-1">
                  {roteiroOrdenado.map((p) => (
                    <li key={`${p.ordem}-${p.setorId}`} className="text-tinta">{`${p.ordem}. ${p.nome}`}</li>
                  ))}
                </ol>
              </div>
            )}
          </div>
        )}
      </div>

      {no.filhos.length > 0 && (
        // 12 px por nível: 5 px até a linha-guia + 2 px dela + 5 px até o cartão filho.
        <ul className={`flex flex-col gap-1 ${filhosRecuam ? 'ml-[5px] border-l-2 border-borda-campo pl-[5px]' : ''}`}>
          {no.filhos.map((filho) => (
            <LinhaDoNo
              key={filho.id}
              no={filho}
              nivel={nivel + 1}
              posicoes={posicoes}
              podeEscrever={podeEscrever}
              onAcrescentarFilho={onAcrescentarFilho}
              onEditar={onEditar}
              onExcluir={onExcluir}
              onDetalhe={onDetalhe}
            />
          ))}
        </ul>
      )}
    </li>
  )
}
