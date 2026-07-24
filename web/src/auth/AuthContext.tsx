import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import * as client from '../api/client'
import { estadoDaSessao, type EstadoSessao } from './estadoDaSessao'

interface ContextoAuth {
  estado: EstadoSessao
  login: (nomeUsuario: string, senha: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<ContextoAuth | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [estado, setEstado] = useState<EstadoSessao>({ status: 'carregando' })
  const tokenRef = useRef<string | null>(null)
  const jaIniciou = useRef(false)

  useEffect(() => {
    // Guarda contra o double-mount do StrictMode em dev: sem isso, o init-refresh dispararia
    // DOIS /auth/refresh no boot; como o backend rotaciona o token, o segundo chegaria com o
    // token ja revogado -> 401 -> a sessao morreria logo ao abrir. Roda exatamente uma vez.
    if (jaIniciou.current) return
    jaIniciou.current = true

    client.inicializar({
      getToken: () => tokenRef.current,
      setToken: (t) => { tokenRef.current = t },
      // Refresh falhou no meio do uso: volta pro login SINALIZANDO que expirou (rotina normal).
      onSessionLost: () => setEstado({ status: 'anonimo', motivo: 'sessao-expirada' }),
    })

    client.tentarRestaurarSessao().then((usuario) => setEstado(estadoDaSessao(usuario)))
  }, [])

  async function login(nomeUsuario: string, senha: string) {
    const usuario = await client.login(nomeUsuario, senha) // lanca ErroDeLogin em 401
    setEstado({ status: 'autenticado', usuario })
  }

  async function logout() {
    await client.logout()
    setEstado({ status: 'anonimo' }) // logout voluntario: sem motivo, nao mostra aviso de expirada
  }

  return <AuthContext.Provider value={{ estado, login, logout }}>{children}</AuthContext.Provider>
}

export function useAuth(): ContextoAuth {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth precisa estar dentro de <AuthProvider>')
  return ctx
}
