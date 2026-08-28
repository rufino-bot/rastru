// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { LinhaDePedido } from './LinhaDePedido'
import type { PedidoDto } from '../api/cadastros'

afterEach(cleanup)

const PEDIDO: PedidoDto = {
  id: 7, numero: 'PED-042', cliente: 'Metalúrgica Alfa', tipo: 'Fabricacao',
  status: 'Aberto', dataAbertura: '2026-08-01T09:30:00-03:00', criadoPorUsuarioId: 1,
}

function renderizar(pedido: PedidoDto) {
  render(<MemoryRouter><LinhaDePedido pedido={pedido} /></MemoryRouter>)
}

describe('LinhaDePedido', () => {
  it('leva ao pedido pelo id, e nao pela posicao na lista', () => {
    // `id: 7` num render de um item só: uma implementação que use índice de array acertaria com
    // `/pedidos/0`, e este teste é o que separa os dois casos.
    renderizar(PEDIDO)

    expect(screen.getByRole('link').getAttribute('href')).toBe('/pedidos/7')
  })

  it('mostra numero, cliente e a data de abertura no formato do projeto', () => {
    // `formatarDataHora` NÃO passa por `Date` de propósito (a data já vem em GMT-3 com offset, e
    // `new Date()` a reconverteria para o fuso do aparelho). Este teste fixa o formato de saída:
    // se alguém trocar a formatação por `toLocaleString`, o dia aparece certo nesta bancada e
    // errado num tablet fora do fuso — e só esta asserção pega.
    renderizar(PEDIDO)

    expect(screen.getByText('PED-042')).toBeTruthy()
    expect(screen.getByText(/Metalúrgica Alfa/)).toBeTruthy()
    expect(screen.getByText(/aberto em 01\/08\/2026 09:30/)).toBeTruthy()
  })

  it('reserva verde para Concluido e vermelho para Cancelado, e deixa o resto neutro', () => {
    // Asserção pela CLASSE, e não pelo texto: `Pilula` renderiza `children` seja qual for o tom,
    // então achar a palavra "Concluido" na tela não prova tom nenhum. É a mesma forma que
    // `PedidosPage.test.tsx` já usa.
    renderizar({ ...PEDIDO, status: 'Concluido' })
    expect(screen.getByText('Concluido').className).toMatch(/positivo-/)
    cleanup()

    renderizar({ ...PEDIDO, status: 'Cancelado' })
    expect(screen.getByText('Cancelado').className).toMatch(/negativo-/)
    cleanup()

    renderizar({ ...PEDIDO, status: 'EmProducao' })
    const neutra = screen.getByText('EmProducao').className
    expect(neutra).not.toMatch(/positivo-/)
    expect(neutra).not.toMatch(/negativo-/)
  })

  it('estende a area clicavel ao item inteiro', () => {
    // ⚠️ Esta asserção é sobre a CLASSE, não sobre o comportamento: jsdom não calcula layout, e
    // nenhum teste desta suíte consegue provar que o overlay realmente cobre o `<li>`. O que ela
    // impede é o apagamento silencioso do overlay — numa bancada com tablet, alvo do tamanho do
    // número em vez do item inteiro erra o clique, e nada na suíte reclamaria.
    renderizar(PEDIDO)

    const classes = screen.getByRole('link').className.split(/\s+/)
    expect(classes).toContain('after:absolute')
    expect(classes).toContain('after:inset-0')
  })
})
