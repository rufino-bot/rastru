import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  listarMateriais, criarMaterial, editarMaterial, definirAtivoMaterial, listarSetores, ehConflito,
  type MaterialDto, type NovoMaterial, type SetorDto,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { Pagina } from '../components/Pagina'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { SeletorDeOrdem, type OpcaoDeOrdem } from '../components/SeletorDeOrdem'
import { ordenarCadastro } from '../cadastros/ordenarCadastro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'
import { EstadoCarregando } from '../components/EstadoCarregando'

// O "Fica em" do formulário é o valor do `<select>`: '' é o almoxarifado, senão o id do Setor.
type Formulario = Omit<NovoMaterial, 'setorDeEstoqueId'> & { ficaEm: string }

const FORMULARIO_VAZIO: Formulario = { codigo: '', descricao: '', unidadeMedida: '', ficaEm: '' }

type Painel = { tipo: 'novo' } | { tipo: 'editar'; material: MaterialDto }

type Ordem = 'recentes' | 'codigo' | 'descricao'

const OPCOES_DE_ORDEM: readonly OpcaoDeOrdem<Ordem>[] = [
  { valor: 'recentes', rotulo: 'Mais recentes' },
  { valor: 'codigo', rotulo: 'Código (A→Z)' },
  { valor: 'descricao', rotulo: 'Descrição (A→Z)' },
]

