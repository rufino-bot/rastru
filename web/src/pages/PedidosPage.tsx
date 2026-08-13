import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarPedidos, criarPedido, ehConflito, formatarDataHora,
  type PedidoDto, type NovoPedido,
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

const FORMULARIO_VAZIO: NovoPedido = { numero: '', cliente: '' }

/**
 * Status do `CK_Pedido_Status`. `Concluido` é o único que ganha tom positivo; `Cancelado`, o
 * negativo. Os intermediários ficam neutros — verde e vermelho são reservados a estado que exige
 * decisão, e "em produção" não exige nenhuma.
 */
function tomDoStatus(status: string): 'neutro' | 'positivo' | 'negativo' {
  if (status === 'Concluido') return 'positivo'
  if (status === 'Cancelado') return 'negativo'
  return 'neutro'
}

export function PedidosPage() {
  const [pedidos, setPedidos] = useState<PedidoDto[]>([])
  const [form, setForm] = useState<NovoPedido>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('pedidos')

  async function carregar() {
    setCarregando(true)
    try {
      setPedidos(await listarPedidos())
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os pedidos.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      const resultado = await criarPedido(form)
      if (ehConflito(resultado)) {
        // Pedido não tem reativação (não há coluna Ativo): o caminho é abrir o que já existe.
        setErro('Já existe um pedido com este número.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar()
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o pedido.'))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Pagina titulo="Pedidos">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código do pedido">
              {(id) => (
                <input
                  id={id}
                  value={form.numero}
                  onChange={(e) => setForm({ ...form, numero: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Cliente">
              {(id) => (
                <input
                  id={id}
                  value={form.cliente}
                  onChange={(e) => setForm({ ...form, cliente: e.target.value })}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Abrindo…" className="self-start">
            Abrir pedido
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : erro === null && pedidos.length === 0 ? (
        // `erro === null` é o que distingue "não há pedidos" de "a listagem falhou": no `catch`
        // de `carregar`, `setPedidos` nunca é chamado, então a lista fica `[]` e `.length === 0`
        // sozinho também seria verdade numa falha de rede — mostrando este estado vazio JUNTO do
        // banner de erro, afirmando "nenhum pedido aberto" a partir de uma falha de conexão.
        <EstadoVazio
          titulo="Nenhum pedido aberto"
          descricao={podeEscrever ? 'Use o formulário acima para abrir o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {pedidos.map((p) => (
            <ItemDeCadastro key={p.id}>
              {/*
                O item inteiro é o alvo do clique, e não só o número: numa tela de bancada com
                tablet, alvo pequeno erra. `after:absolute after:inset-0` estende a área clicável do
                link ao cartão sem aninhar elementos interativos.
              */}
              <Link
                to={`/pedidos/${p.id}`}
                className="flex flex-col gap-1 after:absolute after:inset-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
              >
                <span className="font-medium">
                  <span className="font-mono">{p.numero}</span> — {p.cliente}
                </span>
                <span className="flex items-center gap-2 text-sm text-tinta-fraca">
                  <Pilula tom={tomDoStatus(p.status)}>{p.status}</Pilula>
                  aberto em {formatarDataHora(p.dataAbertura)}
                </span>
              </Link>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
