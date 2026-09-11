import { useEffect, useState, type FormEvent } from 'react'
import { useParams, Link } from 'react-router-dom'
import {
  obterPedido, listarAgrupamentos, criarAgrupamento, excluirAgrupamento, ehConflito,
  formatarDataHora, type PedidoDto, type AgrupamentoDto, type NovoAgrupamento,
  type ResultadoExclusao,
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
import { EstadoCarregando } from '../components/EstadoCarregando'

const FORMULARIO_VAZIO: NovoAgrupamento = { codigo: '', tipo: 'Kit' }

// Tipado contra a união, e não `Record<string, string>`: com o tipo frouxo, renomear ou perder uma
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
  const [enviando, setEnviando] = useState(false)
  const [pendenteExclusao, setPendenteExclusao] = useState<AgrupamentoDto | null>(null)

  const podeEscrever = usePodeEscrever('agrupamentos')

  // Recebe o id como argumento (em vez de fechar sobre `pedidoId` de fora) porque a dependência do
  // useEffect precisa aparecer usada dentro do corpo do callback, senão o exhaustive-deps acusa
  // 'carregar' como dependência faltando.
  async function carregar(id: number) {
    setCarregando(true)
    try {
      // Duas chamadas de propósito: o Pedido e o sub-recurso de Agrupamentos são rotas separadas.
      const [p, a] = await Promise.all([obterPedido(id), listarAgrupamentos(id)])
      setPedido(p)
      setAgrupamentos(a)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar o pedido.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(pedidoId) }, [pedidoId])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      const resultado = await criarAgrupamento(pedidoId, form)
      if (ehConflito(resultado)) {
        setErro('Já existe um agrupamento com este código neste pedido.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar(pedidoId)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o agrupamento.'))
    } finally {
      setEnviando(false)
    }
  }

  // `excluirAgrupamento` só lança para status fora de 204/404/409 — os dois 409 e o 404 chegam como
  // retorno normal, tratados pelo MOTIVO_DA_RECUSA.
  async function excluir(agrupamentoId: number) {
    setErro(null)
    try {
      const desfecho = await excluirAgrupamento(agrupamentoId)
      if (desfecho !== 'ok') setErro(MOTIVO_DA_RECUSA[desfecho])
      await carregar(pedidoId)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível excluir o agrupamento.'))
    }
  }

  function confirmarExclusao() {
    if (!pendenteExclusao) return
    const agrupamentoId = pendenteExclusao.id
    setPendenteExclusao(null)
    excluir(agrupamentoId)
  }

  // SEM early return de página inteira, de propósito — ele existia e causava DOIS defeitos:
  // demolia a tela a cada ação (o que se sente como lentidão) e escondia a mensagem de recusa da
  // exclusão atrás do "Carregando…". O estado de carregamento fica ESCOPADO à lista.
  return (
    <Pagina titulo={pedido ? pedido.numero : 'Pedido'}>
      {pedido && (
        <div className="flex flex-col gap-2 rounded-lg border border-borda bg-superficie p-4">
          <p className="text-lg text-tinta">{pedido.cliente}</p>
          <p className="flex flex-wrap items-center gap-2 text-sm text-tinta-fraca">
            <Pilula>{pedido.tipo}</Pilula>
            <Pilula>{pedido.status}</Pilula>
            aberto em {formatarDataHora(pedido.dataAbertura)}
          </p>
        </div>
      )}

      <h2 className="text-lg font-medium text-tinta">Agrupamentos</h2>

      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código do agrupamento">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Tipo">
              {(id) => (
                <select
                  id={id}
                  value={form.tipo}
                  onChange={(e) => setForm({ ...form, tipo: e.target.value as NovoAgrupamento['tipo'] })}
                  className={CLASSES_DE_CONTROLE}
                >
                  <option value="Kit">Kit</option>
                  <option value="Avulso">Avulso</option>
                </select>
              )}
            </Campo>
          </div>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {carregando ? (
        <EstadoCarregando />
      ) : erro === null && agrupamentos.length === 0 ? (
        // `erro === null` distingue "não há agrupamentos" de "a listagem falhou": no `catch` de
        // `carregar`, `setAgrupamentos` nunca é chamado, então a lista fica `[]` e `.length === 0`
        // sozinho também seria verdade numa falha de rede — mostrando este estado vazio JUNTO do
        // banner de erro, convidando a criar o primeiro agrupamento a partir de uma falha de
        // conexão. É a mesma forma do Critical que o fix pass da Task 8 pagou (achado C1).
        <EstadoVazio
          titulo="Nenhum agrupamento neste pedido"
          descricao={podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro rotulo="Agrupamentos">
          {agrupamentos.map((a) => (
            <ItemDeCadastro
              key={a.id}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => setPendenteExclusao(a)}>Excluir</Botao>
              )}
            >
              {/*
                Task 8 (Fase 2): o item vira link para a árvore de estrutura do Agrupamento.
                DELIBERADAMENTE não usa o padrão de `LinhaDePedido` (`after:absolute after:inset-0`
                cobrindo o `<li>` inteiro): este item já tem uma `acao` (o botão "Excluir" acima),
                e `ListaDeCadastro.tsx` (onde `ItemDeCadastro` é definido) documenta essa
                combinação como uma armadilha MEDIDA em Chrome (m6 da review da Task 8 da Fase 1D)
                — o overlay de link, sobre o `<li>` inteiro, cobre e ENGOLE o clique no botão de
                ação, e `jsdom` (esta suíte) não calcula layout, então não pega isso: ficaria verde
                e quebraria só no navegador. O link aqui fica restrito ao próprio texto do código —
                menor como alvo de toque, mas sem colidir com "Excluir". Extrair o conserto
                documentado (a `acao` num wrapper de `z-index` positivo) fica para quando alguém
                precisar da área inteira clicável aqui, com verificação em navegador de verdade,
                não só na suíte.
              */}
              <Link
                to={`/agrupamentos/${a.id}`}
                className="font-mono font-semibold rounded hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
              >
                {a.codigo}
              </Link>{' '}
              <Pilula>{a.tipo}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}

      {pendenteExclusao && (
        <div className="fixed inset-0 z-10 flex items-center justify-center bg-tinta/50 p-4">
          <div
            role="dialog"
            aria-modal="true"
            className="flex w-full max-w-sm flex-col gap-4 rounded-lg bg-superficie p-5 shadow-lg"
          >
            <p className="text-tinta">
              Excluir o agrupamento <strong className="font-mono">{pendenteExclusao.codigo}</strong>?
              Esta ação não pode ser desfeita.
            </p>
            {/*
              Ordem e peso visual são deliberados, não estética. Esta é a única exclusão física do
              sistema, e até a Fase 1A os dois botões tinham a MESMA classe — um modal de confirmação
              com dois botões idênticos troca a pausa deliberada por um sorteio. "Excluir" em
              vermelho (convenção de ação destrutiva) e "Cancelar" à DIREITA, onde cai o polegar num
              tablet. NÃO trocar a ordem nem igualar os pesos.
            */}
            <div className="flex flex-wrap justify-end gap-2">
              <Botao variante="perigo" onClick={confirmarExclusao}>Excluir</Botao>
              {/*
                autoFocus: mesma intenção, aplicada ao teclado. No DOM "Excluir" vem antes de
                "Cancelar" (ordem visual decidida, não mexer) — sem foco explícito, quem navega por
                teclado sem ler tabularia direto para o botão destrutivo.
                NÃO adicionar Esc / clique-fora / focus-trap: fora de escopo, como já estava.
              */}
              <Botao variante="secundario" onClick={() => setPendenteExclusao(null)} autoFocus>
                Cancelar
              </Botao>
            </div>
          </div>
        </div>
      )}
    </Pagina>
  )
}
