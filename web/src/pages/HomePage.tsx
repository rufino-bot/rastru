import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import type { UsuarioDto } from '../api/tipos'

export function HomePage() {
  const { logout } = useAuth()
  const [usuario, setUsuario] = useState<UsuarioDto | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  async function carregarMe() {
    setCarregando(true)
    setErro(null)
    try {
      const resp = await apiFetch('/me')
      if (!resp.ok) {
        setErro('Não foi possível carregar seus dados.')
        return
      }
      setUsuario((await resp.json()) as UsuarioDto)
    } catch {
      setErro('Não foi possível carregar seus dados.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregarMe() }, [])

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <h1 className="text-2xl font-semibold">Rastru</h1>

      {carregando && <p className="text-gray-600">Carregando…</p>}
      {erro && <p className="text-red-600 text-sm">{erro}</p>}

      {usuario && (
        <dl className="flex flex-col gap-2">
          <div><dt className="text-sm text-gray-500">Usuário</dt><dd>{usuario.nomeUsuario}</dd></div>
          <div><dt className="text-sm text-gray-500">Nome</dt><dd>{usuario.nomeCompleto}</dd></div>
          <div><dt className="text-sm text-gray-500">Perfil</dt><dd>{usuario.perfil}</dd></div>
        </dl>
      )}

      <div className="flex gap-3 mt-4">
        <button onClick={carregarMe} className="border rounded px-3 py-2">Recarregar</button>
        <button onClick={logout} className="border rounded px-3 py-2">Sair</button>
      </div>
    </div>
  )
}
