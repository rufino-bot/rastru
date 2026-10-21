import { describe, it, expect } from 'vitest'
import type { PecaNaExpedicaoDto, PedidoNaExpedicaoDto } from '../api/expedicao'
import {
  SAIU_DO_LOTE, alternar, campoDaPeca, camposInvalidos, digitar, estaMarcado, itensDoLote, marcadosDoLote, marcarTodos,
  pecasDosPedidos, podeEntrarNoLote, reconciliar, todosMarcados, type LoteDaExpedicao,
} from './loteDaExpedicao'

function peca(id: number, expedivel: number, aguardandoLiberacao = 0): PecaNaExpedicaoDto {
  return {
    id, descricao: `Peça ${id}`, codigoDoComponente: null, agrupamentoId: 1, agrupamentoCodigo: 'AGR-1', quantidade: 10,
    requerRelatorioDimensional: false, situacao: 'SemRelatorio', afericao: null,
    posicoes: {
      naExpedicao: expedivel, conforme: 0, naoConforme: 0, aguardandoLiberacao, liberada: 0, emConserto: 0, expedido: 0,
    },
    consertos: [], liberacaoPendente: null, expedivel,
  }
}

const TAMPA = peca(30, 6)
const QUADRO = peca(40, 2.5)
const CHAPA = peca(50, 4)
// Tudo aguardando a Gestão: está na lista, mas não tem o que expedir.
const SO_AGUARDANDO = peca(60, 0, 3)

describe('campo de uma Peça marcada', () => {
  it('o campo intocado mostra o expedível atual; o mexido mostra o texto', () => {
    expect(campoDaPeca(QUADRO, null)).toEqual({ texto: '2,5', leitura: { valor: 2.5, erro: null } })
    expect(campoDaPeca(QUADRO, '1')).toEqual({ texto: '1', leitura: { valor: 1, erro: null } })
  })

  it('aceita vírgula decimal', () => {
    expect(campoDaPeca(TAMPA, '0,5').leitura).toEqual({ valor: 0.5, erro: null })
    expect(campoDaPeca(TAMPA, '2.5').leitura).toEqual({ valor: 2.5, erro: null })
  })

  it('acima do expedível, zero ou texto que não é número é inválido', () => {
    expect(campoDaPeca(QUADRO, '3').leitura).toEqual({ valor: null, erro: 'No máximo 2,5.' })
    expect(campoDaPeca(QUADRO, '0').leitura.erro).toBe('A quantidade precisa ser maior que zero.')
    expect(campoDaPeca(QUADRO, 'dois').leitura.erro).toBe('Digite um número com no máximo quatro casas decimais.')
  })

  it('os campos são inválidos com algum erro, ou sem nenhuma Peça marcada', () => {
    const lote = digitar(alternar(alternar({}, 30, true), 40, true), 30, '0,5')
    expect(camposInvalidos(marcadosDoLote(lote, [TAMPA, QUADRO]))).toBe(false)

    expect(camposInvalidos(marcadosDoLote(digitar(lote, 40, '3'), [TAMPA, QUADRO]))).toBe(true)
    expect(camposInvalidos(marcadosDoLote({}, [TAMPA, QUADRO]))).toBe(true)
  })
})

