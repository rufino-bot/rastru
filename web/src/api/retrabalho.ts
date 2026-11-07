import { apiFetch } from './client'
import type { PedidoDto } from './cadastros'
import type { BaixaDto } from './baixas'
import { enviarComOpcoes, falhar, ler, type Posicao } from './execucao'
import { avisarQueOLivroMudou } from './sinalDoLivro'

/**
 * Cliente da reposição (Fase 5B-2): abrir o Pedido de Retrabalho, a reclamação do cliente, o "não repor" e o "Usar a
 * sobra". Os tipos espelham o contrato JSON de `RetrabalhoDtos.cs` — é o código do servidor a fonte, não este
 * arquivo: campo novo nasce lá primeiro.
 *
 * Toda recusa vira `ErroDeApi` lançado, com `codigo` (o campo `erro` do corpo) e `detalhe` (o campo `mensagem`),
 * pelos mesmos `ler` e `falhar` da execução. Toda escrita mexe no livro ou no que ele soma e avisa o contador de
 * Tarefas, no sucesso e no 409.
 */

export type MotivoDeRetrabalho = 'ReprovacaoDimensional' | 'ErroInterno' | 'SolicitacaoCliente' | 'Perda'

export const ROTULO_DO_MOTIVO_DE_RETRABALHO: Record<MotivoDeRetrabalho, string> = {
  ReprovacaoDimensional: 'Reprovação dimensional',
  ErroInterno: 'Erro interno',
  SolicitacaoCliente: 'Solicitação do cliente',
  Perda: 'Perda',
}

/** O que se faz com a Peça reclamada: conserta no Setor escolhido ou refaz como Retrabalho. */
export type DestinoDaReclamacao = 'Consertar' | 'Refazer'

export const ROTULO_DO_DESTINO: Record<DestinoDaReclamacao, string> = {
  Consertar: 'Consertar',
  Refazer: 'Refazer',
}

/** O corpo de `POST /pedidos/{id}/retrabalhos` ({id} = o original). No máximo uma porta: `perdaId`, `reclamacaoId` ou nenhuma (avulsa). */
export interface NovoRetrabalho {
  perdaId: number | null
  reclamacaoId: number | null
  /** Só na avulsa: uma Peça do original. */
  estruturaItemId: number | null
  quantidade: number | null
  /** Só da reclamação; `setorId` só no `Consertar`. */
  destino: DestinoDaReclamacao | null
  setorId: number | null
  numero: string
  /** `aaaa-mm-dd`; ausente ou nula fica a do original. */
  dataEntrega?: string | null
  motivoRetrabalho: MotivoDeRetrabalho | null
}

export interface OrigemDoRetrabalhoDto {
  tipo: 'Baixa' | 'Reclamacao' | 'Avulsa'
  id: number | null
}

export interface RetrabalhoResumoDto {
  id: number
  numero: string
  motivoRetrabalho: MotivoDeRetrabalho | null
  status: string
  /** `aaaa-mm-dd`. */
  dataEntrega: string
  origem: OrigemDoRetrabalhoDto
}

/** `numeroSugerido` é o que o servidor propõe para o próximo; a tela não o calcula. */
export interface RetrabalhosDoPedidoDto {
  numeroSugerido: string
  retrabalhos: RetrabalhoResumoDto[]
}

export interface PedidoRetrabalhoResumoDto {
  id: number
  numero: string
}

export interface DispensaDto {
  id: number
  justificativa: string
  /** ISO 8601 com offset -03:00 — mostrar com `formatarDataHora`. */
  dataHora: string
  usuarioId: number
  usuarioNome: string
}

export interface NovaReclamacao {
  estruturaItemId: number
  quantidade: number
  /** O que o cliente disse. */
  descricao: string
}

export interface ReclamacaoDto {
  id: number
  pecaId: number
  /** O nome da Peça (não o texto do cliente). */
  descricao: string
  codigo: string | null
  quantidade: number
  /** O que o cliente disse. */
  texto: string
  dataHora: string
  usuarioId: number
  usuarioNome: string
  estornada: boolean
  /** Quem lê pode estorná-la agora. */
  estornavel: boolean
  pedidoRetrabalho: PedidoRetrabalhoResumoDto | null
  dispensa: DispensaDto | null
  aRepor: number
}

