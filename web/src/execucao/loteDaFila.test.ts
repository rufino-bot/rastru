import { describe, it, expect } from 'vitest'
import type { GrupoAguardandoMontagem, LinhaDaFila } from '../api/execucao'
import { fila, no, CHASSI, SUPORTE, PARAFUSO } from '../testes/execucao'
import {
  BLOQUEIO_PEDIDO_PAUSADO, BLOQUEIO_SEM_FILHOS, TITULO_DA_SECAO,
  alternar, chaveDeIniciar, chaveDeIniciarPai, chaveDeTerminar, erroDaLinha, itensDeInicio, itensDeTermino,
  linhasDoLote, marcarTodos, reconciliar, todosMarcados, type LinhaDoLote, type Lote,
} from './loteDaFila'

const PAUSA = { desde: '2026-09-30T09:00:00-03:00', porUsuarioNome: 'PCP', motivo: null }

function linha(n: ReturnType<typeof no>, ordem: number, quantidade: number): LinhaDaFila {
  return { no: n, ordem, quantidade, estornaveis: [] }
}

function grupo(parcial: Partial<GrupoAguardandoMontagem> = {}): GrupoAguardandoMontagem {
  return {
    pai: CHASSI, faltaMontar: 10, daParaMontar: 3, iniciaAqui: true, primeiroPassoDoPai: null, conjuntoCompleto: false, filhos: [],
    ...parcial,
  }
}

// Uma fila com as três seções do lote: Terminar (Suporte), Iniciar (Parafuso e Suporte) e o pai.
function filaCompleta() {
  return fila({
    emTrabalho: [linha(SUPORTE, 1, 4)],
    aIniciar: [linha(PARAFUSO, 1, 6), linha(SUPORTE, 1, 2.5)],
    aguardandoMontagem: [grupo()],
  })
}

function linhaDe(linhas: LinhaDoLote[], chave: string): LinhaDoLote {
  const l = linhas.find((x) => x.chave === chave)
  if (!l) throw new Error(`sem a linha ${chave}`)
  return l
}

