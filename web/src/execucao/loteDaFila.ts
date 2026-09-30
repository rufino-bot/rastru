import type { FilaDoSetorDto, ItemDeInicioEmLote, ItemDeTerminoEmLote } from '../api/execucao'
import { lerQuantidade, quantidadeParaCampo } from './quantidade'

/**
 * A lógica pura do lote da fila do Setor (desvios D8 a D10 do plano 2 dos filtros): quais linhas
 * podem ser marcadas, o que a marcação guarda e o que a atualização da fila faz com ela. Não sabe de
 * React, de filtro nem de rede — quem passa as linhas visíveis é a tela.
 */

export type SecaoDoLote = 'emTrabalho' | 'aIniciar' | 'aguardandoMontagem'

export const TITULO_DA_SECAO: Record<SecaoDoLote, string> = {
  emTrabalho: 'Em trabalho',
  aIniciar: 'A iniciar aqui',
  aguardandoMontagem: 'Aguardando montagem',
}

/**
 * A chave de uma ação da linha, que é também a chave dela no lote (desvio D8). Uma por vez na tela
 * individual: a ação aberta é identificada por ela.
 */
export const chaveDeIniciar = (noId: number, ordem: number) => `iniciar:${noId}:${ordem}`
export const chaveDeTerminar = (noId: number, ordem: number) => `terminar:${noId}:${ordem}`
export const chaveDeIniciarPai = (paiId: number) => `iniciar-pai:${paiId}`

export const BLOQUEIO_PEDIDO_PAUSADO = 'O Pedido está pausado: nada dele começa até alguém retomá-lo.'
export const BLOQUEIO_SEM_FILHOS = 'Não há filhos suficientes aqui para iniciar.'
export const SAIU_DO_LOTE =
  'Um item que você tinha marcado não está mais nesta fila: outra pessoa o moveu. Confira a seleção.'

/**
 * Uma linha que pode entrar no lote. `ordem` é `null` no pai (o início dele não tem passo) e
 * `bloqueio` é o motivo de a linha não poder ser enviada agora, ou `null`.
 */
export interface LinhaDoLote {
  chave: string
  secao: SecaoDoLote
  noId: number
  ordem: number | null
  maximo: number
  bloqueio: string | null
}

/** `quantidades` guarda o TEXTO do campo de cada linha marcada, por chave. */
export interface Lote {
  secao: SecaoDoLote
  quantidades: Record<string, string>
}

/**
 * Toda linha da fila que o lote alcança, na ordem das seções da tela e, dentro de cada uma, na da
 * resposta. O cartão de um pai que NÃO inicia aqui não entra: a ação dele é o "Levar" do filho.
 */
export function linhasDoLote(fila: FilaDoSetorDto): LinhaDoLote[] {
  const linhas: LinhaDoLote[] = []
  for (const l of fila.emTrabalho) {
    linhas.push({
      chave: chaveDeTerminar(l.no.id, l.ordem), secao: 'emTrabalho', noId: l.no.id, ordem: l.ordem,
      maximo: l.quantidade, bloqueio: null,
    })
  }
  for (const l of fila.aIniciar) {
    linhas.push({
      chave: chaveDeIniciar(l.no.id, l.ordem), secao: 'aIniciar', noId: l.no.id, ordem: l.ordem,
      maximo: l.quantidade, bloqueio: l.no.pausa === null ? null : BLOQUEIO_PEDIDO_PAUSADO,
    })
  }
  for (const g of fila.aguardandoMontagem) {
    if (!g.iniciaAqui) continue
    let bloqueio: string | null = null
    if (g.pai.pausa !== null) bloqueio = BLOQUEIO_PEDIDO_PAUSADO
    else if (g.daParaMontar <= 0) bloqueio = BLOQUEIO_SEM_FILHOS
    linhas.push({
      chave: chaveDeIniciarPai(g.pai.id), secao: 'aguardandoMontagem', noId: g.pai.id, ordem: null,
      maximo: g.daParaMontar, bloqueio,
    })
  }
  return linhas
}

/** O que impede a linha marcada de ir: o bloqueio primeiro, senão o que o texto tem de errado. */
export function erroDaLinha(linha: LinhaDoLote, texto: string): string | null {
  if (linha.bloqueio !== null) return linha.bloqueio
  return lerQuantidade(texto, linha.maximo).erro
}

/**
 * Marca ou desmarca uma linha. O lote é de UMA seção (a trava): linha de outra seção, e linha
 * bloqueada que ainda não estava marcada, deixam o lote como veio. Desmarcar a bloqueada que já
 * estava marcada funciona (desvio D8: não se prende a seleção). Devolve `null` quando a seleção
 * esvazia, e é isso que solta a trava.
 */
