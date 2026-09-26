import { formatarQuantidade } from './formatacao'

export type LeituraDeQuantidade = { valor: number; erro: null } | { valor: null; erro: string }

// Dígitos, e no máximo quatro casas depois do separador. Sem sinal, sem expoente, sem milhar: o
// que o operador digita num celular é "4" ou "2,5", e qualquer outra forma é mais provável de ser
// toque errado do que intenção.
const FORMATO = /^\d+(\.\d{1,4})?$/

/**
 * Lê o que o operador digitou. Aceita vírgula OU ponto — o teclado numérico do Android em pt-BR põe
 * vírgula, e o `<input type="number">` recusaria "2,5" em alguns navegadores. Recusa aqui, antes da
 * rede, o que o backend recusaria com `QuantidadeInvalida` ou `SaldoInsuficiente`: zero, mais de
 * quatro casas (a coluna é `DECIMAL(18,4)`) e mais do que o disponível na tela.
 */
export function lerQuantidade(texto: string, maximo: number): LeituraDeQuantidade {
  const normalizado = texto.trim().replace(',', '.')
  if (!FORMATO.test(normalizado)) {
    return { valor: null, erro: 'Digite um número com no máximo quatro casas decimais.' }
  }
  const valor = Number(normalizado)
  if (valor <= 0) return { valor: null, erro: 'A quantidade precisa ser maior que zero.' }
  if (valor > maximo) return { valor: null, erro: `No máximo ${formatarQuantidade(maximo)}.` }
  return { valor, erro: null }
}

/**
 * O valor inicial do campo: todo o disponível (spec §6.1), escrito como o operador escreveria.
 * NÃO é `formatarQuantidade`: ela põe separador de milhar ("1.234"), e `lerQuantidade` leria esse
 * ponto como decimal.
 */
export function quantidadeParaCampo(n: number): string {
  return String(n).replace('.', ',')
}
