import { useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  obterPedido, listarAgrupamentos, criarAgrupamento, excluirAgrupamento, ehConflito,
  formatarDataHora, type PedidoDto, type AgrupamentoDto, type NovoAgrupamento,
  type ResultadoExclusao,
} from '../api/cadastros'

const FORMULARIO_VAZIO: NovoAgrupamento = { codigo: '', tipo: 'Kit' }

// Tipado contra a uniao, e nao `Record<string, string>`: com o tipo frouxo, renomear ou perder uma
// chave compila, passa os testes e passa o lint — e em runtime `MOTIVO_DA_RECUSA[desfecho]` vira
// undefined e a tela fica MUDA no caso que mais acontece. O tipo forte faz o tsc cobrar o mapa.
const MOTIVO_DA_RECUSA: Record<Exclude<ResultadoExclusao, 'ok'>, string> = {
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

  // Sem early return de pagina inteira aqui, de proposito — ele existia e causava DOIS defeitos.
  // `carregar()` roda apos CADA cadastro e exclusao, entao um `if (carregando) return <p>…</p>`
  // demolia e remontava a tela toda a cada acao: e isso que se sente como "lentidao", nao a rede
  // (o hop do proxy do Vite foi medido em ~5-15ms). E, pior, o early return ficava ANTES do bloco
  // `{erro && …}`, entao a mensagem de recusa da exclusao era escrita e imediatamente escondida
  // atras do "Carregando…" — o "erro que pisca" que a review da Task 11 levantou.
  // O estado de carregamento fica ESCOPADO a lista, como em SetoresPage:107 e PedidosPage:74.
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

      {carregando ? <p className="text-gray-600">Carregando…</p> : (
        <ul className="flex flex-col gap-2">
          {agrupamentos.map((a) => (
            <li key={a.id} className="flex items-center justify-between border rounded px-3 py-2">
              <span>
                <strong>{a.codigo}</strong> ({a.tipo})
              </span>
              <button onClick={() => setPendenteExclusao(a)} className="text-sm border rounded px-2 py-1">
                Excluir
              </button>
            </li>
          ))}
        </ul>
      )}

      {pendenteExclusao && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center p-4">
          <div role="dialog" aria-modal="true" className="bg-white rounded p-4 flex flex-col gap-3 max-w-sm w-full">
            <p>
              Excluir o agrupamento <strong>{pendenteExclusao.codigo}</strong>? Esta ação não pode
              ser desfeita.
            </p>
            {/*
              Ordem e peso visual sao deliberados, nao estetica. Esta e a unica exclusao fisica do
              sistema, e ate aqui os dois botoes tinham a MESMA classe — um modal de confirmacao com
              dois botoes identicos troca a pausa deliberada por um sorteio. Excluir vem em vermelho
              solido (convencao de acao destrutiva) e Cancelar fica a DIREITA, onde cai o polegar num
              tablet, e com mais peso: quem clicar sem ler tem que acertar o caminho seguro.
            */}
            <div className="flex justify-end gap-2">
              <button
                onClick={confirmarExclusao}
                className="bg-red-600 text-white rounded px-3 py-2 hover:bg-red-700"
              >
                Excluir
              </button>
              {/*
                autoFocus: mesma intencao do comentario acima, aplicada ao teclado. No DOM
                "Excluir" vem antes de "Cancelar" (ordem visual decidida, nao mexer) — sem foco
                explicito, quem navega por teclado sem ler tabularia direto para o botao
                destrutivo. NAO adicionar Esc / clique-fora / focus-trap: fora de escopo.
              */}
              <button
                onClick={() => setPendenteExclusao(null)}
                className="border-2 border-gray-800 rounded px-3 py-2 font-medium"
                autoFocus
              >
                Cancelar
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
