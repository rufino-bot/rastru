// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import {
  casaComFiltro,
  contarFiltrosAtivos,
  FiltroDeDemanda,
  ROTULO_DE_OPCAO_AUSENTE,
  type Faceta,
} from './FiltroDeDemanda'

afterEach(cleanup)

describe('casaComFiltro', () => {
  it('faceta sem nada marcado nao restringe', () => {
    expect(casaComFiltro({ material: ['3'] }, {})).toBe(true)
    expect(casaComFiltro({ material: ['3'] }, { material: [] })).toBe(true)
  })

  it('OU dentro da faceta', () => {
    expect(casaComFiltro({ pedido: ['42'] }, { pedido: ['41', '42'] })).toBe(true)
    expect(casaComFiltro({ pedido: ['42'] }, { pedido: ['41', '43'] })).toBe(false)
  })

  it('E entre facetas', () => {
    const item = { material: ['3'], pedido: ['42'] }
    expect(casaComFiltro(item, { material: ['3'], pedido: ['42'] })).toBe(true)
    expect(casaComFiltro(item, { material: ['3'], pedido: ['41'] })).toBe(false)
  })

  it('item com varios valores na faceta casa se algum for marcado', () => {
    expect(casaComFiltro({ material: ['3', '5'] }, { material: ['5'] })).toBe(true)
  })

  it('item sem valor numa faceta ativa nao casa', () => {
    expect(casaComFiltro({ material: [] }, { material: ['3'] })).toBe(false)
    expect(casaComFiltro({}, { material: ['3'] })).toBe(false)
  })
})

describe('contarFiltrosAtivos', () => {
  it('soma os valores marcados de todas as facetas', () => {
    expect(contarFiltrosAtivos({})).toBe(0)
    expect(contarFiltrosAtivos({ material: [], pedido: [] })).toBe(0)
    expect(contarFiltrosAtivos({ material: ['3', '5'], pedido: ['42'] })).toBe(3)
  })
})

function facetas(sobrescrita?: Partial<Record<'material' | 'pedido', Faceta['opcoes']>>): Faceta[] {
  return [
    {
      chave: 'material',
      titulo: 'Material',
      opcoes: sobrescrita?.material ?? [
        {
          valor: '3',
          rotulo: 'Chapa SAE 1020 3,00 mm',
          rotuloCurto: 'Chapa 3,00 mm',
          detalhe: 'CH-300',
          contagem: 7,
        },
        { valor: '5', rotulo: 'Barra redonda 20 mm', detalhe: 'BR-20', contagem: 2 },
      ],
    },
    {
      chave: 'pedido',
      titulo: 'Pedido',
      opcoes: sobrescrita?.pedido ?? [
        { valor: '42', rotulo: 'Pedido 1042 · Metalúrgica Alfa', rotuloCurto: 'Pedido 1042', contagem: 4 },
        { valor: '43', rotulo: 'Pedido 1043 · Metalúrgica Beta', rotuloCurto: 'Pedido 1043', contagem: 1 },
      ],
    },
  ]
}

function abrir() {
  fireEvent.click(screen.getByRole('button', { name: /^Filtrar/ }))
}

