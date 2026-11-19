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
      <td className="w-28 p-2">
        <MiniaturaDoSolido
          caminho={caminhoDaMiniatura(componente.id)}
          versao={componente.miniaturaVersao}
          alt={`Desenho de ${componente.codigo}`}
        />
      </td>
      <td className="p-2 text-sm">{pedido.numero}</td>
      <td className="p-2 text-sm">
        <Link to={`/agrupamentos/${peca.agrupamentoId}`} className="font-medium text-acao underline">
          {componente.codigo}
        </Link>
      </td>
      <td className="p-2 text-sm">{componente.descricao}</td>
      <td className="p-2 text-sm">{formatarQuantidade(peca.quantidade)}</td>
      <td className="p-2 text-sm"><PrazoDaPeca pedido={pedido} /></td>
      <td className="p-2"><DistribuicaoDaPeca segmentos={peca.distribuicao} itensEm={peca.itensEm} agora={agora} /></td>
      <td className="p-2 text-sm"><ParadoHa peca={peca} agora={agora} /></td>
      <td className="p-2"><ComplicacoesDaPeca peca={peca} /></td>
      <td className="p-2"><AfericaoDaPeca afericao={peca.afericao} /></td>
      <td className="p-2"><CelulaDeEmbalagem peca={peca} aoGravar={aoGravar} /></td>
    </tr>
  )
}
