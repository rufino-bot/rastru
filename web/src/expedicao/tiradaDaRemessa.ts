import type { OrigemDaLinhaDaRemessa, PosicoesDaPecaDto, SituacaoDaAfericao, TipoDeAviso } from '../api/expedicao'

/**
 * A prévia do que uma remessa tira de uma Peça e do que ela avisa, para o "Expedir" mostrar o aviso ANTES de
 * confirmar (D8 da spec da Fase 5A). É cópia de `CalculoDaExpedicao.Tirar` e `CalculoDaExpedicao.Avisos` do
 * backend, que é quem grava e devolve os avisos de verdade no `201`; os testes dos dois lados fixam a mesma
 * tabela (decisão Q2 do plano de front da Fase 5A). Mudou lá, muda aqui: divergência entre as duas é defeito.
 */

/** Quanto a remessa tira de uma posição: uma linha `Expedicao` do livro, dessa origem para `Expedido`. */
export interface Tirada {
  posicao: OrigemDaLinhaDaRemessa
  quantidade: number
}

/** Um aviso de expedir (D8): o tipo e quantas unidades saem assim. */
export interface AvisoDaRemessa {
  tipo: TipoDeAviso
  quantidade: number
}

/** A ordem em que a remessa tira (C10). `AguardandoLiberacao` não está nela: espera a Gestão. */
const ORDEM_DA_TIRADA: { posicao: OrigemDaLinhaDaRemessa; saldo: (p: PosicoesDaPecaDto) => number }[] = [
  { posicao: 'Liberada', saldo: (p) => p.liberada },
  { posicao: 'Conforme', saldo: (p) => p.conforme },
  { posicao: 'NaExpedicao', saldo: (p) => p.naExpedicao },
  { posicao: 'NaoConforme', saldo: (p) => p.naoConforme },
]

/** Os tipos de aviso, na ordem em que saem. */
const TIPOS_NA_ORDEM: readonly TipoDeAviso[] = ['SemAfericao', 'SemMedicao', 'NaoConformeSemLiberacao']

// O backend conta em `decimal` com quatro casas (a coluna é `DECIMAL(18,4)`); aqui a conta é feita em
// décimos de milésimo inteiros, para 0,1 + 0,2 dar 0,3 como lá, e não 0,30000000000000004.
const ESCALA = 10_000
const emUnidades = (q: number) => Math.round(q * ESCALA)
const deUnidades = (u: number) => u / ESCALA

/**
 * De onde a remessa tira `quantidade`, na ordem de C10, uma `Tirada` por posição que contribui (posição vazia
 * não entra). Nula quando passa do expedível (Liberada + Conforme + NaExpedicao + NaoConforme).
 *
 * Diferente do backend, quantidade não positiva (ou que não é número) também devolve nula em vez de lançar: a
 * prévia roda a cada tecla, e o campo inválido simplesmente não tem prévia.
 */
export function tirarDaRemessa(p: PosicoesDaPecaDto, quantidade: number): Tirada[] | null {
  if (!Number.isFinite(quantidade) || quantidade <= 0) return null
  const expedivel = ORDEM_DA_TIRADA.reduce((soma, o) => soma + emUnidades(o.saldo(p)), 0)
  let falta = emUnidades(quantidade)
  if (falta > expedivel) return null

  const tiradas: Tirada[] = []
  for (const { posicao, saldo } of ORDEM_DA_TIRADA) {
    if (falta === 0) break
    const tira = Math.min(falta, emUnidades(saldo(p)))
    if (tira <= 0) continue
    tiradas.push({ posicao, quantidade: deUnidades(tira) })
    falta -= tira
  }
  return tiradas
}

function tipoDoAviso(s: SituacaoDaAfericao, origem: OrigemDaLinhaDaRemessa): TipoDeAviso | null {
  switch (origem) {
    case 'NaExpedicao':
      if (s === 'Pendente') return 'SemAfericao'
      if (s === 'NaoConforme') return 'SemMedicao'
      return null
    case 'NaoConforme': return 'NaoConformeSemLiberacao'
    case 'Liberada':
    case 'Conforme': return null
  }
}

/**
 * Os avisos de expedir (D8), um por tipo, com a quantidade somada; tipo com zero não sai. `NaExpedicao` avisa
 * conforme a situação (tabela 3.2 da spec da Fase 5A); `NaoConforme` sempre avisa; `Liberada` e `Conforme` nunca.
 */
export function avisosDaRemessa(s: SituacaoDaAfericao, tiradas: Tirada[]): AvisoDaRemessa[] {
  const porTipo = new Map<TipoDeAviso, number>()
  for (const tirada of tiradas) {
    const tipo = tipoDoAviso(s, tirada.posicao)
    if (tipo === null) continue
    porTipo.set(tipo, (porTipo.get(tipo) ?? 0) + emUnidades(tirada.quantidade))
  }
  return TIPOS_NA_ORDEM
    .filter((tipo) => (porTipo.get(tipo) ?? 0) > 0)
    .map((tipo) => ({ tipo, quantidade: deUnidades(porTipo.get(tipo)!) }))
}
