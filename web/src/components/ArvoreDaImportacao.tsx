import { useEffect, useId, useRef, useState } from 'react'
import type { NoDaImportacaoDto, PendenciaDoNo } from '../api/importacao'
import { lerQuantidadeDaConferencia } from '../importacao/quantidade'
import { Campo, CLASSES_DE_CONTROLE } from './Campo'
import { Pilula, type TomDePilula } from './Pilula'

/** O que a tela guarda como seleção: o par que `aoSelecionar` entrega. */
export interface SelecaoDaArvore {
  registroId: number | null
  componenteId: number | null
}

/**
 * A identidade de um código na árvore: o registro do rascunho, ou, no nó que veio só do catálogo,
 * o Componente. É por ela que as ocorrências do mesmo código se reconhecem — a tela usa a mesma
 * para contar o resumo de pendências —, e a decisão sobre um código vale para todas elas.
 */
export function chaveDoCodigo(no: SelecaoDaArvore): string | null {
  if (no.registroId !== null) return `r${no.registroId}`
  if (no.componenteId !== null) return `c${no.componenteId}`
  return null
}

interface Props {
  raiz: NoDaImportacaoDto
  /** O código selecionado, ou `null`. Vale para todas as ocorrências dele (ver `chaveDoCodigo`). */
  selecionado: SelecaoDaArvore | null
  /**
   * Contador que a tela incrementa a cada pedido de "leve-me ao selecionado" (a pílula de resumo).
   * É um contador, e não um booleano, para que pedir DE NOVO o mesmo nó role de novo.
   */
  pedidoDeRolagem?: number
  /**
   * Muda quando a tela quer os campos de quantidade de volta ao valor do servidor — depois de uma
   * escrita que falhou, em que o valor da linha não mudou e a `key` do campo sozinha não remontaria.
   */
  revisao?: number
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
  chave: string | null,
  clicado: string | null,
): string | null {
  if (chave === null) return null
  const doClique = ocorrencias.find((o) => o.caminho === clicado)
  if (doClique && chaveDoCodigo(doClique.no) === chave) return doClique.caminho
  return ocorrencias.find((o) => chaveDoCodigo(o.no) === chave)?.caminho ?? null
}

/**
 * A árvore do rascunho na conferência: o BOM sobreposto ao catálogo, expandido até onde a
 * confirmação o gravaria. Árvore própria, e não a `ArvoreDeEstrutura`: o contrato daquela é o
 * `NoDaEstrutura` real, com ações de nó, posições e Roteiro que não existem no rascunho.
 *
 * Só desenha o que recebe: não busca nada nem sabe de rota. A seleção é da tela, que a usa no painel
 * do Componente; as quantidades editadas vão à tela por `aoAlterarQuantidade`.
 */
