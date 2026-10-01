import { useEffect, useState, type FormEvent } from 'react'
import {
  listarMateriais, criarMaterial, definirAtivoMaterial, ehConflito,
  type MaterialDto, type NovoMaterial,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
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

const FORMULARIO_VAZIO: NovoMaterial = { codigo: '', descricao: '', unidadeMedida: '' }

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
  const [form, setForm] = useState<NovoMaterial>(FORMULARIO_VAZIO)
  const [painelAberto, setPainelAberto] = useState(false)
  // `erro`: carga e Inativar/Reativar do item, fora do painel. `erroDeEscrita`: salvar e "Reativar
  // o existente", dentro do painel (decisão D8 do plano da 1F).
  const [erro, setErro] = useState<string | null>(null)
  const [erroDeEscrita, setErroDeEscrita] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('materiais')

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

  // Desfecho de sucesso de um item NOVO (ou reativado): a lista volta a "Mais recentes" sem os
  // inativos, para o item aparecer (decisão 7 da spec da 1F). Com "Mostrar inativos" marcado, o
  // `useEffect` acima é quem recarrega, porque o valor muda; sem ele, a recarga é daqui.
  async function voltarAoInicio() {
    setOrdem('recentes')
    if (incluirInativos) setIncluirInativos(false)
    else await carregar(false)
  }

  function abrirPainel() {
    setErroDeEscrita(null)
    setIdReativavel(null)
    setForm(FORMULARIO_VAZIO)
    setPainelAberto(true)
  }

  function fecharPainel() {
    setPainelAberto(false)
    setForm(FORMULARIO_VAZIO)
    setErroDeEscrita(null)
    setIdReativavel(null)
  }

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErroDeEscrita(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarMaterial(form)
      if (ehConflito(resultado)) {
        // O conflito é sempre sobre o código (UQ_Material_Codigo); descrição repetida passa.
        if (resultado.existeInativo) {
          setErroDeEscrita(`Já existe um material com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErroDeEscrita('Já existe um material com este código.')
        }
        return
      }
      fecharPainel()
      // A escrita já terminou: solta o envio antes da recarga, senão reabrir o painel durante ela
      // mostraria um "Salvando…" preso.
      setEnviando(false)
      await voltarAoInicio()
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
    try {
      await definirAtivoMaterial(id, true)
      fecharPainel()
      await voltarAoInicio()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível reativar o material.'))
    }
  }

  const visiveis = ordenarCadastro(
    materiais,
    ordem === 'codigo' ? (m) => m.codigo : ordem === 'descricao' ? (m) => m.descricao : null,
  )

  return (
    <Pagina
      titulo="Materiais"
      acao={podeEscrever && !painelAberto && <Botao onClick={abrirPainel}>Novo material</Botao>}
    >
      {podeEscrever && painelAberto && (
        <PainelDeEscrita titulo="Novo material" aoEnviar={salvar} aoFechar={fecharPainel}>
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
          <BannerDeErro mensagem={erroDeEscrita} />
          {idReativavel !== null && (
            <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
              Reativar o existente
            </Botao>
          )}
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
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
                <Botao variante="secundario" onClick={() => alternarAtivo(m)}>
                  {m.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              <span className="font-mono font-semibold">{m.codigo}</span>
              {' — '}
              {m.descricao}
              {' '}
              <Pilula>{m.unidadeMedida}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
