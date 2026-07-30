import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarPedidos, criarPedido, ehConflito, formatarDataHora,
  type PedidoDto, type NovoPedido,
} from '../api/cadastros'

const FORMULARIO_VAZIO: NovoPedido = { numero: '', cliente: '' }

export function PedidosPage() {
  const [pedidos, setPedidos] = useState<PedidoDto[]>([])
  const [form, setForm] = useState<NovoPedido>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar() {
    setCarregando(true)
    try {
      setPedidos(await listarPedidos())
    } catch {
      setErro('Não foi possível carregar os pedidos.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  // POST /pedidos e [Authorize(Roles = "PCP,Administrador")] e o link aparece para todos os
  // perfis: o try/catch e a fronteira real de perfil — sem ele um Operador/Qualidade clicando
  // "Abrir pedido" tomaria 403 e a tela nao diria nada.
  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    try {
      const resultado = await criarPedido(form)
      if (ehConflito(resultado)) {
        // Pedido nao tem reativacao (nao ha coluna Ativo): o caminho e abrir o que ja existe.
        setErro('Já existe um pedido com este número.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar()
    } catch {
      setErro('Não foi possível salvar o pedido.')
    }
  }

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/" className="text-sm text-gray-500">&larr; Início</Link>
      <h1 className="text-2xl font-semibold">Pedidos</h1>

      <form onSubmit={salvar} className="flex flex-col gap-2">
        <input
          value={form.numero}
          onChange={(e) => setForm({ ...form, numero: e.target.value })}
          placeholder="Número do pedido"
          required
          className="border rounded px-3 py-2"
        />
        <input
          value={form.cliente}
          onChange={(e) => setForm({ ...form, cliente: e.target.value })}
          placeholder="Cliente"
          required
          className="border rounded px-3 py-2"
        />
        <button type="submit" className="border rounded px-3 py-2 self-start">Abrir pedido</button>
      </form>

      {erro && <p className="text-red-600 text-sm">{erro}</p>}

      {carregando ? <p className="text-gray-600">Carregando…</p> : (
        <ul className="flex flex-col gap-2">
          {pedidos.map((p) => (
            <li key={p.id} className="border rounded px-3 py-2">
              <Link to={`/pedidos/${p.id}`} className="flex flex-col gap-1">
                <span className="font-medium">{p.numero} — {p.cliente}</span>
                <span className="text-sm text-gray-500">
                  {p.status} · aberto em {formatarDataHora(p.dataAbertura)}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
