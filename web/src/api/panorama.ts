import { apiFetch } from './client'
import { falhar, ler } from './execucao'
import type { PausaResumoDto } from './cadastros'
import type { SituacaoDaAfericao } from './expedicao'

/**
 * Cliente do Panorama. Os tipos espelham os DTOs de `PanoramaDtos.cs`, que são a fonte: campo novo nasce lá.
 * Toda recusa vira `ErroDeApi` lançado, pelos mesmos `ler` e `falhar` da execução.
 *
 * A marca "Embalado" não é movimentação do livro, então a escrita não avisa o contador de Tarefas.
 */

export interface PedidoDoPanoramaDto {
  id: number
  numero: string
  /** Data de entrega, `AAAA-MM-DD`: compara-se como texto, nunca com `Date`. */
  dataEntrega: string
  atrasado: boolean
  retrabalho: boolean
  /** `null` quando o Pedido não está pausado. */
  pausa: PausaResumoDto | null
}

export interface ComponenteDoPanoramaDto {
  id: number
  codigo: string
  descricao: string
  /** O hash da miniatura; `null` sem miniatura. */
  miniaturaVersao: string | null
}

export interface SetorResumoDto {
  setorId: number
  nome: string
}

export type TipoDeSegmento = 'AIniciar' | 'Passo' | 'Conserto' | 'Expedicao' | 'Expedido' | 'Perdido'

/** Um segmento do "Onde está", já na ordem. `setorId`/`setorNome` só em `Passo` e `Conserto`; `ordem` só em `Passo`. */
export interface SegmentoDto {
  tipo: TipoDeSegmento
  setorId: number | null
  setorNome: string | null
  ordem: number | null
  quantidade: number
  aguardandoColeta: number
}

export interface AfericaoDoPanoramaDto {
  exigeRelatorio: boolean
  situacao: SituacaoDaAfericao
}

export interface UltimaEmbalagemDto {
  tipo: string
  /** ISO 8601 com offset -03:00. */
  dataHora: string
  usuario: string
}

export interface EmbalagemDaPecaDto {
  vale: boolean
  podeMarcar: boolean
  /** O "faltam N" das sete posições de produção. */
  emProducao: number
  /** `null` sem registro. */
  ultima: UltimaEmbalagemDto | null
}

export interface PecaDoPanoramaDto {
  estruturaItemId: number
  agrupamentoId: number
  pedido: PedidoDoPanoramaDto
  componente: ComponenteDoPanoramaDto
  quantidade: number
  itensEm: SetorResumoDto[]
  distribuicao: SegmentoDto[]
  afericao: AfericaoDoPanoramaDto
  embalagem: EmbalagemDaPecaDto
}

export interface PanoramaDto {
  pecas: PecaDoPanoramaDto[]
}

/** Toda Peça em aberto, na ordem do servidor (prazo, número do Pedido, código, id do nó). */
export function buscarPanorama(): Promise<PanoramaDto> {
  return ler('/panorama', 'carregar o panorama')
}

/** `true` marca a Peça como embalada, `false` desmarca. Responde 204. */
export async function registrarEmbalagem(estruturaItemId: number, embalado: boolean): Promise<void> {
  const resp = await apiFetch(`/estrutura/${estruturaItemId}/embalagem`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ embalado }),
  })
  if (!resp.ok) return falhar(resp, embalado ? 'marcar como embalado' : 'desmarcar o embalado')
}
