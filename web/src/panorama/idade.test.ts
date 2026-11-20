import { describe, it, expect } from 'vitest'
import { duracaoLegivel, idadeDesde, msDesde } from './idade'

const AGORA = '2026-11-19T15:00:00-03:00'

describe('idade', () => {
  it('menos_de_uma_hora', () => {
    expect(idadeDesde('2026-11-19T14:01:00-03:00', AGORA)).toBe('há menos de 1 h')
    expect(duracaoLegivel(0)).toBe('menos de 1 h')
  })

  it('horas_abaixo_de_um_dia', () => {
    expect(idadeDesde('2026-11-19T14:00:00-03:00', AGORA)).toBe('há 1 h')
    expect(idadeDesde('2026-11-18T15:01:00-03:00', AGORA)).toBe('há 23 h')
  })

  it('dias_arredondados_para_baixo', () => {
    expect(idadeDesde('2026-11-18T15:00:00-03:00', AGORA)).toBe('há 1 d')
    expect(idadeDesde('2026-11-17T16:00:00-03:00', AGORA)).toBe('há 1 d')
    expect(idadeDesde('2026-11-09T15:00:00-03:00', AGORA)).toBe('há 10 d')
  })

  it('agora_antes_de_desde_vale_zero', () => {
    expect(msDesde('2026-11-19T16:00:00-03:00', AGORA)).toBe(0)
    expect(idadeDesde('2026-11-19T16:00:00-03:00', AGORA)).toBe('há menos de 1 h')
    expect(duracaoLegivel(-5_000_000)).toBe('menos de 1 h')
  })

  it('o offset do servidor não muda a conta', () => {
    expect(idadeDesde('2026-11-19T17:00:00Z', '2026-11-19T20:00:00Z')).toBe('há 3 h')
  })
})
