import { apiFetch } from './client'
import { ErroDeApi } from './erros'
import { avisarQueOLivroMudou } from './sinalDoLivro'
import type { MaterialResumoDto, PausaResumoDto } from './cadastros'

/**
 * Cliente da execução (Fase 3). Os tipos espelham a seção "Contrato JSON" do plano 2 da Fase 3
 * (`docs/superpowers/plans/2026-09-25-fase-3-backend.md`) — é ela a fonte, não este arquivo: campo
 * novo nasce lá primeiro.
 *
 * Diferente de `estrutura.ts`, nenhuma função aqui devolve o conflito como valor: TODA recusa vira
 * `ErroDeApi` lançado, com `codigo` (o campo `erro` do corpo) e `detalhe` (o campo `mensagem`). As
 * telas da execução tratam todo erro do mesmo jeito — mostrar `mensagemDeErro` e, no 409, recarregar
 * (spec §8.3) — então uma união por função não compraria nada.
 */

export type Posicao =
  | 'AIniciar'
  | 'NoSetor'
  | 'AguardandoColeta'
  | 'AguardandoMontagem'
  | 'NaExpedicao'
  | 'Montado'

export type TipoDeMovimentacao = 'Inicio' | 'Termino' | 'Entrega' | 'Montagem' | 'Estorno'

/** `setorId`, `setorNome` e `ordem` são `null` quando a posição não os tem (spec §3.1). */
export interface LocalDto {
  posicao: Posicao
  setorId: number | null
  setorNome: string | null
  ordem: number | null
}

export interface MovimentacaoDto {
  id: number
  estruturaItemId: number
  tipo: TipoDeMovimentacao
  quantidade: number
  origem: LocalDto
  destino: LocalDto
  /** Preenchido na baixa de filho de uma montagem (e no estorno dela). */
  montagemId: number | null
  /** Só no `Estorno`: o movimento que ele desfaz. */
  estornoDeId: number | null
  /** ISO 8601 com offset -03:00 — mostrar com `formatarDataHora`, nunca com `Date`. */
  dataHora: string
  usuarioId: number
  usuarioNome: string
  estornada: boolean
}

export interface MontagemDto {
  id: number
  estruturaItemId: number
  setorId: number
  setorNome: string
  quantidade: number
  dataHora: string
  usuarioId: number
  usuarioNome: string
  estornada: boolean
  /** Uma baixa (`MovimentacaoDto` de tipo `Montagem`) por filho direto. */
  baixas: MovimentacaoDto[]
}

/** `descricao` já com o fallback do Componente (regra 19). Na Peça, `paiId`/`paiDescricao` são `null`. */
export interface NoResumoDto {
  id: number
  descricao: string
  codigoDoComponente: string | null
  pedidoId: number
  pedidoNumero: string
  pedidoCliente: string
  agrupamentoId: number
  agrupamentoCodigo: string
  paiId: number | null
  paiDescricao: string | null
  /** Os Materiais do PRÓPRIO nó (não os do catálogo do Componente), por código; `[]` quando não há. */
  materiais: MaterialResumoDto[]
  /** A pausa aberta do Pedido do nó; `null` quando o Pedido não está pausado. */
  pausa: PausaResumoDto | null
  /** O tipo do Agrupamento do nó; `Kit` leva a pílula da Fila e o redirecionamento por conjuntos. */
  agrupamentoTipo: 'Kit' | 'Avulso'
}

export interface SetorResumidoDto {
  id: number
  nome: string
}

export type TipoDeDestino = 'ProximoPasso' | 'Expedicao' | 'Montagem'

/**
 * `ProximoPasso` traz `setorId`/`setorNome`/`ordem`; `Expedicao` não traz nada; `Montagem` traz
 * `paiId` e o Setor do PRIMEIRO passo do pai em `setorId`/`setorNome` (sem `ordem`), porque é lá que o
 * pai começa consumindo os filhos (spec da Fase 3D, §2.2). `paiSemRoteiro` só é `true` em `Montagem`,
 * e aí `setorId` é `null`.
 */
export interface DestinoDto {
  tipo: TipoDeDestino
  setorId: number | null
  setorNome: string | null
  ordem: number | null
  paiId: number | null
  paiSemRoteiro: boolean
}

