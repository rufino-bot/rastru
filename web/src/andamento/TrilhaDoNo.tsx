import { ListaRecolhivel } from '../components/ListaRecolhivel'
import type { AndamentoDoNoDto, BlocoDaTrilhaDto, MaisParadoDto } from '../api/andamento'
import { duracaoLegivel } from '../panorama/idade'
import { formatarQuantidade } from '../execucao/formatacao'
import { LinhaDoRoteiro } from './LinhaDoRoteiro'
import { gruposDoCelular, textoDoBloco } from './textoDoBloco'

interface Props {
  no: AndamentoDoNoDto
  /** O `agora` do servidor (C12). */
  agora: string
  maisParado: MaisParadoDto | null
  /** `Peca` no topo da seção e `Item` na recursão: nomeia o tipo do nó no título. */
  nivel: 'Peca' | 'Item'
}

function temMaisParado(no: AndamentoDoNoDto, maisParado: MaisParadoDto | null): boolean {
  if (!maisParado) return false
  return no.estruturaItemId === maisParado.estruturaItemId || no.itens.some((i) => temMaisParado(i, maisParado))
}

function faixaDeOrdens(blocos: BlocoDaTrilhaDto[]): string {
  const ordens = blocos.map((b) => b.ordem).filter((o): o is number => o !== null)
  if (ordens.length === 0) return ''
  const menor = Math.min(...ordens)
  const maior = Math.max(...ordens)
  return menor === maior ? `${menor}` : `${menor}–${maior}`
}

function nomeNaTrilha(b: BlocoDaTrilhaDto): string {
  return b.tipo === 'Expedicao' ? 'Expedição' : (b.setor ?? 'Passo')
}

/**
 * A trilha de um nó (Peça ou Item): de `md` para cima, a linha do Roteiro com a lista do que tem peça agora e, numa
 * linha, os passos já passados; abaixo de `md`, uma lista vertical única. As duas formas ficam no DOM e o CSS esconde
 * uma. Os Itens filhos ficam numa lista recolhível que abre sozinha quando o "mais parado" está entre eles. Tudo
 * neutro: o bloco mais parado se marca pela borda de 2 px e pelo texto, não por cor de estado.
 */
export function TrilhaDoNo({ no, agora, maisParado, nivel }: Props) {
  const soNosItens = no.itens.some((i) => temMaisParado(i, maisParado))
  const marcado = (b: BlocoDaTrilhaDto) =>
    maisParado !== null && maisParado.estruturaItemId === no.estruturaItemId && maisParado.bloco === b.chave
  const atuais = no.trilha.filter((b) => b.situacao === 'Atual')
  const passados = no.trilha.filter((b) => b.tipo === 'Passo' && b.situacao === 'Passado')

  const linha = (b: BlocoDaTrilhaDto) => {
    const m = marcado(b)
    return (
      <li
        key={b.chave}
        className={`flex flex-wrap items-baseline gap-x-2 rounded-lg bg-superficie px-2 py-1 text-sm text-tinta ${m ? 'border-2 border-tinta' : 'border border-borda'}`}
      >
        <span>{textoDoBloco(b, agora)}</span>
        {m && <span className="font-semibold">mais parado</span>}
      </li>
    )
  }

  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm text-tinta">
        <span className="font-mono font-semibold">{no.codigo}</span>
        <span className="text-tinta-fraca">{` · ${no.descricao} · `}</span>
        <span className="text-tinta-fraca">{`${nivel === 'Peca' ? 'Peça' : 'Item'} · qtd ${formatarQuantidade(no.quantidade)}`}</span>
      </p>
      <div className="hidden flex-col gap-2 md:flex">
        <LinhaDoRoteiro trilha={no.trilha} />
        {atuais.length > 0 && (
          <ul aria-label="Onde está agora" className="flex flex-col gap-1">
            {atuais.map(linha)}
          </ul>
        )}
        {passados.length > 0 && (
          <p className="text-sm text-tinta-fraca">
            {`Já passaram: ${passados
              .map((b) => (b.permanenciaMediaSegundos !== null
                ? `${nomeNaTrilha(b)} (${duracaoLegivel(b.permanenciaMediaSegundos * 1000)})`
                : nomeNaTrilha(b)))
              .join(', ')}`}
          </p>
        )}
      </div>
      <ol aria-label="Trilha no celular" className="flex flex-col gap-1 md:hidden">
        {gruposDoCelular(no.trilha).map((g) => {
          if (g.tipo === 'atual') return linha(g.bloco)
          const faixa = faixaDeOrdens(g.blocos)
          const depois = g.tipo === 'passados' ? `${g.blocos.map(nomeNaTrilha).join(', ')} · já passaram` : 'ainda não chegaram'
          return (
            <li key={g.blocos[0].chave} className="px-2 text-sm text-tinta-fraca">
              {faixa ? `${faixa} · ${depois}` : depois}
            </li>
          )
        })}
      </ol>
      {no.complicacoes.length > 0 && (
        <ul className="flex flex-col gap-0.5 text-sm text-tinta">
          {no.complicacoes.map((c) => <li key={c.codigo}>{c.texto}</li>)}
        </ul>
      )}
      {no.itens.length > 0 && (
        <ListaRecolhivel rotulo="Itens" contagem={no.itens.length} temAcao={soNosItens}>
          <div className="flex flex-col gap-3 border-l border-borda pl-3">
            {no.itens.map((i) => <TrilhaDoNo key={i.estruturaItemId} no={i} agora={agora} maisParado={maisParado} nivel="Item" />)}
          </div>
        </ListaRecolhivel>
      )}
    </div>
  )
}
