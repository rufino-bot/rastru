import { enviarComOpcoes, ler, type MovimentacaoDto, type Posicao } from './execucao'
import type { DispensaDto, PedidoRetrabalhoResumoDto } from './retrabalho'

/**
 * Cliente da baixa: perda e descarte (Fase 5B-1). Os tipos espelham o contrato JSON de `BaixaDtos.cs` — é o
 * código do servidor a fonte, não este arquivo: campo novo nasce lá primeiro.
 *
 * Toda recusa vira `ErroDeApi` lançado, com `codigo` (o campo `erro` do corpo) e `detalhe` (o campo `mensagem`),
 * pelos mesmos `ler` e `falhar` da execução. Dar baixa, descartar e estornar mexem no livro e avisam o contador de
 * Tarefas, no sucesso e no 409; as prévias não gravam nada e não avisam.
 */

export type MotivoDaBaixa = 'PerdaArmazem' | 'MortaEmProcesso' | 'Descarte' | 'Reaproveitado'

/** Os motivos como a pessoa os lê, nos botões e no histórico. */
export const ROTULO_DO_MOTIVO: Record<MotivoDaBaixa, string> = {
  PerdaArmazem: 'Sumiu no armazém',
  MortaEmProcesso: 'Morreu num processo',
  Descarte: 'Descarte',
  Reaproveitado: 'Reaproveitado',
}

/** De onde a baixa de parte tira e quanto: o Setor e o passo só quando a posição os tem. */
export interface OrigemDaBaixa {
  posicao: Posicao
  setorId?: number | null
  ordem?: number | null
  quantidade: number
}

/** O corpo de `POST /estrutura/{id}/baixas` e da prévia. `origens` só na baixa de parte; vazia ou ausente com `tudo`. */
export interface NovaBaixa {
  motivo: MotivoDaBaixa
  observacao?: string | null
  tudo: boolean
  origens?: OrigemDaBaixa[]
  /** Só na baixa de um Item: leva junto as unidades da Peça do topo que a perda trava. */
  incluirTopo: boolean
}

export interface LinhaDaBaixaDto {
  estruturaItemId: number
  descricao: string
  codigo: string | null
  origemPosicao: Posicao
  origemSetorId: number | null
  origemSetorNome: string | null
  origemOrdem: number | null
  quantidade: number
  /** O nó é a Peça do topo (sem pai): a que a reposição nomeia. Só a baixa gravada preenche; a prévia traz falso. */
  ehPeca?: boolean
}

export interface BaixaDto {
  id: number
  motivo: MotivoDaBaixa
  observacao: string | null
  escopo: { tipo: 'No' | 'Agrupamento' | 'Pedido'; id: number }
  tudo: boolean
  /**
   * ISO 8601 com offset -03:00 — mostrar com `formatarDataHora`, nunca com `Date`. É o conversor global de
   * `DateTime` do servidor (`HorarioDeBrasiliaJsonConverter`) que escreve assim, como em todo `dataHora` da API.
   */
  dataHora: string
  usuarioId: number
  usuarioNome: string
  estornada: boolean
  /** Quem lê pode estorná-la agora. */
  estornavel: boolean
  linhas: LinhaDaBaixaDto[]
  /** O Retrabalho que repõe esta baixa, ou que recebeu a sobra (`Reaproveitado`); `null` sem Retrabalho. */
  pedidoRetrabalho: PedidoRetrabalhoResumoDto | null
  /** O "não repor" vigente, ou `null`. */
  dispensa: DispensaDto | null
  /** Unidades de Peça ainda por repor (C1); a tela lê, não calcula. */
  aRepor: number
}

export interface SobraDaBaixaDto {
  estruturaItemId: number
  descricao: string
  quantidade: number
}

/** As unidades da Peça do topo que a baixa trava, e quantas delas ainda estão a iniciar. */
export interface TravaNoTopoDto {
  pecaId: number
  descricao: string
  quantidade: number
  aIniciar: number
}

/** O que a baixa gravaria, sem gravar: as linhas, a sobra que nasce e a trava no topo. */
export interface PreviaDaBaixaDto {
  linhas: LinhaDaBaixaDto[]
  viramSobra: SobraDaBaixaDto[]
  travaNoTopo: TravaNoTopoDto | null
}

function escrever<T>(
  caminho: string, corpo: unknown, oQue: string, avisa: boolean, sinal?: AbortSignal,
): Promise<T> {
  return enviarComOpcoes(caminho, 'POST', corpo, oQue, { avisa, sinal })
}

export function darBaixa(noId: number, b: NovaBaixa): Promise<BaixaDto> {
  return escrever(`/estrutura/${noId}/baixas`, b, 'dar baixa', true)
}

export function preverBaixa(noId: number, b: NovaBaixa, sinal?: AbortSignal): Promise<PreviaDaBaixaDto> {
  return escrever(`/estrutura/${noId}/baixas/previa`, b, 'prever a baixa', false, sinal)
}

/** O "Descartar tudo" do Agrupamento: uma baixa de tudo por `Descarte` de cada Peça dele com algo em produção. */
export function descartarAgrupamento(id: number, observacao?: string | null): Promise<BaixaDto> {
  return escrever(`/agrupamentos/${id}/descarte`, { observacao: observacao ?? null }, 'descartar o agrupamento', true)
}

export function descartarPedido(id: number, observacao?: string | null): Promise<BaixaDto> {
  return escrever(`/pedidos/${id}/descarte`, { observacao: observacao ?? null }, 'descartar o pedido', true)
}

export function preverDescarteDoAgrupamento(id: number, sinal?: AbortSignal): Promise<PreviaDaBaixaDto> {
  return escrever(`/agrupamentos/${id}/descarte/previa`, undefined, 'prever o descarte', false, sinal)
}

export function preverDescarteDoPedido(id: number, sinal?: AbortSignal): Promise<PreviaDaBaixaDto> {
  return escrever(`/pedidos/${id}/descarte/previa`, undefined, 'prever o descarte', false, sinal)
}

/** Estorna a baixa inteira; devolve os estornos gravados no livro. */
export function estornarBaixa(id: number): Promise<MovimentacaoDto[]> {
  return escrever(`/baixas/${id}/estorno`, undefined, 'estornar a baixa', true)
}

/** Mais recente primeiro. */
export function listarBaixasDoNo(noId: number): Promise<BaixaDto[]> {
  return ler(`/estrutura/${noId}/baixas`, 'carregar as baixas')
}

/** Mais recente primeiro. */
export function listarBaixasDoPedido(pedidoId: number): Promise<BaixaDto[]> {
  return ler(`/pedidos/${pedidoId}/baixas`, 'carregar as baixas do pedido')
}
