import { ler } from './execucao'
import type { ComplicacaoDto } from './panorama'

/**
 * Cliente do andamento do Pedido ("Onde está travando", Fase 6A). Os tipos espelham `AndamentoDtos.cs`, que são a
 * fonte: campo novo nasce lá. As datas vêm em ISO 8601 com offset e as durações em segundos inteiros; a idade se
 * calcula contra o `agora` da resposta, nunca contra o relógio do aparelho (C12).
 */

export type TipoDeBloco = 'AIniciar' | 'Passo' | 'Montagem' | 'Conserto' | 'Expedicao'
export type SituacaoDoBloco = 'Atual' | 'Passado' | 'Futuro'

export interface BlocoDaTrilhaDto {
  tipo: TipoDeBloco
  /** `AIniciar`, `Passo:{ordem}`, `Montagem:{setorId}`, `Conserto:{setorId}` ou `Expedicao`. */
  chave: string
  ordem: number | null
  setorId: number | null
  setor: string | null
  situacao: SituacaoDoBloco
  /** O total do passo, no Setor mais esperando coleta (igual ao segmento do Panorama). */
  quantidade: number
  /** A chegada mais antiga (C6); `null` sem idade. */
  desde: string | null
  aguardandoColeta: number
  aguardandoColetaDesde: string | null
  /** A média ponderada do tempo das partes que já saíram do bloco; `null` sem nenhuma. */
  permanenciaMediaSegundos: number | null
}

export interface AndamentoDoNoDto {
  estruturaItemId: number
  codigo: string
  descricao: string
  quantidade: number
  /** Quanto do nó já saiu da fábrica (o saldo `Expedido`); a Expedição só vira bloco enquanto há peça no local. */
  expedido: number
  trilha: BlocoDaTrilhaDto[]
  complicacoes: ComplicacaoDto[]
  itens: AndamentoDoNoDto[]
}

export interface MaisParadoDto {
  estruturaItemId: number
  /** A `chave` do bloco. */
  bloco: string
}

export interface AndamentoDoPedidoDto {
  agora: string
  abertura: string
  /** `null` antes da primeira produção. */
  inicioReal: string | null
  pausadoDepoisDoInicioSegundos: number
  maisParado: MaisParadoDto | null
  pecas: AndamentoDoNoDto[]
}

/** A trilha de cada Peça em aberto do Pedido; Pedido encerrado devolve só os números, com `pecas` vazia. */
export function buscarAndamento(pedidoId: number): Promise<AndamentoDoPedidoDto> {
  return ler(`/pedidos/${pedidoId}/andamento`, 'carregar o andamento do pedido')
}
