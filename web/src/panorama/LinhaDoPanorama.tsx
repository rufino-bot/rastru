import { Link } from 'react-router-dom'
import { caminhoDaMiniatura } from '../api/cadastros'
import type { PecaDoPanoramaDto } from '../api/panorama'
import { MiniaturaDoSolido } from '../components/MiniaturaDoSolido'
import { formatarQuantidade } from '../execucao/formatacao'
import { CelulaDeEmbalagem } from './CelulaDeEmbalagem'
import { ComplicacoesDaPeca, ParadoHa } from './CelulasDeAndamento'
import { DistribuicaoDaPeca } from './DistribuicaoDaPeca'
import { AfericaoDaPeca, PrazoDaPeca } from './pilulasDaPeca'

interface Props {
  peca: PecaDoPanoramaDto
  aoGravar: () => Promise<void>
  /** O `agora` do servidor (C12). */
  agora: string
}

/** Uma Peça na tabela do Panorama, com as colunas de C4. A tela escreve o `<thead>`. */
export function LinhaDoPanorama({ peca, aoGravar, agora }: Props) {
  const { componente, pedido } = peca
  return (
    <tr className="border-b border-borda align-top">
      <td className="w-[4.5rem] px-1.5 py-2">
        <div className="max-w-16">
          <MiniaturaDoSolido
            caminho={caminhoDaMiniatura(componente.id)}
            versao={componente.miniaturaVersao}
            alt={`Desenho de ${componente.codigo}`}
          />
        </div>
      </td>
      <td className="px-1.5 py-2 text-sm">{pedido.numero}</td>
      <td className="px-1.5 py-2 text-sm">
        <Link to={`/agrupamentos/${peca.agrupamentoId}`} className="font-medium text-acao underline">
          {componente.codigo}
        </Link>
      </td>
      <td className="px-1.5 py-2 text-sm">{componente.descricao}</td>
      <td className="px-1.5 py-2 text-sm">{formatarQuantidade(peca.quantidade)}</td>
      <td className="px-1.5 py-2 text-sm"><PrazoDaPeca pedido={pedido} /></td>
      <td className="px-1.5 py-2"><DistribuicaoDaPeca segmentos={peca.distribuicao} itensEm={peca.itensEm} agora={agora} /></td>
      <td className="px-1.5 py-2 text-sm"><ParadoHa peca={peca} agora={agora} /></td>
      <td className="px-1.5 py-2">
        <div className="max-w-[5.75rem]"><ComplicacoesDaPeca peca={peca} /></div>
      </td>
      <td className="px-1.5 py-2"><AfericaoDaPeca afericao={peca.afericao} /></td>
      <td className="px-1.5 py-2">
        <div className="max-w-[5.75rem]"><CelulaDeEmbalagem peca={peca} aoGravar={aoGravar} /></div>
      </td>
    </tr>
  )
}
