import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'

export function LoginPage() {
  const { estado, login } = useAuth()
  const [nomeUsuario, setNomeUsuario] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  if (estado.status === 'autenticado') return <Navigate to="/" replace />

  // Aviso discreto quando o usuário chegou aqui por sessão perdida (onSessionLost), não por acesso
  // normal. Comunica que foi rotina de autenticação, não erro dele.
  const sessaoExpirada = estado.status === 'anonimo' && estado.motivo === 'sessao-expirada'

  async function aoEnviar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      await login(nomeUsuario, senha)
    } catch {
      // Mensagem ÚNICA e genérica: honra o não-oráculo do backend, que responde o mesmo 401 para
      // usuário inexistente, conta trancada e senha errada. Variar a mensagem por caso aqui
      // desfaria no front a defesa que o backend paga BCrypt para manter.
      setErro('Usuário ou senha inválidos.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    // A única tela fora do shell, e por isso a única que ainda carrega `min-h-screen`.
    <div className="min-h-screen bg-chrome font-sans flex items-center justify-center p-4">
      <form
        onSubmit={aoEnviar}
        className="flex w-full max-w-sm flex-col gap-5 rounded-xl bg-superficie p-6 shadow-lg"
      >
        {/* A marca aparece sobre o chrome escuro do fundo, não dentro do cartão claro: o
            verde-água só tem contraste AA sobre o petróleo. */}
        <h1 className="text-center text-2xl font-semibold tracking-tight text-chrome">Rastru</h1>

        {sessaoExpirada && (
          // Âmbar seria um quinto matiz para manter coerente, e os tons claros dele reprovam AA
          // como texto (spec §3). Aviso de rotina fica no cinza-esverdeado dos neutros.
          <p className="rounded-lg border border-borda bg-fundo px-3 py-2 text-center text-sm text-tinta-fraca">
            Sessão expirada. Entre novamente.
          </p>
        )}

        <Campo rotulo="Usuário">
          {(id) => (
            <input
              id={id}
              value={nomeUsuario}
              onChange={(e) => setNomeUsuario(e.target.value)}
              required
              autoComplete="username"
              className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>

        <Campo rotulo="Senha">
          {(id) => (
            <input
              id={id}
              type="password"
              value={senha}
              onChange={(e) => setSenha(e.target.value)}
              required
              autoComplete="current-password"
              className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>

        <BannerDeErro mensagem={erro} />

        <Botao type="submit" carregando={enviando} rotuloCarregando="Entrando…">Entrar</Botao>
      </form>
    </div>
  )
}
