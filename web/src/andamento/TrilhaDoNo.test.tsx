// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { TrilhaDoNo } from './TrilhaDoNo'
import { AGORA_DO_ANDAMENTO, bloco, no } from '../testes/andamento'

afterEach(cleanup)

function montar(n = no(), maisParado: { estruturaItemId: number; bloco: string } | null = null) {
  return render(<TrilhaDoNo no={n} agora={AGORA_DO_ANDAMENTO} maisParado={maisParado} />)
}

describe('TrilhaDoNo', () => {
  it('bloco_atual_com_idade_e_coleta', () => {
    montar(no({
      trilha: [bloco({
        situacao: 'Atual', quantidade: 4, desde: '2026-11-17T12:00:00-03:00', aguardandoColeta: 1,
        aguardandoColetaDesde: '2026-11-19T06:00:00-03:00',
      })],
    }))
    const item = screen.getByRole('listitem', { name: /Corte/ })
    expect(within(item).getByText(/4/)).toBeTruthy()
    expect(within(item).getByText(/há 2 d/)).toBeTruthy()
    expect(within(item).getByText('coleta 1 · há 6 h')).toBeTruthy()
  })

  it('bloco_passado_com_permanencia', () => {
    montar(no({ trilha: [bloco({ situacao: 'Passado', permanenciaMediaSegundos: 86400 * 2 })] }))
    expect(screen.getByText('passou · 2 d no Setor')).toBeTruthy()
  })

  it('bloco_futuro', () => {
    montar(no({ trilha: [bloco({ situacao: 'Futuro' })] }))
    const item = screen.getByRole('listitem', { name: /Corte/ })
    expect(within(item).getByText('ainda não chegou')).toBeTruthy()
    expect(item.className).toContain('border-dashed')
    expect(item.className).toContain('border-borda-campo')
  })

  it('mais_parado_marcado_sem_cor_de_estado', () => {
    const { container } = montar(
      no({ trilha: [bloco({ situacao: 'Atual', quantidade: 2, desde: '2026-11-19T06:00:00-03:00' })] }),
      { estruturaItemId: 812, bloco: 'Passo:1' },
    )
    const item = screen.getByRole('listitem', { name: /Corte/ })
    expect(within(item).getByText('mais parado')).toBeTruthy()
    expect(item.className).toContain('border-2')
    expect(item.className).toContain('border-tinta')
    expect(container.innerHTML).not.toMatch(/positivo|negativo|atencao|atraso/)
  })

  it('itens_abrem_quando_o_mais_parado_e_um_item', () => {
    const item = no({
      estruturaItemId: 900, codigo: 'ITEM-1',
      trilha: [bloco({ situacao: 'Atual', quantidade: 2, desde: '2026-11-19T06:00:00-03:00' })],
    })
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
})
