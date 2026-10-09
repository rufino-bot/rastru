import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  listarSetores, criarSetor, editarSetor, definirAtivoSetor, ehConflito, type SetorDto,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { Pagina } from '../components/Pagina'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { SeletorDeOrdem, type OpcaoDeOrdem } from '../components/SeletorDeOrdem'
import { ordenarCadastro } from '../cadastros/ordenarCadastro'
import { Botao } from '../components/Botao'
import { Pilula } from '../components/Pilula'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { EstadoVazio } from '../components/EstadoVazio'
import { EstadoCarregando } from '../components/EstadoCarregando'

type Ordem = 'recentes' | 'nome'

const OPCOES_DE_ORDEM: readonly OpcaoDeOrdem<Ordem>[] = [
  { valor: 'recentes', rotulo: 'Mais recentes' },
  { valor: 'nome', rotulo: 'Nome (A→Z)' },
]

type Painel = { tipo: 'novo' } | { tipo: 'editar'; setor: SetorDto }

export function SetoresPage() {
  const [setores, setSetores] = useState<SetorDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [ordem, setOrdem] = useState<Ordem>('recentes')
  const [nome, setNome] = useState('')
  const [atividade, setAtividade] = useState('')
  const [utilizaKit, setUtilizaKit] = useState(false)
  const [painel, setPainel] = useState<Painel | null>(null)
  // `erro`: carga e Inativar/Reativar do item, fora do painel. `erroDeEscrita`: salvar e "Reativar
  // o existente", dentro do painel (decisão D8 do plano da 1F).
  const [erro, setErro] = useState<string | null>(null)
  const [erroDeEscrita, setErroDeEscrita] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)
  const [reativando, setReativando] = useState(false)

  const podeEscrever = usePodeEscrever('setores')

  // Quem abriu o painel recebe o foco de volta quando ele fecha: o "Novo setor" do cabeçalho, ou o
  // "Editar" do setor editado, para quem percorre uma lista longa não voltar ao topo. Se o setor
  // editado não voltou com a recarga, o foco cai no "Novo setor". `origem` nulo é o cabeçalho.
  const botaoNovo = useRef<HTMLButtonElement>(null)
  const botoesEditar = useRef(new Map<number, HTMLButtonElement>())
  const origem = useRef<number | null>(null)

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setSetores(await listarSetores(comInativos))
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os setores.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  const editando = painel?.tipo === 'editar' ? painel.setor : null

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
    setNome('')
    setAtividade('')
    setUtilizaKit(false)
    setPainel({ tipo: 'novo' })
  }

  function fecharPainel() {
    setPainel(null)
    setNome('')
    setAtividade('')
    setUtilizaKit(false)
    setErroDeEscrita(null)
    setIdReativavel(null)
  }

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErroDeEscrita(null)
    setIdReativavel(null)
    setEnviando(true)
    const corpo = { nome, atividade: atividade.trim() === '' ? null : atividade, utilizaKit }
    try {
      const resultado = editando ? await editarSetor(editando.id, corpo) : await criarSetor(corpo)
      if (ehConflito(resultado)) {
        if (!editando && resultado.existeInativo) {
          setErroDeEscrita(`Já existe um setor "${nome}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErroDeEscrita('Já existe um setor com este nome.')
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
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível salvar o setor.'))
    } finally {
      setEnviando(false)
    }
  }

  function editar(setor: SetorDto) {
    origem.current = setor.id
    setErroDeEscrita(null)
    setIdReativavel(null)
    setNome(setor.nome)
    setAtividade(setor.atividade ?? '')
    setUtilizaKit(setor.utilizaKit)
    setPainel({ tipo: 'editar', setor })
  }

  // O `try/catch` continua sendo a fronteira REAL de perfil (F2): esconder o botão é conveniência
  // de interface, e o 403 do backend segue valendo para quem chamar a API por fora da tela.
  async function alternarAtivo(setor: SetorDto) {
    try {
      await definirAtivoSetor(setor.id, !setor.ativo)
      setErro(null)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar o setor.'))
    }
  }

  async function reativar(id: number) {
    setReativando(true)
    try {
      await definirAtivoSetor(id, true)
      fecharPainel()
      setReativando(false)
      await voltarAoInicio()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível reativar o setor.'))
    } finally {
      setReativando(false)
    }
  }

  const visiveis = ordenarCadastro(setores, ordem === 'nome' ? (s) => s.nome : null)

  return (
    <Pagina
      titulo="Setores"
      acao={podeEscrever && painel?.tipo !== 'novo' && <Botao ref={botaoNovo} onClick={abrirNovo}>Novo setor</Botao>}
    >
      {podeEscrever && painel && (
        <PainelDeEscrita
          key={editando ? `editar-${editando.id}` : 'novo'}
          titulo={editando ? 'Editar setor' : 'Novo setor'}
          subtitulo={editando?.nome}
          aoEnviar={salvar}
          aoFechar={fecharPainel}
          enviando={enviando || reativando}
        >
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Nome do setor">
              {(id) => (
                <input id={id} value={nome} onChange={(e) => setNome(e.target.value)} required className={CLASSES_DE_CONTROLE} />
              )}
            </Campo>
            <Campo
              rotulo="Atividade (opcional)"
              dica="Completa os botões da fila: com “montagem”, eles ficam “Iniciar montagem” e “Terminar montagem”."
            >
              {(id, idDaDica) => (
                <input
                  id={id}
                  value={atividade}
                  onChange={(e) => setAtividade(e.target.value)}
                  maxLength={40}
                  aria-describedby={idDaDica}
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <label className="flex items-center gap-2 text-sm text-tinta">
            <input
              type="checkbox"
              checked={utilizaKit}
              onChange={(e) => setUtilizaKit(e.target.checked)}
              className="size-4 accent-acao"
            />
            Utiliza Kit
          </label>
          <p className="text-xs text-tinta-fraca">
            Marque o Setor onde o Kit é montado: lá, os filhos de um Kit só entram em conjunto completo.
          </p>
          <BannerDeErro mensagem={erroDeEscrita} />
          {idReativavel !== null && (
            <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
              Reativar o existente
            </Botao>
          )}
          <div className="flex flex-wrap gap-2">
            <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…">
              {editando ? 'Salvar alterações' : 'Adicionar'}
            </Botao>
          </div>
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
      ) : erro === null && setores.length === 0 ? (
        // `erro === null` é o que distingue "não há setores" de "a listagem falhou": no `catch`
        // de `carregar`, `setSetores` nunca é chamado, então a lista fica `[]` e `.length === 0`
        // sozinho também seria verdade numa falha de rede — mostrando este estado vazio JUNTO do
        // banner de erro, afirmando "nenhum setor cadastrado" a partir de uma falha de conexão.
        <EstadoVazio
          titulo="Nenhum setor cadastrado"
          descricao={podeEscrever ? 'Use o botão Novo setor para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {visiveis.map((s) => (
            <ItemDeCadastro
              key={s.id}
              ativo={s.ativo}
              acao={podeEscrever && (
                <div className="flex flex-wrap gap-2">
                  <Botao
                    variante="secundario"
                    aria-label={`Editar ${s.nome}`}
                    ref={(el) => {
                      if (el) botoesEditar.current.set(s.id, el)
                      else botoesEditar.current.delete(s.id)
                    }}
                    onClick={() => editar(s)}
                  >
                    Editar
                  </Botao>
                  <Botao variante="secundario" onClick={() => alternarAtivo(s)}>
                    {s.ativo ? 'Inativar' : 'Reativar'}
                  </Botao>
                </div>
              )}
            >
              {s.nome}
              {s.atividade && <span className="ml-2 text-sm text-tinta-fraca">{`· ${s.atividade}`}</span>}
              {s.utilizaKit && <span className="ml-2"><Pilula>Utiliza Kit</Pilula></span>}
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
