import { describe, it, expect } from 'vitest'
import { STATUS_DO_PEDIDO, ENCERRADOS, tomDoStatus, rotuloDoStatus } from './statusDoPedido'

describe('statusDoPedido', () => {
  // A ordem NÃO é decorativa: é a do CK_Pedido_Status em specs/02-modelo-de-dados.sql:170-171, e
  // é a ordem em que o resumo da Home apresenta os cinco. Um `sort()` alfabético a trocaria para
  // AguardandoExpedicao/Aberto/Cancelado/Concluido/EmProducao, que não é o fluxo do domínio.
  it('lista os cinco status na ordem do CK_Pedido_Status', () => {
    expect(STATUS_DO_PEDIDO).toEqual([
      'Aberto', 'EmProducao', 'AguardandoExpedicao', 'Concluido', 'Cancelado',
    ])
  })

  // `ENCERRADOS` é o que o front trata como encerrado: a Home o exclui da contagem de abertos, e o
  // `ControleDePausa` não oferece pausa a Pedido encerrado. Se alguém acrescentar 'Cancelado' e
  // esquecer 'Concluido' — ou vice-versa —, a Home contaria Pedido encerrado como aberto. O servidor
  // tem a lista dele (`PrazoDeEntrega.StatusEncerrados`), e quem exclui os encerrados da seção "Prazos
  // de entrega" é ele, não o front.
  it('trata Concluido e Cancelado como encerrados, e mais nenhum', () => {
    expect(ENCERRADOS).toEqual(['Concluido', 'Cancelado'])

    // O complemento, afirmado explicitamente: os três que SOBRAM são os que contam como abertos na
    // Home. Sem esta metade, acrescentar 'Aberto' a ENCERRADOS por engano passaria
    // — a primeira asserção sozinha não diz nada sobre quem ficou de fora.
    // `.some(===)` e não `.includes`: `ENCERRADOS` é uma tupla `readonly`, e `.includes` exigiria
    // um cast para aceitar um status que não está nela.
    const naoEncerrados = STATUS_DO_PEDIDO.filter((s) => !ENCERRADOS.some((e) => e === s))
    expect(naoEncerrados).toEqual(['Aberto', 'EmProducao', 'AguardandoExpedicao'])
  })

  // A regra do CLAUDE.md: cor de estado nunca decora. Verde só para Concluido, vermelho só para
  // Cancelado; os intermediários ficam neutros porque "em produção" não exige decisão de ninguém.
  it('reserva verde e vermelho, e deixa os intermediarios neutros', () => {
    expect(tomDoStatus('Concluido')).toBe('positivo')
    expect(tomDoStatus('Cancelado')).toBe('negativo')
    expect(tomDoStatus('Aberto')).toBe('neutro')
    expect(tomDoStatus('EmProducao')).toBe('neutro')
    expect(tomDoStatus('AguardandoExpedicao')).toBe('neutro')
    // Valor que o domínio não tem: neutro, nunca uma cor de estado por acidente.
    expect(tomDoStatus('QualquerCoisa')).toBe('neutro')
  })

  it('rotuloDoStatus traduz os cinco status', () => {
    expect(rotuloDoStatus('Aberto')).toBe('Aberto')
    expect(rotuloDoStatus('EmProducao')).toBe('Em produção')
    expect(rotuloDoStatus('AguardandoExpedicao')).toBe('Aguardando expedição')
    expect(rotuloDoStatus('Concluido')).toBe('Concluído')
    expect(rotuloDoStatus('Cancelado')).toBe('Cancelado')
  })

  it('rotuloDoStatus devolve status desconhecido como veio', () => {
    expect(rotuloDoStatus('QualquerCoisa')).toBe('QualquerCoisa')
  })
})
