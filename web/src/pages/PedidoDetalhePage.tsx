import { useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  obterPedido, listarAgrupamentos, criarAgrupamento, excluirAgrupamento, ehConflito,
  formatarDataHora, type PedidoDto, type AgrupamentoDto, type NovoAgrupamento,
} from '../api/cadastros'

const FORMULARIO_VAZIO: NovoAgrupamento = { codigo: '', quantidade: 1, tipo: 'Kit' }

const MOTIVO_DA_RECUSA: Record<string, string> = {
  AgrupamentoNaoVazio: 'Este agrupamento já tem estrutura e não pode mais ser excluído.',
  PedidoNaoAberto: 'O pedido não está mais aberto: não dá para excluir agrupamentos dele.',
  NaoEncontrado: 'Este agrupamento já não existe mais.',
}

export function PedidoDetalhePage() {
  const { id } = useParams<{ id: string }>()
  const pedidoId = Number(id)

  const [pedido, setPedido] = useState<PedidoDto | null>(null)
  const [agrupamentos, setAgrupamentos] = useState<AgrupamentoDto[]>([])
  const [form, setForm] = useState<NovoAgrupamento>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [pendenteExclusao, setPendenteExclusao] = useState<AgrupamentoDto | null>(null)

  // Recebe o id como argumento (em vez de fechar sobre `pedidoId` de fora) para casar com o
  // padrao de SetoresPage/MateriaisPage: a dependencia do useEffect precisa aparecer usada
  // dentro do corpo do callback, senao o react-hooks(exhaustive-deps) acusa 'carregar' como
  // dependencia faltando.
  async function carregar(id: number) {
    setCarregando(true)
    try {
      // Duas chamadas de proposito: o Pedido e o sub-recurso de Agrupamentos sao rotas separadas.
      const [p, a] = await Promise.all([obterPedido(id), listarAgrupamentos(id)])
      setPedido(p)
      setAgrupamentos(a)
    } catch {
      setErro('Não foi possível carregar o pedido.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(pedidoId) }, [pedidoId])

  // POST /pedidos/{pedidoId}/agrupamentos e [Authorize(Roles = "PCP,Administrador")] e o link
  // aparece para todos os perfis: o try/catch e a fronteira real de perfil — sem ele um
  // Operador/Qualidade clicando "Adicionar" tomaria 403 e a tela nao diria nada.
  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    try {
      const resultado = await criarAgrupamento(pedidoId, form)
      if (ehConflito(resultado)) {
        setErro('Já existe um agrupamento com este código neste pedido.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar(pedidoId)
    } catch {
      setErro('Não foi possível salvar o agrupamento.')
    }
  }

  // DELETE /agrupamentos/{id} e [Authorize(Roles = "PCP,Administrador")]: mesma fronteira do
  // salvar() acima. excluirAgrupamento so lanca para status fora de 204/404/409 — os dois 409 e
  // o 404 chegam como retorno normal, tratados pelo MOTIVO_DA_RECUSA.
  async function excluir(agrupamentoId: number) {
    setErro(null)
    try {
      const desfecho = await excluirAgrupamento(agrupamentoId)
      if (desfecho !== 'ok') setErro(MOTIVO_DA_RECUSA[desfecho])
      await carregar(pedidoId)
    } catch {
      setErro('Não foi possível excluir o agrupamento.')
    }
  }

  function confirmarExclusao() {
    if (!pendenteExclusao) return
    const agrupamentoId = pendenteExclusao.id
    setPendenteExclusao(null)
    excluir(agrupamentoId)
  }

  if (carregando) return <p className="p-6 text-gray-600">Carregando…</p>

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/pedidos" className="text-sm text-gray-500">&larr; Pedidos</Link>

      {pedido && (
        <header className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold">{pedido.numero}</h1>
          <p className="text-gray-600">{pedido.cliente}</p>
          <p className="text-sm text-gray-500">
            {pedido.tipo} · {pedido.status} · aberto em {formatarDataHora(pedido.dataAbertura)}
          </p>
        </header>
      )}

      <h2 className="text-lg font-medium mt-2">Agrupamentos</h2>

      <form onSubmit={salvar} className="flex flex-col gap-2">
        <input
          value={form.codigo}
          onChange={(e) => setForm({ ...form, codigo: e.target.value })}
          placeholder="Código do agrupamento"
          required
          className="border rounded px-3 py-2"
        />
        <input
          type="number"
          min="0.0001"
          step="0.0001"
          value={form.quantidade}
          onChange={(e) => setForm({ ...form, quantidade: Number(e.target.value) })}
          placeholder="Quantidade"
          required
          className="border rounded px-3 py-2"
        />
        <select
          value={form.tipo}
          onChange={(e) => setForm({ ...form, tipo: e.target.value as NovoAgrupamento['tipo'] })}
          className="border rounded px-3 py-2"
        >
          <option value="Kit">Kit</option>
          <option value="Avulso">Avulso</option>
        </select>
        <button type="submit" className="border rounded px-3 py-2 self-start">Adicionar</button>
      </form>

      {erro && <p className="text-red-600 text-sm">{erro}</p>}

      <ul className="flex flex-col gap-2">
        {agrupamentos.map((a) => (
          <li key={a.id} className="flex items-center justify-between border rounded px-3 py-2">
            <span>
              <strong>{a.codigo}</strong> — {a.quantidade} ({a.tipo})
            </span>
            <button onClick={() => setPendenteExclusao(a)} className="text-sm border rounded px-2 py-1">
              Excluir
            </button>
          </li>
        ))}
      </ul>

      {pendenteExclusao && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center p-4">
          <div role="dialog" aria-modal="true" className="bg-white rounded p-4 flex flex-col gap-3 max-w-sm w-full">
            <p>
              Excluir o agrupamento <strong>{pendenteExclusao.codigo}</strong>? Esta ação não pode
              ser desfeita.
            </p>
            <div className="flex justify-end gap-2">
              <button onClick={() => setPendenteExclusao(null)} className="border rounded px-3 py-2">
                Cancelar
              </button>
              <button onClick={confirmarExclusao} className="border rounded px-3 py-2">
                Excluir
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
