import { describe, it, expect } from 'vitest'
import { chaveDoSaldo, POSICOES_DE_ORIGEM, saldosParaBaixa } from './posicoesDaBaixa'
import type { Posicao, SaldoDto } from '../api/execucao'

const saldo = (posicao: Posicao, quantidade: number, setorId: number | null = null, ordem: number | null = null): SaldoDto =>
  ({ posicao, quantidade, setorId, setorNome: setorId === null ? null : 'Corte', ordem })

describe('posicoesDaBaixa', () => {
  it('as origens são as onze em produção, sem AguardandoLiberacao, Montado, Expedido e Perdido', () => {
    expect([...POSICOES_DE_ORIGEM].sort()).toEqual([
      'AIniciar', 'AguardandoColeta', 'AguardandoConserto', 'AguardandoMontagem', 'Conforme', 'ConsertoPronto',
      'EmConserto', 'Liberada', 'NaExpedicao', 'NaoConforme', 'NoSetor',
    ])
  })

  it('saldosParaBaixa deixa só as origens com saldo positivo', () => {
    const lista = [
      saldo('AIniciar', 2), saldo('NoSetor', 0, 1, 1), saldo('AguardandoLiberacao', 3),
      saldo('Montado', 4), saldo('Expedido', 1), saldo('Perdido', 1), saldo('Conforme', 1),
    ]

    expect(saldosParaBaixa(lista).map((s) => s.posicao)).toEqual(['AIniciar', 'Conforme'])
  })

  it('a chave distingue o mesmo Setor em dois passos', () => {
    expect(chaveDoSaldo(saldo('NoSetor', 1, 1, 1))).not.toBe(chaveDoSaldo(saldo('NoSetor', 1, 1, 3)))
  })
})
