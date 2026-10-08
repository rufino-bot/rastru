/**
 * O tamanho de um arquivo para o texto de tela, usado pelo upload do sólido e pelo painel do arquivo
 * do BOM.
 *
 * Abaixo de 1024 bytes, "N bytes"; abaixo de 1 MiB, KiB com uma casa; acima, MiB com uma casa —
 * sempre com `toLocaleString('pt-BR', …)`, para a vírgula decimal.
 */
export function formatarTamanho(bytes: number): string {
  if (bytes < 1024) return `${bytes.toLocaleString('pt-BR')} bytes`
  const kib = bytes / 1024
  if (kib < 1024) {
    return `${kib.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} KiB`
  }
  const mib = kib / 1024
  return `${mib.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} MiB`
}
