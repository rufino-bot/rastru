import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  listarPedidos, listarMateriaisDosPedidos, criarPedido, ehConflito,
  type PedidoDto, type MaterialResumoDto, type NovoPedido,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { useBuscaPaginada, type FiltroDeBusca, type PaginaDeBusca } from '../hooks/useBuscaPaginada'
import { useSelecaoNaUrl } from '../hooks/useSelecaoNaUrl'
import { LinhaDePedido } from '../pedidos/LinhaDePedido'
import { STATUS_DO_PEDIDO, rotuloDoStatus } from '../pedidos/statusDoPedido'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { EstadoVazio } from '../components/EstadoVazio'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { ControlesDePaginacao } from '../components/ControlesDePaginacao'
import { FiltroDeDemanda, type Faceta } from '../components/FiltroDeDemanda'

const FORMULARIO_VAZIO: NovoPedido = { numero: '', cliente: '' }

const CHAVES_DO_FILTRO = ['status', 'material'] as const

// O mesmo teto do `int` do servidor: acima dele o `int.TryParse` de lá falharia e viraria 400.
const MAIOR_ID = 2147483647

function ehStatusValido(valor: string): boolean {
  return STATUS_DO_PEDIDO.some((status) => status === valor)
}

function ehIdValido(valor: string): boolean {
  return /^\d+$/.test(valor) && Number(valor) >= 1 && Number(valor) <= MAIOR_ID
}

function paginaDaUrl(bruto: string | null): number {
  return bruto !== null && ehIdValido(bruto) ? Number(bruto) : 1
}

/**
 * Função de módulo, e não lambda: o hook a guarda num ref, mas a estável é mais clara. Adapta o
 * `FiltroDeBusca` do hook (que carrega os `filtros` genéricos) ao `FiltroDePedidos` da API.
 */
function buscarPedidos(f: FiltroDeBusca): Promise<PaginaDeBusca<PedidoDto>> {
  return listarPedidos({
    busca: f.busca,
    status: f.filtros?.status ?? [],
    material: f.filtros?.material ?? [],
    pagina: f.pagina,
    tamanho: f.tamanho,
  })
}

const FACETA_DE_STATUS: Faceta = {
  chave: 'status',
  titulo: 'Status',
  opcoes: STATUS_DO_PEDIDO.map((status) => ({ valor: status, rotulo: rotuloDoStatus(status) })),
}

