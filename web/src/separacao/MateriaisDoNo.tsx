import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import {
  obterMateriaisDoNo, substituirMateriaisDoNo, type MaterialDoNoComSeparacao,
} from '../api/separacao'
import { listarMateriais, type MaterialDto } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { Botao } from '../components/Botao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { Pilula } from '../components/Pilula'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { formatarQuantidade } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'

/** Uma linha do editor. A quantidade fica como texto até salvar: o PCP digita "2,5". */
interface LinhaNoEditor {
  materialId: number
  codigo: string
  descricao: string
  unidadeMedida: string
  quantidade: string
}

/** O planejado no editor: só o que está no plano — o que saiu dele e tem separação não é planejado. */
function paraEditor(materiais: MaterialDoNoComSeparacao[]): LinhaNoEditor[] {
  return materiais
    .filter((m) => m.noPlanejado)
    .map((m) => ({
      materialId: m.materialId,
      codigo: m.codigo,
      descricao: m.descricao,
      unidadeMedida: m.unidadeMedida,
      quantidade: quantidadeParaCampo(m.planejado),
    }))
}

/**
 * Planejado, separado e, só do material do almoxarifado que está no plano, a pendência (regra 34): o
 * material de um Setor o operador pega ali, e o que saiu do plano não tem o que faltar.
 */
function numerosDo(m: MaterialDoNoComSeparacao): string {
  const un = m.unidadeMedida
  const base = `Planejado ${formatarQuantidade(m.planejado)} ${un} · Separado ${formatarQuantidade(m.separado)} ${un}`
  return m.ficaEm === null && m.noPlanejado ? `${base} · Pendência ${formatarQuantidade(m.pendencia)} ${un}` : base
}

interface Props {
  noId: number
  /** O PCP e o Administrador (`materiaisDoNo`); quem decide é o 403 do backend. */
  podeEditar: boolean
  /** O planejado mudou no servidor — o painel recarrega o histórico de separação. */
  aoSalvar: () => void
}

/**
 * Os materiais de UM nó com a conta da separação (spec da Fase 4, seção 6.2): o planejado, o separado, a
 * pendência e onde cada um fica. O que saiu do plano e tem separação continua aqui, marcado "fora do
 * planejado"; o que passou do planejado leva "acima do planejado". As duas marcas são informação, não
 * erro, e por isso não usam cor de estado.
 *
 * "Editar materiais" abre um `PainelDeEscrita` no molde dos materiais da receita padrão da
 * `ComponenteDetalhePage`, mas com a quantidade editável em cada linha, e salva a lista inteira num
 * `PUT` só (`substituirMateriaisDoNo`): o que não vai na lista sai do plano. Editar é livre a qualquer
 * momento (D14): o que já foi separado é fato, e fica no histórico.
 */
