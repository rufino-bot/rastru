import type { AndamentoDoNoDto, AndamentoDoPedidoDto, BlocoDaTrilhaDto } from '../api/andamento'

/** Fábricas dos DTOs do andamento, para os testes das telas e dos componentes dele. */
export const AGORA_DO_ANDAMENTO = '2026-11-19T12:00:00-03:00'

export function bloco(parcial: Partial<BlocoDaTrilhaDto> = {}): BlocoDaTrilhaDto {
  return {
    tipo: 'Passo', chave: 'Passo:1', ordem: 1, setorId: 4, setor: 'Corte', situacao: 'Futuro', quantidade: 0,
    desde: null, aguardandoColeta: 0, aguardandoColetaDesde: null, permanenciaMediaSegundos: null, ...parcial,
  }
}

export function no(parcial: Partial<AndamentoDoNoDto> = {}): AndamentoDoNoDto {
  return {
    estruturaItemId: 812, codigo: 'BASE-200', descricao: 'Base', quantidade: 10, trilha: [], complicacoes: [], itens: [],
    ...parcial,
  }
}

export function andamento(parcial: Partial<AndamentoDoPedidoDto> = {}): AndamentoDoPedidoDto {
  return {
    agora: AGORA_DO_ANDAMENTO, abertura: '2026-11-14T12:00:00-03:00', inicioReal: '2026-11-16T12:00:00-03:00',
    pausadoDepoisDoInicioSegundos: 0, maisParado: null, pecas: [no()], ...parcial,
  }
}
