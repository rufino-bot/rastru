import type { LinhaDeMaterial, MaterialPendente, NoDoLoteDeSeparacao, PendenciaDeSeparacaoDto } from '../api/separacao'
import { formatarQuantidade } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'

/**
 * O campo de quantidade da separação e a lógica pura do lote da tela "Separação", no molde do `loteDaFila`
 * da execução. O campo é o mesmo no painel individual (`PainelDeSeparacao`) e no cartão marcado do lote;
 * o lote não sabe de React, de filtro nem de rede — quem passa os cartões visíveis é a tela.
 */

/** O que um campo diz: não vai no corpo (vazio ou zero), vai com este valor, ou está errado. */
export type LeituraDoCampo = { tipo: 'nao-vai' } | { tipo: 'vai'; valor: number } | { tipo: 'erro'; erro: string }

// Zero escrito de qualquer jeito ("0", "0,0", "00.00"): é o Separador dizendo "deste não levei", não erro.
const ZERO = /^0+([.,]0+)?$/

/**
 * Lê um campo sem máximo: acima do planejado é aceito (D9 da spec da Fase 4 — o parafuso que caiu), então
 * `lerQuantidade` recebe infinito e só recusa o formato.
 */
export function lerCampo(texto: string): LeituraDoCampo {
  const aparado = texto.trim()
  if (aparado === '' || ZERO.test(aparado)) return { tipo: 'nao-vai' }
  const leitura = lerQuantidade(aparado, Number.POSITIVE_INFINITY)
  return leitura.erro === null ? { tipo: 'vai', valor: leitura.valor } : { tipo: 'erro', erro: leitura.erro }
}

/** O texto embaixo do campo: o erro, o aviso de "acima" (neutro) ou quanto falta. */
export function dicaDoCampo(m: MaterialPendente, leitura: LeituraDoCampo): string {
  if (leitura.tipo === 'erro') return leitura.erro
  if (leitura.tipo === 'vai' && leitura.valor > m.pendencia) {
    return `acima do planejado em ${formatarQuantidade(leitura.valor - m.pendencia)} ${m.unidadeMedida}`
  }
  return `Falta ${formatarQuantidade(m.pendencia)} ${m.unidadeMedida}`
}

/** Um campo de material: o texto mostrado e o que ele diz. */
export interface CampoDeMaterial {
  material: MaterialPendente
  texto: string
  leitura: LeituraDoCampo
}

/**
 * Os campos de UM nó, um por material pendente. `textos` guarda só o que o Separador mexeu, por
 * `materialId`: o campo intocado mostra a pendência ATUAL, como no painel individual, então uma recarga
 * que muda a pendência muda o campo que ninguém tocou, e nunca o que foi digitado.
 */
export function camposDoNo(item: PendenciaDeSeparacaoDto, textos: Record<number, string>): CampoDeMaterial[] {
  return item.materiais.map((m) => {
    const texto = textos[m.materialId] ?? quantidadeParaCampo(m.pendencia)
    return { material: m, texto, leitura: lerCampo(texto) }
  })
}

/** As linhas que vão no corpo: só os campos que não ficaram vazios nem zero. */
export function linhasDosCampos(campos: CampoDeMaterial[]): LinhaDeMaterial[] {
  return campos.flatMap((c) =>
    c.leitura.tipo === 'vai' ? [{ materialId: c.material.materialId, quantidade: c.leitura.valor }] : [])
}

/** O que impede os campos de irem: algum texto que não é número, ou nenhum campo que vá. */
export function camposInvalidos(campos: CampoDeMaterial[]): boolean {
  return campos.some((c) => c.leitura.tipo === 'erro') || linhasDosCampos(campos).length === 0
}

/**
 * O lote da tela "Separação": os nós marcados (a presença da chave é a marca) e, de cada um, o texto dos
 * campos que o Separador mexeu. A caixa marca o cartão inteiro, com todos os materiais pendentes dele.
 */
export type LoteDaSeparacao = Record<number, Record<number, string>>

export const SAIU_DO_LOTE =
  'Um item que você tinha marcado não está mais na lista: outra pessoa pode tê-lo separado. Confira a seleção.'

export const estaMarcado = (lote: LoteDaSeparacao, noId: number) => noId in lote

/** Marca ou desmarca um cartão. Marcar não traz texto nenhum: os campos começam na pendência. */
export function alternar(lote: LoteDaSeparacao, noId: number, marcado: boolean): LoteDaSeparacao {
  if (marcado) return estaMarcado(lote, noId) ? lote : { ...lote, [noId]: {} }
  if (!estaMarcado(lote, noId)) return lote
  const { [noId]: _tirado, ...resto } = lote
  return resto
}

/** Guarda o texto de um campo de um cartão marcado. */
export function digitar(lote: LoteDaSeparacao, noId: number, materialId: number, texto: string): LoteDaSeparacao {
  if (!estaMarcado(lote, noId)) return lote
  return { ...lote, [noId]: { ...lote[noId], [materialId]: texto } }
}

/** Todos os cartões visíveis estão marcados (e há pelo menos um). */
export function todosMarcados(lote: LoteDaSeparacao, visiveis: PendenciaDeSeparacaoDto[]): boolean {
  return visiveis.length > 0 && visiveis.every((v) => estaMarcado(lote, v.no.id))
}

/**
 * "Marcar todos" / "Desmarcar todos", sobre os cartões visíveis, como na Fila: com todos marcados, desmarca
 * só eles (o marcado oculto pelo filtro continua); senão marca os que faltam, sem mexer no que o Separador
 * já digitou nos que estavam marcados.
 */
export function marcarTodos(lote: LoteDaSeparacao, visiveis: PendenciaDeSeparacaoDto[]): LoteDaSeparacao {
  const marcar = !todosMarcados(lote, visiveis)
  return visiveis.reduce((depois, v) => alternar(depois, v.no.id, marcar), lote)
}

/**
 * A recarga mexe na seleção só no que saiu: tira os nós que não estão na lista INTEIRA (não a filtrada — o
 * filtro não é "outra pessoa agiu") e diz se tirou algum. O texto dos que ficam nunca é reescrito.
 */
export function reconciliar(
  lote: LoteDaSeparacao, itens: PendenciaDeSeparacaoDto[],
): { lote: LoteDaSeparacao; saiu: boolean } {
  const presentes = new Set(itens.map((i) => i.no.id))
  const marcados = Object.keys(lote).map(Number)
  const ficam = marcados.filter((id) => presentes.has(id))
  if (ficam.length === marcados.length) return { lote, saiu: false }
  return { lote: Object.fromEntries(ficam.map((id) => [id, lote[id]])), saiu: true }
}

/** Os cartões marcados, na ordem da lista inteira, com os campos de cada um. */
export function marcadosDoLote(
  lote: LoteDaSeparacao, itens: PendenciaDeSeparacaoDto[],
): { item: PendenciaDeSeparacaoDto; campos: CampoDeMaterial[] }[] {
  return itens.filter((i) => estaMarcado(lote, i.no.id)).map((item) => ({ item, campos: camposDoNo(item, lote[item.no.id]) }))
}

/**
 * O corpo do `POST /separacoes`. Quem impede que um cartão marcado sem nada a mandar vire lote parcial é
 * `camposInvalidos`, que a tela usa para o `invalido` da barra e para recusar o envio.
 */
export function nosDoLote(marcados: { item: PendenciaDeSeparacaoDto; campos: CampoDeMaterial[] }[]): NoDoLoteDeSeparacao[] {
  return marcados.map(({ item, campos }) => ({ estruturaItemId: item.no.id, linhas: linhasDosCampos(campos) }))
}
