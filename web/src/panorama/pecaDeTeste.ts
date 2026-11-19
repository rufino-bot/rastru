import type { PecaDoPanoramaDto, SegmentoDto } from '../api/panorama'

/** Uma Peça do Panorama para os testes de tela, com o que importa para cada caso sobreposto em `sobre`. */
export function pecaDeTeste(sobre: Partial<PecaDoPanoramaDto> = {}): PecaDoPanoramaDto {
  return {
    estruturaItemId: 7, agrupamentoId: 4, quantidade: 5,
    pedido: { id: 1, numero: '293', dataEntrega: '2026-11-20', atrasado: false, retrabalho: false, pausa: null },
    componente: { id: 9, codigo: 'SUP-01', descricao: 'Suporte lateral', miniaturaVersao: null },
    itensEm: [],
    distribuicao: [],
    afericao: { exigeRelatorio: false, situacao: 'SemRelatorio' },
    embalagem: { vale: false, podeMarcar: false, emProducao: 0, ultima: null },
    paradoDesde: null,
    complicacoes: [],
    ...sobre,
  }
}

export const seg = (tipo: SegmentoDto['tipo'], quantidade: number, o: Partial<SegmentoDto> = {}): SegmentoDto => ({
  tipo, setorId: null, setorNome: null, ordem: null, quantidade, aguardandoColeta: 0, desde: null, aguardandoColetaDesde: null, ...o,
})
