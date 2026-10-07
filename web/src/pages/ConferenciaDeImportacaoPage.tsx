import { useEffect, useId, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ErroDeApi, mensagemDeErro } from '../api/erros'
import {
  alterarFilho, alterarPeca, AVISO_IMPORTACAO_DESATUALIZADA, confirmarImportacao, descartarImportacao,
  ehImportacaoDesatualizada, LIMITE_DO_BOM_LEGIVEL, obterImportacao, reimportar,
  type ImportacaoDto, type NoDaImportacaoDto, type PendenciaDoNo,
} from '../api/importacao'
import { usePodeEscrever } from '../auth/usePermissao'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { ArvoreDaImportacao, chaveDoCodigo, type SelecaoDaArvore } from '../components/ArvoreDaImportacao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { Confirmacao } from '../components/Confirmacao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { Pagina } from '../components/Pagina'
import { Pilula, type TomDePilula } from '../components/Pilula'
import { PainelDoArquivoDoBom } from '../importacao/PainelDoArquivoDoBom'
import { PainelDoComponenteDaImportacao } from '../importacao/PainelDoComponenteDaImportacao'
import { lerQuantidadeDaConferencia } from '../importacao/quantidade'

const AVISO_DESATUALIZADA = AVISO_IMPORTACAO_DESATUALIZADA
const AVISO_BLOQUEIOS = 'A importação ainda tem bloqueios; a lista foi atualizada.'
const AVISO_RECEITA_MUDOU =
  'A receita de um Componente do catálogo mudou desde a conferência; a tela foi atualizada. '
  + 'Confira as escolhas e confirme de novo.'

/** O que a tela escreve por pendência no resumo: o tom segue o da pílula na árvore. */
const RESUMO_DA_PENDENCIA: Record<PendenciaDoNo, { singular: string; plural: string; tom: TomDePilula }> = {
  Divergente: { singular: 'divergência', plural: 'divergências', tom: 'atencao' },
  SemSolido: { singular: 'sem sólido', plural: 'sem sólido', tom: 'atencao' },
  Inativo: { singular: 'inativo', plural: 'inativos', tom: 'atencao' },
  Novo: { singular: 'novo', plural: 'novos', tom: 'neutro' },
}
const ORDEM_DO_RESUMO: PendenciaDoNo[] = ['Divergente', 'SemSolido', 'Inativo', 'Novo']

function nosEmOrdem(no: NoDaImportacaoDto, saida: NoDaImportacaoDto[] = []): NoDaImportacaoDto[] {
  saida.push(no)
  no.filhos.forEach((f) => nosEmOrdem(f, saida))
  return saida
}

function resumir(raiz: NoDaImportacaoDto) {
  const resumo = new Map<PendenciaDoNo, { codigos: Set<string>; primeiro: NoDaImportacaoDto }>()
  for (const no of nosEmOrdem(raiz)) {
    for (const p of no.pendencias) {
      const atual = resumo.get(p)
      if (atual) atual.codigos.add(chaveDoCodigo(no) ?? '')
      else resumo.set(p, { codigos: new Set([chaveDoCodigo(no) ?? '']), primeiro: no })
    }
  }
  return ORDEM_DO_RESUMO.flatMap((p) => {
    const r = resumo.get(p)
    return r ? [{ pendencia: p, quantidade: r.codigos.size, primeiro: r.primeiro }] : []
  })
}

/** O primeiro nó do código selecionado, pela mesma chave que a árvore usa para marcá-lo. */
function acharNo(raiz: NoDaImportacaoDto, s: SelecaoDaArvore): NoDaImportacaoDto | undefined {
  const chave = chaveDoCodigo(s)
  return chave === null ? undefined : nosEmOrdem(raiz).find((n) => chaveDoCodigo(n) === chave)
}

