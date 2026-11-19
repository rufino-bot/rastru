import type { BlocoDaTrilhaDto } from '../api/andamento'
import { idadeDesde } from '../panorama/idade'
import { formatarQuantidade } from '../execucao/formatacao'

export type GrupoDoCelular =
  | { tipo: 'passados' | 'futuros'; blocos: BlocoDaTrilhaDto[] }
  | { tipo: 'atual'; bloco: BlocoDaTrilhaDto }

function comIdade(texto: string, desde: string | null, agora: string): string {
  return desde ? `${texto} · ${idadeDesde(desde, agora)}` : texto
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
      const partes: string[] = []
      if (b.quantidade > 0) partes.push(comIdade(`${formatarQuantidade(b.quantidade)} no Setor`, b.desde, agora))
      if (b.aguardandoColeta > 0) {
        partes.push(comIdade(`${formatarQuantidade(b.aguardandoColeta)} prontas, esperando coleta`, b.aguardandoColetaDesde, agora))
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