export function alternar(lote: Lote | null, linha: LinhaDoLote, marcado: boolean): Lote | null {
  if (lote !== null && lote.secao !== linha.secao) return lote
  if (marcado) {
    if (linha.bloqueio !== null) return lote
    return {
      secao: linha.secao,
      quantidades: { ...lote?.quantidades, [linha.chave]: quantidadeParaCampo(linha.maximo) },
    }
  }
  if (lote === null || !(linha.chave in lote.quantidades)) return lote
  const { [linha.chave]: _tirada, ...resto } = lote.quantidades
  return Object.keys(resto).length === 0 ? null : { secao: lote.secao, quantidades: resto }
}

function livres(secao: SecaoDoLote, visiveis: LinhaDoLote[]): LinhaDoLote[] {
  return visiveis.filter((l) => l.secao === secao && l.bloqueio === null)
}

/**
 * "Marcar todos" / "Desmarcar todos" (desvio D10): age só sobre as linhas visíveis e não bloqueadas
 * da seção. Com todas elas marcadas, desmarca só elas (o marcado oculto pelo filtro continua);
 * senão marca as que faltam, sem reescrever o texto de quem já estava marcado.
 */
export function marcarTodos(lote: Lote | null, secao: SecaoDoLote, visiveis: LinhaDoLote[]): Lote | null {
  if (lote !== null && lote.secao !== secao) return lote
  const alvo = livres(secao, visiveis)
  if (alvo.length === 0) return lote
  if (todosMarcados(lote, secao, visiveis)) {
    let depois = lote
    for (const l of alvo) depois = alternar(depois, l, false)
    return depois
  }
  let depois = lote
  for (const l of alvo) {
    if (depois === null || !(l.chave in depois.quantidades)) depois = alternar(depois, l, true)
  }
  return depois
}

/** Todas as linhas visíveis e não bloqueadas da seção estão marcadas (e há pelo menos uma). */
export function todosMarcados(lote: Lote | null, secao: SecaoDoLote, visiveis: LinhaDoLote[]): boolean {
  if (lote === null || lote.secao !== secao) return false
  const alvo = livres(secao, visiveis)
  return alvo.length > 0 && alvo.every((l) => l.chave in lote.quantidades)
}

/**
 * A atualização da fila mexe na seleção só no que saiu: tira as chaves que não estão na resposta
 * INTEIRA (não a filtrada — o filtro não é "outra pessoa agiu") e diz se tirou alguma. Linha que
 * ficou bloqueada continua marcada, e o texto nunca é reescrito: se o máximo caiu abaixo dele, é o
 * `erroDaLinha` que acusa (desvios D8 e D9).
 */
export function reconciliar(lote: Lote, linhas: LinhaDoLote[]): { lote: Lote | null; saiu: boolean } {
  const presentes = new Set(linhas.map((l) => l.chave))
  const restantes = Object.entries(lote.quantidades).filter(([chave]) => presentes.has(chave))
  if (restantes.length === Object.keys(lote.quantidades).length) return { lote, saiu: false }
  if (restantes.length === 0) return { lote: null, saiu: true }
  return { lote: { secao: lote.secao, quantidades: Object.fromEntries(restantes) }, saiu: true }
}

/** As linhas marcadas do lote, na ordem das linhas da fila, com a quantidade já lida. */
function marcadas(lote: Lote, linhas: LinhaDoLote[]): { linha: LinhaDoLote; quantidade: number }[] {
  const saida: { linha: LinhaDoLote; quantidade: number }[] = []
  for (const linha of linhas) {
    const texto = lote.quantidades[linha.chave]
    if (texto === undefined || linha.secao !== lote.secao) continue
    const { valor } = lerQuantidade(texto, linha.maximo)
    // Quem chama só envia com tudo válido; uma linha que não lê não entra calada no corpo.
    if (valor !== null) saida.push({ linha, quantidade: valor })
  }
  return saida
}

export function itensDeInicio(lote: Lote, linhas: LinhaDoLote[]): ItemDeInicioEmLote[] {
  return marcadas(lote, linhas).map(({ linha, quantidade }) => ({ estruturaItemId: linha.noId, quantidade }))
}

export function itensDeTermino(lote: Lote, linhas: LinhaDoLote[]): ItemDeTerminoEmLote[] {
  return marcadas(lote, linhas).map(({ linha, quantidade }) => ({
    estruturaItemId: linha.noId, ordem: linha.ordem ?? 0, quantidade,
  }))
}
