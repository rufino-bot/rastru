import { describe, it, expect } from 'vitest'
import {
  CHAVES_DA_DEMANDA, facetasDaFila, filtrarFila, facetasDasTarefas, filtrarTarefas,
} from './filtroDaDemanda'
import type { LinhaDaFila, TarefasDoSetorDto } from '../api/execucao'
import type { MaterialResumoDto } from '../api/cadastros'
import type { Faceta } from '../components/FiltroDeDemanda'
import { destino, fila, no } from '../testes/execucao'

const CHAPA_3: MaterialResumoDto = { id: 3, codigo: 'CH-300', descricao: 'Chapa SAE 1020 3,00 mm' }
const CHAPA_6: MaterialResumoDto = { id: 6, codigo: 'CH-600', descricao: 'Chapa SAE 1020 6,00 mm' }

const PEDIDO_2 = { pedidoId: 2, pedidoNumero: 'PED-2026-02', pedidoCliente: 'Beta Máquinas' }

// Pedido 1 (PED-2026-01, Metalúrgica Alfa) e Pedido 2 (PED-2026-02, Beta Máquinas).
const P1_CH3 = no({ id: 11, descricao: 'Suporte', materiais: [CHAPA_3] })
const P1_CH3_B = no({ id: 12, descricao: 'Lateral', materiais: [CHAPA_3] })
const P1_CH6 = no({ id: 13, descricao: 'Base', materiais: [CHAPA_6] })
const P2_CH3 = no({ id: 21, descricao: 'Tampa', materiais: [CHAPA_3], ...PEDIDO_2 })
const P2_SEM_MATERIAL = no({ id: 22, descricao: 'Parafuso', materiais: [], ...PEDIDO_2 })

const linha = (n: ReturnType<typeof no>, ordem = 1): LinhaDaFila => ({ no: n, ordem, quantidade: 5, estornaveis: [] })

function faceta(facetas: Faceta[], chave: string): Faceta {
  const achada = facetas.find((f) => f.chave === chave)
  if (!achada) throw new Error(`faceta ${chave} ausente`)
  return achada
}

const contagens = (f: Faceta) => Object.fromEntries(f.opcoes.map((o) => [o.valor, o.contagem]))

describe('CHAVES_DA_DEMANDA', () => {
  it('são as duas facetas, com as chaves que vão para a URL', () => {
    expect([...CHAVES_DA_DEMANDA]).toEqual(['material', 'pedido'])
  })
})

