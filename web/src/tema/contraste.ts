/**
 * Luminância relativa de uma cor `#RRGGBB`, pela fórmula da WCAG 2.x.
 *
 * Implementada à mão em nove linhas em vez de trazer uma dependência: a fase inteira existe para
 * *reduzir* superfície (a Task 5 da 1B recusou até `jest-dom`), e o que se ganharia era isto aqui.
 */
export function luminanciaRelativa(hex: string): number {
  const canais = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
  const [r, g, b] = canais.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** Razão de contraste entre duas cores `#RRGGBB`. Vai de 1 (idênticas) a 21 (preto/branco). */
export function razaoDeContraste(a: string, b: string): number {
  const la = luminanciaRelativa(a)
  const lb = luminanciaRelativa(b)
  const [claro, escuro] = la > lb ? [la, lb] : [lb, la]
  return (claro + 0.05) / (escuro + 0.05)
}