/**
 * Um registro por trás de uma linha da fila que ainda dá para estornar (spec da Fase 3D, §2.4). O
 * servidor já filtrou: não estornado, cabe no saldo da posição, e quem lê pode estorná-lo.
 * `Montagem` é o início de um pai, que consumiu os filhos: estorna-se pela rota da montagem.
 * A quantidade do registro NÃO é limitada pela da linha (desvio D4 do plano da Fase 3D): um
 * Término maior que a tarefa aparece na linha da tarefa.
 */
export interface Estornavel {
  tipo: 'Inicio' | 'Termino' | 'Montagem'
  id: number
  quantidade: number
  usuarioId: number
  usuarioNome: string
  /** ISO 8601 com offset -03:00 — mostrar com `formatarDataHora`. */
  dataHora: string
}

export interface LinhaDaFila {
  no: NoResumoDto
  ordem: number
  quantidade: number
  estornaveis: Estornavel[]
}

export interface LinhaAguardandoColeta extends LinhaDaFila {
  destino: DestinoDto
}

export interface FilhoNaMontagem {
  no: NoResumoDto
  quantidadePorPai: number
  presente: number
  /** `null` quando não há próxima unidade a montar (`daParaMontar + 1 > faltaMontar`). */
  necessarioParaProxima: number | null
  faltaParaProxima: number | null
}

export interface GrupoAguardandoMontagem {
  pai: NoResumoDto
  faltaMontar: number
  daParaMontar: number
  /** Este Setor é o primeiro passo do pai: é aqui que "Iniciar" o pai consome os filhos. */
  iniciaAqui: boolean
  /** Para onde levar os filhos quando não é aqui; `null` se o pai não tem Roteiro. */
  primeiroPassoDoPai: SetorResumidoDto | null
  /** O pai recebe os filhos só em conjunto completo (regra 25): a Fila leva o Kit inteiro, não filho a filho. */
  conjuntoCompleto: boolean
  /** TODOS os filhos diretos do pai, inclusive os ausentes deste Setor (`presente` 0). */
  filhos: FilhoNaMontagem[]
}

export interface LinhaDeSobra {
  no: NoResumoDto
  origem: 'UltimoPasso' | 'Montagem'
  /** Só em `UltimoPasso`. */
  ordem: number | null
  quantidade: number
  /** Só em `Montagem`: o filho aguarda montagem em mais de um Setor. */
  emMaisDeUmSetor: boolean
  estornaveis: Estornavel[]
}

export interface FilaDoSetorDto {
  setorId: number
  setorNome: string
  /** Substantivo que completa os botões ("Iniciar montagem"); `null` = "Iniciar"/"Terminar". */
  setorAtividade: string | null
  aIniciar: LinhaDaFila[]
  emTrabalho: LinhaDaFila[]
  /** Só a parte que é tarefa (desvio D8 do plano 2); a sobra está em `sobra`. */
  aguardandoColeta: LinhaAguardandoColeta[]
  aguardandoMontagem: GrupoAguardandoMontagem[]
  sobra: LinhaDeSobra[]
}

export interface ItemDeTarefa {
  no: NoResumoDto
  ordem: number
  quantidade: number
  destino: DestinoDto
}

export interface TarefasDoSetorDto {
  setorId: number
  setorNome: string
  itens: ItemDeTarefa[]
}

/**
 * Um filho no cartão do Kit. `origem` e `ordem`: o último passo do filho, onde ele aguarda coleta; `null` quando ele
 * não tem Roteiro. `jaNoDestino`: esse passo é no Setor onde o pai começa.
 */
export interface FilhoDoKitDto {
  no: NoResumoDto
  quantidadePorPai: number
  origem: SetorResumidoDto | null
  ordem: number | null
  pronto: number
  jaNoDestino: boolean
}

/** Um Kit nas Tarefas (regra 23). `conjuntos` é zero nos incompletos. */
export interface KitDto {
  pai: NoResumoDto
  destino: SetorResumidoDto
  conjuntos: number
  filhos: FilhoDoKitDto[]
}

/** `GET /tarefas`: os "Item pronto" por Setor de origem, sem os filhos de Kit, e os Kits (spec da Fase 3B, D7). */
export interface TarefasDto {
  grupos: TarefasDoSetorDto[]
  kitsMontaveis: KitDto[]
  kitsIncompletos: KitDto[]
}