describe('facetasDaFila', () => {
  it('facetas da fila listam os materiais e os pedidos presentes com a contagem', () => {
    const f = fila({
      emTrabalho: [linha(P1_CH3)],
      aIniciar: [linha(P1_CH6), linha(P2_CH3)],
    })

    const facetas = facetasDaFila(f, {})

    expect(facetas.map((x) => x.chave)).toEqual(['material', 'pedido'])
    expect(faceta(facetas, 'material').opcoes).toEqual([
      { valor: '3', rotulo: 'Chapa SAE 1020 3,00 mm', detalhe: 'CH-300', contagem: 2 },
      { valor: '6', rotulo: 'Chapa SAE 1020 6,00 mm', detalhe: 'CH-600', contagem: 1 },
    ])
    expect(faceta(facetas, 'pedido').opcoes).toEqual([
      { valor: '1', rotulo: 'PED-2026-01 · Metalúrgica Alfa', rotuloCurto: 'Pedido PED-2026-01', contagem: 2 },
      { valor: '2', rotulo: 'PED-2026-02 · Beta Máquinas', rotuloCurto: 'Pedido PED-2026-02', contagem: 1 },
    ])
  })

  it('as opções de material vêm por descrição e as de pedido por número, ambos com ordem numérica', () => {
    const f = fila({
      aIniciar: [
        linha(no({ id: 31, pedidoId: 10, pedidoNumero: 'PED-10', materiais: [CHAPA_6] })),
        linha(no({ id: 32, pedidoId: 9, pedidoNumero: 'PED-9', materiais: [{ id: 8, codigo: 'X', descricao: 'Chapa 10 mm' }] })),
        linha(no({ id: 33, pedidoId: 2, pedidoNumero: 'PED-2', materiais: [{ id: 9, codigo: 'Y', descricao: 'Chapa 2 mm' }] })),
      ],
    })

    const facetas = facetasDaFila(f, {})

    expect(faceta(facetas, 'pedido').opcoes.map((o) => o.rotuloCurto)).toEqual(['Pedido PED-2', 'Pedido PED-9', 'Pedido PED-10'])
    expect(faceta(facetas, 'material').opcoes.map((o) => o.rotulo)).toEqual([
      'Chapa 2 mm', 'Chapa 10 mm', 'Chapa SAE 1020 6,00 mm',
    ])
  })

  it('material com a mesma descricao e codigos diferentes vira duas opcoes', () => {
    const igual = { id: 4, codigo: 'CH-301', descricao: CHAPA_3.descricao }
    const f = fila({ aIniciar: [linha(P1_CH3), linha(no({ id: 14, materiais: [igual] }))] })

    const opcoes = faceta(facetasDaFila(f, {}), 'material').opcoes

    expect(opcoes.map((o) => o.valor).sort()).toEqual(['3', '4'])
    expect(opcoes.map((o) => o.detalhe).sort()).toEqual(['CH-300', 'CH-301'])
  })

  it('cartao de montagem casa pelo filho presente e conta uma vez por opcao', () => {
    const CHASSI_SEM_MATERIAL = no({ id: 2, descricao: 'Chassi', paiId: null, paiDescricao: null, materiais: [] })
    const cartao = {
      pai: CHASSI_SEM_MATERIAL, faltaMontar: 6, daParaMontar: 2, iniciaAqui: true, primeiroPassoDoPai: null,
      filhos: [
        { no: P1_CH3, quantidadePorPai: 1, presente: 3, necessarioParaProxima: null, faltaParaProxima: null },
        { no: P1_CH3_B, quantidadePorPai: 1, presente: 2, necessarioParaProxima: null, faltaParaProxima: null },
        { no: P1_CH6, quantidadePorPai: 1, presente: 0, necessarioParaProxima: 1, faltaParaProxima: 1 },
      ],
    }
    const f = fila({ aguardandoMontagem: [cartao] })

    const facetas = facetasDaFila(f, {})

    // Dois filhos presentes com CHAPA_3: o cartão conta UMA vez. O filho ausente (CHAPA_6) não é opção.
    expect(faceta(facetas, 'material').opcoes.map((o) => [o.valor, o.contagem])).toEqual([['3', 1]])
    const filtrada = filtrarFila(f, { material: ['3'] })
    expect(filtrada.aguardandoMontagem).toHaveLength(1)
    expect(filtrada.aguardandoMontagem[0].filhos.map((x) => x.no.id)).toEqual([11, 12, 13])
  })

  it('filho ausente do cartao nao faz o cartao casar', () => {
    const cartao = {
      pai: no({ id: 2, paiId: null, paiDescricao: null, materiais: [] }),
      faltaMontar: 6, daParaMontar: 2, iniciaAqui: true, primeiroPassoDoPai: null,
      filhos: [
        { no: P1_CH3, quantidadePorPai: 1, presente: 3, necessarioParaProxima: null, faltaParaProxima: null },
        { no: P1_CH6, quantidadePorPai: 1, presente: 0, necessarioParaProxima: 1, faltaParaProxima: 1 },
      ],
    }
    const f = fila({ aguardandoMontagem: [cartao] })

    expect(filtrarFila(f, { material: ['6'] }).aguardandoMontagem).toEqual([])
  })

  it('o pai do cartao tambem faz o cartao casar, pelo proprio material ou Pedido', () => {
    const cartao = {
      pai: no({ id: 2, paiId: null, paiDescricao: null, materiais: [CHAPA_6] }),
      faltaMontar: 6, daParaMontar: 2, iniciaAqui: true, primeiroPassoDoPai: null,
      filhos: [{ no: P1_CH3, quantidadePorPai: 1, presente: 3, necessarioParaProxima: null, faltaParaProxima: null }],
    }
    const f = fila({ aguardandoMontagem: [cartao] })

    expect(filtrarFila(f, { material: ['6'] }).aguardandoMontagem).toHaveLength(1)
  })

  it('contagem de uma faceta respeita a selecao da outra', () => {
    const f = fila({
      emTrabalho: [linha(P1_CH3), linha(P1_CH3_B)],
      aIniciar: [linha(P1_CH6), linha(P2_CH3)],
    })

    const facetas = facetasDaFila(f, { material: ['3'] })

    // Sem o material marcado seria 3 e 1; com ele, só as linhas de CHAPA_3 contam.
    expect(contagens(faceta(facetas, 'pedido'))).toEqual({ 1: 2, 2: 1 })
    // A LISTA de opções continua sendo a da fila inteira: CHAPA_6 não some enquanto se marca CHAPA_3.
    // Já a contagem de cada material troca a faceta pela própria opção (D2): a de CHAPA_6 conta as linhas dela.
    expect(contagens(faceta(facetas, 'material'))).toEqual({ 3: 3, 6: 1 })

    const comPedido = facetasDaFila(f, { pedido: ['2'] })
    expect(contagens(faceta(comPedido, 'material'))).toEqual({ 3: 1, 6: 0 })
  })

  it('no sem material some com o filtro de material ativo', () => {
    const f = fila({ aIniciar: [linha(P2_SEM_MATERIAL), linha(P1_CH3)] })

    expect(filtrarFila(f, { material: ['3'] }).aIniciar.map((l) => l.no.id)).toEqual([11])
    expect(filtrarFila(f, {}).aIniciar.map((l) => l.no.id)).toEqual([22, 11])
  })

  it('filtrar a fila age em todas as secoes e preserva a ordem de cada uma', () => {
    const destinoColeta = destino()
    const cartao = (pai: ReturnType<typeof no>, filho: ReturnType<typeof no>) => ({
      pai, faltaMontar: 1, daParaMontar: 1, iniciaAqui: true, primeiroPassoDoPai: null,
      filhos: [{ no: filho, quantidadePorPai: 1, presente: 1, necessarioParaProxima: null, faltaParaProxima: null }],
    })
    const f = fila({
      emTrabalho: [linha(P1_CH3), linha(P2_CH3), linha(P1_CH3_B)],
      aIniciar: [linha(P1_CH6), linha(P2_CH3, 2), linha(P1_CH3_B, 2)],
      aguardandoColeta: [
        { ...linha(P2_CH3, 3), destino: destinoColeta },
        { ...linha(P1_CH3, 3), destino: destinoColeta },
        { ...linha(P1_CH3_B, 3), destino: destinoColeta },
      ],
      aguardandoMontagem: [
        cartao(no({ id: 41, pedidoId: 3, pedidoNumero: 'PED-3' }), no({ id: 42, pedidoId: 3, pedidoNumero: 'PED-3' })),
        cartao(no({ id: 43 }), no({ id: 44 })),
        cartao(no({ id: 45 }), no({ id: 46 })),
      ],
      sobra: [
        { no: P2_CH3, origem: 'UltimoPasso', ordem: 1, quantidade: 1, emMaisDeUmSetor: false, estornaveis: [] },
        { no: P1_CH3, origem: 'UltimoPasso', ordem: 1, quantidade: 1, emMaisDeUmSetor: false, estornaveis: [] },
        { no: P1_CH3_B, origem: 'Montagem', ordem: null, quantidade: 1, emMaisDeUmSetor: false, estornaveis: [] },
      ],
    })

    const r = filtrarFila(f, { pedido: ['1'] })

    expect(r.emTrabalho.map((l) => l.no.id)).toEqual([11, 12])
    expect(r.aIniciar.map((l) => l.no.id)).toEqual([13, 12])
    expect(r.aguardandoColeta.map((l) => l.no.id)).toEqual([11, 12])
    expect(r.aguardandoMontagem.map((g) => g.pai.id)).toEqual([43, 45])
    expect(r.sobra.map((s) => s.no.id)).toEqual([11, 12])
    expect(r.setorNome).toBe(f.setorNome)
  })

  it('selecao vazia devolve a fila como veio', () => {
    const f = fila({ aIniciar: [linha(P1_CH3)] })

    expect(filtrarFila(f, {})).toEqual(f)
  })
})

