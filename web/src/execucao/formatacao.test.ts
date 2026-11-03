import { describe, it, expect } from 'vitest'
import {
  formatarQuantidade, quantidadeConcordada, rotuloDoNo, caminhoDoNo, descreverDestino, rotuloDoSaldo, rotuloDoLocal, rotuloDoTipo,
  rotuloDaAcao, rotuloDoEstornavel, mensagemDoEstorno, textoDoResumoDeMaterial,
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

describe('quantidadeConcordada', () => {
  it.each([
    [0, '0 pronto'],
    [1, '1 pronto'],
    [1.5, '1,5 pronto'],
    [1.9999, '1,9999 pronto'],
    [2, '2 prontos'],
    [2.5, '2,5 prontos'],
    [10, '10 prontos'],
  ])('%s vira "%s"', (q, texto) => {
    expect(quantidadeConcordada(q, 'pronto', 'prontos')).toBe(texto)
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
      '2,5 prontos, aguardando a montagem do pai em Solda'],
    [{ posicao: 'AguardandoMontagem' as const, setorId: 4, setorNome: 'Solda', ordem: null, quantidade: 1 },
      '1 pronto, aguardando a montagem do pai em Solda'],
    [{ posicao: 'AguardandoMontagem' as const, setorId: 4, setorNome: 'Solda', ordem: null, quantidade: 1.5 },
      '1,5 pronto, aguardando a montagem do pai em Solda'],
    [{ ...base, posicao: 'NaExpedicao' as const, quantidade: 10 }, '10 no local de expedição'],
    [{ ...base, posicao: 'Montado' as const, quantidade: 8 }, '8 montados no pai'],
  ])('%o vira "%s"', (saldo, texto) => {
    expect(rotuloDoSaldo(saldo)).toBe(texto)
  })

  // Fase 5A: as oito posições novas do fim do caminho. As três do conserto nomeiam o Setor.
  it.each([
    ['AguardandoConserto', '1 aguardando levar ao conserto em Dobra'],
    ['EmConserto', '1 em conserto em Dobra'],
    ['ConsertoPronto', '1 pronto do conserto em Dobra'],
    ['Conforme', '1 conforme no local de expedição'],
    ['NaoConforme', '1 não conforme no local de expedição'],
    ['AguardandoLiberacao', '1 aguardando liberação'],
    ['Liberada', '1 liberada pela Gestão'],
    ['Expedido', '1 expedido'],
  ] as const)('rotuloDoSaldo de %s', (posicao, texto) => {
    const doConserto = ['AguardandoConserto', 'EmConserto', 'ConsertoPronto'].includes(posicao)
    const setor = doConserto
      ? { setorId: 2, setorNome: 'Dobra' }
      : { setorId: null, setorNome: null }

    expect(rotuloDoSaldo({ posicao, ...setor, ordem: null, quantidade: 1 })).toBe(texto)
  })

  it('concorda o que é adjetivo no plural, e deixa em paz o que não é', () => {
    const vazio = { setorId: null, setorNome: null, ordem: null }

    expect(rotuloDoSaldo({ ...vazio, posicao: 'Conforme', quantidade: 2 })).toBe('2 conformes no local de expedição')
    expect(rotuloDoSaldo({ ...vazio, posicao: 'NaoConforme', quantidade: 3 })).toBe('3 não conformes no local de expedição')
    expect(rotuloDoSaldo({ ...vazio, posicao: 'Liberada', quantidade: 2 })).toBe('2 liberadas pela Gestão')
    expect(rotuloDoSaldo({ ...vazio, posicao: 'Expedido', quantidade: 2.5 })).toBe('2,5 expedidos')
    expect(rotuloDoSaldo({ posicao: 'EmConserto', setorId: 2, setorNome: 'Dobra', ordem: null, quantidade: 2 }))
      .toBe('2 em conserto em Dobra')
  })
})

