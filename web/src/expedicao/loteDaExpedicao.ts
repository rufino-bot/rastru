import type { PecaNaExpedicaoDto, PedidoNaExpedicaoDto } from '../api/expedicao'
import { lerQuantidade, quantidadeParaCampo, type LeituraDeQuantidade } from '../execucao/quantidade'

/**
 * A lógica pura do lote da tela "Expedição" (C9 da spec da Fase 5A): várias Peças, de um ou mais Pedidos, numa
 * remessa só, pela `BarraDoLote` "Expedir". No molde do `loteDaSeparacao`, com um campo de quantidade por Peça
 * em vez de um por material. Não sabe de React, de filtro nem de rede: quem passa as Peças visíveis é a tela.
 */

/**
 * As Peças marcadas (a presença da chave é a marca) e, de cada uma, o texto do campo, ou `null` enquanto ninguém
 * o mexeu: o campo intocado mostra o expedível ATUAL, então uma recarga que muda o expedível muda o campo que
 * ninguém tocou, e nunca o que foi digitado.
 */
export type LoteDaExpedicao = Record<number, string | null>

export const SAIU_DO_LOTE =
  'Uma Peça que você tinha marcado não tem mais o que expedir: outra pessoa pode tê-la expedido. Confira a seleção.'

/** Só a Peça com algo a expedir entra no lote: a que está toda aguardando a Gestão, ou em conserto, não. */
export const podeEntrarNoLote = (peca: PecaNaExpedicaoDto) => peca.expedivel > 0

export const estaMarcado = (lote: LoteDaExpedicao, pecaId: number) => pecaId in lote

/** Todas as Peças da lista, na ordem da tela (Pedido por prazo, e as Peças de cada um na ordem do servidor). */
export const pecasDosPedidos = (pedidos: PedidoNaExpedicaoDto[]) => pedidos.flatMap((p) => p.pecas)

/** Marca ou desmarca uma Peça. Marcar não traz texto nenhum: o campo começa no expedível. */
export function alternar(lote: LoteDaExpedicao, pecaId: number, marcado: boolean): LoteDaExpedicao {
  if (marcado) return estaMarcado(lote, pecaId) ? lote : { ...lote, [pecaId]: null }
  if (!estaMarcado(lote, pecaId)) return lote
  const { [pecaId]: _tirada, ...resto } = lote
  return resto
}

/** Guarda o texto do campo de uma Peça marcada. */
export function digitar(lote: LoteDaExpedicao, pecaId: number, texto: string): LoteDaExpedicao {
  if (!estaMarcado(lote, pecaId)) return lote
  return { ...lote, [pecaId]: texto }
}

/** Todas as Peças visíveis que podem entrar no lote estão marcadas (e há pelo menos uma). */
export function todosMarcados(lote: LoteDaExpedicao, visiveis: PecaNaExpedicaoDto[]): boolean {
  const elegiveis = visiveis.filter(podeEntrarNoLote)
  return elegiveis.length > 0 && elegiveis.every((p) => estaMarcado(lote, p.id))
}

/**
 * "Marcar todos" / "Desmarcar todos", sobre as Peças visíveis que podem entrar, como na Separação: com todas
 * marcadas, desmarca só elas (a marcada oculta pelo filtro continua); senão marca as que faltam, sem mexer no que
 * já foi digitado nas que estavam marcadas.
 */
export function marcarTodos(lote: LoteDaExpedicao, visiveis: PecaNaExpedicaoDto[]): LoteDaExpedicao {
  const marcar = !todosMarcados(lote, visiveis)
  return visiveis.filter(podeEntrarNoLote).reduce((depois, p) => alternar(depois, p.id, marcar), lote)
}

/**
 * A recarga mexe na seleção só no que saiu: tira as Peças que não estão na lista INTEIRA (não a filtrada — o
 * filtro não é "outra pessoa agiu") ou que continuam nela sem nada a expedir, e diz se tirou alguma. O texto das
 * que ficam nunca é reescrito.
 */
export function reconciliar(
  lote: LoteDaExpedicao, pecas: PecaNaExpedicaoDto[],
): { lote: LoteDaExpedicao; saiu: boolean } {
  const presentes = new Set(pecas.filter(podeEntrarNoLote).map((p) => p.id))
  const marcadas = Object.keys(lote).map(Number)
  const ficam = marcadas.filter((id) => presentes.has(id))
  if (ficam.length === marcadas.length) return { lote, saiu: false }
  return { lote: Object.fromEntries(ficam.map((id) => [id, lote[id]])), saiu: true }
}

/** O campo de uma Peça: o texto mostrado e a leitura dele, com o expedível como máximo. */
export interface CampoDaPeca {
  texto: string
  leitura: LeituraDeQuantidade
}

/** Acima do expedível é inválido aqui, antes da rede: o servidor recusaria com `SaldoInsuficiente`. */
export function campoDaPeca(peca: PecaNaExpedicaoDto, texto: string | null): CampoDaPeca {
  const mostrado = texto ?? quantidadeParaCampo(peca.expedivel)
  return { texto: mostrado, leitura: lerQuantidade(mostrado, peca.expedivel) }
}

export interface MarcadaNoLote {
  peca: PecaNaExpedicaoDto
  campo: CampoDaPeca
}

/** As Peças marcadas, na ordem da lista inteira, com o campo de cada uma. */
export function marcadosDoLote(lote: LoteDaExpedicao, pecas: PecaNaExpedicaoDto[]): MarcadaNoLote[] {
  return pecas.filter((p) => estaMarcado(lote, p.id)).map((peca) => ({ peca, campo: campoDaPeca(peca, lote[peca.id]) }))
}

/**
 * O que impede o lote de ir: algum campo inválido, ou nenhuma Peça marcada. A tela usa para o `invalido` da barra
 * e para recusar o envio, para uma Peça marcada com campo ruim nunca virar remessa parcial.
 */
export function camposInvalidos(marcadas: MarcadaNoLote[]): boolean {
  return marcadas.length === 0 || marcadas.some((m) => m.campo.leitura.erro !== null)
}

/** O corpo do `POST /remessas`. Só chame com `camposInvalidos` falso: campo inválido não entra. */
export function itensDoLote(marcadas: MarcadaNoLote[]): { estruturaItemId: number; quantidade: number }[] {
  return marcadas.flatMap(({ peca, campo }) =>
    campo.leitura.erro === null ? [{ estruturaItemId: peca.id, quantidade: campo.leitura.valor }] : [])
}
