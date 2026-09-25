import { describe, it, expect } from 'vitest'
import { formatarQuantidade, rotuloDoNo, caminhoDoNo, descreverDestino, rotuloDoSaldo } from './formatacao'
import type { DestinoDto, NoResumoDto } from '../api/execucao'

const SUPORTE: NoResumoDto = {
  id: 7, descricao: 'Suporte', codigoDoComponente: 'SUP-01',
  pedidoId: 1, pedidoNumero: 'PED-2026-01', agrupamentoId: 3, agrupamentoCodigo: 'AG-01',
  paiId: 2, paiDescricao: 'Chassi',
}

const DESTINO_VAZIO: DestinoDto = {
  tipo: 'Expedicao', setorId: null, setorNome: null, ordem: null,
  paiId: null, sugestaoSetorId: null, setoresPossiveis: [], paiSemRoteiro: false,
}

describe('formatarQuantidade', () => {
  it('usa vírgula decimal e corta em quatro casas', () => {
    expect(formatarQuantidade(6)).toBe('6')
    expect(formatarQuantidade(2.5)).toBe('2,5')
    expect(formatarQuantidade(0.1234)).toBe('0,1234')
    expect(formatarQuantidade(1234)).toBe('1.234')
  })
})

describe('rotuloDoNo', () => {
  it('põe o código antes da descrição, e só a descrição no ad-hoc', () => {
    expect(rotuloDoNo(SUPORTE)).toBe('SUP-01 — Suporte')
    expect(rotuloDoNo({ ...SUPORTE, codigoDoComponente: null })).toBe('Suporte')
  })
})

describe('caminhoDoNo', () => {
  it('vai do Pedido ao pai', () => {
    expect(caminhoDoNo(SUPORTE)).toBe('PED-2026-01 › AG-01 › Chassi')
  })

  it('para no Agrupamento quando o nó é Peça', () => {
    expect(caminhoDoNo({ ...SUPORTE, paiId: null, paiDescricao: null })).toBe('PED-2026-01 › AG-01')
  })
})

describe('descreverDestino', () => {
  it('próximo passo nomeia Setor e passo', () => {
    expect(descreverDestino({ ...DESTINO_VAZIO, tipo: 'ProximoPasso', setorId: 3, setorNome: 'Dobra', ordem: 2 }, SUPORTE))
      .toBe('Dobra (passo 2)')
  })

  it('Peça no fim do Roteiro vai ao local de expedição', () => {
    expect(descreverDestino(DESTINO_VAZIO, SUPORTE)).toBe('Local de expedição')
  })

  it('montagem nomeia o pai e a sugestão', () => {
    const destino: DestinoDto = {
      ...DESTINO_VAZIO, tipo: 'Montagem', paiId: 2, sugestaoSetorId: 4,
      setoresPossiveis: [{ id: 4, nome: 'Solda' }, { id: 6, nome: 'Montagem final' }],
    }
    expect(descreverDestino(destino, SUPORTE)).toBe('Montagem de Chassi (sugestão: Solda)')
  })

  it('montagem sem sugestão não inventa uma', () => {
    const destino: DestinoDto = {
      ...DESTINO_VAZIO, tipo: 'Montagem', paiId: 2, sugestaoSetorId: null,
      setoresPossiveis: [{ id: 4, nome: 'Solda' }],
    }
    expect(descreverDestino(destino, SUPORTE)).toBe('Montagem de Chassi')
  })

  it('pai sem Roteiro diz isso em vez de sugerir', () => {
    // Desvio D7 do plano 2: o item aparece, e a pendência é do PCP.
    const destino: DestinoDto = { ...DESTINO_VAZIO, tipo: 'Montagem', paiId: 2, paiSemRoteiro: true }
    expect(descreverDestino(destino, SUPORTE)).toBe('Montagem de Chassi — o pai não tem Roteiro')
  })
})

describe('rotuloDoSaldo', () => {
  const base = { setorId: null, setorNome: null, ordem: null }

  it.each([
    [{ ...base, posicao: 'AIniciar' as const, quantidade: 4 }, '4 a iniciar'],
    [{ posicao: 'NoSetor' as const, setorId: 1, setorNome: 'Corte', ordem: 1, quantidade: 6 }, '6 em Corte (passo 1)'],
    [{ posicao: 'AguardandoColeta' as const, setorId: 1, setorNome: 'Corte', ordem: 3, quantidade: 2 },
      '2 aguardando coleta em Corte (passo 3)'],
    [{ posicao: 'AguardandoMontagem' as const, setorId: 4, setorNome: 'Solda', ordem: null, quantidade: 2.5 },
      '2,5 aguardando montagem em Solda'],
    [{ ...base, posicao: 'NaExpedicao' as const, quantidade: 10 }, '10 no local de expedição'],
    [{ ...base, posicao: 'Montado' as const, quantidade: 8 }, '8 montados no pai'],
  ])('%o vira "%s"', (saldo, texto) => {
    expect(rotuloDoSaldo(saldo)).toBe(texto)
  })
})