describe('rotuloDoLocal', () => {
  const vazio = { setorId: null, setorNome: null, ordem: null }

  it.each([
    [{ ...vazio, posicao: 'AIniciar' as const }, 'a iniciar'],
    [{ posicao: 'NoSetor' as const, setorId: 1, setorNome: 'Corte', ordem: 1 }, 'Corte (passo 1)'],
    [{ posicao: 'AguardandoColeta' as const, setorId: 1, setorNome: 'Corte', ordem: 1 }, 'aguardando coleta em Corte (passo 1)'],
    [{ posicao: 'AguardandoMontagem' as const, setorId: 4, setorNome: 'Solda', ordem: null }, 'aguardando a montagem do pai em Solda'],
    [{ ...vazio, posicao: 'NaExpedicao' as const }, 'local de expedição'],
    [{ ...vazio, posicao: 'Montado' as const }, 'montado no pai'],
    [{ posicao: 'AguardandoConserto' as const, setorId: 2, setorNome: 'Dobra', ordem: null },
      'aguardando levar ao conserto em Dobra'],
    [{ posicao: 'EmConserto' as const, setorId: 2, setorNome: 'Dobra', ordem: null }, 'conserto em Dobra'],
    [{ posicao: 'ConsertoPronto' as const, setorId: 2, setorNome: 'Dobra', ordem: null }, 'pronto do conserto em Dobra'],
    [{ ...vazio, posicao: 'Conforme' as const }, 'local de expedição (conforme)'],
    [{ ...vazio, posicao: 'NaoConforme' as const }, 'local de expedição (não conforme)'],
    [{ ...vazio, posicao: 'AguardandoLiberacao' as const }, 'local de expedição (aguardando liberação)'],
    [{ ...vazio, posicao: 'Liberada' as const }, 'local de expedição (liberada pela Gestão)'],
    [{ ...vazio, posicao: 'Expedido' as const }, 'expedido'],
  ])('%o vira "%s"', (local, texto) => {
    expect(rotuloDoLocal(local)).toBe(texto)
  })
})

describe('rotuloDoTipo', () => {
  it('põe acento e chama a baixa de filho pelo que ela é', () => {
    expect((['Inicio', 'Termino', 'Entrega', 'Montagem', 'Estorno'] as const).map(rotuloDoTipo))
      .toEqual(['Início', 'Término', 'Entrega', 'Baixa de montagem', 'Estorno'])
  })

  it('rotuloDoTipo traduz os seis tipos novos', () => {
    expect((['Medicao', 'Conserto', 'PedidoDeLiberacao', 'Liberacao', 'Recusa', 'Expedicao'] as const).map(rotuloDoTipo))
      .toEqual(['Medição', 'Conserto', 'Pedido de liberação', 'Liberação', 'Recusa', 'Expedição'])
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

  it('a ida ao conserto é uma entrega', () => {
    // Fase 5A: a linha `EmConserto` da Fila traz como registro estornável a Entrega que pôs a Peça no Setor.
    expect(rotuloDoEstornavel({ ...TERMINO, tipo: 'Entrega' })).toBe('Entrega de 5 · Operador do Corte · 28/09/2026 10:14')
  })
})

describe('mensagemDoEstorno', () => {
  it('confirma o registro e diz o que acontece', () => {
    expect(mensagemDoEstorno(TERMINO))
      .toBe('Estornar o término de 5, registrado por Operador do Corte em 28/09/2026 10:14? O movimento inverso fica no histórico.')
  })

  it('na entrega do conserto, concorda o artigo', () => {
    expect(mensagemDoEstorno({ ...TERMINO, tipo: 'Entrega' }))
      .toBe('Estornar a entrega de 5, registrado por Operador do Corte em 28/09/2026 10:14? O movimento inverso fica no histórico.')
  })

  it('no início de um pai, diz que o pai volta a "a iniciar" e os filhos a aguardar montagem', () => {
    expect(mensagemDoEstorno({ ...TERMINO, tipo: 'Montagem' }))
      .toBe('Estornar o início de 5 (com o consumo dos filhos), registrado por Operador do Corte em 28/09/2026 10:14? O pai volta para "a iniciar" e os filhos voltam a aguardar montagem, onde estavam antes; o estorno fica no histórico.')
  })
})

describe('textoDoResumoDeMaterial', () => {
  it('com todos os materiais completos, diz que o material está completo', () => {
    expect(textoDoResumoDeMaterial({ completos: 3, total: 3 })).toBe('Material completo')
  })

  it('com material faltando, diz quantos estão completos de quantos', () => {
    expect(textoDoResumoDeMaterial({ completos: 1, total: 3 })).toBe('Material: 1 de 3 completos')
    expect(textoDoResumoDeMaterial({ completos: 0, total: 2 })).toBe('Material: 0 de 2 completos')
  })
})
