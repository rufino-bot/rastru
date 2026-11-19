import { describe, it, expect } from 'vitest'
import { gruposDoCelular, textoDoBloco } from './textoDoBloco'
import { AGORA_DO_ANDAMENTO, bloco } from '../testes/andamento'

describe('textoDoBloco', () => {
  it('passo_com_quantidade_e_coleta', () => {
    const soColeta = bloco({
      setor: 'Solda', situacao: 'Atual', quantidade: 0, aguardandoColeta: 20, aguardandoColetaDesde: '2026-11-10T12:00:00-03:00',
    })
    expect(textoDoBloco(soColeta, AGORA_DO_ANDAMENTO)).toBe('Solda — 20 prontas, esperando coleta · há 9 d')

    const as2 = bloco({
      setor: 'Solda', situacao: 'Atual', quantidade: 4, desde: '2026-11-17T12:00:00-03:00', aguardandoColeta: 20,
      aguardandoColetaDesde: '2026-11-10T12:00:00-03:00',
    })
    expect(textoDoBloco(as2, AGORA_DO_ANDAMENTO)).toBe('Solda — 4 no Setor · há 2 d · 20 prontas, esperando coleta · há 9 d')

    const uma = bloco({ setor: 'Solda', situacao: 'Atual', quantidade: 0, aguardandoColeta: 1, aguardandoColetaDesde: '2026-11-10T12:00:00-03:00' })
    expect(textoDoBloco(uma, AGORA_DO_ANDAMENTO)).toBe('Solda — 1 pronta, esperando coleta · há 9 d')
    const meia = bloco({ setor: 'Solda', situacao: 'Atual', quantidade: 0, aguardandoColeta: 1.5, aguardandoColetaDesde: '2026-11-10T12:00:00-03:00' })
    expect(textoDoBloco(meia, AGORA_DO_ANDAMENTO)).toBe('Solda — 1,5 prontas, esperando coleta · há 9 d')

    const soSetor = bloco({ setor: 'Corte', situacao: 'Atual', quantidade: 4, desde: '2026-11-19T06:00:00-03:00' })
    expect(textoDoBloco(soSetor, AGORA_DO_ANDAMENTO)).toBe('Corte — 4 no Setor · há 6 h')
  })

  it('a_iniciar', () => {
    const b = bloco({ tipo: 'AIniciar', chave: 'AIniciar', ordem: null, setor: null, situacao: 'Atual', quantidade: 12, desde: '2026-11-17T12:00:00-03:00' })
    expect(textoDoBloco(b, AGORA_DO_ANDAMENTO)).toBe('A iniciar — 12 · há 2 d')
  })

  it('montagem_e_conserto_com_setor', () => {
    const m = bloco({ tipo: 'Montagem', chave: 'Montagem:4', ordem: null, setor: 'Solda', situacao: 'Atual', quantidade: 3, desde: '2026-11-19T06:00:00-03:00' })
    expect(textoDoBloco(m, AGORA_DO_ANDAMENTO)).toBe('Aguardando montagem · Solda — 3 · há 6 h')
    const c = bloco({ tipo: 'Conserto', chave: 'Conserto:4', ordem: null, setor: 'Dobra', situacao: 'Atual', quantidade: 1, desde: null })
    expect(textoDoBloco(c, AGORA_DO_ANDAMENTO)).toBe('Em conserto · Dobra — 1')
  })

  it('expedicao', () => {
    const e = bloco({ tipo: 'Expedicao', chave: 'Expedicao', ordem: null, setor: null, situacao: 'Atual', quantidade: 5, desde: '2026-11-19T06:00:00-03:00' })
    expect(textoDoBloco(e, AGORA_DO_ANDAMENTO)).toBe('Expedição — 5 · há 6 h')
  })
})

describe('gruposDoCelular', () => {
  const passo = (n: number, situacao: 'Passado' | 'Atual' | 'Futuro') =>
    bloco({ chave: `Passo:${n}`, ordem: n, situacao })

  it('grupos_juntam_passados_e_futuros_consecutivos', () => {
    const t = [passo(1, 'Passado'), passo(2, 'Passado'), passo(3, 'Atual'), passo(4, 'Futuro'), passo(5, 'Futuro')]
    const g = gruposDoCelular(t)
    expect(g.map((x) => x.tipo)).toEqual(['passados', 'atual', 'futuros'])
    expect(g[0].tipo === 'passados' && g[0].blocos.map((b) => b.ordem)).toEqual([1, 2])
    expect(g[2].tipo === 'futuros' && g[2].blocos.map((b) => b.ordem)).toEqual([4, 5])
  })

  it('grupos_nao_juntam_atual_com_passado', () => {
    const t = [passo(1, 'Passado'), passo(2, 'Atual'), passo(3, 'Atual'), passo(4, 'Passado')]
    expect(gruposDoCelular(t).map((x) => x.tipo)).toEqual(['passados', 'atual', 'atual', 'passados'])
  })
})