/** Uma posição da sobra do original que o Retrabalho pode receber como pronto. */
export interface ItemDaSobraDto {
  estruturaItemId: number
  descricao: string
  codigo: string | null
  posicao: Posicao
  setorId: number | null
  setorNome: string | null
  ordem: number | null
  sobra: number
  precisa: number
  sugerido: number
}

export interface PreviaDoReaproveitamentoDto {
  itens: ItemDaSobraDto[]
}

export interface NovoReaproveitamento {
  itens: Array<{
    estruturaItemId: number
    posicao: Posicao
    setorId?: number | null
    ordem?: number | null
    quantidade: number
  }>
}

function escrever<T>(caminho: string, corpo: unknown, oQue: string): Promise<T> {
  return enviarComOpcoes(caminho, 'POST', corpo, oQue, { avisa: true })
}

/** Abre o Retrabalho do Pedido `pedidoId` (o original); devolve o Pedido novo. */
export function abrirRetrabalho(pedidoId: number, r: NovoRetrabalho): Promise<PedidoDto> {
  return escrever(`/pedidos/${pedidoId}/retrabalhos`, r, 'abrir o retrabalho')
}

export function listarRetrabalhos(pedidoId: number): Promise<RetrabalhosDoPedidoDto> {
  return ler(`/pedidos/${pedidoId}/retrabalhos`, 'carregar os retrabalhos')
}

export function registrarReclamacao(pedidoId: number, r: NovaReclamacao): Promise<ReclamacaoDto> {
  return escrever(`/pedidos/${pedidoId}/reclamacoes`, r, 'registrar a reclamação')
}

/** Mais recente primeiro. */
export function listarReclamacoes(pedidoId: number): Promise<ReclamacaoDto[]> {
  return ler(`/pedidos/${pedidoId}/reclamacoes`, 'carregar as reclamações')
}

export function estornarReclamacao(id: number): Promise<ReclamacaoDto> {
  return escrever(`/reclamacoes/${id}/estorno`, undefined, 'estornar a reclamação')
}

export function naoReporBaixa(id: number, justificativa: string): Promise<DispensaDto> {
  return escrever(`/baixas/${id}/dispensa`, { justificativa }, 'dispensar a reposição')
}

export function naoReporReclamacao(id: number, justificativa: string): Promise<DispensaDto> {
  return escrever(`/reclamacoes/${id}/dispensa`, { justificativa }, 'dispensar a reposição')
}

/** Desfaz o "não repor"; o servidor responde 204. */
export async function desfazerNaoRepor(dispensaId: number): Promise<void> {
  const resp = await apiFetch(`/dispensas/${dispensaId}/estorno`, { method: 'POST' })
  if (resp.ok || resp.status === 409) avisarQueOLivroMudou()
  if (!resp.ok) return falhar(resp, 'desfazer o "não repor"')
}

/** A sobra do original que o Retrabalho `retrabalhoId` pode receber, com a sugestão. Lê e não grava, não avisa. */
export async function preverReaproveitamento(
  retrabalhoId: number, sinal?: AbortSignal,
): Promise<PreviaDoReaproveitamentoDto> {
  const resp = await apiFetch(`/pedidos/${retrabalhoId}/sobra-reaproveitavel`, sinal ? { signal: sinal } : undefined)
  if (!resp.ok) return falhar(resp, 'prever o reaproveitamento')
  return (await resp.json()) as PreviaDoReaproveitamentoDto
}

/** Usa a sobra no Retrabalho; devolve a baixa `Reaproveitado`, que é do Pedido original. */
export function reaproveitar(retrabalhoId: number, r: NovoReaproveitamento): Promise<BaixaDto> {
  return escrever(`/pedidos/${retrabalhoId}/sobra-reaproveitada`, r, 'usar a sobra')
}
