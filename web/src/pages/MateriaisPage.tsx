import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarMateriais, criarMaterial, definirAtivoMaterial, ehConflito,
  type MaterialDto, type NovoMaterial,
} from '../api/cadastros'

const FORMULARIO_VAZIO: NovoMaterial = { codigo: '', descricao: '', unidadeMedida: '' }

export function MateriaisPage() {
  const [materiais, setMateriais] = useState<MaterialDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [form, setForm] = useState<NovoMaterial>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setMateriais(await listarMateriais(comInativos))
      setErro(null)
    } catch {
      setErro('Não foi possível carregar os materiais.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    try {
      const resultado = await criarMaterial(form)
      if (ehConflito(resultado)) {
        // O conflito e sempre sobre o codigo (UQ_Material_Codigo); descricao repetida passa.
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
    } catch {
      setErro('Não foi possível salvar o material.')
    }
  }

  // O 403 do backend e a fronteira de perfil (o link aparece para todos de proposito, e
  // PATCH /materiais/{id}/ativo e [Authorize(Roles = "Administrador")]), entao aqui e onde um
  // usuario sem permissao descobre isso — sem try/catch viraria uma promise rejeitada sem
  // tratamento e a tela nao diria nada.
  async function alternarAtivo(material: MaterialDto) {
    try {
      await definirAtivoMaterial(material.id, !material.ativo)
      setErro(null)
      await carregar(incluirInativos)
    } catch {
      setErro('Não foi possível alterar o material.')
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoMaterial(id, true)
      setErro(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await carregar(incluirInativos)
    } catch {
      setErro('Não foi possível reativar o material.')
    }
  }

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/" className="text-sm text-gray-500">&larr; Início</Link>
      <h1 className="text-2xl font-semibold">Materiais</h1>

      <form onSubmit={salvar} className="flex flex-col gap-2">
        <input
          value={form.codigo}
          onChange={(e) => setForm({ ...form, codigo: e.target.value })}
          placeholder="Código"
          required
          className="border rounded px-3 py-2"
        />
        <input
          value={form.descricao}
          onChange={(e) => setForm({ ...form, descricao: e.target.value })}
          placeholder="Descrição"
          required
          className="border rounded px-3 py-2"
        />
        {/* Texto livre de proposito: NVARCHAR(10) sem CHECK no DDL, sem lista fechada. */}
        <input
          value={form.unidadeMedida}
          onChange={(e) => setForm({ ...form, unidadeMedida: e.target.value })}
          placeholder="Unidade (UN, KG, M…)"
          required
          className="border rounded px-3 py-2"
        />
        <button type="submit" className="border rounded px-3 py-2 self-start">Adicionar</button>
      </form>

      {erro && <p className="text-red-600 text-sm">{erro}</p>}
      {idReativavel !== null && (
        <button onClick={() => reativar(idReativavel)} className="border rounded px-3 py-2 self-start">
          Reativar o existente
        </button>
      )}

      <label className="flex items-center gap-2 text-sm text-gray-600">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => setIncluirInativos(e.target.checked)}
        />
        Mostrar inativos
      </label>

      {carregando ? <p className="text-gray-600">Carregando…</p> : (
        <ul className="flex flex-col gap-2">
          {materiais.map((m) => (
            <li key={m.id} className="flex items-center justify-between border rounded px-3 py-2">
              <span className={m.ativo ? '' : 'text-gray-400 line-through'}>
                <strong>{m.codigo}</strong> — {m.descricao} ({m.unidadeMedida})
              </span>
              <button onClick={() => alternarAtivo(m)} className="text-sm border rounded px-2 py-1">
                {m.ativo ? 'Inativar' : 'Reativar'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
