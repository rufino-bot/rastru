import { useId, useRef, useState } from 'react'
import { Botao } from './Botao'
import { Campo, CLASSES_DE_CONTROLE } from './Campo'

/** Valores marcados por faceta, indexados pela `chave` dela. Faceta sem nada marcado não tem chave. */
export type Selecao = Record<string, string[]>

export interface OpcaoDeFaceta {
  valor: string
  rotulo: string
  /** Texto da pílula ("Pedido 1042"); sem ele a pílula usa `rotulo`. */
  rotuloCurto?: string
  /** Código do Material, desenhado em fonte monoespaçada. */
  detalhe?: string
  contagem?: number
}

export interface Faceta {
  chave: string
  titulo: string
  opcoes: OpcaoDeFaceta[]
}

/** Acima disto a faceta ganha um campo de busca; até isto, a lista inteira cabe na tela. */
export const LIMITE_PARA_BUSCA_NA_FACETA = 8

/** Rótulo do valor marcado (vindo da URL, por exemplo) que a tela nunca chegou a ver. */
export const ROTULO_DE_OPCAO_AUSENTE = 'Não está mais na lista'

/**
 * Regra única de casamento do filtro: OU dentro da faceta, E entre facetas, e faceta sem nada
 * marcado não restringe. Nenhuma tela reescreve isto.
 */
export function casaComFiltro(valoresDoItem: Record<string, readonly string[]>, selecao: Selecao): boolean {
  return Object.entries(selecao).every(([chave, marcados]) => {
    if (marcados.length === 0) return true
    const doItem = valoresDoItem[chave] ?? []
    return marcados.some((valor) => doItem.includes(valor))
  })
}

export function contarFiltrosAtivos(selecao: Selecao): number {
  return Object.values(selecao).reduce((total, marcados) => total + marcados.length, 0)
}

// Busca sem caixa nem acento: quem digita no celular não acentua ("solda" acha "Sôlda").
function normalizar(texto: string): string {
  return texto.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
}

function semValor(selecao: Selecao, chave: string, valor: string): Selecao {
  const { [chave]: marcados = [], ...resto } = selecao
  const restantes = marcados.filter((v) => v !== valor)
  return restantes.length > 0 ? { ...resto, [chave]: restantes } : resto
}

function comValor(selecao: Selecao, chave: string, valor: string): Selecao {
  return { ...selecao, [chave]: [...(selecao[chave] ?? []), valor] }
}

interface RotuloLembrado {
  rotulo: string
  rotuloCurto: string
}

interface OpcaoExibida extends OpcaoDeFaceta {
  marcada: boolean
}

interface Props {
  facetas: Faceta[]
  selecao: Selecao
  aoMudar: (selecao: Selecao) => void
}

/**
 * Filtro por facetas de uma lista de demanda: fechado, uma linha com "Filtrar", as pílulas dos
 * valores marcados e "Limpar"; aberto, um grupo de caixas por faceta.
 *
 * A seleção é controlada por quem chama. O componente só lembra os RÓTULOS: uma opção marcada que
 * some da lista (a demanda mudou) continua marcada e visível, com zero, para o usuário poder
 * desmarcá-la — e um valor que a tela nunca viu (URL colada, F5) aparece como
 * `ROTULO_DE_OPCAO_AUSENTE`, também removível. Cor de estado nunca decora aqui: pílula e contagem
 * são neutras.
 */