/**
 * A conferência de um rascunho de importação do BOM, de cima para baixo: o painel do Componente
 * selecionado, a faixa da Peça (quantidade, pendências, Confirmar, Reimportar e Descartar) e a árvore
 * expandida.
 *
 * O estado da tela é o `ImportacaoDto` mais recente, e **toda** escrita o substitui pela resposta. Os
 * bloqueios e as pendências são calculados pelo servidor a cada leitura; a tela não os recalcula.
 *
 * As escritas andam **em fila**, uma de cada vez, e cada uma monta a requisição na hora de sair, a
 * partir do rascunho mais recente (`atual`), e não do que estava na tela quando foi pedida. Os campos
 * de texto do Componente novo não travam durante uma escrita (quem sai do Código com Tab já está
 * digitando na Descrição), então uma segunda escrita pode ser pedida com a primeira em voo: montada
 * na hora do pedido, ela levaria a versão que a primeira gastou e voltaria 409, e levaria o Código
 * de antes, desfazendo a primeira.
 */
export function ConferenciaDeImportacaoPage() {
  const { id: idDaRota } = useParams()
  const id = Number(idDaRota)
  const navegar = useNavigate()
  const podeEscrever = usePodeEscrever('estrutura')
  const idDosBloqueios = useId()

  const [importacao, setImportacao] = useState<ImportacaoDto | null>(null)
  // O rascunho mais recente, para a escrita da fila que sai depois de outra: o `importacao` do render
  // em que ela foi pedida já é velho quando ela sai.
  const atual = useRef<ImportacaoDto | null>(null)
  const fila = useRef<Promise<unknown>>(Promise.resolve())
  const naFila = useRef(0)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [descartando, setDescartando] = useState(false)
  const [reimportando, setReimportando] = useState(false)
  // O painel do arquivo sai do DOM ao fechar (Cancelar ou sucesso) e leva o foco junto; o hook o
  // devolve ao botão que o abriu.
  const botaoReimportar = useRef<HTMLButtonElement>(null)
  useDevolverFoco(reimportando, () => botaoReimportar.current)
  const [selecao, setSelecao] = useState<SelecaoDaArvore | null>(null)
  // Pedidos de "leve-me ao nó" da pílula de resumo; a árvore rola a cada incremento.
  const [pedidoDeRolagem, setPedidoDeRolagem] = useState(0)
  // Incrementa ao fim de toda escrita ou releitura: os campos de quantidade remontam com o valor do
  // servidor, e o digitado de uma escrita que falhou não fica na tela.
  const [revisao, setRevisao] = useState(0)

  useEffect(() => {
    let cancelado = false
    setImportacao(null)
    setErroDeCarga(null)
    obterImportacao(id)
      .then((r) => { if (!cancelado) guardar(r) })
      .catch((e) => {
        if (!cancelado) setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar a importação.'))
      })
    return () => { cancelado = true }
  }, [id])

  function guardar(r: ImportacaoDto) {
    atual.current = r
    setImportacao(r)
  }

  /**
   * Põe `tarefa` na fila das escritas: ela começa quando a anterior termina, com sucesso ou não. A
   * tela fica `enviando` enquanto houver tarefa na fila, e não só durante a que está em voo.
   */
  function enfileirar<T>(tarefa: () => Promise<T>): Promise<T> {
    naFila.current += 1
    setEnviando(true)
    const vez = fila.current.then(tarefa)
    fila.current = vez.catch(() => {})
    return vez.finally(() => {
      naFila.current -= 1
      if (naFila.current === 0) setEnviando(false)
    })
  }

  /**
   * Relê o rascunho e mostra `mensagem` (a razão da releitura) no topo; se a releitura falha, mostra
   * o erro dela. Com `mensagem` nula, quem pediu a releitura mostra a razão em outro lugar.
   */
  async function reler(mensagem: string | null) {
    try {
      guardar(await obterImportacao(id))
      if (mensagem !== null) setAviso(mensagem)
    } catch (e) {
      setAviso(mensagemDeErro(e, 'Não foi possível recarregar a importação.'))
    }
  }

  /**
   * Toda escrita do rascunho, pela fila: `acao` recebe o rascunho mais recente, de onde tira a versão
   * e o que mais a requisição leva, e a resposta vira o estado; versão velha (409) relê e avisa.
   *
   * Com `noPainel`, a falha não vira aviso no topo: a escrita relança o erro (depois de reler, no
   * 409) para o painel que a pediu mostrá-lo onde o usuário está olhando — o do Componente, o do
   * sólido e o do reimport, cujo erro traz a lista de linhas do arquivo.
   */
  function escrever(acao: (atual: ImportacaoDto) => Promise<ImportacaoDto>, noPainel = false): Promise<void> {
    return enfileirar(async () => {
      setAviso(null)
      try {
        guardar(await acao(atual.current!))
      } catch (e) {
        if (ehImportacaoDesatualizada(e)) await reler(noPainel ? null : AVISO_DESATUALIZADA)
        else if (!noPainel) setAviso(mensagemDeErro(e, 'Não foi possível salvar a alteração.'))
        if (noPainel) throw e
      } finally {
        setRevisao((r) => r + 1)
      }
    })
  }

  /** Troca o arquivo do rascunho; o painel fecha só no sucesso, e a falha fica nele, com o arquivo descartado. */
  async function reimportarArquivo(arquivo: File) {
    await escrever((a) => reimportar(a.id, a.versao, arquivo), true)
    setReimportando(false)
  }

  function confirmar() {
    return enfileirar(confirmarNaVez)
  }

  async function confirmarNaVez() {
    const rascunho = atual.current
    if (!rascunho) return
    setAviso(null)
    try {
      const resultado = await confirmarImportacao(rascunho.id, rascunho.versao)
      if (typeof resultado !== 'string') {
        navegar(`/agrupamentos/${rascunho.agrupamentoId}`)
        return
      }
      // A recusa não traz a lista de bloqueios (P15 do plano): o `GET` a traz, e é a releitura que a mostra.
      await reler(
        resultado === 'ImportacaoComBloqueios' ? AVISO_BLOQUEIOS
          : resultado === 'ReceitaDoCatalogoMudou' ? AVISO_RECEITA_MUDOU
            : AVISO_DESATUALIZADA,
      )
    } catch (e) {
      setAviso(mensagemDeErro(e, 'Não foi possível confirmar a importação.'))
    } finally {
      setRevisao((r) => r + 1)
    }
  }

  function descartar() {
    setDescartando(false)
    return enfileirar(async () => {
      const rascunho = atual.current
      if (!rascunho) return
      setAviso(null)
      try {
        await descartarImportacao(rascunho.id)
        navegar(`/agrupamentos/${rascunho.agrupamentoId}`)
      } catch (e) {
        // 404: o rascunho já não existe (descartado em outra aba), e o destino é o mesmo.
        if (e instanceof ErroDeApi && e.status === 404) navegar(`/agrupamentos/${rascunho.agrupamentoId}`)
        else setAviso(mensagemDeErro(e, 'Não foi possível descartar a importação.'))
      }
    })
  }

  if (erroDeCarga !== null) {
    return <Pagina titulo="Conferência da importação"><BannerDeErro mensagem={erroDeCarga} /></Pagina>
  }
  if (importacao === null) {
    return <Pagina titulo="Conferência da importação"><EstadoCarregando /></Pagina>
  }

  const { raiz, bloqueios } = importacao
  // A seleção que já não está na árvore (a releitura a tirou) cai na raiz — no painel e na árvore, os
  // dois pela MESMA seleção, para nenhum dos dois ficar apontando para um nó que sumiu.
  const daRaiz: SelecaoDaArvore | null = raiz ? { registroId: raiz.registroId, componenteId: raiz.componenteId } : null
  const selecaoEfetiva = selecao && raiz && acharNo(raiz, selecao) ? selecao : daRaiz
  const noSelecionado = raiz && selecaoEfetiva ? (acharNo(raiz, selecaoEfetiva) ?? raiz) : null
  const resumo = raiz ? resumir(raiz) : []
  const travado = enviando || !podeEscrever

  return (
    <Pagina titulo="Conferência da importação">
      <BannerDeErro mensagem={aviso} />

      {/* Região 1 — o painel fixo do Componente selecionado. */}
      {noSelecionado ? (
        <PainelDoComponenteDaImportacao
          importacao={importacao}
          registroId={noSelecionado.registroId}
          componenteId={noSelecionado.componenteId}
          escrever={escrever}
          desabilitado={enviando}
          revisao={revisao}
        />
      ) : (
        <section aria-label="Componente selecionado" className="rounded-lg border border-borda bg-superficie px-4 py-3">
          <span className="text-tinta-fraca">Nenhum componente para mostrar.</span>
        </section>
      )}

      {/* Região 2 — a faixa da Peça. */}
      <section aria-labelledby="titulo-da-peca" className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie px-4 py-4">
        <div className="flex flex-col gap-1">
          <h2 id="titulo-da-peca" className="text-lg font-medium text-tinta">Peça</h2>
          <p className="text-sm text-tinta-fraca">
            {`${importacao.nomeDoArquivo} · ${importacao.criadoPor}`}
          </p>
        </div>

        <div className="flex flex-wrap items-end gap-x-6 gap-y-3">
          <CampoDeQuantidadeDaPeca
            key={`${importacao.quantidadeDaPeca}-${revisao}`}
            valor={importacao.quantidadeDaPeca}
            desabilitado={travado}
            aoConfirmar={(q) => { void escrever((a) => alterarPeca(a.id, a.versao, q, a.requerRelatorioDimensional)) }}
          />
          <label className="flex items-center gap-2 pb-2.5 text-sm text-tinta-fraca">
            <input
              type="checkbox"
              checked={importacao.requerRelatorioDimensional}
              disabled={travado}
              onChange={(e) => {
                const requer = e.target.checked
                void escrever((a) => alterarPeca(a.id, a.versao, a.quantidadeDaPeca, requer))
              }}
              className="size-4 accent-acao"
            />
            Requer relatório dimensional
          </label>
        </div>

        {resumo.length > 0 && (
          <div className="flex flex-wrap gap-2">
            {resumo.map(({ pendencia, quantidade, primeiro }) => {
              const texto = RESUMO_DA_PENDENCIA[pendencia]
              const rotulo = `${quantidade} ${quantidade === 1 ? texto.singular : texto.plural}`
              return (
                <Botao
                  key={pendencia}
                  variante="secundario"
                  onClick={() => {
                    setSelecao({ registroId: primeiro.registroId, componenteId: primeiro.componenteId })
                    setPedidoDeRolagem((n) => n + 1)
                  }}
                >
                  <Pilula tom={texto.tom}>{rotulo}</Pilula>
                </Botao>
              )
            })}
          </div>
        )}

        {bloqueios.length > 0 && (
          <ul
            id={idDosBloqueios}
            aria-label="O que falta para confirmar"
            className="list-disc pl-5 text-sm text-tinta"
          >
            {bloqueios.map((b, i) => <li key={`${b.tipo}-${b.registroId}-${i}`}>{b.mensagem}</li>)}
          </ul>
        )}

        {podeEscrever && (
          <div className="flex flex-wrap items-center gap-3">
            <Botao
              onClick={confirmar}
              disabled={bloqueios.length > 0}
              carregando={enviando}
              aria-describedby={bloqueios.length > 0 ? idDosBloqueios : undefined}
            >
              Confirmar
            </Botao>
            {/* Some enquanto o painel está aberto, como o botão que abre qualquer `PainelDeEscrita`. */}
            {!reimportando && (
              <Botao ref={botaoReimportar} variante="secundario" disabled={enviando} onClick={() => setReimportando(true)}>
                Reimportar
              </Botao>
            )}
            <Botao variante="secundario" disabled={enviando} onClick={() => setDescartando(true)}>
              Descartar
            </Botao>
          </div>
        )}

        {podeEscrever && reimportando && (
          <PainelDoArquivoDoBom
            titulo="Reimportar BOM"
            subtitulo="Troca o arquivo do rascunho. O que você decidiu por código que continua no arquivo é mantido; as quantidades voltam ao que o arquivo diz."
            rotuloDoEnvio="Reimportar"
            rotuloEnviando="Reimportando…"
            fallbackDoErro={`Não foi possível reimportar o BOM. Envie um arquivo .xlsx ou .csv de até ${LIMITE_DO_BOM_LEGIVEL}.`}
            aoEnviar={reimportarArquivo}
            aoFechar={() => setReimportando(false)}
            mensagemDoErro={(e) => (ehImportacaoDesatualizada(e) ? AVISO_DESATUALIZADA : null)}
          />
        )}
      </section>

      {/* Região 3 — a árvore expandida. */}
      <section aria-labelledby="titulo-da-estrutura" className="flex flex-col gap-3">
        <h2 id="titulo-da-estrutura" className="text-lg font-medium text-tinta">Estrutura</h2>
        {raiz ? (
          <ArvoreDaImportacao
            raiz={raiz}
            selecionado={selecaoEfetiva}
            pedidoDeRolagem={pedidoDeRolagem}
            revisao={revisao}
            aoSelecionar={(registroId, componenteId) => setSelecao({ registroId, componenteId })}
            aoAlterarQuantidade={podeEscrever ? (filhoId, quantidade) => {
              void escrever((a) => alterarFilho(a.id, filhoId, a.versao, quantidade))
            } : undefined}
            desabilitado={enviando}
          />
        ) : (
          <EstadoVazio
            titulo="A estrutura não pôde ser expandida"
            descricao="O motivo está na lista do que falta para confirmar, acima."
          />
        )}
      </section>

      <Confirmacao
        aberto={descartando}
        mensagem={(
          <>
            Descartar a importação de <strong className="font-mono">{importacao.nomeDoArquivo}</strong>?
            O que foi conferido nela se perde. Esta ação não pode ser desfeita.
          </>
        )}
        rotuloConfirmar="Descartar"
        aoConfirmar={descartar}
        aoCancelar={() => setDescartando(false)}
      />
    </Pagina>
  )
}

