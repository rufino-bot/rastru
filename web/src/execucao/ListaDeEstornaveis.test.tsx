// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { ListaDeEstornaveis } from './ListaDeEstornaveis'
import type { Estornavel } from '../api/execucao'

afterEach(cleanup)

const DOIS: Estornavel[] = [
  { tipo: 'Termino', id: 42, quantidade: 3, usuarioId: 12, usuarioNome: 'Ana', dataHora: '2026-09-28T10:20:00-03:00' },
  { tipo: 'Termino', id: 41, quantidade: 5, usuarioId: 12, usuarioNome: 'Ana', dataHora: '2026-09-28T10:14:00-03:00' },
]

describe('ListaDeEstornaveis', () => {
  it('lista cada registro na ordem recebida, com um Estornar próprio', () => {
    render(<ListaDeEstornaveis estornaveis={DOIS} aoEscolher={() => {}} aoCancelar={() => {}} />)

    expect(screen.getAllByRole('button', { name: /^Estornar / }).map((b) => b.getAttribute('aria-label'))).toEqual([
      'Estornar Término de 3 · Ana · 28/09/2026 10:20',
      'Estornar Término de 5 · Ana · 28/09/2026 10:14',
    ])
  })

  it('escolher entrega o registro ao chamador', () => {
    const aoEscolher = vi.fn()
    render(<ListaDeEstornaveis estornaveis={DOIS} aoEscolher={aoEscolher} aoCancelar={() => {}} />)

    fireEvent.click(screen.getByRole('button', { name: 'Estornar Término de 5 · Ana · 28/09/2026 10:14' }))

    expect(aoEscolher).toHaveBeenCalledWith(DOIS[1])
  })

  it('cancelar avisa o chamador', () => {
    const aoCancelar = vi.fn()
    render(<ListaDeEstornaveis estornaveis={DOIS} aoEscolher={() => {}} aoCancelar={aoCancelar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(aoCancelar).toHaveBeenCalledOnce()
  })
})
