import type { Posicao, SaldoDto } from '../api/execucao'

/**
 * De onde se dá baixa (C4 da spec da Fase 5B-1): toda posição em produção menos `AguardandoLiberacao`, que está
 * presa a um pedido de liberação em aberto. `Montado`, `Expedido` e `Perdido` são terminais e nunca saem por baixa.
 * Espelha `CalculoDaBaixa` do backend: mudou lá, muda aqui.
 */
export const POSICOES_DE_ORIGEM: ReadonlySet<Posicao> = new Set<Posicao>([
  'AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'AguardandoConserto', 'EmConserto',
  'ConsertoPronto', 'NaExpedicao', 'Conforme', 'NaoConforme', 'Liberada',
])

/** Os saldos do nó de onde uma baixa pode tirar. */
export function saldosParaBaixa(saldos: readonly SaldoDto[]): SaldoDto[] {
  return saldos.filter((s) => POSICOES_DE_ORIGEM.has(s.posicao) && s.quantidade > 0)
}

/** A chave de um saldo entre os do nó: a posição, com Setor e passo quando ela os tem. */
export function chaveDoSaldo(s: Pick<SaldoDto, 'posicao' | 'setorId' | 'ordem'>): string {
  return `${s.posicao}-${s.setorId ?? ''}-${s.ordem ?? ''}`
}
