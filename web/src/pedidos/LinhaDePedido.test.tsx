// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { LinhaDePedido } from './LinhaDePedido'
import type { PedidoDto } from '../api/cadastros'

afterEach(cleanup)

const PEDIDO: PedidoDto = {
  id: 7, numero: 'PED-042', cliente: 'Metalúrgica Alfa', tipo: 'Fabricacao',
  status: 'Aberto', dataAbertura: '2026-08-01T09:30:00-03:00', dataEntrega: '2026-10-22', atrasado: false,
  criadoPorUsuarioId: 1, pausa: null,
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
    expect(screen.getByText('Concluído').className).toMatch(/positivo-/)
    cleanup()

    renderizar({ ...PEDIDO, status: 'Cancelado' })
    expect(screen.getByText('Cancelado').className).toMatch(/negativo-/)
    cleanup()

    renderizar({ ...PEDIDO, status: 'EmProducao' })
    const neutra = screen.getByText('Em produção').className
    // Neutro afirmado token a token: o âmbar (`atencao-`) também é cor de estado, e "nem verde nem
    // vermelho" não o vê.
    const classesNeutras = neutra.split(/\s+/)
    expect(classesNeutras).toContain('bg-acao-fundo')
    expect(classesNeutras).toContain('text-acao')
    expect(neutra).not.toMatch(/positivo-/)
    expect(neutra).not.toMatch(/negativo-/)
  })

  it('a pilula do status mostra o rotulo em portugues, e nao o nome do valor', () => {
    renderizar({ ...PEDIDO, status: 'EmProducao' })

    expect(screen.getByText('Em produção')).toBeTruthy()
    expect(screen.queryByText('EmProducao')).toBeNull()
  })

  it('mostra a pilula "Pausado" so quando o Pedido tem pausa aberta, e so ela', () => {
    // A lista diz QUE está pausado; quem, quando e por quê ficam no detalhe (spec da Fase 3D, §6.3).
    renderizar(PEDIDO)
    expect(screen.queryByText('Pausado')).toBeNull()
    cleanup()

    renderizar({ ...PEDIDO, pausa: { desde: '2026-09-28T10:14:00-03:00', porUsuarioNome: 'PCP', motivo: 'urgente' } })
    expect(screen.getByText('Pausado')).toBeTruthy()
    expect(screen.queryByText(/urgente/)).toBeNull()
    expect(screen.queryByText(/28\/09\/2026/)).toBeNull()
  })

  it('a pilula "Pausado" usa o tom de atencao: vermelho e verde sao reservados a erro e aprovado', () => {
    renderizar({ ...PEDIDO, pausa: { desde: '2026-09-28T10:14:00-03:00', porUsuarioNome: 'PCP', motivo: null } })

    const classes = screen.getByText('Pausado').className.split(/\s+/)
    expect(classes).toContain('bg-atencao-fundo')
    expect(classes).toContain('text-atencao-texto')
    expect(classes.some((c) => /positivo-|negativo-/.test(c))).toBe(false)
  })

  it('mostra o prazo de entrega antes da data de abertura, cada um num bloco que nao quebra', () => {
    renderizar(PEDIDO)

    const entrega = screen.getByText('entrega em 22/10/2026 ·')
    const abertura = screen.getByText('aberto em 01/08/2026 09:30')
    expect(entrega.className.split(/\s+/)).toContain('whitespace-nowrap')
    expect(abertura.className.split(/\s+/)).toContain('whitespace-nowrap')
    // Ordem no documento: o prazo vem antes da abertura.
    expect(entrega.compareDocumentPosition(abertura) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('mostra a pilula Atrasado so quando o servidor diz que esta atrasado', () => {
    // A tela nao recalcula o atraso: um Pedido de prazo vencido com `atrasado: false` (encerrado) nao
    // ganha pilula.
    renderizar({ ...PEDIDO, dataEntrega: '2020-01-01', atrasado: false })
    expect(screen.queryByText('Atrasado')).toBeNull()
    cleanup()

    renderizar({ ...PEDIDO, atrasado: true })
    expect(screen.getByText('Atrasado')).toBeTruthy()
  })

  it('a pilula Atrasado usa o tom roxo, e nao o vermelho nem o ambar', () => {
    renderizar({ ...PEDIDO, atrasado: true })

    const classes = screen.getByText('Atrasado').className.split(/\s+/)
    expect(classes).toContain('bg-atraso-fundo')
    expect(classes).toContain('text-atraso-texto')
    expect(classes.some((c) => /negativo-|atencao-/.test(c))).toBe(false)
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