describe('loteDaFila', () => {
  it('as chaves mantem o formato que a fila ja usava', () => {
    expect(chaveDeIniciar(7, 1)).toBe('iniciar:7:1')
    expect(chaveDeTerminar(7, 2)).toBe('terminar:7:2')
    expect(chaveDeIniciarPai(2)).toBe('iniciar-pai:2')
  })

  it('os titulos das secoes sao os da tela', () => {
    expect(TITULO_DA_SECAO).toEqual({
      emTrabalho: 'Em trabalho', aIniciar: 'A iniciar aqui', aguardandoMontagem: 'Aguardando montagem',
    })
  })

  it('linhas do lote cobrem em trabalho, a iniciar e o pai que inicia aqui, nessa ordem', () => {
    const linhas = linhasDoLote(filaCompleta())

    expect(linhas).toEqual([
      { chave: 'terminar:7:1', secao: 'emTrabalho', noId: 7, ordem: 1, maximo: 4, bloqueio: null },
      { chave: 'iniciar:8:1', secao: 'aIniciar', noId: 8, ordem: 1, maximo: 6, bloqueio: null },
      { chave: 'iniciar:7:1', secao: 'aIniciar', noId: 7, ordem: 1, maximo: 2.5, bloqueio: null },
      { chave: 'iniciar-pai:2', secao: 'aguardandoMontagem', noId: 2, ordem: null, maximo: 3, bloqueio: null },
    ])
  })

  it('cartao que nao inicia aqui nao entra no lote', () => {
    const linhas = linhasDoLote(fila({
      aguardandoMontagem: [grupo({ iniciaAqui: false, primeiroPassoDoPai: { id: 4, nome: 'Solda' } })],
    }))

    expect(linhas).toEqual([])
  })

  it('pausado e da para iniciar zero entram bloqueados com o motivo', () => {
    const pausado = no({ id: 9, pausa: PAUSA })
    const linhas = linhasDoLote(fila({
      aIniciar: [linha(pausado, 1, 5)],
      aguardandoMontagem: [
        grupo({ pai: no({ id: 20, pausa: PAUSA }) }),
        grupo({ pai: no({ id: 21 }), daParaMontar: 0 }),
      ],
    }))

    expect(linhaDe(linhas, 'iniciar:9:1').bloqueio).toBe(BLOQUEIO_PEDIDO_PAUSADO)
    expect(linhaDe(linhas, 'iniciar-pai:20').bloqueio).toBe(BLOQUEIO_PEDIDO_PAUSADO)
    expect(linhaDe(linhas, 'iniciar-pai:21').bloqueio).toBe(BLOQUEIO_SEM_FILHOS)
    expect(linhaDe(linhas, 'iniciar-pai:21').maximo).toBe(0)
  })

  it('marcar preenche o saldo e o pai com o da para iniciar', () => {
    const linhas = linhasDoLote(filaCompleta())

    const lote = alternar(null, linhaDe(linhas, 'iniciar:7:1'), true)
    expect(lote).toEqual({ secao: 'aIniciar', quantidades: { 'iniciar:7:1': '2,5' } })

    const doPai = alternar(null, linhaDe(linhas, 'iniciar-pai:2'), true)
    expect(doPai).toEqual({ secao: 'aguardandoMontagem', quantidades: { 'iniciar-pai:2': '3' } })
  })

  it('marcar em outra secao e marcar bloqueada nao mudam o lote', () => {
    const linhas = linhasDoLote(fila({
      aIniciar: [linha(PARAFUSO, 1, 6), linha(no({ id: 9, pausa: PAUSA }), 1, 5)],
      emTrabalho: [linha(SUPORTE, 1, 4)],
    }))
    const lote = alternar(null, linhaDe(linhas, 'iniciar:8:1'), true)

    expect(alternar(lote, linhaDe(linhas, 'terminar:7:1'), true)).toBe(lote)
    expect(alternar(lote, linhaDe(linhas, 'iniciar:9:1'), true)).toBe(lote)
    // Sem lote nenhum, a bloqueada também não abre um.
    expect(alternar(null, linhaDe(linhas, 'iniciar:9:1'), true)).toBeNull()
  })

  it('desmarcar a bloqueada que ja estava marcada funciona', () => {
    const bloqueada = { ...linhaDe(linhasDoLote(filaCompleta()), 'iniciar:8:1'), bloqueio: BLOQUEIO_PEDIDO_PAUSADO }
    const lote: Lote = { secao: 'aIniciar', quantidades: { 'iniciar:8:1': '6', 'iniciar:7:1': '2,5' } }

    expect(alternar(lote, bloqueada, false)).toEqual({ secao: 'aIniciar', quantidades: { 'iniciar:7:1': '2,5' } })
  })

  it('desmarcar o ultimo devolve null', () => {
    const linhas = linhasDoLote(filaCompleta())
    const lote = alternar(null, linhaDe(linhas, 'iniciar:8:1'), true)

    expect(alternar(lote, linhaDe(linhas, 'iniciar:8:1'), false)).toBeNull()
    // Desmarcar o que nunca esteve marcado não inventa lote.
    expect(alternar(null, linhaDe(linhas, 'iniciar:8:1'), false)).toBeNull()
  })

  it('marcar todos marca so as visiveis nao bloqueadas e preserva o texto ja digitado', () => {
    const linhas = linhasDoLote(fila({
      aIniciar: [linha(PARAFUSO, 1, 6), linha(SUPORTE, 1, 2.5), linha(no({ id: 9, pausa: PAUSA }), 1, 5)],
    }))
    const visiveis = linhas
    const lote: Lote = { secao: 'aIniciar', quantidades: { 'iniciar:8:1': '1' } }

    const marcado = marcarTodos(lote, 'aIniciar', visiveis)

    expect(marcado).toEqual({ secao: 'aIniciar', quantidades: { 'iniciar:8:1': '1', 'iniciar:7:1': '2,5' } })
    // Sem lote, abre um na seção pedida.
    expect(marcarTodos(null, 'aIniciar', visiveis)?.secao).toBe('aIniciar')
    // Seção diferente da do lote: não muda nada.
    expect(marcarTodos(lote, 'emTrabalho', visiveis)).toBe(lote)
  })

  it('marcar todos sem nenhuma linha livre nao abre lote', () => {
    const bloqueada = { ...linhaDe(linhasDoLote(filaCompleta()), 'iniciar:8:1'), bloqueio: BLOQUEIO_PEDIDO_PAUSADO }

    expect(marcarTodos(null, 'aIniciar', [bloqueada])).toBeNull()
    expect(marcarTodos(null, 'aIniciar', [])).toBeNull()
  })

  it('desmarcar todos desmarca so as visiveis', () => {
    const linhas = linhasDoLote(fila({ aIniciar: [linha(PARAFUSO, 1, 6), linha(SUPORTE, 1, 2.5)] }))
    const lote: Lote = { secao: 'aIniciar', quantidades: { 'iniciar:8:1': '6', 'iniciar:7:1': '2,5' } }
    const visiveis = [linhaDe(linhas, 'iniciar:7:1')]

    expect(todosMarcados(lote, 'aIniciar', visiveis)).toBe(true)
    // O marcado fora de `visiveis` (oculto pelo filtro) continua.
    expect(marcarTodos(lote, 'aIniciar', visiveis)).toEqual({ secao: 'aIniciar', quantidades: { 'iniciar:8:1': '6' } })
    // Desmarcar as visiveis quando elas eram o lote inteiro solta a trava.
    const so: Lote = { secao: 'aIniciar', quantidades: { 'iniciar:7:1': '2,5' } }
    expect(marcarTodos(so, 'aIniciar', visiveis)).toBeNull()
  })

  it('todos marcados ignora as bloqueadas', () => {
    const linhas = linhasDoLote(fila({
      aIniciar: [linha(PARAFUSO, 1, 6), linha(no({ id: 9, pausa: PAUSA }), 1, 5)],
    }))
    const lote: Lote = { secao: 'aIniciar', quantidades: { 'iniciar:8:1': '6' } }

    expect(todosMarcados(lote, 'aIniciar', linhas)).toBe(true)
    expect(todosMarcados(null, 'aIniciar', linhas)).toBe(false)
    expect(todosMarcados(lote, 'emTrabalho', linhas)).toBe(false)
    // Sem linha livre, não há o que estar "todo marcado".
    expect(todosMarcados(lote, 'aIniciar', [linhas[1]])).toBe(false)
    expect(todosMarcados(lote, 'aIniciar', [])).toBe(false)
  })

  it('erro da linha e o bloqueio antes da quantidade', () => {
    const livre = linhaDe(linhasDoLote(filaCompleta()), 'iniciar:8:1')
    const bloqueada = { ...livre, bloqueio: BLOQUEIO_PEDIDO_PAUSADO }

    expect(erroDaLinha(bloqueada, '1')).toBe(BLOQUEIO_PEDIDO_PAUSADO)
    expect(erroDaLinha(livre, '6')).toBeNull()
    expect(erroDaLinha(livre, '0')).toBe('A quantidade precisa ser maior que zero.')
    expect(erroDaLinha(livre, 'abc')).toBe('Digite um número com no máximo quatro casas decimais.')
  })

  it('reconciliar tira o que saiu da resposta e mantem o que ficou bloqueado', () => {
    const inicial = linhasDoLote(filaCompleta())
    const lote: Lote = {
      secao: 'aIniciar', quantidades: { 'iniciar:8:1': '6', 'iniciar:7:1': '2,5' },
    }
    // O Parafuso saiu da fila; o Suporte continua, agora com o Pedido pausado.
    const agora = linhasDoLote(fila({ aIniciar: [linha(no({ id: 7, pausa: PAUSA }), 1, 2.5)] }))

    const { lote: depois, saiu } = reconciliar(lote, agora)

    expect(saiu).toBe(true)
    expect(depois).toEqual({ secao: 'aIniciar', quantidades: { 'iniciar:7:1': '2,5' } })
    expect(erroDaLinha(linhaDe(agora, 'iniciar:7:1'), '2,5')).toBe(BLOQUEIO_PEDIDO_PAUSADO)
    expect(inicial).toHaveLength(4)
  })

  it('reconciliar sem nada que saiu devolve o mesmo lote e diz que nao saiu', () => {
    const linhas = linhasDoLote(filaCompleta())
    const lote: Lote = { secao: 'aIniciar', quantidades: { 'iniciar:7:1': '2,5' } }

    expect(reconciliar(lote, linhas)).toEqual({ lote, saiu: false })
  })

  it('reconciliar devolve null quando tudo saiu', () => {
    const lote: Lote = { secao: 'aIniciar', quantidades: { 'iniciar:8:1': '6' } }

    expect(reconciliar(lote, [])).toEqual({ lote: null, saiu: true })
  })

  it('reconciliar nao reescreve o texto quando o maximo cai', () => {
    const lote: Lote = { secao: 'aIniciar', quantidades: { 'iniciar:7:1': '2,5' } }
    const agora = linhasDoLote(fila({ aIniciar: [linha(SUPORTE, 1, 1)] }))

    expect(reconciliar(lote, agora).lote).toEqual(lote)
  })

  it('erro da linha acusa o texto acima do maximo novo', () => {
    const agora = linhasDoLote(fila({ aIniciar: [linha(SUPORTE, 1, 1)] }))

    expect(erroDaLinha(linhaDe(agora, 'iniciar:7:1'), '2,5')).toBe('No máximo 1.')
  })

  it('itens de inicio e de termino saem na ordem das linhas com a quantidade lida', () => {
    const linhas = linhasDoLote(filaCompleta())

    // A ordem do `quantidades` é a da marcação; a do lote enviado é a das linhas.
    const inicio: Lote = {
      secao: 'aIniciar', quantidades: { 'iniciar:7:1': '2,5', 'iniciar:8:1': '4' },
    }
    expect(itensDeInicio(inicio, linhas)).toEqual([
      { estruturaItemId: 8, quantidade: 4 },
      { estruturaItemId: 7, quantidade: 2.5 },
    ])

    const pai: Lote = { secao: 'aguardandoMontagem', quantidades: { 'iniciar-pai:2': '2' } }
    expect(itensDeInicio(pai, linhas)).toEqual([{ estruturaItemId: 2, quantidade: 2 }])

    const termino: Lote = { secao: 'emTrabalho', quantidades: { 'terminar:7:1': '3' } }
    expect(itensDeTermino(termino, linhas)).toEqual([{ estruturaItemId: 7, ordem: 1, quantidade: 3 }])
  })
})