export function FiltroDeDemanda({ facetas, selecao, aoMudar }: Props) {
  const idDoPainel = useId()
  const [aberto, setAberto] = useState(false)
  const [buscas, setBuscas] = useState<Record<string, string>>({})
  const lembrados = useRef(new Map<string, RotuloLembrado>())

  // Atualizada a cada render com o que está presente: é o que a opção lembra quando sair da lista.
  for (const faceta of facetas) {
    for (const opcao of faceta.opcoes) {
      lembrados.current.set(`${faceta.chave}:${opcao.valor}`, {
        rotulo: opcao.rotulo,
        rotuloCurto: opcao.rotuloCurto ?? opcao.rotulo,
      })
    }
  }

  const ativos = contarFiltrosAtivos(selecao)

  const pilulas = facetas.flatMap((faceta) =>
    (selecao[faceta.chave] ?? []).map((valor) => ({
      chave: faceta.chave,
      valor,
      rotulo: lembrados.current.get(`${faceta.chave}:${valor}`)?.rotuloCurto ?? ROTULO_DE_OPCAO_AUSENTE,
    })),
  )

  function opcoesExibidas(faceta: Faceta): OpcaoExibida[] {
    const marcados = selecao[faceta.chave] ?? []
    const presentes = new Set(faceta.opcoes.map((o) => o.valor))
    const ausentes: OpcaoExibida[] = marcados
      .filter((valor) => !presentes.has(valor))
      .map((valor) => ({
        valor,
        rotulo: lembrados.current.get(`${faceta.chave}:${valor}`)?.rotulo ?? ROTULO_DE_OPCAO_AUSENTE,
        contagem: 0,
        marcada: true,
      }))
    return [...faceta.opcoes.map((o) => ({ ...o, marcada: marcados.includes(o.valor) })), ...ausentes]
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <Botao
          variante="secundario"
          aria-expanded={aberto}
          aria-controls={idDoPainel}
          onClick={() => setAberto((a) => !a)}
        >
          {ativos > 0 ? `Filtrar (${ativos})` : 'Filtrar'}
        </Botao>
        {pilulas.map((p) => (
          <Botao
            key={`${p.chave}:${p.valor}`}
            variante="secundario"
            aria-label={`Remover filtro ${p.rotulo}`}
            onClick={() => aoMudar(semValor(selecao, p.chave, p.valor))}
          >
            {p.rotulo} <span aria-hidden="true">×</span>
          </Botao>
        ))}
        {ativos > 0 && (
          <Botao variante="secundario" onClick={() => aoMudar({})}>
            Limpar
          </Botao>
        )}
      </div>

      <div id={idDoPainel} hidden={!aberto}>
        <div className="flex flex-col gap-4">
          {facetas.map((faceta) => {
            const opcoes = opcoesExibidas(faceta)
            const busca = buscas[faceta.chave] ?? ''
            const consulta = normalizar(busca.trim())
            const visiveis = consulta
              ? opcoes.filter((o) => normalizar(`${o.rotulo} ${o.detalhe ?? ''}`).includes(consulta))
              : opcoes
            return (
              <fieldset key={faceta.chave} className="flex flex-col gap-2">
                <legend className="mb-1 text-sm font-semibold text-tinta">{faceta.titulo}</legend>
                {opcoes.length > LIMITE_PARA_BUSCA_NA_FACETA && (
                  <Campo rotulo={`Buscar em ${faceta.titulo}`}>
                    {(id) => (
                      <input
                        id={id}
                        type="search"
                        value={busca}
                        onChange={(e) => setBuscas((b) => ({ ...b, [faceta.chave]: e.target.value }))}
                        className={CLASSES_DE_CONTROLE}
                      />
                    )}
                  </Campo>
                )}
                {visiveis.length === 0 && (
                  <p className="text-sm text-tinta-fraca">
                    {opcoes.length === 0 ? 'Nada para filtrar aqui.' : 'Nenhuma opção encontrada.'}
                  </p>
                )}
                {visiveis.map((opcao) => {
                  const idBase = `${idDoPainel}-${faceta.chave}-${opcao.valor}`
                  return (
                    <div key={opcao.valor} className="flex items-center gap-3">
                      <input
                        id={`${idBase}-caixa`}
                        type="checkbox"
                        checked={opcao.marcada}
                        onChange={() =>
                          aoMudar(
                            opcao.marcada
                              ? semValor(selecao, faceta.chave, opcao.valor)
                              : comValor(selecao, faceta.chave, opcao.valor),
                          )
                        }
                        aria-labelledby={`${idBase}-rotulo`}
                        className="size-5 accent-acao"
                      />
                      <label htmlFor={`${idBase}-caixa`} className="flex flex-1 flex-wrap items-baseline gap-x-2 text-tinta">
                        <span id={`${idBase}-rotulo`}>{opcao.rotulo}</span>
                        {opcao.detalhe && <span className="font-mono text-sm text-tinta-fraca">{opcao.detalhe}</span>}
                      </label>
                      {opcao.contagem !== undefined && (
                        <span className="text-sm text-tinta-fraca">{opcao.contagem}</span>
                      )}
                    </div>
                  )
                })}
              </fieldset>
            )
          })}
        </div>
      </div>
    </div>
  )
}
