import { useEffect, useState, type FormEvent } from 'react'
import {
  listarMateriais, criarMaterial, definirAtivoMaterial, ehConflito,
  type MaterialDto, type NovoMaterial,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'
import { EstadoCarregando } from '../components/EstadoCarregando'

const FORMULARIO_VAZIO: NovoMaterial = { codigo: '', descricao: '', unidadeMedida: '' }

export function MateriaisPage() {
  const [materiais, setMateriais] = useState<MaterialDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [form, setForm] = useState<NovoMaterial>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
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

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarMaterial(form)
      if (ehConflito(resultado)) {
        // O conflito é sempre sobre o código (UQ_Material_Codigo); descrição repetida passa.
        if (resultado.existeInativo) {
          setErro(`Já existe um material com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um material com este código.')
        }
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o material.'))
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
      setErro(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível reativar o material.'))
    }
  }

  return (
    <Pagina titulo="Materiais">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
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
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      <label className="flex items-center gap-2 text-sm text-tinta-fraca">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => setIncluirInativos(e.target.checked)}
          className="size-4 accent-acao"
        />
        Mostrar inativos
      </label>

      {carregando ? (
        <EstadoCarregando />
      ) : erro === null && materiais.length === 0 ? (
        // `erro === null` é o que distingue "não há materiais" de "a listagem falhou": no `catch`
        // de `carregar`, `setMateriais` nunca é chamado, então a lista fica `[]` e `.length === 0`
        // sozinho também seria verdade numa falha de rede — mostrando este estado vazio JUNTO do
        // banner de erro, afirmando "nenhum material cadastrado" a partir de uma falha de conexão.
        <EstadoVazio
          titulo="Nenhum material cadastrado"
          descricao={podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {materiais.map((m) => (
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
