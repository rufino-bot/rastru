// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react'
import { CartaoDeKitIncompleto, CartaoDeKitMontavel } from './CartaoDeKit'
import { no } from '../testes/execucao'
import type { FilhoDoKitDto, KitDto } from '../api/execucao'

afterEach(cleanup)

const CHASSI = no({ id: 2, descricao: 'Chassi', codigoDoComponente: 'CH-01', paiId: null, paiDescricao: null, agrupamentoTipo: 'Kit' })

function filho(parcial: Partial<FilhoDoKitDto> = {}): FilhoDoKitDto {
  return {
    no: no({ agrupamentoTipo: 'Kit' }), quantidadePorPai: 4,
    origem: { id: 1, nome: 'Corte' }, ordem: 1, pronto: 8, jaNoDestino: false,
    ...parcial,
  }
}

/** Chassi com Suporte (razão 4, 8 prontos no Corte) e Parafuso (razão 1, já na Solda): dá para levar 2. */
function kit(parcial: Partial<KitDto> = {}): KitDto {
  return {
    pai: CHASSI,
    destino: { id: 4, nome: 'Solda' },
    conjuntos: 2,
    filhos: [
      filho(),
      filho({
        no: no({ id: 8, descricao: 'Parafuso', codigoDoComponente: null, agrupamentoTipo: 'Kit' }),
        quantidadePorPai: 1, origem: { id: 4, nome: 'Solda' }, ordem: 2, pronto: 3, jaNoDestino: true,
      }),
    ],
    ...parcial,
  }
}

const linhasDosFilhos = () =>
  within(screen.getByRole('list', { name: 'Filhos do Kit Chassi' })).getAllByRole('listitem').map((li) => li.textContent)

function montavel(props: Partial<Parameters<typeof CartaoDeKitMontavel>[0]> = {}) {
  const aoAlternar = vi.fn()
  const aoMudar = vi.fn()
  render(
    <ul>
      <CartaoDeKitMontavel kit={kit()} podeEntregar conjuntos={undefined} aoAlternar={aoAlternar} aoMudar={aoMudar} {...props} />
    </ul>,
  )
  return { aoAlternar, aoMudar }
}

describe('CartaoDeKitMontavel', () => {
  it('mostra o pai, o destino, quantos conjuntos dá para levar e a origem de cada filho', () => {
    montavel()

    expect(screen.getByText('CH-01 — Chassi')).toBeTruthy()
    expect(screen.getByText('PED-2026-01 › AG-01')).toBeTruthy()
    expect(screen.getByText('Destino: Solda (início de Chassi)')).toBeTruthy()
    expect(screen.getByText('Dá para levar 2 conjunto(s)')).toBeTruthy()
    // Sem marcar, a quantidade de cada filho é a do N máximo.
    expect(linhasDosFilhos()).toEqual([
      'SUP-01 — Suporte: 8 (de Corte, passo 1)',
      'Parafuso: 2 (já está em Solda)',
    ])
  })

  it('desmarcado não mostra o campo Conjuntos', () => {
    montavel()

    expect(screen.queryByLabelText('Conjuntos')).toBeNull()
    expect((screen.getByRole('checkbox', { name: 'Levar o Kit CH-01 — Chassi' }) as HTMLInputElement).checked).toBe(false)
  })

  it('marcar chama aoAlternar(true); desmarcar, aoAlternar(false)', () => {
    const { aoAlternar } = montavel()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit CH-01 — Chassi' }))
    expect(aoAlternar).toHaveBeenLastCalledWith(true)

    cleanup()
    const segundo = montavel({ conjuntos: '2' })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Levar o Kit CH-01 — Chassi' }))
    expect(segundo.aoAlternar).toHaveBeenLastCalledWith(false)
  })

  it('com conjuntos="1", a quantidade de cada filho acompanha: 1 × razão', () => {
    montavel({ conjuntos: '1' })

    expect((screen.getByLabelText('Conjuntos') as HTMLInputElement).value).toBe('1')
    expect(linhasDosFilhos()).toEqual([
      'SUP-01 — Suporte: 4 (de Corte, passo 1)',
      'Parafuso: 1 (já está em Solda)',
    ])
    expect(screen.getByLabelText('Conjuntos').getAttribute('aria-invalid')).toBe('false')
    expect(screen.getByText('Dá para levar: 2')).toBeTruthy()
  })

  it('digitar no campo chama aoMudar com o texto', () => {
    const { aoMudar } = montavel({ conjuntos: '2' })

    fireEvent.change(screen.getByLabelText('Conjuntos'), { target: { value: '1' } })

    expect(aoMudar).toHaveBeenCalledWith('1')
  })

  it('conjunto fracionado fica aria-invalid e diz por quê', () => {
    montavel({ conjuntos: '1,5' })

    expect(screen.getByLabelText('Conjuntos').getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByText('Digite um número inteiro de conjuntos.')).toBeTruthy()
  })

  it('acima do máximo fica aria-invalid', () => {
    montavel({ conjuntos: '3' })

    expect(screen.getByLabelText('Conjuntos').getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByText('No máximo 2.')).toBeTruthy()
  })

  it('a quantidade do filho arredonda a quatro casas (3 × 0,1 = 0,3)', () => {
    montavel({
      kit: kit({ conjuntos: 3, filhos: [filho({ quantidadePorPai: 0.1, pronto: 0.3 })] }),
      conjuntos: '3',
    })

    expect(linhasDosFilhos()).toEqual(['SUP-01 — Suporte: 0,3 (de Corte, passo 1)'])
  })

  it('Pedido pausado mostra a pílula Pausado', () => {
    montavel({ kit: kit({ pai: { ...CHASSI, pausa: { desde: '2026-09-28T10:14:00-03:00', porUsuarioNome: 'PCP', motivo: null } } }) })

    expect(screen.getByText('Pausado')).toBeTruthy()
  })

  it('sem pausa não mostra a pílula', () => {
    montavel()

    expect(screen.queryByText('Pausado')).toBeNull()
  })

  it('sem permissão de entregar não há caixa', () => {
    montavel({ podeEntregar: false })

    expect(screen.queryByRole('checkbox')).toBeNull()
  })
})

describe('CartaoDeKitIncompleto', () => {
  it('mostra o que está pronto e o que falta para 1 conjunto, sem caixa de marcar', () => {
    render(
      <ul>
        <CartaoDeKitIncompleto
          kit={kit({
            conjuntos: 0,
            filhos: [
              filho({ quantidadePorPai: 9, pronto: 8 }),
              filho({ no: no({ id: 8, descricao: 'Parafuso', codigoDoComponente: null }), quantidadePorPai: 2, pronto: 0, origem: null, ordem: null }),
              filho({ no: no({ id: 9, descricao: 'Porca', codigoDoComponente: null }), quantidadePorPai: 1, pronto: 5 }),
            ],
          })}
        />
      </ul>,
    )

    expect(screen.getByText('CH-01 — Chassi')).toBeTruthy()
    expect(linhasDosFilhos()).toEqual([
      'SUP-01 — Suporte: pronto 8, falta 1 para 1 conjunto',
      'Parafuso: nenhum pronto, falta 2 para 1 conjunto',
      'Porca: pronto 5',
    ])
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.queryByLabelText('Conjuntos')).toBeNull()
  })
})
