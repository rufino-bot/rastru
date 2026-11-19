import { ListaRecolhivel } from '../components/ListaRecolhivel'
import type { AndamentoDoNoDto, BlocoDaTrilhaDto, MaisParadoDto } from '../api/andamento'
import { duracaoLegivel, idadeDesde } from '../panorama/idade'
import { formatarQuantidade } from '../execucao/formatacao'

interface Props {
  no: AndamentoDoNoDto
  /** O `agora` do servidor (C12). */
  agora: string
  maisParado: MaisParadoDto | null
}

const ROTULO_DO_TIPO: Record<BlocoDaTrilhaDto['tipo'], string> = {
  AIniciar: 'A iniciar', Passo: 'Passo', Montagem: 'Aguardando montagem', Conserto: 'Em conserto', Expedicao: 'Expedição',
}

function nomeDoBloco(b: BlocoDaTrilhaDto): string {
  const base = b.tipo === 'Passo' ? (b.setor ?? ROTULO_DO_TIPO.Passo) : ROTULO_DO_TIPO[b.tipo]
  return b.tipo !== 'Passo' && b.setor ? `${base} · ${b.setor}` : base
}

function temMaisParado(no: AndamentoDoNoDto, maisParado: MaisParadoDto | null): boolean {
  if (!maisParado) return false
  return no.estruturaItemId === maisParado.estruturaItemId || no.itens.some((i) => temMaisParado(i, maisParado))
}

/**
 * A trilha de um nó (Peça ou Item): os blocos na ordem do servidor, com os Itens filhos numa lista recolhível que abre
 * sozinha quando o "mais parado" está entre eles (C10). Tudo neutro (C9): o bloco mais parado se marca pela borda de
 * 2 px e pelo texto, não por cor de estado. Abaixo de `md` a trilha empilha; de `md` para cima é uma linha.
 */
export function TrilhaDoNo({ no, agora, maisParado }: Props) {
  const soNosItens = no.itens.some((i) => temMaisParado(i, maisParado))
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm text-tinta">
        <span className="font-mono font-semibold">{no.codigo}</span>{' '}
        <span className="text-tinta-fraca">{`${no.descricao} · ${formatarQuantidade(no.quantidade)}`}</span>
      </p>
      <ol className="flex flex-col gap-2 md:flex-row md:flex-wrap">
        {no.trilha.map((b) => {
          const marcado = maisParado !== null && maisParado.estruturaItemId === no.estruturaItemId && maisParado.bloco === b.chave
          const borda = marcado ? 'border-2 border-tinta' : b.situacao === 'Futuro' ? 'border border-dashed border-borda-campo' : 'border border-borda'
          return (
            <li
              key={b.chave}
              aria-label={nomeDoBloco(b)}
              className={`flex min-w-32 flex-col gap-0.5 rounded-lg bg-superficie p-2 text-sm text-tinta ${borda}`}
            >
              <span className="font-medium">{nomeDoBloco(b)}</span>
              {b.situacao === 'Atual' && (
                <>
                  <span>{`${formatarQuantidade(b.quantidade)}${b.desde ? ` · ${idadeDesde(b.desde, agora)}` : ''}`}</span>
                  {b.aguardandoColeta > 0 && (
                    <span className="text-tinta-fraca">
                      {`coleta ${formatarQuantidade(b.aguardandoColeta)}${b.aguardandoColetaDesde ? ` · ${idadeDesde(b.aguardandoColetaDesde, agora)}` : ''}`}
                    </span>
                  )}
                </>
              )}
              {b.situacao === 'Passado' && (
                <span className="text-tinta-fraca">
                  {`passou${b.permanenciaMediaSegundos !== null ? ` · ${duracaoLegivel(b.permanenciaMediaSegundos * 1000)} no Setor` : ''}`}
                </span>
              )}
              {b.situacao === 'Futuro' && <span className="text-tinta-fraca">ainda não chegou</span>}
              {marcado && <span className="font-semibold">mais parado</span>}
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
            {no.itens.map((i) => <TrilhaDoNo key={i.estruturaItemId} no={i} agora={agora} maisParado={maisParado} />)}
          </div>
        </ListaRecolhivel>
      )}
    </div>
  )
}
