import type { LeituraDeQuantidade } from './quantidade'

const INTEIRO = /^\d+$/

/**
 * Lê o número de conjuntos de um Kit: inteiro, de 1 ao máximo. Um Kit só se move em conjunto completo (regra 25),
 * então meio conjunto não existe — recusado aqui, antes da rede, como o servidor recusaria com `ConjuntoIncompleto`.
 */
export function lerConjuntos(texto: string, maximo: number): LeituraDeQuantidade {
  const t = texto.trim()
  if (!INTEIRO.test(t)) return { valor: null, erro: 'Digite um número inteiro de conjuntos.' }
  const valor = Number(t)
  if (valor < 1) return { valor: null, erro: 'Leve pelo menos um conjunto.' }
  if (valor > maximo) return { valor: null, erro: `No máximo ${maximo}.` }
  return { valor, erro: null }
}

/**
 * Quanto de um filho vai em N conjuntos: N × razão, arredondado a quatro casas (a coluna é DECIMAL(18,4)). Sem o
 * arredondamento, 3 × 0,1 iria como 0,30000000000000004, que o servidor não reconheceria como três conjuntos
 * (decisão P7 do plano da Fase 3B).
 */
export function quantidadeDoConjunto(conjuntos: number, quantidadePorPai: number): number {
  return Math.round(conjuntos * quantidadePorPai * 10000) / 10000
}

/** Quantos conjuntos completos os filhos presentes formam: o menor, entre eles, de ⌊presente ÷ razão⌋. */
export function conjuntosPresentes(filhos: { presente: number; quantidadePorPai: number }[]): number {
  if (filhos.length === 0) return 0
  return Math.min(...filhos.map((f) => (f.quantidadePorPai > 0 ? Math.floor(f.presente / f.quantidadePorPai) : 0)))
}