describe('tarefas', () => {
  const item = (n: ReturnType<typeof no>, ordem = 1) => ({ no: n, ordem, quantidade: 2, destino: destino() })
  const GRUPOS: TarefasDoSetorDto[] = [
    { setorId: 1, setorNome: 'Corte', itens: [item(P1_CH3), item(P2_CH3), item(P1_CH3_B)] },
    { setorId: 2, setorNome: 'Solda', itens: [item(P2_SEM_MATERIAL), item(P2_CH3, 2)] },
    { setorId: 3, setorNome: 'Pintura', itens: [item(P1_CH6)] },
  ]

  it('facetas das tarefas contam os itens de todos os grupos', () => {
    // Cada faceta conta sob a seleção da OUTRA: o Pedido 2 tem 3 itens, dois deles de CHAPA_3.
    expect(contagens(faceta(facetasDasTarefas(GRUPOS, { material: ['6'] }), 'pedido'))).toEqual({ 1: 1, 2: 0 })
    expect(contagens(faceta(facetasDasTarefas(GRUPOS, { pedido: ['2'] }), 'material'))).toEqual({ 3: 2, 6: 0 })
    expect(contagens(faceta(facetasDasTarefas(GRUPOS, {}), 'pedido'))).toEqual({ 1: 3, 2: 3 })
  })

  it('tarefas: grupo sem item depois do filtro sai; itens de cada grupo mantem a ordem', () => {
    const r = filtrarTarefas(GRUPOS, { pedido: ['1'], material: ['3'] })

    expect(r.map((g) => g.setorId)).toEqual([1])
    expect(r[0].itens.map((i) => i.no.id)).toEqual([11, 12])
    expect(filtrarTarefas(GRUPOS, { pedido: ['2'] }).map((g) => [g.setorId, g.itens.map((i) => i.no.id)]))
      .toEqual([[1, [21]], [2, [22, 21]]])
  })

  it('sem selecao devolve todos os grupos', () => {
    expect(filtrarTarefas(GRUPOS, {})).toEqual(GRUPOS)
  })
})
