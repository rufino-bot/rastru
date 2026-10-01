import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
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
import { FiltroDeDemanda, type Faceta, type Selecao } from '../components/FiltroDeDemanda'

const FORMULARIO_VAZIO: NovoPedido = { numero: '', cliente: '' }

const CHAVES_DO_FILTRO = ['status', 'material'] as const

const SELECAO_VAZIA: Selecao = {}

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
  const { selecao: selecaoDaUrl, mudarSelecao, limpar } = useSelecaoNaUrl(CHAVES_DO_FILTRO)

  // Salvar zera a URL, mas o router a aplica em prioridade baixa (`startTransition`), enquanto o
  // estado do hook é urgente. Sem esta guarda, a lista buscaria duas vezes: uma com a busca já zerada
  // e as facetas ainda velhas, e outra quando a URL chegasse. Enquanto `urlAZerar` vale, a tela lê
  // o padrão em vez da URL; quando a URL zerada chega, a guarda cai, e o valor lido (o mesmo) não
  // dispara busca nenhuma — os `filtros` do hook são comparados por valor.
  const [urlAZerar, setUrlAZerar] = useState(false)
  const textoDosParams = params.toString()
  useEffect(() => {
    if (urlAZerar && textoDosParams === '') setUrlAZerar(false)
  }, [urlAZerar, textoDosParams])
  const selecao = urlAZerar ? SELECAO_VAZIA : selecaoDaUrl
  const ordem = urlAZerar ? 'recentes' : ordemDaUrl(params.get('ordem'))

  // Só o que o servidor aceita vai a ele: um `?material=abc` colado à mão viraria 400 na tela. O
  // valor inválido continua na seleção — o `FiltroDeDemanda` o mostra como opção ausente, marcada e
  // removível — e só deixa de ser enviado.
  const filtros = useMemo(() => ({
    status: (selecao.status ?? []).filter(ehStatusValido),
    material: (selecao.material ?? []).filter(ehIdValido),
    ordem: [ordem],
  }), [selecao, ordem])

  // Toda escrita da página na URL passa por aqui. O `setParams` parte dos parâmetros do render em
  // que foi criado, e o router aplica a navegação em prioridade baixa (`startTransition`) enquanto
  // o estado do hook é urgente: salvar zera a URL e o hook, e o efeito do hook roda ANTES de a URL
  // nova chegar — partindo dos parâmetros antigos, ele os escreveria de volta. `escritaPendente`
  // guarda a última URL pedida, e a escrita seguinte parte dela enquanto a anterior não chegou.
  const escritaPendente = useRef<string | null>(null)
  useEffect(() => { escritaPendente.current = null }, [textoDosParams])

  const escreverNaUrl = useCallback(
    (mudar: (base: URLSearchParams) => URLSearchParams) => {
      setParams(
        (anterior) => {
          const proxima = mudar(new URLSearchParams(escritaPendente.current ?? anterior))
          escritaPendente.current = proxima.toString()
          return proxima
        },
        { replace: true },
      )
    },
    [setParams],
  )

  // O padrão não vai à URL: escolher "Mais recentes" apaga o parâmetro. Como as facetas, escreve com
  // `replace` — trocar a ordem não cria entrada de histórico.
  const mudarOrdem = useCallback(
    (nova: OrdemDePedidos) => {
      escreverNaUrl((proxima) => {
        if (nova === 'recentes') proxima.delete('ordem')
        else proxima.set('ordem', nova)
        return proxima
      })
    },
    [escreverNaUrl],
  )

  const guardarConsultaNaUrl = useCallback(
    ({ busca, pagina }: { busca: string; pagina: number }) => {
      escreverNaUrl((proxima) => {
        if (busca) proxima.set('busca', busca)
        else proxima.delete('busca')
        if (pagina > 1) proxima.set('pagina', String(pagina))
        else proxima.delete('pagina')
        return proxima
      })
    },
    [escreverNaUrl],
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
  // ordem —, para o pedido novo aparecer (decisão 7 da spec da 1F). A URL é limpa por inteiro; no
  // mesmo handler, os `filtros` voltam ao padrão e o `voltarAoInicio` zera o resto, e os dois viram
  // UMA requisição, então não há `recarregar` junto.
  function concluirComSucesso() {
    fecharPainel()
    setUrlAZerar(true)
    escreverNaUrl(() => new URLSearchParams())
    lista.voltarAoInicio()
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