describe('FiltroDeDemanda', () => {
  it('fechado mostra Filtrar com o numero de filtros ativos, as pilulas e Limpar', () => {
    render(<FiltroDeDemanda facetas={facetas()} selecao={{ material: ['3'], pedido: ['42'] }} aoMudar={vi.fn()} />)

    const filtrar = screen.getByRole('button', { name: 'Filtrar (2)' })
    expect(filtrar.getAttribute('aria-expanded')).toBe('false')
    expect(screen.getByRole('button', { name: 'Remover filtro Chapa 3,00 mm' }).textContent).toContain(
      'Chapa 3,00 mm',
    )
    expect(screen.getByRole('button', { name: 'Remover filtro Pedido 1042' }).textContent).toContain(
      'Pedido 1042',
    )
    expect(screen.getByRole('button', { name: 'Limpar' })).toBeTruthy()
    expect(screen.queryByRole('group', { name: 'Material' })).toBeNull()
  })

  it('sem filtro ativo nao mostra pilulas nem Limpar', () => {
    render(<FiltroDeDemanda facetas={facetas()} selecao={{}} aoMudar={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Filtrar' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Remover filtro/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Limpar' })).toBeNull()
  })

  it('abrir mostra um grupo por faceta com as opcoes marcaveis e a contagem', () => {
    render(<FiltroDeDemanda facetas={facetas()} selecao={{}} aoMudar={vi.fn()} />)

    abrir()

    expect(screen.getByRole('button', { name: 'Filtrar' }).getAttribute('aria-expanded')).toBe('true')
    const grupo = screen.getByRole('group', { name: 'Material' })
    expect(within(grupo).getByRole('checkbox', { name: 'Chapa SAE 1020 3,00 mm' })).toBeTruthy()
    expect(within(grupo).getByText('7')).toBeTruthy()
    expect(within(grupo).getByText('CH-300').className.split(/\s+/)).toContain('font-mono')
    expect(screen.getByRole('group', { name: 'Pedido' })).toBeTruthy()
  })

  it('marcar uma opcao chama aoMudar com a selecao nova', () => {
    const aoMudar = vi.fn()
    render(<FiltroDeDemanda facetas={facetas()} selecao={{ material: ['3'] }} aoMudar={aoMudar} />)

    abrir()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Pedido 1042 · Metalúrgica Alfa' }))

    expect(aoMudar).toHaveBeenCalledWith({ material: ['3'], pedido: ['42'] })
  })

  it('desmarcar uma opcao tira so aquele valor da faceta', () => {
    const aoMudar = vi.fn()
    render(<FiltroDeDemanda facetas={facetas()} selecao={{ material: ['3', '5'] }} aoMudar={aoMudar} />)

    abrir()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Chapa SAE 1020 3,00 mm' }))

    expect(aoMudar).toHaveBeenCalledWith({ material: ['5'] })
  })

  it('remover pilula tira so aquele valor', () => {
    const aoMudar = vi.fn()
    render(<FiltroDeDemanda facetas={facetas()} selecao={{ material: ['3'], pedido: ['42'] }} aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Remover filtro Pedido 1042' }))

    expect(aoMudar).toHaveBeenCalledWith({ material: ['3'] })
  })

  it('Limpar zera a selecao', () => {
    const aoMudar = vi.fn()
    render(<FiltroDeDemanda facetas={facetas()} selecao={{ material: ['3'], pedido: ['42'] }} aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Limpar' }))

    expect(aoMudar).toHaveBeenCalledWith({})
  })

  it('opcao marcada que some da lista continua marcada e visivel com zero', () => {
    const selecao = { pedido: ['42'] }
    const { rerender } = render(<FiltroDeDemanda facetas={facetas()} selecao={selecao} aoMudar={vi.fn()} />)

    rerender(
      <FiltroDeDemanda
        facetas={facetas({
          pedido: [{ valor: '43', rotulo: 'Pedido 1043 · Metalúrgica Beta', rotuloCurto: 'Pedido 1043', contagem: 1 }],
        })}
        selecao={selecao}
        aoMudar={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: 'Remover filtro Pedido 1042' })).toBeTruthy()
    abrir()
    const grupo = screen.getByRole('group', { name: 'Pedido' })
    const caixa = within(grupo).getByRole('checkbox', {
      name: 'Pedido 1042 · Metalúrgica Alfa',
    }) as HTMLInputElement
    expect(caixa.checked).toBe(true)
    expect(within(grupo).getByText('0')).toBeTruthy()
  })

  it('valor marcado que a tela nunca viu aparece como ausente, com zero, removivel', () => {
    const aoMudar = vi.fn()
    render(<FiltroDeDemanda facetas={facetas()} selecao={{ pedido: ['99'] }} aoMudar={aoMudar} />)

    abrir()
    const grupo = screen.getByRole('group', { name: 'Pedido' })
    const caixa = within(grupo).getByRole('checkbox', { name: ROTULO_DE_OPCAO_AUSENTE }) as HTMLInputElement
    expect(caixa.checked).toBe(true)
    expect(within(grupo).getByText('0')).toBeTruthy()

    fireEvent.click(caixa)

    expect(aoMudar).toHaveBeenCalledWith({})
  })

  it('faceta com mais opcoes que o limite ganha campo de busca', () => {
    const opcoes = (n: number) =>
      Array.from({ length: n }, (_, i) => ({ valor: String(i + 1), rotulo: `Pedido ${i + 1}` }))

    const { unmount } = render(
      <FiltroDeDemanda facetas={facetas({ pedido: opcoes(9) })} selecao={{}} aoMudar={vi.fn()} />,
    )
    abrir()
    expect(screen.getByRole('searchbox', { name: 'Buscar em Pedido' })).toBeTruthy()
    expect(screen.queryByRole('searchbox', { name: 'Buscar em Material' })).toBeNull()
    unmount()

    render(<FiltroDeDemanda facetas={facetas({ pedido: opcoes(8) })} selecao={{}} aoMudar={vi.fn()} />)
    abrir()
    expect(screen.queryByRole('searchbox', { name: 'Buscar em Pedido' })).toBeNull()
  })

  it('busca na faceta ignora caixa e acento', () => {
    const outras = Array.from({ length: 7 }, (_, i) => ({ valor: String(i + 10), rotulo: `Barra ${i + 1}` }))
    const opcoes = [
      { valor: '1', rotulo: 'Chapa fina' },
      { valor: '2', rotulo: 'Sôlda MIG' },
      ...outras,
    ]
    render(<FiltroDeDemanda facetas={facetas({ material: opcoes })} selecao={{}} aoMudar={vi.fn()} />)
    abrir()
    const grupo = screen.getByRole('group', { name: 'Material' })
    const busca = within(grupo).getByRole('searchbox', { name: 'Buscar em Material' })

    fireEvent.change(busca, { target: { value: 'solda' } })
    expect(within(grupo).getAllByRole('checkbox')).toHaveLength(1)
    expect(within(grupo).getByRole('checkbox', { name: 'Sôlda MIG' })).toBeTruthy()

    fireEvent.change(busca, { target: { value: 'CHAPA' } })
    expect(within(grupo).getAllByRole('checkbox')).toHaveLength(1)
    expect(within(grupo).getByRole('checkbox', { name: 'Chapa fina' })).toBeTruthy()
  })

  it('pilula de filtro nao usa cor de estado', () => {
    const { container } = render(
      <FiltroDeDemanda facetas={facetas()} selecao={{ material: ['3'], pedido: ['99'] }} aoMudar={vi.fn()} />,
    )
    abrir()

    const html = container.innerHTML
    expect(html).not.toMatch(/positivo|negativo|atencao|atraso/)
  })
})
