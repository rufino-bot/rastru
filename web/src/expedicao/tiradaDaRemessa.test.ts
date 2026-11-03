import { describe, it, expect } from 'vitest'
import type { PosicoesDaPecaDto, SituacaoDaAfericao } from '../api/expedicao'
import { avisosDaRemessa, tirarDaRemessa, type Tirada } from './tiradaDaRemessa'

/**
 * A mesma tabela de `CalculoDaExpedicaoTests` do backend, com os mesmos números (decisão Q2 do plano de front da
 * Fase 5A): a prévia do aviso no front e os avisos do `201` saem da mesma conta, e divergir é defeito.
 */
function pos(p: Partial<PosicoesDaPecaDto> = {}): PosicoesDaPecaDto {
  return {
    naExpedicao: 0, conforme: 0, naoConforme: 0, aguardandoLiberacao: 0, liberada: 0, emConserto: 0, expedido: 0,
    ...p,
  }
}

const t = (posicao: Tirada['posicao'], quantidade: number): Tirada => ({ posicao, quantidade })

describe('tirarDaRemessa (C10)', () => {
  it('Tirar_segue_Liberada_Conforme_NaExpedicao_NaoConforme', () => {
    const p = pos({ liberada: 1, conforme: 2, naExpedicao: 3, naoConforme: 4 })

    expect(tirarDaRemessa(p, 6)).toEqual([t('Liberada', 1), t('Conforme', 2), t('NaExpedicao', 3)])
    expect(tirarDaRemessa(p, 10)).toEqual([t('Liberada', 1), t('Conforme', 2), t('NaExpedicao', 3), t('NaoConforme', 4)])
    // Tirada parcial no meio da ordem: o resto da posição fica.
    expect(tirarDaRemessa(p, 2)).toEqual([t('Liberada', 1), t('Conforme', 1)])
  })

  it('Tirar_pula_posicao_vazia', () => {
    expect(tirarDaRemessa(pos({ naoConforme: 3 }), 2)).toEqual([t('NaoConforme', 2)])
  })

  it('Tirar_nunca_tira_AguardandoLiberacao', () => {
    expect(tirarDaRemessa(pos({ aguardandoLiberacao: 5 }), 1)).toBeNull()
  })

  it('Tirar_acima_do_expedivel_devolve_null', () => {
    // Conserto e expedido também não saem: o expedível aqui é 1 + 2 + 3 + 4 = 10.
    const p = pos({
      liberada: 1, conforme: 2, naExpedicao: 3, naoConforme: 4, aguardandoLiberacao: 5, emConserto: 6, expedido: 7,
    })

    expect(tirarDaRemessa(p, 10)).not.toBeNull()
    expect(tirarDaRemessa(p, 10.001)).toBeNull()
    expect(tirarDaRemessa(p, 11)).toBeNull()
  })

  it('quantidade não positiva (ou que não é número) não tem tirada: devolve null em vez de lançar', () => {
    // O backend lança (é erro de quem chama); no front a prévia roda a cada tecla, e campo inválido não tem prévia.
    for (const q of [0, -1, Number.NaN]) expect(tirarDaRemessa(pos({ naExpedicao: 3 }), q), String(q)).toBeNull()
  })

  it('a conta é em casas decimais exatas, como o decimal do backend, e não em ponto flutuante', () => {
    // 0,1 + 0,2 em ponto flutuante dá 0,30000000000000004; a remessa de 0,3 tira 0,1 e 0,2, nem mais nem menos.
    const p = pos({ liberada: 0.1, conforme: 0.2, naExpedicao: 0.7 })

    expect(tirarDaRemessa(p, 0.3)).toEqual([t('Liberada', 0.1), t('Conforme', 0.2)])
    expect(tirarDaRemessa(p, 1)).toEqual([t('Liberada', 0.1), t('Conforme', 0.2), t('NaExpedicao', 0.7)])
    expect(tirarDaRemessa(p, 1.0001)).toBeNull()
  })
})

describe('avisosDaRemessa (D8)', () => {
  // Aviso_por_situacao_e_origem: [situação, origem, aviso esperado ou null].
  const tabela: [SituacaoDaAfericao, Tirada['posicao'], string | null][] = [
    ['SemRelatorio', 'NaExpedicao', null],
    ['Pendente', 'NaExpedicao', 'SemAfericao'],
    ['Conforme', 'NaExpedicao', null],
    ['NaoConforme', 'NaExpedicao', 'SemMedicao'],
    ['NaoConforme', 'NaoConforme', 'NaoConformeSemLiberacao'],
    ['NaoConforme', 'Liberada', null],
    ['NaoConforme', 'Conforme', null],
  ]

  it.each(tabela)('Aviso_por_situacao_e_origem: %s tirando de %s avisa %s', (s, origem, aviso) => {
    const avisos = avisosDaRemessa(s, [t(origem, 3)])

    if (aviso === null) expect(avisos).toEqual([])
    else expect(avisos).toEqual([{ tipo: aviso, quantidade: 3 }])
  })

  it('Avisos_agrega_por_tipo_e_nao_devolve_tipo_com_zero', () => {
    const avisos = avisosDaRemessa('NaoConforme', [
      t('Liberada', 1), t('NaExpedicao', 2), t('NaoConforme', 4), t('NaExpedicao', 0.5), t('NaoConforme', 0),
    ])

    expect(avisos).toEqual([
      { tipo: 'SemMedicao', quantidade: 2.5 },
      { tipo: 'NaoConformeSemLiberacao', quantidade: 4 },
    ])
    // Tirada de zero não vira aviso de zero.
    expect(avisosDaRemessa('Pendente', [t('NaExpedicao', 0)])).toEqual([])
  })

  it('os avisos saem na ordem SemAfericao, SemMedicao, NaoConformeSemLiberacao, seja qual for a das tiradas', () => {
    // Na Pendente, `NaoConforme` (se houver) avisa sempre e `NaExpedicao` avisa sem aferição.
    expect(avisosDaRemessa('Pendente', [t('NaoConforme', 1), t('NaExpedicao', 2)]).map((a) => a.tipo))
      .toEqual(['SemAfericao', 'NaoConformeSemLiberacao'])
  })

  it('a soma é exata em casas decimais', () => {
    expect(avisosDaRemessa('Pendente', [t('NaExpedicao', 0.1), t('NaExpedicao', 0.2)]))
      .toEqual([{ tipo: 'SemAfericao', quantidade: 0.3 }])
  })

  it('a tirada da tabela C10 e os avisos juntos: a prévia de expedir tudo de uma Peça não conforme', () => {
    const p = pos({ liberada: 1, conforme: 2, naExpedicao: 3, naoConforme: 4, aguardandoLiberacao: 5 })

    expect(avisosDaRemessa('NaoConforme', tirarDaRemessa(p, 10)!)).toEqual([
      { tipo: 'SemMedicao', quantidade: 3 },
      { tipo: 'NaoConformeSemLiberacao', quantidade: 4 },
    ])
  })
})