export function MateriaisDoNo({ noId, podeEditar, aoSalvar }: Props) {
  const idDoTitulo = useId()
  const [materiais, setMateriais] = useState<MaterialDoNoComSeparacao[] | null>(null)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)

  const [editando, setEditando] = useState(false)
  const [linhas, setLinhas] = useState<LinhaNoEditor[]>([])
  const [catalogo, setCatalogo] = useState<MaterialDto[]>([])
  const [erroDoCatalogo, setErroDoCatalogo] = useState<string | null>(null)
  const [materialEscolhido, setMaterialEscolhido] = useState<number | null>(null)
  const [quantidadeNova, setQuantidadeNova] = useState('')
  const [salvando, setSalvando] = useState(false)
  const [erroAoSalvar, setErroAoSalvar] = useState<string | null>(null)
  // Mesmo padrão do `EditorDeRoteiroDoNo`: o `ref` guarda a função inteira contra o segundo toque no
  // mesmo quadro, antes de o React redesenhar o botão desabilitado; o submit pelo teclado (Enter num
  // campo) também passa por aqui, e não pelo botão.
  const enviandoRef = useRef(false)
  const botaoEditarRef = useRef<HTMLButtonElement>(null)

  useDevolverFoco(editando, () => botaoEditarRef.current)

  // Recebe o id como argumento (molde de `EditorDeRoteiroDoNo`): o exhaustive-deps cobraria
  // `carregar` como dependência do efeito se o corpo fechasse sobre `noId`.
  async function carregar(id: number) {
    setErroDeCarga(null)
    try {
      setMateriais((await obterMateriaisDoNo(id)).materiais)
    } catch (e) {
      setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar os materiais.'))
    }
  }

  useEffect(() => { carregar(noId) }, [noId])

  useEffect(() => {
    if (!podeEditar) return
    let cancelado = false
    listarMateriais(false)
      .then((m) => { if (!cancelado) setCatalogo(m) })
      .catch((e) => { if (!cancelado) setErroDoCatalogo(mensagemDeErro(e, 'Não foi possível carregar os materiais do cadastro.')) })
    return () => { cancelado = true }
  }, [podeEditar])

  function abrirEditor() {
    if (materiais === null) return
    setLinhas(paraEditor(materiais))
    setMaterialEscolhido(null)
    setQuantidadeNova('')
    setErroAoSalvar(null)
    setEditando(true)
  }

  // O seletor não oferece o que já está na lista: o servidor recusaria o material repetido com 400.
  const naLista = new Set(linhas.map((l) => l.materialId))
  const oferecidos = catalogo.filter((m) => !naLista.has(m.id))
  const leituraDaNova = lerQuantidade(quantidadeNova, Infinity)

  function adicionar() {
    const material = catalogo.find((m) => m.id === materialEscolhido)
    if (!material || leituraDaNova.valor === null) return
    setLinhas([...linhas, {
      materialId: material.id,
      codigo: material.codigo,
      descricao: material.descricao,
      unidadeMedida: material.unidadeMedida,
      quantidade: quantidadeNova,
    }])
    setMaterialEscolhido(null)
    setQuantidadeNova('')
  }

  function mudarQuantidade(materialId: number, quantidade: string) {
    setLinhas(linhas.map((l) => (l.materialId === materialId ? { ...l, quantidade } : l)))
  }

  function remover(materialId: number) {
    setLinhas(linhas.filter((l) => l.materialId !== materialId))
  }

  async function salvar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current) return
    // Lê todas antes de enviar: a lista vai inteira, então uma linha ruim segura o envio todo.
    const lidas = linhas.map((l) => ({ linha: l, leitura: lerQuantidade(l.quantidade, Infinity) }))
    const ruim = lidas.find((x) => x.leitura.erro !== null)
    if (ruim) {
      setErroAoSalvar(`${ruim.linha.codigo}: ${ruim.leitura.erro}`)
      return
    }
    enviandoRef.current = true
    setSalvando(true)
    setErroAoSalvar(null)
    try {
      const salvo = await substituirMateriaisDoNo(
        noId,
        lidas.map((x) => ({ materialId: x.linha.materialId, quantidade: x.leitura.valor as number })),
      )
      setMateriais(salvo.materiais)
      setEditando(false)
      aoSalvar()
    } catch (erro) {
      setErroAoSalvar(mensagemDeErro(erro, 'Não foi possível salvar os materiais.'))
    } finally {
      enviandoRef.current = false
      setSalvando(false)
    }
  }

  return (
    <section aria-labelledby={idDoTitulo} className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h3 id={idDoTitulo} className="font-medium text-tinta">Materiais</h3>
        {podeEditar && materiais !== null && !editando && (
          <Botao ref={botaoEditarRef} variante="secundario" onClick={abrirEditor}>Editar materiais</Botao>
        )}
      </div>
      <BannerDeErro mensagem={erroDeCarga} />
      {materiais === null && erroDeCarga === null && <EstadoCarregando />}
      {editando && (
        <PainelDeEscrita titulo="Editar materiais" aoEnviar={salvar} aoFechar={() => setEditando(false)} enviando={salvando}>
          {linhas.length === 0 && (
            <p className="text-sm text-tinta-fraca">Nenhum material no plano. Salvar assim tira todos.</p>
          )}
          {linhas.length > 0 && (
            <ul aria-label="Materiais planejados" className="flex flex-col gap-3">
              {linhas.map((l) => (
                <li key={l.materialId}>
                  {/* O "Remover" vai no `aoLado`: fica na linha da caixa do campo, da altura dela, e a dica
                      (descrição e unidade) desce para baixo dos dois. */}
                  <Campo
                    rotulo={`Quantidade de ${l.codigo}`}
                    dica={`${l.descricao} (${l.unidadeMedida})`}
                    aoLado={(
                      <Botao
                        variante="secundario"
                        aria-label={`Remover ${l.codigo}`}
                        onClick={() => remover(l.materialId)}
                        disabled={salvando}
                      >
                        Remover
                      </Botao>
                    )}
                  >
                    {(id, idDaDica) => (
                      <input
                        id={id}
                        aria-describedby={idDaDica}
                        inputMode="decimal"
                        value={l.quantidade}
                        onChange={(ev) => mudarQuantidade(l.materialId, ev.target.value)}
                        disabled={salvando}
                        className={CLASSES_DE_CONTROLE}
                      />
                    )}
                  </Campo>
                </li>
              ))}
            </ul>
          )}
          {/* Duas colunas alinhadas pelo início: os rótulos ficam na mesma linha, e o `<select>` e a caixa da
              Quantidade, com as mesmas classes, na de baixo; o "Adicionar" vai no `aoLado` da Quantidade, da
              altura da caixa dela. */}
          <div className="grid gap-3 sm:grid-cols-2">
            <Campo rotulo="Material">
              {(id) => (
                <select
                  id={id}
                  value={materialEscolhido ?? ''}
                  onChange={(ev) => setMaterialEscolhido(ev.target.value ? Number(ev.target.value) : null)}
                  disabled={salvando}
                  className={CLASSES_DE_CONTROLE}
                >
                  <option value="">Selecione um material</option>
                  {oferecidos.map((m) => <option key={m.id} value={m.id}>{`${m.codigo} — ${m.descricao}`}</option>)}
                </select>
              )}
            </Campo>
            <Campo
              rotulo="Quantidade"
              aoLado={(
                <Botao
                  variante="secundario"
                  onClick={adicionar}
                  disabled={materialEscolhido === null || leituraDaNova.valor === null || salvando}
                >
                  Adicionar
                </Botao>
              )}
            >
              {(id) => (
                <input
                  id={id}
                  inputMode="decimal"
                  value={quantidadeNova}
                  onChange={(ev) => setQuantidadeNova(ev.target.value)}
                  disabled={salvando}
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <BannerDeErro mensagem={erroDoCatalogo} />
          <BannerDeErro mensagem={erroAoSalvar} />
          <Botao type="submit" carregando={salvando} rotuloCarregando="Salvando…" className="self-start">
            Salvar materiais
          </Botao>
        </PainelDeEscrita>
      )}
      {materiais !== null && materiais.length === 0 && <EstadoVazio titulo="Este item não tem materiais." />}
      {materiais !== null && materiais.length > 0 && (
        <ListaDeCadastro rotulo="Materiais do item">
          {materiais.map((m) => (
            <ItemComAcao key={m.materialId}>
              <span className="flex flex-wrap items-center gap-2 text-sm font-medium text-tinta">
                {`${m.codigo} ${m.descricao}`}
                {!m.noPlanejado && <Pilula>fora do planejado</Pilula>}
              </span>
              <span className="text-sm text-tinta">{numerosDo(m)}</span>
              {m.acimaDoPlanejado && <span className="text-sm text-tinta-fraca">acima do planejado</span>}
              <span className="text-xs text-tinta-fraca">{`Fica em: ${m.ficaEm?.nome ?? 'Almoxarifado'}`}</span>
            </ItemComAcao>
          ))}
        </ListaDeCadastro>
      )}
    </section>
  )
}
