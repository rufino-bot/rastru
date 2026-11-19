import { Link } from 'react-router-dom'
import { caminhoDaMiniatura } from '../api/cadastros'
import type { PecaDoPanoramaDto } from '../api/panorama'
import { MiniaturaDoSolido } from '../components/MiniaturaDoSolido'
import { formatarQuantidade } from '../execucao/formatacao'
import { CelulaDeEmbalagem } from './CelulaDeEmbalagem'
import { DistribuicaoDaPeca } from './DistribuicaoDaPeca'
import { AfericaoDaPeca, PrazoDaPeca } from './pilulasDaPeca'

interface Props {
  peca: PecaDoPanoramaDto
  aoGravar: () => Promise<void>
}

/** A mesma Peça da `LinhaDoPanorama`, num cartão, para abaixo de `lg` (C14). */
export function CartaoDoPanorama({ peca, aoGravar }: Props) {
  const { componente, pedido } = peca
  return (
    <li className="flex flex-col gap-3 rounded-lg border border-borda bg-superficie p-4">
      <div className="flex gap-3">
        <MiniaturaDoSolido
          caminho={caminhoDaMiniatura(componente.id)}
          versao={componente.miniaturaVersao}
          alt={`Desenho de ${componente.codigo}`}
          className="w-24 shrink-0"
        />
        <div className="flex min-w-0 flex-col gap-1">
          <Link to={`/agrupamentos/${peca.agrupamentoId}`} className="font-medium text-acao underline">
            {componente.codigo}
          </Link>
          <span className="text-sm text-tinta">{componente.descricao}</span>
          <span className="text-sm text-tinta-fraca">{`Pedido ${pedido.numero} · Qtd ${formatarQuantidade(peca.quantidade)}`}</span>
        </div>
      </div>
      <div className="text-sm"><PrazoDaPeca pedido={pedido} /></div>
      <DistribuicaoDaPeca segmentos={peca.distribuicao} itensEm={peca.itensEm} />
      <AfericaoDaPeca afericao={peca.afericao} />
      <CelulaDeEmbalagem peca={peca} aoGravar={aoGravar} />
    </li>
  )
}