export interface SaldoDto extends LocalDto {
  quantidade: number
}

/** `totalMontado` é número nos nós com filhos (0 inclusive) e `null` nos sem filhos. */
export interface PosicoesDoNoDto {
  estruturaItemId: number
  saldos: SaldoDto[]
  totalMontado: number | null
}

export interface PassoDoRoteiroDoNo {
  setorId: number
  nome: string
  ordem: number
  alcancado: boolean
}

export interface RoteiroDoNoDto {
  estruturaItemId: number
  passos: PassoDoRoteiroDoNo[]
}

export interface LivroDoNoDto {
  /** O livro do nó, por Id. */
  movimentacoes: MovimentacaoDto[]
  /** As montagens em que o nó é o PAI montado, por Id. */
  montagens: MontagemDto[]
}

export interface OrigemDaEntrega {
  posicao: 'AguardandoColeta' | 'AguardandoMontagem'
  setorId: number
  /** Passo de `AguardandoColeta`; `null` em `AguardandoMontagem`. */
  ordem: number | null
}

export interface ItemDaEntrega {
  estruturaItemId: number
  origem: OrigemDaEntrega
  quantidade: number
}

/**
 * Lê `{ erro, mensagem }` do corpo sem deixar um corpo ilegível substituir o erro original (mesmo
 * cuidado de `detalheDoCorpo`, em `receitaPadrao.ts`). O 404 da execução vem sem `erro` — às vezes
 * com um ProblemDetails do ASP.NET no corpo, que não tem nenhum dos dois campos.
 */
async function falhar(resp: Response, oQue: string): Promise<never> {
  let codigo: string | undefined
  let mensagem: string | undefined
  try {
    const corpo: unknown = await resp.json()
    if (corpo && typeof corpo === 'object') {
      const { erro, mensagem: frase } = corpo as { erro?: unknown; mensagem?: unknown }
      if (typeof erro === 'string' && erro.trim() !== '') codigo = erro
      if (typeof frase === 'string' && frase.trim() !== '') mensagem = frase
    }
  } catch {
    // Corpo vazio ou não-JSON: sem código nem frase, e `mensagemDeErro` cai no texto do status.
  }
  throw new ErroDeApi(resp.status, `Falha ao ${oQue} (${resp.status}).`, mensagem, codigo)
}

async function ler<T>(caminho: string, oQue: string): Promise<T> {
  const resp = await apiFetch(caminho)
  if (!resp.ok) return falhar(resp, oQue)
  return (await resp.json()) as T
}

/**
 * Toda escrita da execução passa por aqui, e é aqui que se avisa o contador de Tarefas
 * (`avisarQueOLivroMudou`) — o que cobre de uma vez também a próxima escrita que alguém criar. O
 * aviso sai no 409 (o que estava na tela ficou velho, spec §8.3) e no sucesso ANTES de ler o corpo:
 * a escrita já foi gravada, e um corpo ilegível não pode esconder isso do contador.
 */
async function enviar<T>(caminho: string, metodo: 'POST' | 'PUT', corpo: unknown, oQue: string): Promise<T> {
  const init: RequestInit = { method: metodo }
  if (corpo !== undefined) {
    init.headers = { 'Content-Type': 'application/json' }
    init.body = JSON.stringify(corpo)
  }
  const resp = await apiFetch(caminho, init)
  if (resp.ok || resp.status === 409) avisarQueOLivroMudou()
  if (!resp.ok) return falhar(resp, oQue)
  return (await resp.json()) as T
}

/**
 * O 409 da execução quase sempre quer dizer que o que está na tela ficou velho (spec §8.3): a tela
 * que recebe um recarrega os dados depois de mostrar a mensagem.
 */
export function ehConflito(e: unknown): boolean {
  return e instanceof ErroDeApi && e.status === 409
}

export interface PausaDto {
  id: number
  pedidoId: number
  pausadoEm: string
  pausadoPorUsuarioId: number
  pausadoPorNome: string
  motivo: string | null
  retomadoEm: string | null
  retomadoPorUsuarioId: number | null
  retomadoPorNome: string | null
}

/**
 * Pausar e retomar passam pelo `enviar` da execução (mesmo `{ erro, mensagem }`); o aviso de que o
 * livro mudou, que ele dá, só faz o contador de Tarefas recontar — inofensivo aqui.
 */
