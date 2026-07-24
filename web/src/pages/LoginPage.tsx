import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export function LoginPage() {
  const { estado, login } = useAuth()
  const [nomeUsuario, setNomeUsuario] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  if (estado.status === 'autenticado') return <Navigate to="/" replace />

  // Aviso discreto quando o usuario chegou aqui por sessao perdida (onSessionLost), nao por
  // acesso normal. Comunica que foi rotina de autenticacao, nao erro dele.
  const sessaoExpirada = estado.status === 'anonimo' && estado.motivo === 'sessao-expirada'

  async function aoEnviar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      await login(nomeUsuario, senha)
    } catch {
      // Mensagem unica e generica: honra o nao-oraculo do backend (401 identico para todos os casos).
      setErro('Usuário ou senha inválidos.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center p-4">
      <form onSubmit={aoEnviar} className="w-full max-w-sm flex flex-col gap-4">
        <h1 className="text-2xl font-semibold text-center">Rastru</h1>

        {sessaoExpirada && (
          <p className="rounded bg-amber-100 text-amber-800 text-sm px-3 py-2 text-center">
            Sessão expirada. Entre novamente.
          </p>
        )}

        <label className="flex flex-col gap-1">
          <span className="text-sm">Usuário</span>
          <input
            className="border rounded px-3 py-2"
            value={nomeUsuario}
            onChange={(e) => setNomeUsuario(e.target.value)}
            required
            autoComplete="username"
          />
        </label>

        <label className="flex flex-col gap-1">
          <span className="text-sm">Senha</span>
          <input
            type="password"
            className="border rounded px-3 py-2"
            value={senha}
            onChange={(e) => setSenha(e.target.value)}
            required
            autoComplete="current-password"
          />
        </label>

        {erro && <p className="text-red-600 text-sm">{erro}</p>}

        <button
          type="submit"
          disabled={enviando}
          className="bg-gray-800 text-white rounded px-3 py-2 disabled:opacity-50"
        >
          {enviando ? 'Entrando…' : 'Entrar'}
        </button>
      </form>
    </div>
  )
}