export function MateriaisPage() {
  const [materiais, setMateriais] = useState<MaterialDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [ordem, setOrdem] = useState<Ordem>('recentes')
  const [form, setForm] = useState<Formulario>(FORMULARIO_VAZIO)
  const [painel, setPainel] = useState<Painel | null>(null)
  // Os Setores ativos do "Fica em", pedidos só a quem escreve. Falhar não trava o painel: o
  // `<select>` ainda tem o almoxarifado e o Setor atual do material editado. `null` é "ainda não
  // chegou".
  const [setores, setSetores] = useState<SetorDto[] | null>(null)
  const [erroDosSetores, setErroDosSetores] = useState<string | null>(null)
  // `erro`: carga e Inativar/Reativar do item, fora do painel. `erroDeEscrita`: salvar e "Reativar
  // o existente", dentro do painel (decisão D8 do plano da 1F).
  const [erro, setErro] = useState<string | null>(null)
  const [erroDeEscrita, setErroDeEscrita] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)
  const [reativando, setReativando] = useState(false)

  const podeEscrever = usePodeEscrever('materiais')

  // Quem abriu o painel recebe o foco de volta quando ele fecha: o "Novo material" do cabeçalho, ou
  // o "Editar" do material editado. Se o material editado não voltou com a recarga, o foco cai no
  // "Novo material". `origem` nulo é o cabeçalho.
  const botaoNovo = useRef<HTMLButtonElement>(null)
  const botoesEditar = useRef(new Map<number, HTMLButtonElement>())
  const origem = useRef<number | null>(null)

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setMateriais(await listarMateriais(comInativos))
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os materiais.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  useEffect(() => {
    if (!podeEscrever) return
    let ativo = true
    listarSetores(false).then(
      (lista) => { if (ativo) { setSetores(lista); setErroDosSetores(null) } },
      () => { if (ativo) setErroDosSetores('Não foi possível carregar os setores.') },
    )
    return () => { ativo = false }
  }, [podeEscrever])

  const editando = painel?.tipo === 'editar' ? painel.material : null

  // `pronto` espera a recarga: durante ela a lista dá lugar ao "Carregando…", e o "Editar" de
  // destino ainda não está no DOM.
  useDevolverFoco(
    painel !== null,
    () => (origem.current === null ? null : botoesEditar.current.get(origem.current)) ?? botaoNovo.current,
    !carregando,
  )

  // Desfecho de sucesso de um item NOVO (ou reativado): a lista volta a "Mais recentes" sem os
  // inativos, para o item aparecer (decisão 7 da spec da 1F). Com "Mostrar inativos" marcado, o
  // `useEffect` que observa `incluirInativos` é quem recarrega, porque o valor muda; sem ele, a
  // recarga é daqui.
  async function voltarAoInicio() {
    setOrdem('recentes')
    if (incluirInativos) setIncluirInativos(false)
    else await carregar(false)
  }

  function abrirNovo() {
    origem.current = null
    setErroDeEscrita(null)
    setIdReativavel(null)
    setForm(FORMULARIO_VAZIO)
    setPainel({ tipo: 'novo' })
  }

  function editar(material: MaterialDto) {
    origem.current = material.id
    setErroDeEscrita(null)
    setIdReativavel(null)
    setForm({
      codigo: material.codigo,
      descricao: material.descricao,
      unidadeMedida: material.unidadeMedida,
      ficaEm: material.ficaEm ? String(material.ficaEm.setorId) : '',
    })
    setPainel({ tipo: 'editar', material })
  }

  function fecharPainel() {
    setPainel(null)
    setForm(FORMULARIO_VAZIO)
    setErroDeEscrita(null)
    setIdReativavel(null)
  }

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErroDeEscrita(null)
    setIdReativavel(null)
    setEnviando(true)
    // O `setorDeEstoqueId` vai sempre, `null` no almoxarifado: o PUT é substituição inteira, e omiti-lo
    // devolveria o material ao almoxarifado.
    const { ficaEm, ...dados } = form
    const corpo: NovoMaterial = { ...dados, setorDeEstoqueId: ficaEm === '' ? null : Number(ficaEm) }
    try {
      const resultado = editando ? await editarMaterial(editando.id, corpo) : await criarMaterial(corpo)
      if (ehConflito(resultado)) {
        // O conflito é sempre sobre o código (UQ_Material_Codigo); descrição repetida passa.
        if (!editando && resultado.existeInativo) {
          setErroDeEscrita(`Já existe um material com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErroDeEscrita('Já existe um material com este código.')
        }
        return
      }
      const eraEdicao = editando !== null
      fecharPainel()
      // A escrita já terminou: solta o envio antes da recarga, senão reabrir o painel durante ela
      // mostraria um "Salvando…" preso.
      setEnviando(false)
      // Editar mantém ordem e inativos: o item editado já estava na tela (decisão D6 do plano da 1F).
      if (eraEdicao) await carregar(incluirInativos)
      else await voltarAoInicio()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível salvar o material.'))
    } finally {
      setEnviando(false)
    }
  }

  async function alternarAtivo(material: MaterialDto) {
    try {
      await definirAtivoMaterial(material.id, !material.ativo)
      setErro(null)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar o material.'))
    }
  }

  async function reativar(id: number) {
    setReativando(true)
    try {
      await definirAtivoMaterial(id, true)
      fecharPainel()
      setReativando(false)
      await voltarAoInicio()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível reativar o material.'))
    } finally {
      setReativando(false)
    }
  }

  const visiveis = ordenarCadastro(
    materiais,
    ordem === 'codigo' ? (m) => m.codigo : ordem === 'descricao' ? (m) => m.descricao : null,
  )

  // Um Setor inativo que já é o `ficaEm` do material editado não vem no `GET /setores` (só ativos);
  // entra como opção a mais, senão o `<select>` cairia em "Almoxarifado" e salvar trocaria o Setor
  // em silêncio.
  // Só se afirma "inativo" com a lista na mão: antes dela, o nome do Setor atual aparece sem sufixo.
  const setorAtual = editando?.ficaEm && !(setores ?? []).some((s) => s.id === editando.ficaEm!.setorId)
    ? editando.ficaEm
    : null
  const setorAtualInativo = setorAtual !== null && setores !== null

  return (
    <Pagina
      titulo="Materiais"
      acao={podeEscrever && painel?.tipo !== 'novo' && <Botao ref={botaoNovo} onClick={abrirNovo}>Novo material</Botao>}
    >
      {podeEscrever && painel && (
        <PainelDeEscrita
          key={editando ? `editar-${editando.id}` : 'novo'}
          titulo={editando ? 'Editar material' : 'Novo material'}
          subtitulo={editando?.codigo}
          aoEnviar={salvar}
          aoFechar={fecharPainel}
          enviando={enviando || reativando}
        >
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  // Monoespaçada em código de material: alinha na coluna e facilita conferir
                  // contra o desenho na bancada (spec §4). É funcional, não decorativo.
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Unidade" dica="UN, KG, M…">
              {(id, idDaDica) => (
                /* Texto livre de propósito: NVARCHAR(10) sem CHECK no DDL, sem lista fechada. */
                <input
                  id={id}
                  aria-describedby={idDaDica}
                  value={form.unidadeMedida}
                  onChange={(e) => setForm({ ...form, unidadeMedida: e.target.value })}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <Campo rotulo="Descrição">
            {(id) => (
              <input
                id={id}
                value={form.descricao}
                onChange={(e) => setForm({ ...form, descricao: e.target.value })}
                required
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
          <Campo rotulo="Fica em">
            {(id) => (
              <select
                id={id}
                value={form.ficaEm}
                onChange={(e) => setForm({ ...form, ficaEm: e.target.value })}
                className={CLASSES_DE_CONTROLE}
              >
                <option value="">Almoxarifado</option>
                {setorAtual && (
                  <option value={String(setorAtual.setorId)}>
                    {setorAtualInativo ? `${setorAtual.nome} (inativo)` : setorAtual.nome}
                  </option>
                )}
                {(setores ?? []).map((s) => <option key={s.id} value={String(s.id)}>{s.nome}</option>)}
              </select>
            )}
          </Campo>
          <BannerDeErro mensagem={erroDosSetores} />
          <BannerDeErro mensagem={erroDeEscrita} />
          {idReativavel !== null && (
            <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
              Reativar o existente
            </Botao>
          )}
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            {editando ? 'Salvar alterações' : 'Adicionar'}
          </Botao>
        </PainelDeEscrita>
      )}

      <BannerDeErro mensagem={erro} />

      <div className="flex flex-wrap items-end justify-between gap-4">
        <label className="flex items-center gap-2 text-sm text-tinta-fraca">
          <input
            type="checkbox"
            checked={incluirInativos}
            onChange={(e) => setIncluirInativos(e.target.checked)}
            className="size-4 accent-acao"
          />
          Mostrar inativos
        </label>
        <SeletorDeOrdem opcoes={OPCOES_DE_ORDEM} valor={ordem} aoMudar={setOrdem} />
      </div>

      {carregando ? (
        <EstadoCarregando />
      ) : erro === null && materiais.length === 0 ? (
        // `erro === null` é o que distingue "não há materiais" de "a listagem falhou": no `catch`
        // de `carregar`, `setMateriais` nunca é chamado, então a lista fica `[]` e `.length === 0`
        // sozinho também seria verdade numa falha de rede — mostrando este estado vazio JUNTO do
        // banner de erro, afirmando "nenhum material cadastrado" a partir de uma falha de conexão.
        <EstadoVazio
          titulo="Nenhum material cadastrado"
          descricao={podeEscrever ? 'Use o botão Novo material para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {visiveis.map((m) => (
            <ItemDeCadastro
              key={m.id}
              ativo={m.ativo}
              acao={podeEscrever && (
                <div className="flex flex-wrap gap-2">
                  <Botao
                    variante="secundario"
                    aria-label={`Editar ${m.codigo}`}
                    ref={(el) => {
                      if (el) botoesEditar.current.set(m.id, el)
                      else botoesEditar.current.delete(m.id)
                    }}
                    onClick={() => editar(m)}
                  >
                    Editar
                  </Botao>
                  <Botao variante="secundario" onClick={() => alternarAtivo(m)}>
                    {m.ativo ? 'Inativar' : 'Reativar'}
                  </Botao>
                </div>
              )}
            >
              <span className="font-mono font-semibold">{m.codigo}</span>
              {' — '}
              {m.descricao}
              {' '}
              <Pilula>{m.unidadeMedida}</Pilula>
              {m.ficaEm && <span className="ml-2 text-sm text-tinta-fraca">{`Fica em: ${m.ficaEm.nome}`}</span>}
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
