import { startTransition, useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  listarPedidos, listarMateriaisDosPedidos, criarPedido, ehConflito,
  type PedidoDto, type MaterialResumoDto, type NovoPedido, type OrdemDePedidos,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { useBuscaPaginada, type FiltroDeBusca, type PaginaDeBusca } from '../hooks/useBuscaPaginada'
import { useSelecaoNaUrl } from '../hooks/useSelecaoNaUrl'
import { LinhaDePedido } from '../pedidos/LinhaDePedido'
import { STATUS_DO_PEDIDO, rotuloDoStatus } from '../pedidos/statusDoPedido'
import { Pagina } from '../components/Pagina'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { SeletorDeOrdem, type OpcaoDeOrdem } from '../components/SeletorDeOrdem'
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

const OPCOES_DE_ORDEM: readonly OpcaoDeOrdem<OrdemDePedidos>[] = [
  { valor: 'recentes', rotulo: 'Mais recentes' },
  { valor: 'numero', rotulo: 'Número (A→Z)' },
  { valor: 'cliente', rotulo: 'Cliente (A→Z)' },
]

// O mesmo teto do `int` do servidor: acima dele o `int.TryParse` de lá falharia e viraria 400.
const MAIOR_ID = 2147483647

function ehStatusValido(valor: string): boolean {
  return STATUS_DO_PEDIDO.some((status) => status === valor)
}

function ehIdValido(valor: string): boolean {
  return /^\d+$/.test(valor) && Number(valor) >= 1 && Number(valor) <= MAIOR_ID
}

// Valor desconhecido na URL (um link velho, um `?ordem=lixo` colado à mão) vale a ordem padrão e não
// vai ao servidor: lá seria 400.
function ordemDaUrl(bruto: string | null): OrdemDePedidos {
  return OPCOES_DE_ORDEM.find((o) => o.valor === bruto)?.valor ?? 'recentes'
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
    ordem: f.filtros?.ordem?.[0] as OrdemDePedidos | undefined,
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
  const [painelAberto, setPainelAberto] = useState(false)
  const [erroDeEscrita, setErroDeEscrita] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [materiais, setMateriais] = useState<MaterialResumoDto[]>([])
  const [erroDeMateriais, setErroDeMateriais] = useState<string | null>(null)

  const podeEscrever = usePodeEscrever('pedidos')

  // A URL é a memória da tela (F5 e o "voltar" de um detalhe remontam a página): lida UMA vez, na
  // montagem, para o estado inicial do hook, e escrita a cada mudança. A seleção do filtro mora só
  // na URL, e a ordem também; busca e página moram no hook e são COPIADAS para ela.
  const [params, setParams] = useSearchParams()
  const [inicial] = useState(() => ({
    busca: params.get('busca') ?? '',
    pagina: paginaDaUrl(params.get('pagina')),
  }))
  const { selecao, mudarSelecao, limpar } = useSelecaoNaUrl(CHAVES_DO_FILTRO)
  const ordem = ordemDaUrl(params.get('ordem'))

  // Só o que o servidor aceita vai a ele: um `?material=abc` colado à mão viraria 400 na tela. O
  // valor inválido continua na seleção — o `FiltroDeDemanda` o mostra como opção ausente, marcada e
  // removível — e só deixa de ser enviado.
  const filtros = useMemo(() => ({
    status: (selecao.status ?? []).filter(ehStatusValido),
    material: (selecao.material ?? []).filter(ehIdValido),
    ordem: [ordem],
  }), [selecao, ordem])

  // O padrão não vai à URL: escolher "Mais recentes" apaga o parâmetro. Como as facetas, escreve com
  // `replace` — trocar a ordem não cria entrada de histórico.
  const mudarOrdem = useCallback(
    (nova: OrdemDePedidos) => {
      setParams(
        (anterior) => {
          const proxima = new URLSearchParams(anterior)
          if (nova === 'recentes') proxima.delete('ordem')
          else proxima.set('ordem', nova)
          return proxima
        },
        { replace: true },
      )
    },
    [setParams],
  )

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

  // O erro de LEITURA vem do hook e é apagado pela recarga seguinte; o de ESCRITA é estado da tela,
  // mostrado dentro do painel, e sobrevive a qualquer recarga.
  const erroDeLeitura = lista.erro === null
    ? null
    : mensagemDeErro(lista.erro, 'Não foi possível carregar os pedidos.')

  // "Filtrando" é o que foi de fato ENVIADO ao servidor (busca já debounced, filtros válidos): é
  // isso que explica uma lista vazia. Valor inválido da URL não é enviado e não conta.
  const filtrando = lista.busca.trim() !== '' || filtros.status.length + filtros.material.length > 0

  function abrirPainel() {
    setErroDeEscrita(null)
    setForm(FORMULARIO_VAZIO)
    setPainelAberto(true)
  }

  function fecharPainel() {
    setPainelAberto(false)
    setForm(FORMULARIO_VAZIO)
    setErroDeEscrita(null)
  }

  // Desfecho de sucesso: fecha o painel e devolve a consulta ao padrão — busca, página, facetas e
  // ordem —, para o pedido novo aparecer (decisão 7 da spec da 1F). O router aplica a mudança de URL
  // em `startTransition`, e a seleção e a ordem saem da URL; se o `voltarAoInicio` ficasse de fora,
  // o estado do hook (urgente) commitaria antes da URL: a lista buscaria duas vezes, e o efeito do
  // hook que copia a consulta para a URL partiria da URL ainda antiga e a reescreveria. Os dois
  // juntos na mesma transição commitam num render só: uma requisição, com a URL já zerada.
  function concluirComSucesso() {
    fecharPainel()
    startTransition(() => {
      setParams(new URLSearchParams(), { replace: true })
      lista.voltarAoInicio()
    })
  }

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
      concluirComSucesso()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível salvar o pedido.'))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Pagina
      titulo="Pedidos"
      acao={podeEscrever && !painelAberto && <Botao onClick={abrirPainel}>Novo pedido</Botao>}
    >
      {podeEscrever && painelAberto && (
        <PainelDeEscrita titulo="Novo pedido" aoEnviar={salvar} aoFechar={fecharPainel}>
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
          <BannerDeErro mensagem={erroDeEscrita} />
          <Botao type="submit" carregando={enviando} rotuloCarregando="Abrindo…" className="self-start">
            Abrir pedido
          </Botao>
        </PainelDeEscrita>
      )}

      <BannerDeErro mensagem={erroDeLeitura} />
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

      <SeletorDeOrdem opcoes={OPCOES_DE_ORDEM} valor={ordem} aoMudar={mudarOrdem} />

      {lista.carregando ? (
        <EstadoCarregando />
      ) : erroDeLeitura === null && lista.total === 0 ? (
        // `erroDeLeitura === null` é o que distingue "não há pedidos" de "a listagem falhou": o hook
        // só zera `total` na montagem, então numa falha da PRIMEIRA carga `total === 0` sozinho
        // também seria verdade, e mostraria este estado vazio JUNTO do banner de erro, afirmando
        // "nenhum pedido" a partir de uma falha de conexão. Depois de uma carga que deu certo, uma
        // recarga que falha mantém `total` e a lista da consulta ANTERIOR (o banner
        // avisa), e a guarda continua valendo nos dois casos.
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
            descricao={podeEscrever ? 'Use o botão Novo pedido para abrir o primeiro.' : undefined}
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
