import { apiFetch } from './client'
import { ErroDeApi } from './erros'

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
  agrupamentoId: number
  agrupamentoCodigo: string
  paiId: number | null
  paiDescricao: string | null
}

export interface SetorResumidoDto {
  id: number
  nome: string
}

export type TipoDeDestino = 'ProximoPasso' | 'Expedicao' | 'Montagem'

/**
 * `ProximoPasso` traz `setorId`/`setorNome`/`ordem`; `Expedicao` não traz nada; `Montagem` traz
 * `paiId`, `sugestaoSetorId` (pode ser `null`) e `setoresPossiveis`. `paiSemRoteiro` só é `true` em
 * `Montagem` com a lista vazia (desvio D7 do plano 2).
 */
export interface DestinoDto {
  tipo: TipoDeDestino
  setorId: number | null
  setorNome: string | null
  ordem: number | null
  paiId: number | null
  sugestaoSetorId: number | null
  setoresPossiveis: SetorResumidoDto[]
  paiSemRoteiro: boolean
}

export interface LinhaDaFila {
  no: NoResumoDto
  ordem: number
  quantidade: number
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
}

export interface FilaDoSetorDto {
  setorId: number
  setorNome: string
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
  /** Só quando o destino é montagem (ou redirecionamento); `null` quando o destino é calculado. */
  destinoSetorId: number | null
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

async function enviar<T>(caminho: string, metodo: 'POST' | 'PUT', corpo: unknown, oQue: string): Promise<T> {
  const init: RequestInit = { method: metodo }
  if (corpo !== undefined) {
    init.headers = { 'Content-Type': 'application/json' }
    init.body = JSON.stringify(corpo)
  }
  const resp = await apiFetch(caminho, init)
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

export function obterFila(setorId: number): Promise<FilaDoSetorDto> {
  return ler(`/setores/${setorId}/fila`, 'carregar a fila')
}

export function listarTarefas(): Promise<TarefasDoSetorDto[]> {
  return ler('/tarefas', 'carregar as tarefas')
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

export function montar(paiId: number, corpo: { setorId: number; quantidade: number }): Promise<MontagemDto> {
  return enviar(`/estrutura/${paiId}/montagens`, 'POST', corpo, 'montar')
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

/** `passos` são SetorIds em ordem; quem numera é o servidor (mesma regra de `LinhaDeRoteiro`). */
export function substituirRoteiroDoNo(noId: number, passos: number[]): Promise<RoteiroDoNoDto> {
  return enviar(`/estrutura/${noId}/roteiro`, 'PUT', { passos }, 'salvar o roteiro')
}
