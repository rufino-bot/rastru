import { useEffect, useRef, useState } from 'react'
import type { NoDaImportacaoDto, PendenciaDoNo } from '../api/importacao'
import { Campo, CLASSES_DE_CONTROLE } from './Campo'
import { Pilula, type TomDePilula } from './Pilula'

interface Props {
  raiz: NoDaImportacaoDto
  /**
   * `registroId` do código selecionado, ou `null`. A decisão sobre um código vale para todas as
   * ocorrências dele, então é o registro que se seleciona, e não uma linha.
   */
  selecionado: number | null
  /** `registroId` é nulo no nó que veio só do catálogo; `componenteId`, no registro "criar novo". */
  aoSelecionar: (registroId: number | null, componenteId: number | null) => void
  /**
   * Corrige a quantidade por pai de uma aresta do BOM (a que tem `filhoId`). Ausente = a tela é de
   * leitura, e a quantidade vira texto em toda linha.
   */
  aoAlterarQuantidade?: (filhoId: number, quantidade: number) => void
  /** Uma escrita em voo: trava os campos de quantidade. */
  desabilitado?: boolean
}

// `atencao` para o que pede decisão ou cuidado de quem confere; `neutro` para "Novo", que descreve
// o nó e não pede nada por si — "cor de estado nunca decora" (CLAUDE.md, seção Interface).
const PILULA_DA_PENDENCIA: Record<PendenciaDoNo, { rotulo: string; tom: TomDePilula }> = {
  Novo: { rotulo: 'Novo', tom: 'neutro' },
  Divergente: { rotulo: 'Receita divergente', tom: 'atencao' },
  SemSolido: { rotulo: 'Sem sólido', tom: 'atencao' },
  Inativo: { rotulo: 'Inativo', tom: 'atencao' },
}

const RECUO_BASE_PX = 12
const RECUO_POR_NIVEL_PX = 20

/** Cada ocorrência com a chave dela: o caminho de índices da raiz até ela ("0", "0-1", "0-1-0"). */
function nosEmOrdem(no: NoDaImportacaoDto, caminho: string, saida: { caminho: string; no: NoDaImportacaoDto }[]) {
  saida.push({ caminho, no })
  no.filhos.forEach((f, i) => nosEmOrdem(f, `${caminho}-${i}`, saida))
  return saida
}

/**
 * Qual ocorrência é "a selecionada". O mesmo código aparece em vários lugares, e `selecionado` só
 * diz o registro: a ocorrência em que o usuário CLICOU vale enquanto ela ainda representa esse
 * registro; se a seleção veio de fora (a pílula de resumo da tela), vale a primeira em ordem de
 * árvore.
 */
function caminhoDaSelecionada(
  ocorrencias: { caminho: string; no: NoDaImportacaoDto }[],
  selecionado: number | null,
  clicado: string | null,
): string | null {
  const doClique = ocorrencias.find((o) => o.caminho === clicado)
  if (doClique && doClique.no.registroId === selecionado) return doClique.caminho
  if (selecionado === null) return null
  return ocorrencias.find((o) => o.no.registroId === selecionado)?.caminho ?? null
}

/**
 * A árvore do rascunho na conferência: o BOM sobreposto ao catálogo, expandido até onde a
 * confirmação o gravaria. Árvore própria, e não a `ArvoreDeEstrutura`: o contrato daquela é o
 * `NoDaEstrutura` real, com ações de nó, posições e Roteiro que não existem no rascunho.
 *
 * Só desenha o que recebe: não busca nada nem sabe de rota. A seleção é da tela, que a usa no painel
 * do Componente; as quantidades editadas vão à tela por `aoAlterarQuantidade`.
 */
export function ArvoreDaImportacao({ raiz, selecionado, aoSelecionar, aoAlterarQuantidade, desabilitado = false }: Props) {
  const [clicado, setClicado] = useState<string | null>(null)
  const ocorrencias = nosEmOrdem(raiz, '0', [])
  const atual = caminhoDaSelecionada(ocorrencias, selecionado, clicado)

  return (
    <ul aria-label="Árvore da importação" className="flex flex-col gap-1">
      <LinhaDoNo
        no={raiz}
        caminho="0"
        nivel={0}
        selecionado={selecionado}
        atual={atual}
        clicado={clicado}
        aoClicar={(caminho, no) => {
          setClicado(caminho)
          aoSelecionar(no.registroId, no.componenteId)
        }}
        aoAlterarQuantidade={aoAlterarQuantidade}
        desabilitado={desabilitado}
      />
    </ul>
  )
}

interface PropsDaLinha {
  no: NoDaImportacaoDto
  caminho: string
  nivel: number
  selecionado: number | null
  atual: string | null
  clicado: string | null
  aoClicar: (caminho: string, no: NoDaImportacaoDto) => void
  aoAlterarQuantidade?: (filhoId: number, quantidade: number) => void
  desabilitado: boolean
}