export function pausarPedido(pedidoId: number, motivo: string | null): Promise<PausaDto> {
  return enviar(`/pedidos/${pedidoId}/pausas`, 'POST', { motivo }, 'pausar o pedido')
}

export function retomarPedido(pedidoId: number): Promise<PausaDto> {
  return enviar(`/pedidos/${pedidoId}/retomada`, 'POST', undefined, 'retomar o pedido')
}

export function obterFila(setorId: number): Promise<FilaDoSetorDto> {
  return ler(`/setores/${setorId}/fila`, 'carregar a fila')
}

export function listarTarefas(): Promise<TarefasDto> {
  return ler<TarefasDto>('/tarefas', 'carregar as tarefas')
}

export async function contarTarefas(): Promise<number> {
  const { total } = await ler<{ total: number }>('/tarefas/contagem', 'contar as tarefas')
  return total
}

export function obterPosicoes(agrupamentoId: number): Promise<PosicoesDoNoDto[]> {
  return ler(`/agrupamentos/${agrupamentoId}/posicoes`, 'carregar onde está cada peça')
}

export function obterLivroDoNo(noId: number): Promise<LivroDoNoDto> {
  return ler(`/estrutura/${noId}/movimentacoes`, 'carregar o histórico')
}

export function obterRoteiroDoNo(noId: number): Promise<RoteiroDoNoDto> {
  return ler(`/estrutura/${noId}/roteiro`, 'carregar o roteiro')
}

export function iniciar(noId: number, corpo: { setorId: number; quantidade: number }): Promise<MovimentacaoDto> {
  return enviar(`/estrutura/${noId}/inicios`, 'POST', corpo, 'iniciar')
}

export function terminar(
  noId: number,
  corpo: { setorId: number; ordem: number; quantidade: number },
): Promise<MovimentacaoDto> {
  return enviar(`/estrutura/${noId}/terminos`, 'POST', corpo, 'terminar')
}

export interface ItemDeInicioEmLote {
  estruturaItemId: number
  quantidade: number
}

export interface ItemDeTerminoEmLote {
  estruturaItemId: number
  ordem: number
  quantidade: number
}

/**
 * O lote da fila do Setor: "A iniciar aqui" e o início do pai, numa requisição, tudo ou nada. A
 * resposta vem na ordem dos itens; no pai, é o Início dele (com `montagemId`).
 */
export function iniciarEmLote(setorId: number, itens: ItemDeInicioEmLote[]): Promise<MovimentacaoDto[]> {
  return enviar('/inicios', 'POST', { setorId, itens }, 'iniciar os itens')
}

/** O lote de "Em trabalho": tudo ou nada, na ordem dos itens. */
export function terminarEmLote(setorId: number, itens: ItemDeTerminoEmLote[]): Promise<MovimentacaoDto[]> {
  return enviar('/terminos', 'POST', { setorId, itens }, 'terminar os itens')
}

/** Lista inteira numa requisição: tudo ou nada (spec §4.3). A resposta vem na ordem da lista. */
export function entregar(itens: ItemDaEntrega[]): Promise<MovimentacaoDto[]> {
  return enviar('/entregas', 'POST', { itens }, 'entregar')
}

export function estornarMovimentacao(id: number): Promise<MovimentacaoDto> {
  return enviar(`/movimentacoes/${id}/estorno`, 'POST', undefined, 'estornar')
}

/** Um estorno por baixa de filho. */
export function estornarMontagem(id: number): Promise<MovimentacaoDto[]> {
  return enviar(`/montagens/${id}/estorno`, 'POST', undefined, 'estornar a montagem')
}

/** O estorno certo para o registro da fila: o início de um pai é a montagem inteira. */
export function estornar(e: Estornavel): Promise<MovimentacaoDto | MovimentacaoDto[]> {
  return e.tipo === 'Montagem' ? estornarMontagem(e.id) : estornarMovimentacao(e.id)
}

/** `passos` são SetorIds em ordem; quem numera é o servidor (mesma regra de `LinhaDeRoteiro`). */
export function substituirRoteiroDoNo(noId: number, passos: number[]): Promise<RoteiroDoNoDto> {
  return enviar(`/estrutura/${noId}/roteiro`, 'PUT', { passos }, 'salvar o roteiro')
}