export function ArvoreDaImportacao({
  raiz, selecionado, pedidoDeRolagem = 0, revisao = 0, aoSelecionar, aoAlterarQuantidade, desabilitado = false,
}: Props) {
  const [clicado, setClicado] = useState<string | null>(null)
  const ocorrencias = nosEmOrdem(raiz, '0', [])
  const chave = selecionado ? chaveDoCodigo(selecionado) : null
  const atual = caminhoDaSelecionada(ocorrencias, chave, clicado)

  return (
    <ul aria-label="Árvore da importação" className="flex flex-col gap-1">
      <LinhaDoNo
        no={raiz}
        caminho="0"
        nivel={0}
        chave={chave}
        atual={atual}
        pedidoDeRolagem={pedidoDeRolagem}
        revisao={revisao}
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
  chave: string | null
  atual: string | null
  pedidoDeRolagem: number
  revisao: number
  aoClicar: (caminho: string, no: NoDaImportacaoDto) => void
  aoAlterarQuantidade?: (filhoId: number, quantidade: number) => void
  desabilitado: boolean
}

function LinhaDoNo({
  no, caminho, nivel, chave, atual, pedidoDeRolagem, revisao, aoClicar, aoAlterarQuantidade, desabilitado,
}: PropsDaLinha) {
  const ehAtual = atual === caminho
  const ehMesmoCodigo = !ehAtual && chave !== null && chaveDoCodigo(no) === chave
  const linha = useRef<HTMLDivElement>(null)

  // Só um pedido de rolagem leva a linha à vista — um clique não precisa, a linha já está sob o
  // dedo. Cada linha lembra o último pedido que viu, para que a troca de `ehAtual` num clique não
  // reaproveite um pedido antigo. `scrollIntoView` não existe no jsdom, daí a chamada opcional.
  const pedidoVisto = useRef(0)
  useEffect(() => {
    if (pedidoDeRolagem === pedidoVisto.current) return
    pedidoVisto.current = pedidoDeRolagem
    if (ehAtual) linha.current?.scrollIntoView?.({ block: 'nearest' })
  }, [pedidoDeRolagem, ehAtual])

  const editavel = aoAlterarQuantidade !== undefined && no.filhoId !== null && no.quantidadePorPai !== null

  return (
    <li>
      <div
        ref={linha}
        data-testid={`linha-importacao-${caminho}`}
        className={
          // `md:scroll-mt`: sem ele o `scrollIntoView` deixa a linha sob a região fixa do topo da tela, que só
          // é fixa de `md` para cima.
          'relative isolate md:scroll-mt-28 flex flex-wrap items-center justify-between gap-x-3 gap-y-1 rounded-lg border py-2 pr-3 '
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
            // `after:absolute after:inset-0` estica a área de toque do botão por toda a linha (que é
            // `relative isolate`): no celular, acertar só o texto é difícil. O campo de quantidade
            // fica acima do overlay com `z-[1]`, e o `isolate` prende esse z-index DENTRO da linha —
            // sem ele o campo disputaria a raiz com o painel fixo do topo da tela (`z-10`) e, mais
            // adiante no DOM, pintaria por cima dele quando a árvore rola por baixo.
            className="inline-flex flex-wrap items-center gap-2 rounded text-left after:absolute after:inset-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
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
          <CampoDeQuantidade
            key={`${no.filhoId}-${no.quantidadePorPai}-${revisao}`}
            rotulo={`Quantidade por pai de ${no.descricao}`}
            valor={no.quantidadePorPai!}
            desabilitado={desabilitado}
            aoConfirmar={(q) => aoAlterarQuantidade!(no.filhoId!, q)}
          />
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
              chave={chave}
              atual={atual}
              pedidoDeRolagem={pedidoDeRolagem}
              revisao={revisao}
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
 * válido e diferente do atual: uma requisição por tecla estouraria a versão a cada caractere. O que
 * o servidor recusaria (`lerQuantidadeDaConferencia`) volta ao que estava, com o motivo numa linha
 * própria da árvore, ligada ao campo por `aria-describedby`. A `key` do chamador remonta o campo
 * quando a resposta traz o valor novo.
 */
function CampoDeQuantidade({
  rotulo, valor, desabilitado, aoConfirmar,
}: { rotulo: string; valor: number; desabilitado: boolean; aoConfirmar: (quantidade: number) => void }) {
  const [texto, setTexto] = useState(formatar(valor))
  const [motivo, setMotivo] = useState<string | null>(null)
  const idDoMotivo = useId()

  function confirmar() {
    const lida = lerQuantidadeDaConferencia(texto)
    setMotivo(lida.motivo)
    if (lida.valor === null) {
      setTexto(formatar(valor))
      return
    }
    if (lida.valor !== valor) aoConfirmar(lida.valor)
  }

  return (
    <>
      {/* O rótulo existe para o leitor de tela e para o teste; visível, repetiria "Quantidade por
          pai de …" em toda linha. */}
      <div className="relative z-[1] w-24 [&_label]:sr-only">
        <Campo rotulo={rotulo}>
          {(id) => (
            <input
              id={id}
              type="text"
              inputMode="decimal"
              value={texto}
              disabled={desabilitado}
              aria-describedby={motivo ? idDoMotivo : undefined}
              onChange={(e) => setTexto(e.target.value)}
              onBlur={confirmar}
              onKeyDown={(e) => { if (e.key === 'Enter') e.currentTarget.blur() }}
              className={`${CLASSES_DE_CONTROLE} px-2 py-1 text-right`}
            />
          )}
        </Campo>
      </div>
      {/* `basis-full`: a linha é `flex-wrap`, e o motivo desce para uma linha inteira em vez de se
          espremer na largura do campo. */}
      {motivo && (
        <p id={idDoMotivo} className="relative z-[1] basis-full text-right text-sm text-negativo-texto">{motivo}</p>
      )}
    </>
  )
}
