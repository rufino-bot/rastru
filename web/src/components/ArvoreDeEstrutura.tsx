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
    // Rótulo no PLURAL desde a Task 8 (Minor 4 da re-review da Task 7): um Agrupamento tem N
    // Peças (`nos` pode ter mais de uma raiz — o teste "um Agrupamento com duas Peças" da própria
    // suíte desta primitiva prova isso), então "Estrutura da peça" no singular mentia sobre o que
    // a lista raiz contém. Hardcoded (sem prop nova): hoje o único consumidor é
    // `AgrupamentoDetalhePage`, e uma prop sem segundo chamador seria parâmetro sem uso real.
    <ul aria-label="Estrutura do agrupamento" className="flex flex-col gap-1">
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
  const recuoPx = RECUO_BASE_PX + nivel * RECUO_POR_NIVEL_PX
  const temAcao = podeEscrever && (onAcrescentarFilho || onEditar || onExcluir)

  return (
    <li>
      <div
        data-testid={`linha-no-${no.id}`}
        className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-borda bg-superficie py-2 pr-3"
        style={{ paddingLeft: `${recuoPx}px` }}
      >
        <div className="flex flex-wrap items-center gap-2">
          {temDetalhe && (
            // `<button>` cru, exceção consciente aceita pelo usuário em 2026-08-29 (não extrair
            // primitiva agora): `Botao` carrega peso de CTA — `secundario` é `px-4 py-2`
            // (`Botao.tsx:25`), dimensionado para "Acrescentar filho"/"Editar"/"Excluir" — peso
            // incompatível com um alternador de ícone de um caractere embutido na linha. Há
            // precedente de FORMA para `<button>` cru fora de `Botao` (`AppShell.tsx:118-126`,
            // o hambúrguer), mas não de motivo: lá a razão documentada é contraste do anel de
            // foco sobre fundo escuro (`AppShell.tsx:47-49`), não peso visual. Extrair
            // `AlternadorDeDisclosure` para os dois usos foi adiado pelo usuário, não descartado.
            <button
              type="button"
              onClick={() => setExpandido((v) => !v)}
              aria-expanded={expandido}
              aria-label={`${expandido ? 'Recolher' : 'Expandir'} ${no.descricao}`}
              // Área de toque declarada, não herdada do glifo: `▸`/`▾` (U+25B8/U+25BE) caem fora do
              // `unicode-range` do subset auto-hospedado (`index.css`) e vão para o fallback do SO,
              // então o tamanho do caractere varia por dispositivo. `min-h-6 min-w-6` = 24×24px CSS,
              // o mínimo da WCAG 2.2 AA 2.5.8 — uso declarado é celular Android no chão de fábrica
              // (`AppShell.tsx:129-130`). Anel de foco por `outline-acao`, o mesmo token do `Botao`
              // (`Botao.tsx:17`): esta primitiva vive sobre fundo claro, não sobre o chrome escuro
              // que justifica `outline-marca` no `AppShell` (`AppShell.tsx:47-49`).
              className="inline-flex min-h-6 min-w-6 items-center justify-center rounded text-tinta-fraca focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
            >
              {expandido ? '▾' : '▸'}
            </button>
          )}
          {no.codigoDoComponente && (
            <span className="font-mono text-sm text-tinta-fraca">{no.codigoDoComponente}</span>
          )}
          <span className="text-tinta">{no.descricao}</span>
          {ehAdHoc && <Pilula tom="neutro">Ad-hoc</Pilula>}
          {/* Task 8, decisão do usuário (review da Task 7): o campo nasce na criação da Peça e
              nenhum pixel o mostrava — nem um dos 15 testes nomeados das Tasks 7 e 8 somadas o
              exercitava (MEDIDO ao ler `ArvoreDeEstrutura.test.tsx` e o brief desta task). Tom
              NEUTRO de propósito: verde/vermelho são reservados a aprovado/reprovado (a tela de
              Qualidade da Fase 5 vai dividir a linha com essa marca), e este rótulo é dado de
              cadastro, não resultado de avaliação. Exibição apenas — editar continua fora (D4). */}
          {no.requerRelatorioDimensional && <Pilula tom="neutro">Requer relatório dimensional</Pilula>}
          <span className="text-sm text-tinta-fraca">{`Qtd: ${no.quantidade}`}</span>
        </div>

        {temAcao && (
          <div data-testid={`acoes-do-no-${no.id}`} className="flex flex-wrap items-center gap-2">
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
          style={{ marginLeft: `${recuoPx}px` }}
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
