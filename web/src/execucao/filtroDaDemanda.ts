import type {
  FilaDoSetorDto, GrupoAguardandoMontagem, KitDto, NoResumoDto, TarefasDoSetorDto, TarefasDto,
} from '../api/execucao'
import { casaComFiltro, type Faceta, type Selecao } from '../components/FiltroDeDemanda'

/** As chaves da query string: `?material=3,5&pedido=42`. Também são as chaves das facetas. */
export const CHAVES_DA_DEMANDA = ['material', 'pedido'] as const

/**
 * A unidade de contagem e de casamento do filtro: os nós de uma linha (ela mesma) ou de um cartão
 * de montagem (o pai e os filhos que estão presentes neste Setor). Uma unidade casa quando ALGUM
 * nó dela casa; por isso o cartão que casa aparece inteiro, e conta uma vez só por opção.
 */
type Unidade = readonly NoResumoDto[]

function casa(unidade: Unidade, selecao: Selecao): boolean {
  return unidade.some((no) => casaComFiltro(
    { material: no.materiais.map((m) => String(m.id)), pedido: [String(no.pedidoId)] },
    selecao,
  ))
}

const porTexto = (a: string, b: string) => a.localeCompare(b, 'pt-BR', { numeric: true })

/**
 * As opções são o que existe nas unidades SEM filtro nenhum, para a lista não pular enquanto o
 * operador marca. A contagem de uma opção troca a seleção DESTA faceta só por ela e deixa as outras
 * como estão: a opção que daria lista vazia mostra 0.
 */
function facetasDeUnidades(unidades: readonly Unidade[], selecao: Selecao): Faceta[] {
  const materiais = new Map<number, { codigo: string; descricao: string }>()
  const pedidos = new Map<number, { numero: string; cliente: string }>()
  for (const unidade of unidades) {
    for (const no of unidade) {
      for (const m of no.materiais) materiais.set(m.id, { codigo: m.codigo, descricao: m.descricao })
      pedidos.set(no.pedidoId, { numero: no.pedidoNumero, cliente: no.pedidoCliente })
    }
  }

  const contar = (chave: string, valor: string) =>
    unidades.filter((u) => casa(u, { ...selecao, [chave]: [valor] })).length

  return [
    {
      chave: 'material',
      titulo: 'Material',
      opcoes: [...materiais]
        .sort(([, a], [, b]) => porTexto(a.descricao, b.descricao) || porTexto(a.codigo, b.codigo))
        .map(([id, m]) => ({
          valor: String(id), rotulo: m.descricao, detalhe: m.codigo, contagem: contar('material', String(id)),
        })),
    },
    {
      chave: 'pedido',
      titulo: 'Pedido',
      opcoes: [...pedidos]
        .sort(([, a], [, b]) => porTexto(a.numero, b.numero))
        .map(([id, p]) => ({
          valor: String(id),
          rotulo: `${p.numero} · ${p.cliente}`,
          rotuloCurto: `Pedido ${p.numero}`,
          contagem: contar('pedido', String(id)),
        })),
    },
  ]
}

/** O pai e os filhos que estão presentes neste Setor: o filho ausente não faz o cartão casar. */
function nosDoCartao(g: GrupoAguardandoMontagem): Unidade {
  return [g.pai, ...g.filhos.filter((f) => f.presente > 0).map((f) => f.no)]
}

/** Cada linha da fila é uma unidade; cada cartão de montagem, outra. */
function unidadesDaFila(fila: FilaDoSetorDto): Unidade[] {
  return [
    ...fila.emTrabalho.map((l) => [l.no]),
    ...fila.aIniciar.map((l) => [l.no]),
    ...fila.aguardandoMontagem.map(nosDoCartao),
    ...fila.aguardandoColeta.map((l) => [l.no]),
    ...fila.sobra.map((s) => [s.no]),
  ]
}

export function facetasDaFila(fila: FilaDoSetorDto, selecao: Selecao): Faceta[] {
  return facetasDeUnidades(unidadesDaFila(fila), selecao)
}

/** A fila que sobra sob a seleção: mesmas seções, mesma ordem dentro de cada uma. */
export function filtrarFila(fila: FilaDoSetorDto, selecao: Selecao): FilaDoSetorDto {
  return {
    ...fila,
    emTrabalho: fila.emTrabalho.filter((l) => casa([l.no], selecao)),
    aIniciar: fila.aIniciar.filter((l) => casa([l.no], selecao)),
    aguardandoMontagem: fila.aguardandoMontagem.filter((g) => casa(nosDoCartao(g), selecao)),
    aguardandoColeta: fila.aguardandoColeta.filter((l) => casa([l.no], selecao)),
    sobra: fila.sobra.filter((s) => casa([s.no], selecao)),
  }
}

/**
 * O pai e os filhos prontos: o Material mora nos filhos (decisão P5 do plano da Fase 3B; mesmo critério de
 * `nosDoCartao`). O filho que ainda não tem nada pronto não faz o Kit casar.
 */
function nosDoKit(k: KitDto): Unidade {
  return [k.pai, ...k.filhos.filter((f) => f.pronto > 0).map((f) => f.no)]
}

/** Cada item solto é uma unidade; cada Kit, montável ou incompleto, outra. */
export function facetasDasTarefas(tarefas: TarefasDto, selecao: Selecao): Faceta[] {
  return facetasDeUnidades([
    ...tarefas.grupos.flatMap((g) => g.itens.map((i) => [i.no])),
    ...tarefas.kitsMontaveis.map(nosDoKit),
    ...tarefas.kitsIncompletos.map(nosDoKit),
  ], selecao)
}

/** Os Kits que sobram sob a seleção, na ordem em que vieram. */
export function filtrarKits(kits: KitDto[], selecao: Selecao): KitDto[] {
  return kits.filter((k) => casa(nosDoKit(k), selecao))
}

/** Grupo que fica sem item some; os itens de cada grupo mantêm a ordem. */
export function filtrarTarefas(grupos: TarefasDoSetorDto[], selecao: Selecao): TarefasDoSetorDto[] {
  return grupos
    .map((g) => ({ ...g, itens: g.itens.filter((i) => casa([i.no], selecao)) }))
    .filter((g) => g.itens.length > 0)
}
