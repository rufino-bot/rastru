import { describe, it, expect } from 'vitest'
import type { PecaNaExpedicaoDto, PedidoNaExpedicaoDto, SituacaoDaAfericao } from '../api/expedicao'
import type { Faceta } from '../components/FiltroDeDemanda'
import { CHAVES_DA_EXPEDICAO, ROTULOS_DA_AFERICAO, facetasDaExpedicao, filtrarExpedicao } from './filtroDaExpedicao'

function peca(id: number, situacao: SituacaoDaAfericao): PecaNaExpedicaoDto {
  return {
    id, descricao: `Peça ${id}`, codigoDoComponente: null, agrupamentoId: 1, agrupamentoCodigo: 'AGR-1', quantidade: 10,
    requerRelatorioDimensional: situacao !== 'SemRelatorio', situacao, afericao: null,
    posicoes: { naExpedicao: 6, conforme: 0, naoConforme: 0, aguardandoLiberacao: 0, liberada: 0, emConserto: 0, expedido: 0 },
    consertos: [], liberacaoPendente: null, expedivel: 6,
  }
}

function pedido(id: number, numero: string, cliente: string, pecas: PecaNaExpedicaoDto[]): PedidoNaExpedicaoDto {
  return { id, numero, cliente, dataEntrega: '2026-10-22', atrasado: false, pausa: null, pecas }
}

// O Pedido 2 vem antes na lista (prazo mais próximo): a ordem das opções de Pedido é pelo número, não pela da API.
const PED_2 = pedido(2, 'PED-2026-02', 'Beta Máquinas', [peca(21, 'Conforme'), peca(22, 'Pendente')])
const PED_1 = pedido(1, 'PED-2026-01', 'Metalúrgica Alfa', [peca(11, 'NaoConforme'), peca(12, 'Pendente'), peca(13, 'SemRelatorio')])
const TODOS = [PED_2, PED_1]

function faceta(facetas: Faceta[], chave: string): Faceta {
  const achada = facetas.find((f) => f.chave === chave)
  if (!achada) throw new Error(`faceta ${chave} ausente`)
  return achada
}

const contagens = (f: Faceta) => Object.fromEntries(f.opcoes.map((o) => [o.valor, o.contagem]))
const ids = (pedidos: PedidoNaExpedicaoDto[]) => pedidos.map((p) => [p.id, p.pecas.map((x) => x.id)])

describe('CHAVES_DA_EXPEDICAO', () => {
  it('são as duas facetas, com as chaves que vão para a URL', () => {
    expect([...CHAVES_DA_EXPEDICAO]).toEqual(['pedido', 'afericao'])
  })
})

describe('facetasDaExpedicao', () => {
  it('as facetas são Pedido e Aferição, com contagem por Peça e os rótulos das pílulas do cartão', () => {
    const facetas = facetasDaExpedicao(TODOS)

    expect(facetas.map((f) => [f.chave, f.titulo])).toEqual([['pedido', 'Pedido'], ['afericao', 'Aferição']])
    expect(faceta(facetas, 'pedido').opcoes).toEqual([
      { valor: '1', rotulo: 'PED-2026-01 · Metalúrgica Alfa', rotuloCurto: 'Pedido PED-2026-01', contagem: 3 },
      { valor: '2', rotulo: 'PED-2026-02 · Beta Máquinas', rotuloCurto: 'Pedido PED-2026-02', contagem: 2 },
    ])
    // Ordem fixa da tabela 3.2, não a de aparição.
    expect(faceta(facetas, 'afericao').opcoes).toEqual([
      { valor: 'SemRelatorio', rotulo: 'Sem relatório', contagem: 1 },
      { valor: 'Pendente', rotulo: 'Aferição pendente', contagem: 2 },
      { valor: 'Conforme', rotulo: 'Conforme', contagem: 1 },
      { valor: 'NaoConforme', rotulo: 'Não conforme', contagem: 1 },
    ])
  })

  it('os rótulos da Aferição são os textos exatos da tela', () => {
    expect(ROTULOS_DA_AFERICAO).toEqual({
      SemRelatorio: 'Sem relatório', Pendente: 'Aferição pendente', Conforme: 'Conforme', NaoConforme: 'Não conforme',
    })
  })

  it('só aparecem as situações que a lista tem', () => {
    expect(faceta(facetasDaExpedicao([PED_2]), 'afericao').opcoes.map((o) => o.valor)).toEqual(['Pendente', 'Conforme'])
  })

  it('a contagem de uma faceta respeita a seleção da outra, e a lista de opções não encolhe', () => {
    const facetas = facetasDaExpedicao(TODOS, { afericao: ['Pendente'] })

    expect(contagens(faceta(facetas, 'pedido'))).toEqual({ 1: 1, 2: 1 })
    // A da própria faceta troca a seleção dela pela opção.
    expect(contagens(faceta(facetas, 'afericao'))).toEqual({ SemRelatorio: 1, Pendente: 2, Conforme: 1, NaoConforme: 1 })

    expect(contagens(faceta(facetasDaExpedicao(TODOS, { pedido: ['2'] }), 'afericao')))
      .toEqual({ SemRelatorio: 0, Pendente: 1, Conforme: 1, NaoConforme: 0 })
  })
})

describe('filtrarExpedicao', () => {
  it('OU dentro da faceta, E entre facetas, e faceta vazia não restringe', () => {
    expect(ids(filtrarExpedicao(TODOS, { afericao: ['Pendente', 'Conforme'] }))).toEqual([[2, [21, 22]], [1, [12]]])
    expect(ids(filtrarExpedicao(TODOS, { afericao: ['Pendente'], pedido: ['1'] }))).toEqual([[1, [12]]])
    expect(ids(filtrarExpedicao(TODOS, { pedido: ['1', '2'] }))).toEqual([[2, [21, 22]], [1, [11, 12, 13]]])
    expect(ids(filtrarExpedicao(TODOS, { afericao: [] }))).toEqual([[2, [21, 22]], [1, [11, 12, 13]]])
    expect(filtrarExpedicao(TODOS, {})).toEqual(TODOS)
  })

  it('Pedido sem Peça que casa sai da lista; a ordem dos Pedidos e das Peças é a da API', () => {
    expect(ids(filtrarExpedicao(TODOS, { afericao: ['NaoConforme'] }))).toEqual([[1, [11]]])
    expect(filtrarExpedicao(TODOS, { afericao: ['NaoConforme'], pedido: ['2'] })).toEqual([])
  })

  it('não altera a lista de entrada', () => {
    filtrarExpedicao(TODOS, { afericao: ['Conforme'] })

    expect(PED_2.pecas.map((p) => p.id)).toEqual([21, 22])
  })
})
