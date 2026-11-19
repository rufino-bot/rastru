import type { PecaDoPanoramaDto } from '../api/panorama'
import { casaComFiltro, normalizarParaBusca, type Faceta, type Selecao } from '../components/FiltroDeDemanda'

/** As chaves da query string (`?pedido=2&embalado=PodeEmbalar`), que também são as chaves das facetas. */
export const CHAVES_DO_PANORAMA = ['pedido', 'setor', 'situacao', 'relatorio', 'embalado'] as const

type EstadoDoEmbalado = 'Embalado' | 'PodeEmbalar' | 'EmProducao'

const ROTULOS_DA_SITUACAO: Record<string, string> = { Atrasado: 'Atrasado', NoPrazo: 'No prazo', Pausado: 'Pausado' }
const ROTULOS_DO_RELATORIO: Record<string, string> = { ExigeRelatorio: 'Exige relatório', SemRelatorio: 'Sem relatório' }
const ROTULOS_DO_EMBALADO: Record<EstadoDoEmbalado, string> = {
  Embalado: 'Embalado', PodeEmbalar: 'Pode embalar', EmProducao: 'Em produção',
}

const porTexto = (a: string, b: string) => a.localeCompare(b, 'pt-BR', { numeric: true })

/** Os Setores da Peça: os passos e os consertos dela e os de "Itens em", sem repetir. */
function setoresDaPeca(peca: PecaDoPanoramaDto): Map<number, string> {
  const setores = new Map<number, string>()
  for (const s of peca.distribuicao) {
    if ((s.tipo === 'Passo' || s.tipo === 'Conserto') && s.setorId !== null) setores.set(s.setorId, s.setorNome ?? '')
  }
  for (const s of peca.itensEm) setores.set(s.setorId, s.nome)
  return setores
}

function situacoesDaPeca(peca: PecaDoPanoramaDto): string[] {
  const situacoes = [peca.pedido.atrasado ? 'Atrasado' : 'NoPrazo']
  if (peca.pedido.pausa) situacoes.push('Pausado')
  return situacoes
}

function estadoDoEmbalado(peca: PecaDoPanoramaDto): EstadoDoEmbalado {
  if (peca.embalagem.vale) return 'Embalado'
  return peca.embalagem.podeMarcar ? 'PodeEmbalar' : 'EmProducao'
}

const valoresDaPeca = (peca: PecaDoPanoramaDto): Record<string, string[]> => ({
  pedido: [String(peca.pedido.id)],
  setor: [...setoresDaPeca(peca).keys()].map(String),
  situacao: situacoesDaPeca(peca),
  relatorio: [peca.afericao.exigeRelatorio ? 'ExigeRelatorio' : 'SemRelatorio'],
  embalado: [estadoDoEmbalado(peca)],
})

/**
 * As facetas do Panorama, cada Peça contada uma vez por opção. Mesmo critério da expedição: as opções são as da
 * lista inteira, para não sumirem enquanto se marca, e a contagem de uma opção troca a seleção DA PRÓPRIA faceta
 * por ela e mantém as outras.
 */
export function facetasDoPanorama(pecas: PecaDoPanoramaDto[], selecao: Selecao = {}): Faceta[] {
  const valores = pecas.map(valoresDaPeca)
  const contar = (chave: string, valor: string) =>
    valores.filter((v) => casaComFiltro(v, { ...selecao, [chave]: [valor] })).length

  const pedidos = new Map<number, string>()
  const setores = new Map<number, string>()
  for (const peca of pecas) {
    pedidos.set(peca.pedido.id, peca.pedido.numero)
    for (const [id, nome] of setoresDaPeca(peca)) setores.set(id, nome)
  }

  const fixas = (chave: string, rotulos: Record<string, string>) =>
    Object.keys(rotulos)
      .filter((valor) => valores.some((v) => v[chave].includes(valor)))
      .map((valor) => ({ valor, rotulo: rotulos[valor], contagem: contar(chave, valor) }))

  return [
    {
      chave: 'pedido',
      titulo: 'Pedido',
      opcoes: [...pedidos]
        .sort(([, a], [, b]) => porTexto(a, b))
        .map(([id, numero]) => ({ valor: String(id), rotulo: numero, contagem: contar('pedido', String(id)) })),
    },
    {
      chave: 'setor',
      titulo: 'Setor',
      opcoes: [...setores]
        .sort(([, a], [, b]) => porTexto(a, b))
        .map(([id, nome]) => ({ valor: String(id), rotulo: nome, contagem: contar('setor', String(id)) })),
    },
    { chave: 'situacao', titulo: 'Situação', opcoes: fixas('situacao', ROTULOS_DA_SITUACAO) },
    { chave: 'relatorio', titulo: 'Relatório', opcoes: fixas('relatorio', ROTULOS_DO_RELATORIO) },
    { chave: 'embalado', titulo: 'Embalado', opcoes: fixas('embalado', ROTULOS_DO_EMBALADO) },
  ]
}

export interface ConsultaDoPanorama {
  selecao: Selecao
  busca: string
  /** `AAAA-MM-DD` ou vazio: limite inferior inclusivo do prazo de entrega. */
  de: string
  /** `AAAA-MM-DD` ou vazio: limite superior inclusivo. */
  ate: string
}

/**
 * As Peças que casam com a seleção (OU dentro da faceta, E entre elas), a busca (número do Pedido, código e
 * descrição, sem caixa nem acento) e o intervalo do prazo. A ordem é a de entrada.
 */
export function filtrarPanorama(pecas: PecaDoPanoramaDto[], { selecao, busca, de, ate }: ConsultaDoPanorama): PecaDoPanoramaDto[] {
  const consulta = normalizarParaBusca(busca.trim())
  return pecas.filter((peca) => {
    if (!casaComFiltro(valoresDaPeca(peca), selecao)) return false
    if (consulta !== '') {
      const texto = normalizarParaBusca(`${peca.pedido.numero} ${peca.componente.codigo} ${peca.componente.descricao}`)
      if (!texto.includes(consulta)) return false
    }
    const prazo = peca.pedido.dataEntrega
    if (de !== '' && prazo < de) return false
    if (ate !== '' && prazo > ate) return false
    return true
  })
}
