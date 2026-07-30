import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarSetores, criarSetor, definirAtivoSetor, ehConflito, type SetorDto,
} from '../api/cadastros'

export function SetoresPage() {
  const [setores, setSetores] = useState<SetorDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [nome, setNome] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setSetores(await listarSetores(comInativos))
    } catch {
      setErro('Não foi possível carregar os setores.')
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
      const resultado = await criarSetor(nome)
      if (ehConflito(resultado)) {
        if (resultado.existeInativo) {
          setErro(`Já existe um setor "${nome}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um setor com este nome.')
        }
        return
      }
      setNome('')
      await carregar(incluirInativos)
    } catch {
      setErro('Não foi possível salvar o setor.')
    }
  }

  // O 403 do backend e a fronteira de perfil (o link aparece para todos de proposito), entao
  // aqui e onde um usuario sem permissao descobre isso — sem try/catch viraria uma promise
  // rejeitada sem tratamento e a tela nao diria nada.
  async function alternarAtivo(setor: SetorDto) {
    try {
      await definirAtivoSetor(setor.id, !setor.ativo)
      setErro(null)
      await carregar(incluirInativos)
    } catch {
      setErro('Não foi possível alterar o setor.')
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoSetor(id, true)
      setErro(null)
      setIdReativavel(null)
      setNome('')
      await carregar(incluirInativos)
    } catch {
      setErro('Não foi possível reativar o setor.')
    }
  }

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/" className="text-sm text-gray-500">&larr; Início</Link>
      <h1 className="text-2xl font-semibold">Setores</h1>

      <form onSubmit={salvar} className="flex gap-2">
        <input
          value={nome}
          onChange={(e) => setNome(e.target.value)}
          placeholder="Nome do setor"
          required
          className="border rounded px-3 py-2 flex-1"
        />
        <button type="submit" className="border rounded px-3 py-2">Adicionar</button>
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
          {setores.map((s) => (
            <li key={s.id} className="flex items-center justify-between border rounded px-3 py-2">
              <span className={s.ativo ? '' : 'text-gray-400 line-through'}>{s.nome}</span>
              <button onClick={() => alternarAtivo(s)} className="text-sm border rounded px-2 py-1">
                {s.ativo ? 'Inativar' : 'Reativar'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