export function PedidosPage() {
  const [form, setForm] = useState<NovoPedido>(FORMULARIO_VAZIO)
  const [erroDeEscrita, setErroDeEscrita] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [materiais, setMateriais] = useState<MaterialResumoDto[]>([])
  const [erroDeMateriais, setErroDeMateriais] = useState<string | null>(null)

  const podeEscrever = usePodeEscrever('pedidos')

  // A URL é a memória da tela (F5 e o "voltar" de um detalhe remontam a página): lida UMA vez, na
  // montagem, para o estado inicial do hook, e escrita a cada mudança. A seleção do filtro mora só
  // na URL; busca e página moram no hook e são COPIADAS para ela.
  const [params, setParams] = useSearchParams()
  const [inicial] = useState(() => ({
    busca: params.get('busca') ?? '',
    pagina: paginaDaUrl(params.get('pagina')),
  }))
  const { selecao, mudarSelecao, limpar } = useSelecaoNaUrl(CHAVES_DO_FILTRO)

  // Só o que o servidor aceita vai a ele: um `?material=abc` colado à mão viraria 400 na tela. O
  // valor inválido continua na seleção — o `FiltroDeDemanda` o mostra como opção ausente, marcada e
  // removível — e só deixa de ser enviado.
  const filtros = useMemo(() => ({
    status: (selecao.status ?? []).filter(ehStatusValido),
    material: (selecao.material ?? []).filter(ehIdValido),
  }), [selecao])

  const guardarConsultaNaUrl = useCallback(
    ({ busca, pagina }: { busca: string; pagina: number }) => {
      setParams(
        (anterior) => {
          const proxima = new URLSearchParams(anterior)
          if (busca) proxima.set('busca', busca)
          else proxima.delete('busca')
          if (pagina > 1) proxima.set('pagina', String(pagina))
          else proxima.delete('pagina')
          return proxima
        },
        { replace: true },
      )
    },
    [setParams],
  )

  const lista = useBuscaPaginada<PedidoDto>({
    buscar: buscarPedidos,
    inicial,
    filtros,
    aoMudarConsulta: guardarConsultaNaUrl,
  })

  // Os materiais em uso: carregados uma vez, e sem eles a lista funciona (a faceta fica vazia).
  useEffect(() => {
    let ativo = true
    listarMateriaisDosPedidos()
      .then((lidos) => { if (ativo) setMateriais(lidos) })
      .catch((e) => {
        if (ativo) setErroDeMateriais(mensagemDeErro(e, 'Não foi possível carregar os materiais do filtro.'))
      })
    return () => { ativo = false }
  }, [])

  const facetas = useMemo<Faceta[]>(() => [
    FACETA_DE_STATUS,
    {
      chave: 'material',
      titulo: 'Material',
      opcoes: materiais.map((m) => ({ valor: String(m.id), rotulo: m.descricao, detalhe: m.codigo })),
    },
  ], [materiais])

  // Dois erros, e não um: o de LEITURA vem do hook e é apagado pela recarga seguinte; o de ESCRITA
  // tem de sobreviver à recarga que o próprio `salvar` dispara.
  const erroDeLeitura = lista.erro === null
    ? null
    : mensagemDeErro(lista.erro, 'Não foi possível carregar os pedidos.')

  // "Filtrando" é o que foi de fato ENVIADO ao servidor (busca já debounced, filtros válidos): é
  // isso que explica uma lista vazia. Valor inválido da URL não é enviado e não conta.
  const filtrando = lista.busca.trim() !== '' || filtros.status.length + filtros.material.length > 0

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErroDeEscrita(null)
    setEnviando(true)
    try {
      const resultado = await criarPedido(form)
      if (ehConflito(resultado)) {
        // Pedido não tem reativação (não há coluna Ativo): o caminho é abrir o que já existe.
        setErroDeEscrita('Já existe um pedido com este número.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      // Recarrega a MESMA consulta: busca, filtros e página seguem como estavam.
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível salvar o pedido.'))
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

      <BannerDeErro mensagem={erroDeEscrita ?? erroDeLeitura} />
      <BannerDeErro mensagem={erroDeMateriais} />

      <Campo rotulo="Buscar por número, cliente ou código de peça">
        {(id) => (
          <input
            id={id}
            type="search"
            value={lista.textoDaBusca}
            onChange={(e) => lista.mudarBusca(e.target.value)}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>

      <FiltroDeDemanda facetas={facetas} selecao={selecao} aoMudar={mudarSelecao} />

      {lista.carregando ? (
        <EstadoCarregando />
      ) : erroDeLeitura === null && lista.total === 0 ? (
        // `erroDeLeitura === null` é o que distingue "não há pedidos" de "a listagem falhou": no
        // erro o hook mantém `total` em 0, então `total === 0` sozinho também seria verdade numa
        // falha de rede — mostrando este estado vazio JUNTO do banner de erro, afirmando "nenhum
        // pedido aberto" a partir de uma falha de conexão.
        //
        // E há dois vazios: "não achei" (busca ou filtro) e "não há nada" (cadastro).
        filtrando ? (
          <EstadoVazio
            titulo="Nenhum pedido com essa busca ou esses filtros"
            acao={(
              <Botao
                variante="secundario"
                onClick={() => {
                  lista.mudarBusca('')
                  limpar()
                }}
              >
                Limpar filtros
              </Botao>
            )}
          />
        ) : (
          <EstadoVazio
            titulo="Nenhum pedido aberto"
            descricao={podeEscrever ? 'Use o formulário acima para abrir o primeiro.' : undefined}
          />
        )
      ) : (
        <ListaDeCadastro>
          {lista.itens.map((p) => (
            <ItemDeCadastro key={p.id}>
              <LinhaDePedido pedido={p} />
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}

      <ControlesDePaginacao
        pagina={lista.pagina}
        totalDePaginas={lista.totalDePaginas}
        total={lista.total}
        aoMudarPagina={lista.irParaPagina}
      />
    </Pagina>
  )
}
