// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { TrilhaDoNo } from './TrilhaDoNo'
import { AGORA_DO_ANDAMENTO, bloco, no } from '../testes/andamento'

afterEach(cleanup)

function montar(n = no(), maisParado: { estruturaItemId: number; bloco: string } | null = null, nivel: 'Peca' | 'Item' = 'Peca') {
  return render(<TrilhaDoNo no={n} agora={AGORA_DO_ANDAMENTO} maisParado={maisParado} nivel={nivel} />)
}

const agora = () => screen.getByRole('list', { name: 'Onde está agora' })
const celular = () => screen.getByRole('list', { name: 'Trilha no celular' })

const ATUAL = { situacao: 'Atual' as const, quantidade: 2, desde: '2026-11-19T06:00:00-03:00' }

describe('TrilhaDoNo', () => {
  it('titulo_com_tipo_e_quantidade', () => {
    montar(no({ codigo: 'P01', descricao: 'Base', quantidade: 40 }))
    expect(screen.getByText('P01').className).toContain('font-mono')
    expect(screen.getByText('Peça · qtd 40').className).toContain('text-tinta-fraca')
    cleanup()
    montar(no({ quantidade: 3 }), null, 'Item')
    expect(screen.getByText('Item · qtd 3')).toBeTruthy()
  })

  it('lista_do_agora_so_com_blocos_atuais', () => {
    montar(no({
      trilha: [
        bloco({ chave: 'Passo:1', ordem: 1, setor: 'Corte', situacao: 'Passado' }),
        bloco({ chave: 'Passo:2', ordem: 2, setor: 'Dobra', ...ATUAL, quantidade: 4, aguardandoColeta: 1, aguardandoColetaDesde: '2026-11-19T06:00:00-03:00' }),
        bloco({ chave: 'Passo:3', ordem: 3, setor: 'Solda', situacao: 'Futuro' }),
      ],
    }))
    const itens = within(agora()).getAllByRole('listitem')
    expect(itens).toHaveLength(1)
    expect(itens[0].textContent).toBe('Dobra — 4 no Setor · há 6 h · 1 prontas, esperando coleta · há 6 h')
  })

  it('passados_numa_linha_com_permanencia_de_cada_um', () => {
    montar(no({
      trilha: [
        bloco({ chave: 'Passo:1', ordem: 1, setor: 'Corte', situacao: 'Passado', permanenciaMediaSegundos: 60 }),
        bloco({ chave: 'Passo:2', ordem: 2, setor: 'Dobra', situacao: 'Passado', permanenciaMediaSegundos: 86400 * 2 }),
        bloco({ chave: 'Passo:3', ordem: 3, setor: 'Solda', situacao: 'Passado', permanenciaMediaSegundos: null }),
      ],
    }))
    expect(screen.getByText('Já passaram: Corte (menos de 1 h), Dobra (2 d), Solda')).toBeTruthy()
  })

  it('mais_parado_marcado_sem_cor_de_estado', () => {
    const { container } = montar(
      no({ trilha: [bloco(ATUAL)] }), { estruturaItemId: 812, bloco: 'Passo:1' },
    )
    const item = within(agora()).getAllByRole('listitem')[0]
    expect(within(item).getByText('mais parado')).toBeTruthy()
    expect(item.className).toContain('border-2')
    expect(item.className).toContain('border-tinta')
    expect(container.innerHTML).not.toMatch(/positivo|negativo|atencao|atraso/)
  })

  it('itens_abrem_quando_o_mais_parado_e_um_item', () => {
    const item = no({ estruturaItemId: 900, codigo: 'ITEM-1', trilha: [bloco(ATUAL)] })
    const { container } = montar(no({ itens: [item] }), { estruturaItemId: 900, bloco: 'Passo:1' })
    expect(screen.getByText('Itens (1)')).toBeTruthy()
    expect(container.querySelector('details')?.open).toBe(true)

    cleanup()
    const outra = montar(no({ itens: [item] }), { estruturaItemId: 812, bloco: 'Passo:1' })
    expect(outra.container.querySelector('details')?.open).toBe(false)
  })

  it('complicacoes em texto neutro abaixo da trilha', () => {
    montar(no({ complicacoes: [{ codigo: 'MaterialPendente', texto: 'Falta material' }] }))
    expect(screen.getByText('Falta material').closest('ul')!.className).toContain('text-tinta')
  })

  it('celular_agrupa_passados_e_futuros', () => {
    montar(no({
      trilha: [
        bloco({ chave: 'Passo:1', ordem: 1, setor: 'Corte laser', situacao: 'Passado' }),
        bloco({ chave: 'Passo:2', ordem: 2, setor: 'Rebarbação', situacao: 'Passado' }),
        bloco({ chave: 'Passo:3', ordem: 3, setor: 'Dobra', ...ATUAL }),
        bloco({ chave: 'Passo:4', ordem: 4, setor: 'Solda', situacao: 'Futuro' }),
        bloco({ chave: 'Passo:5', ordem: 5, setor: 'Pintura', situacao: 'Futuro' }),
      ],
    }))
    const itens = within(celular()).getAllByRole('listitem').map((i) => i.textContent)
    expect(itens).toEqual([
      '1–2 · Corte laser, Rebarbação · já passaram',
      'Dobra — 2 no Setor · há 6 h',
      '4–5 · ainda não chegaram',
    ])
  })
})
