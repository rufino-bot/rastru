import { describe, it, expect } from 'vitest'
import {
  formatarQuantidade, rotuloDoNo, caminhoDoNo, descreverDestino, rotuloDoSaldo, rotuloDoLocal, rotuloDoTipo,
  rotuloDaAcao, rotuloDoEstornavel, mensagemDoEstorno,
} from './formatacao'
import type { DestinoDto, Estornavel, NoResumoDto } from '../api/execucao'
import { destino, DESTINO_MONTAGEM } from '../testes/execucao'

const SUPORTE: NoResumoDto = {
  id: 7, descricao: 'Suporte', codigoDoComponente: 'SUP-01',
  pedidoId: 1, pedidoNumero: 'PED-2026-01', pedidoCliente: 'Metalúrgica Alfa',
  agrupamentoId: 3, agrupamentoCodigo: 'AG-01',
  paiId: 2, paiDescricao: 'Chassi', materiais: [], pausa: null, agrupamentoTipo: 'Avulso',
}

const DESTINO_VAZIO: DestinoDto = {
  tipo: 'Expedicao', setorId: null, setorNome: null, ordem: null,
  paiId: null, paiSemRoteiro: false,
}

describe('formatarQuantidade', () => {
  it('usa vírgula decimal, com até quatro casas', () => {
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

  it('montagem nomeia o pai e o Setor onde ele começa', () => {
    expect(descreverDestino(DESTINO_MONTAGEM, SUPORTE)).toBe('Montagem de Chassi em Solda')
  })

  it('pai sem Roteiro diz que não há para onde levar', () => {
    // Desvio D7 do plano 2: o item aparece, e a pendência é do PCP.
    const semRoteiro = destino({
      tipo: 'Montagem', setorId: null, setorNome: null, ordem: null, paiId: 2, paiSemRoteiro: true,
    })
    expect(descreverDestino(semRoteiro, SUPORTE)).toBe('Montagem de Chassi — o pai não tem Roteiro')
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

describe('rotuloDoLocal', () => {
  const vazio = { setorId: null, setorNome: null, ordem: null }

  it.each([
    [{ ...vazio, posicao: 'AIniciar' as const }, 'a iniciar'],
    [{ posicao: 'NoSetor' as const, setorId: 1, setorNome: 'Corte', ordem: 1 }, 'Corte (passo 1)'],
    [{ posicao: 'AguardandoColeta' as const, setorId: 1, setorNome: 'Corte', ordem: 1 }, 'aguardando coleta em Corte (passo 1)'],
    [{ posicao: 'AguardandoMontagem' as const, setorId: 4, setorNome: 'Solda', ordem: null }, 'aguardando montagem em Solda'],
    [{ ...vazio, posicao: 'NaExpedicao' as const }, 'local de expedição'],
    [{ ...vazio, posicao: 'Montado' as const }, 'montado no pai'],
  ])('%o vira "%s"', (local, texto) => {
    expect(rotuloDoLocal(local)).toBe(texto)
  })
})

describe('rotuloDoTipo', () => {
  it('põe acento e chama a baixa de filho pelo que ela é', () => {
    expect((['Inicio', 'Termino', 'Entrega', 'Montagem', 'Estorno'] as const).map(rotuloDoTipo))
      .toEqual(['Início', 'Término', 'Entrega', 'Baixa de montagem', 'Estorno'])
  })
})

describe('rotuloDaAcao', () => {
  it('compõe o verbo com a atividade do Setor', () => {
    expect(rotuloDaAcao('Iniciar', 'montagem')).toBe('Iniciar montagem')
    expect(rotuloDaAcao('Terminar', 'solda')).toBe('Terminar solda')
  })

  it('sem atividade, fica só o verbo', () => {
    expect(rotuloDaAcao('Iniciar', null)).toBe('Iniciar')
    expect(rotuloDaAcao('Terminar', '   ')).toBe('Terminar')
  })
})

const TERMINO: Estornavel = {
  tipo: 'Termino', id: 41, quantidade: 5, usuarioId: 12, usuarioNome: 'Operador do Corte', dataHora: '2026-09-28T10:14:00-03:00',
}

describe('rotuloDoEstornavel', () => {
  it('diz o que é, quanto, quem e quando', () => {
    expect(rotuloDoEstornavel(TERMINO)).toBe('Término de 5 · Operador do Corte · 28/09/2026 10:14')
    expect(rotuloDoEstornavel({ ...TERMINO, tipo: 'Inicio' })).toBe('Início de 5 · Operador do Corte · 28/09/2026 10:14')
    expect(rotuloDoEstornavel({ ...TERMINO, tipo: 'Montagem' })).toBe('Início de 5 (com o consumo dos filhos) · Operador do Corte · 28/09/2026 10:14')
  })
})

describe('mensagemDoEstorno', () => {
  it('confirma o registro e diz o que acontece', () => {
    expect(mensagemDoEstorno(TERMINO))
      .toBe('Estornar o término de 5, registrado por Operador do Corte em 28/09/2026 10:14? O movimento inverso fica no histórico.')
  })

  it('no início de um pai, diz que o pai volta a "a iniciar" e os filhos a aguardar montagem', () => {
    expect(mensagemDoEstorno({ ...TERMINO, tipo: 'Montagem' }))
      .toBe('Estornar o início de 5 (com o consumo dos filhos), registrado por Operador do Corte em 28/09/2026 10:14? O pai volta para "a iniciar" e os filhos voltam a aguardar montagem, onde estavam antes; o estorno fica no histórico.')
  })
})