/**
 * Guarda o que foi digitado e só escreve ao sair do campo: uma requisição por tecla gastaria a
 * versão do rascunho a cada caractere. Campo vazio vira `null` (a Peça sem quantidade, que é um
 * bloqueio); o que o servidor recusaria (`lerQuantidadeDaConferencia`) volta ao valor anterior, com
 * o motivo na dica do campo.
 */
function CampoDeQuantidadeDaPeca({
  valor, desabilitado, aoConfirmar,
}: { valor: number | null; desabilitado: boolean; aoConfirmar: (quantidade: number | null) => void }) {
  const [texto, setTexto] = useState(valor === null ? '' : String(valor).replace('.', ','))
  const [motivo, setMotivo] = useState<string | null>(null)
  const idDoMotivo = useId()

  function confirmar() {
    const lida = texto.trim() === '' ? { valor: null, motivo: null } : lerQuantidadeDaConferencia(texto)
    setMotivo(lida.motivo)
    if (lida.motivo !== null) {
      setTexto(valor === null ? '' : String(valor).replace('.', ','))
      return
    }
    if (lida.valor !== valor) aoConfirmar(lida.valor)
  }

  return (
    <>
      <div className="w-32">
        <Campo rotulo="Quantidade da Peça">
          {(id) => (
            <input
              id={id}
              type="text"
              inputMode="decimal"
              value={texto}
              aria-describedby={motivo ? idDoMotivo : undefined}
              disabled={desabilitado}
              onChange={(e) => setTexto(e.target.value)}
              onBlur={confirmar}
              onKeyDown={(e) => { if (e.key === 'Enter') e.currentTarget.blur() }}
              className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>
      </div>
      {/* Numa linha inteira, depois do resto da faixa: na largura do campo, a frase se espremeria. */}
      {motivo && <p id={idDoMotivo} className="order-last basis-full text-sm text-negativo-texto">{motivo}</p>}
    </>
  )
}
