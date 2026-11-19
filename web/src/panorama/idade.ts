const MS_POR_HORA = 3_600_000
const HORAS_POR_DIA = 24

/**
 * A distância entre `desde` e `agora`, os dois como o servidor os manda (ISO 8601 com offset). O `agora` é o relógio
 * do servidor (C12): o cliente nunca usa o próprio para a idade. Negativa (`agora` antes de `desde`, por relógios
 * ou arredondamentos) ou ilegível vale zero.
 */
export function msDesde(desde: string, agora: string): number {
  const ms = Date.parse(agora) - Date.parse(desde)
  return Number.isFinite(ms) && ms > 0 ? ms : 0
}

/** C7: abaixo de 1 h "menos de 1 h"; abaixo de 24 h "N h"; senão "N d", em dias inteiros para baixo. */
export function duracaoLegivel(ms: number): string {
  const horas = Math.floor(Math.max(0, ms) / MS_POR_HORA)
  if (horas < 1) return 'menos de 1 h'
  if (horas < HORAS_POR_DIA) return `${horas} h`
  return `${Math.floor(horas / HORAS_POR_DIA)} d`
}

/** "há menos de 1 h", "há 3 h", "há 2 d". */
export function idadeDesde(desde: string, agora: string): string {
  return `há ${duracaoLegivel(msDesde(desde, agora))}`
}
