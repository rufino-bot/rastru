/** O que a tela leu de um campo de quantidade da conferência: o número, ou a frase de por que não. */
export type LeituraDaQuantidade = { valor: number; motivo: null } | { valor: null; motivo: string }

// Dígitos e no máximo quatro casas depois do separador, sem sinal, expoente nem milhar.
const FORMATO = /^\d+(\.\d{1,4})?$/

// A parte inteira da coluna `DECIMAL(18,4)`: 14 dígitos, até 99.999.999.999.999,9999. Conferida pelos
// dígitos, e não comparando com um `number`: o maior valor da coluna não cabe exato num `double`.
const DIGITOS_INTEIROS = 14

/**
 * Lê a quantidade que o PCP digitou na conferência, com a mesma regra que o servidor aplica à
 * quantidade da Peça e à quantidade por pai (maior que zero, no máximo 4 casas, dentro da coluna):
 * o que o servidor recusaria não sai da tela, e o motivo fica ao lado do campo. Aceita vírgula ou
 * ponto como separador decimal.
 */
export function lerQuantidadeDaConferencia(texto: string): LeituraDaQuantidade {
  const normalizado = texto.trim().replace(',', '.')
  if (!FORMATO.test(normalizado)) {
    return { valor: null, motivo: 'Digite um número com no máximo quatro casas decimais.' }
  }
  const valor = Number(normalizado)
  if (valor <= 0) return { valor: null, motivo: 'A quantidade precisa ser maior que zero.' }
  if (normalizado.split('.')[0].replace(/^0+(?=\d)/, '').length > DIGITOS_INTEIROS) {
    return { valor: null, motivo: 'No máximo 99.999.999.999.999,9999.' }
  }
  return { valor, motivo: null }
}
