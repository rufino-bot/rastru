import type { BlocoDaTrilhaDto } from '../api/andamento'
import { duracaoLegivel, idadeDesde } from '../panorama/idade'
import { formatarQuantidade } from '../execucao/formatacao'

export type GrupoDoCelular =
  | { tipo: 'passados' | 'futuros'; blocos: BlocoDaTrilhaDto[] }
  | { tipo: 'atual'; bloco: BlocoDaTrilhaDto }

function comIdade(texto: string, desde: string | null, agora: string): string {
  return desde ? `${texto} · ${idadeDesde(desde, agora)}` : texto
}

/** A permanência média, que o servidor manda em segundos, como a tela a escreve ("2 d", "menos de 1 h"). */
export function permanenciaLegivel(segundos: number): string {
  return duracaoLegivel(segundos * 1000)
}

/** "já saiu 1" / "já saíram 5": o que o nó já expediu, quando a Expedição não tem peça agora. */
export function textoDoExpedido(expedido: number): string {
  return expedido === 1 ? 'já saiu 1' : `já saíram ${formatarQuantidade(expedido)}`
}

/** O texto de um bloco que tem peça agora, sem a marca de "mais parado". */
export function textoDoBloco(b: BlocoDaTrilhaDto, agora: string): string {
  const quantidade = comIdade(formatarQuantidade(b.quantidade), b.desde, agora)
  switch (b.tipo) {
    case 'AIniciar':
      return `A iniciar — ${quantidade}`
    case 'Montagem':
      return `Aguardando montagem · ${b.setor ?? ''} — ${quantidade}`
    case 'Conserto':
      return `Em conserto · ${b.setor ?? ''} — ${quantidade}`
    case 'Expedicao':
      return `Expedição — ${quantidade}`
    case 'Passo': {
      // `quantidade` é o total do passo (no Setor + esperando coleta), como no Panorama: o "no Setor" é a diferença.
      const partes: string[] = []
      const noSetor = b.quantidade - b.aguardandoColeta
      if (noSetor > 0) partes.push(comIdade(`${formatarQuantidade(noSetor)} no Setor`, b.desde, agora))
      if (b.aguardandoColeta > 0) {
        partes.push(comIdade(`${formatarQuantidade(b.aguardandoColeta)} ${b.aguardandoColeta === 1 ? 'pronta' : 'prontas'}, esperando coleta`, b.aguardandoColetaDesde, agora))
      }
      // O passo é passado E atual quando parte das peças já saiu dele: a permanência delas vem junto (seção 5.3 da spec).
      if (b.situacao === 'Atual' && b.permanenciaMediaSegundos !== null) {
        partes.push(`as que passaram ficaram ${permanenciaLegivel(b.permanenciaMediaSegundos)}`)
      }
      return `${b.setor ?? 'Passo'} — ${partes.join(' · ')}`
    }
  }
}

/** A trilha do celular: passados e futuros consecutivos viram um grupo só; cada bloco atual fica sozinho. */
export function gruposDoCelular(trilha: BlocoDaTrilhaDto[]): GrupoDoCelular[] {
  const grupos: GrupoDoCelular[] = []
  for (const b of trilha) {
    if (b.situacao === 'Atual') {
      grupos.push({ tipo: 'atual', bloco: b })
      continue
    }
    const tipo = b.situacao === 'Passado' ? 'passados' : 'futuros'
    const ultimo = grupos[grupos.length - 1]
    if (ultimo && ultimo.tipo === tipo) ultimo.blocos.push(b)
    else grupos.push({ tipo, blocos: [b] })
  }
  return grupos
}
