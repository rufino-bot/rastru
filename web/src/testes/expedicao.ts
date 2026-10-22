import type {
  AfericaoDto, PecaNaExpedicaoDto, PosicoesDaPecaDto, RegistroDaExpedicaoDto,
} from '../api/expedicao'

/**
 * Massa de teste da expedição, no formato do contrato JSON do plano de backend da Fase 5A. Uma Peça só, a
 * mesma em todos os testes: **Quadro** (QD-01, id 30) do AGR-1, 10 unidades, com relatório dimensional.
 */
export function posicoes(parcial: Partial<PosicoesDaPecaDto> = {}): PosicoesDaPecaDto {
  return {
    naExpedicao: 0, conforme: 0, naoConforme: 0, aguardandoLiberacao: 0, liberada: 0, emConserto: 0, expedido: 0,
    ...parcial,
  }
}

export function afericao(parcial: Partial<AfericaoDto> = {}): AfericaoDto {
  return {
    id: 50, resultado: 'NaoConforme', dataHora: '2026-10-20T09:00:00-03:00', usuarioId: 12,
    usuarioNome: 'Qualidade do turno', estornada: false, estornavel: true,
    ...parcial,
  }
}

export function pecaNaExpedicao(parcial: Partial<PecaNaExpedicaoDto> = {}): PecaNaExpedicaoDto {
  return {
    id: 30, descricao: 'Quadro', codigoDoComponente: 'QD-01', agrupamentoId: 1, agrupamentoCodigo: 'AGR-1',
    quantidade: 10, requerRelatorioDimensional: true, situacao: 'Pendente', afericao: null,
    posicoes: posicoes({ naExpedicao: 6 }), consertos: [], liberacaoPendente: null, expedivel: 6,
    ...parcial,
  }
}

export function registro(parcial: Partial<RegistroDaExpedicaoDto> = {}): RegistroDaExpedicaoDto {
  return {
    tipo: 'Afericao', id: 1, dataHora: '2026-10-20T09:00:00-03:00', usuarioId: 12, usuarioNome: 'Qualidade do turno',
    resultado: null, conformes: null, naoConformes: null, quantidade: null, setorNome: null, texto: null,
    estornada: false, estornavel: true,
    ...parcial,
  }
}