describe('seleção do lote', () => {
  it('marcar não traz texto; marcar de novo não apaga o digitado; desmarcar tira a Peça', () => {
    let lote: LoteDaExpedicao = {}
    lote = alternar(lote, 30, true)
    expect(lote).toEqual({ 30: null })
    expect(estaMarcado(lote, 30)).toBe(true)
    lote = digitar(lote, 30, '1')
    expect(alternar(lote, 30, true)).toBe(lote)
    expect(alternar(lote, 30, false)).toEqual({})
    expect(alternar({}, 30, false)).toEqual({})
    // Digitar numa Peça que não está marcada não a marca.
    expect(digitar({}, 40, '1')).toEqual({})
  })

  it('só entra no lote a Peça com algo a expedir', () => {
    expect(podeEntrarNoLote(TAMPA)).toBe(true)
    expect(podeEntrarNoLote(SO_AGUARDANDO)).toBe(false)
  })

  it('"Marcar todos" marca só as visíveis que podem entrar, sem reescrever o digitado; com todas marcadas, desmarca só elas', () => {
    const comOculta = digitar(alternar({}, 30, true), 30, '1')   // a Tampa marcada, e o filtro vai escondê-la

    const marcados = marcarTodos(digitar(alternar(comOculta, 40, true), 40, '2'), [QUADRO, CHAPA, SO_AGUARDANDO])
    expect(marcados).toEqual({ 30: '1', 40: '2', 50: null })
    expect(todosMarcados(marcados, [QUADRO, CHAPA, SO_AGUARDANDO])).toBe(true)

    expect(marcarTodos(marcados, [QUADRO, CHAPA, SO_AGUARDANDO])).toEqual({ 30: '1' })
    expect(todosMarcados({}, [])).toBe(false)
    expect(todosMarcados({}, [SO_AGUARDANDO])).toBe(false)
  })

  it('a recarga tira da seleção a Peça que saiu da lista inteira, e não reescreve o texto das que ficam', () => {
    const lote: LoteDaExpedicao = { 30: '1', 40: null }

    expect(reconciliar(lote, [TAMPA, QUADRO])).toEqual({ lote, saiu: false })
    expect(reconciliar(lote, [TAMPA, CHAPA])).toEqual({ lote: { 30: '1' }, saiu: true })
    expect(reconciliar(lote, [CHAPA])).toEqual({ lote: {}, saiu: true })
    expect(SAIU_DO_LOTE).toBe(
      'Uma Peça que você tinha marcado não tem mais o que expedir: outra pessoa pode tê-la expedido. Confira a seleção.',
    )
  })

  it('a recarga também tira a Peça que continua na lista mas ficou sem nada a expedir', () => {
    const lote: LoteDaExpedicao = { 30: '1', 60: null }

    expect(reconciliar(lote, [TAMPA, SO_AGUARDANDO])).toEqual({ lote: { 30: '1' }, saiu: true })
  })

  it('o campo intocado acompanha o expedível da recarga; o digitado fica, e passa a inválido se ficou acima', () => {
    const lote: LoteDaExpedicao = { 30: null, 40: '2' }
    const depois = [peca(30, 4), peca(40, 1)]

    const marcados = marcadosDoLote(reconciliar(lote, depois).lote, depois)
    expect(marcados.map((m) => m.campo.texto)).toEqual(['4', '2'])
    expect(camposInvalidos(marcados)).toBe(true)
  })
})

describe('corpo do lote', () => {
  it('segue a ordem da lista, não a da marcação, e leva a quantidade lida de cada campo', () => {
    const lote = digitar(alternar(alternar({}, 40, true), 30, true), 30, '0,5')

    expect(itensDoLote(marcadosDoLote(lote, [TAMPA, QUADRO, CHAPA]))).toEqual([
      { estruturaItemId: 30, quantidade: 0.5 },
      { estruturaItemId: 40, quantidade: 2.5 },
    ])
  })

  it('pecasDosPedidos achata as Peças na ordem da lista, que é a ordem da tela', () => {
    const pedidos: PedidoNaExpedicaoDto[] = [
      { id: 2, numero: 'P2', cliente: 'B', dataEntrega: '2026-10-22', atrasado: false, pausa: null, pecas: [CHAPA] },
      { id: 1, numero: 'P1', cliente: 'A', dataEntrega: '2026-10-23', atrasado: false, pausa: null, pecas: [TAMPA, QUADRO] },
    ]

    expect(pecasDosPedidos(pedidos).map((p) => p.id)).toEqual([50, 30, 40])
  })
})