function LinhaDoNo({
  no, caminho, nivel, selecionado, atual, clicado, aoClicar, aoAlterarQuantidade, desabilitado,
}: PropsDaLinha) {
  const ehAtual = atual === caminho
  const ehMesmoCodigo = !ehAtual && selecionado !== null && no.registroId === selecionado
  const linha = useRef<HTMLDivElement>(null)

  // Seleção que veio de fora (pílula de resumo) leva a linha à vista; um clique não precisa disso,
  // a linha já está sob o dedo. `scrollIntoView` não existe no jsdom, daí a chamada opcional.
  const rolar = ehAtual && clicado !== caminho
  useEffect(() => {
    if (rolar) linha.current?.scrollIntoView?.({ block: 'nearest' })
  }, [rolar])

  const editavel = aoAlterarQuantidade !== undefined && no.filhoId !== null && no.quantidadePorPai !== null

  return (
    <li>
      <div
        ref={linha}
        data-testid={`linha-importacao-${caminho}`}
        className={
          'flex flex-wrap items-center justify-between gap-x-3 gap-y-1 rounded-lg border py-2 pr-3 '
          + (ehAtual ? 'border-acao bg-acao-fundo' : ehMesmoCodigo ? 'border-borda bg-acao-fundo' : 'border-borda bg-superficie')
        }
        style={{ paddingLeft: `${RECUO_BASE_PX + nivel * RECUO_POR_NIVEL_PX}px` }}
      >
        <div className="flex flex-wrap items-center gap-2">
          {/* `<button>` cru, na exceção de "botão de chrome" do CLAUDE.md (seção Interface), e o
              caso é o mais próximo do limite dela: é controle de NAVEGAÇÃO — escolhe qual nó o painel
              mostra, não dispara ação de formulário. `Botao` não serve: a `secundario` traz borda e
              `px-4 py-2` de botão com rótulo, e a linha inteira da árvore viraria uma pilha de
              botões. Anel de foco pelo mesmo token que a `BASE` do `Botao` usa. */}
          <button
            type="button"
            aria-label={`${no.codigo} ${no.descricao}`}
            aria-current={ehAtual ? 'true' : undefined}
            onClick={() => aoClicar(caminho, no)}
            className="inline-flex flex-wrap items-center gap-2 rounded text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
          >
            <span className="font-mono text-sm text-tinta-fraca">{no.codigo}</span>
            <span className="text-tinta">{no.descricao}</span>
          </button>
          {no.pendencias.map((p) => (
            <Pilula key={p} tom={PILULA_DA_PENDENCIA[p].tom}>{PILULA_DA_PENDENCIA[p].rotulo}</Pilula>
          ))}
          {ehMesmoCodigo && <span className="text-xs text-tinta-fraca">mesmo código</span>}
        </div>
        {editavel ? (
          // O rótulo existe para o leitor de tela e para o teste; visível, repetiria "Quantidade por
          // pai de …" em toda linha.
          <div className="w-24 [&_label]:sr-only">
            <CampoDeQuantidade
              key={`${no.filhoId}-${no.quantidadePorPai}`}
              rotulo={`Quantidade por pai de ${no.descricao}`}
              valor={no.quantidadePorPai!}
              desabilitado={desabilitado}
              aoConfirmar={(q) => aoAlterarQuantidade!(no.filhoId!, q)}
            />
          </div>
        ) : (
          no.quantidadePorPai !== null && (
            <span className="text-sm text-tinta-fraca">{`× ${formatar(no.quantidadePorPai)}`}</span>
          )
        )}
      </div>
      {no.filhos.length > 0 && (
        <ul className="mt-1 flex flex-col gap-1">
          {no.filhos.map((f, i) => (
            <LinhaDoNo
              key={`${caminho}-${i}`}
              no={f}
              caminho={`${caminho}-${i}`}
              nivel={nivel + 1}
              selecionado={selecionado}
              atual={atual}
              clicado={clicado}
              aoClicar={aoClicar}
              aoAlterarQuantidade={aoAlterarQuantidade}
              desabilitado={desabilitado}
            />
          ))}
        </ul>
      )}
    </li>
  )
}

/** `1,5`, como o usuário digita; o ponto decimal do JSON sai daqui. */
function formatar(n: number): string {
  return String(n).replace('.', ',')
}

/**
 * O campo guarda o que foi digitado e só escreve ao sair dele (ou no Enter), e só se o valor for
 * um número positivo e diferente do atual: uma requisição por tecla estouraria a versão a cada
 * caractere. Valor inválido volta ao que estava. A `key` do chamador remonta o campo quando a
 * resposta traz o valor novo.
 */
function CampoDeQuantidade({
  rotulo, valor, desabilitado, aoConfirmar,
}: { rotulo: string; valor: number; desabilitado: boolean; aoConfirmar: (quantidade: number) => void }) {
  const [texto, setTexto] = useState(formatar(valor))

  function confirmar() {
    const n = Number(texto.trim().replace(',', '.'))
    if (texto.trim() === '' || !Number.isFinite(n) || n <= 0) {
      setTexto(formatar(valor))
      return
    }
    if (n !== valor) aoConfirmar(n)
  }

  return (
    <Campo rotulo={rotulo}>
      {(id) => (
        <input
          id={id}
          type="text"
          inputMode="decimal"
          value={texto}
          disabled={desabilitado}
          onChange={(e) => setTexto(e.target.value)}
          onBlur={confirmar}
          onKeyDown={(e) => { if (e.key === 'Enter') e.currentTarget.blur() }}
          className={`${CLASSES_DE_CONTROLE} px-2 py-1 text-right`}
        />
      )}
    </Campo>
  )
}
