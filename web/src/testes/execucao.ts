import type {
  DestinoDto, FilaDoSetorDto, MovimentacaoDto, NoResumoDto,
} from '../api/execucao'

/**
 * Massa de teste da execução, no formato do "Contrato JSON" do plano 2 da Fase 3. Uma árvore só,
 * a mesma em todos os testes de tela: Peça **Chassi** (id 2) com os filhos **Suporte** (id 7) e
 * **Parafuso** (id 8, ad-hoc), no Pedido PED-2026-01 › AG-01.
 */
export function no(parcial: Partial<NoResumoDto> = {}): NoResumoDto {
  return {
    id: 7, descricao: 'Suporte', codigoDoComponente: 'SUP-01',
    pedidoId: 1, pedidoNumero: 'PED-2026-01', pedidoCliente: 'Metalúrgica Alfa',
    agrupamentoId: 3, agrupamentoCodigo: 'AG-01',
    paiId: 2, paiDescricao: 'Chassi', materiais: [], pausa: null,
    ...parcial,
  }
}

export const CHASSI = no({ id: 2, descricao: 'Chassi', codigoDoComponente: 'CH-01', paiId: null, paiDescricao: null })
export const SUPORTE = no()
export const PARAFUSO = no({ id: 8, descricao: 'Parafuso', codigoDoComponente: null })

export function destino(parcial: Partial<DestinoDto> = {}): DestinoDto {
  return {
    tipo: 'ProximoPasso', setorId: 3, setorNome: 'Dobra', ordem: 2, paiId: null, paiSemRoteiro: false,
    ...parcial,
  }
}

export const DESTINO_MONTAGEM = destino({ tipo: 'Montagem', setorId: 4, setorNome: 'Solda', ordem: null, paiId: 2 })

export function fila(parcial: Partial<FilaDoSetorDto> = {}): FilaDoSetorDto {
  return {
    setorId: 1, setorNome: 'Corte', setorAtividade: null,
    aIniciar: [], emTrabalho: [], aguardandoColeta: [], aguardandoMontagem: [], sobra: [],
    ...parcial,
  }
}

export function movimentacao(parcial: Partial<MovimentacaoDto> = {}): MovimentacaoDto {
  return {
    id: 41, estruturaItemId: 7, tipo: 'Inicio', quantidade: 4,
    origem: { posicao: 'AIniciar', setorId: null, setorNome: null, ordem: null },
    destino: { posicao: 'NoSetor', setorId: 1, setorNome: 'Corte', ordem: 1 },
    montagemId: null, estornoDeId: null, dataHora: '2026-09-25T10:14:00-03:00',
    usuarioId: 12, usuarioNome: 'Operador do Corte', estornada: false,
    ...parcial,
  }
}
