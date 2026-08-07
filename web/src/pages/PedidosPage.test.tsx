// @vitest-environment jsdom
//
// Ambiente por ARQUIVO, e não em `vite.config.ts`: os testes de `api/` rodam em ambiente `node` e
// usam `new Response(...)`; trocar o ambiente global arriscaria mexer nos globals deles sem ganho.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { PedidosPage } from './PedidosPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

// O auto-cleanup do RTL depende de um `afterEach` GLOBAL, que só existe com `globals: true` no
// Vitest — e este projeto importa `describe`/`it`/`expect` explicitamente, ou seja, globals off.
// Sem esta linha o segundo teste renderiza por cima do primeiro e os `getBy*` falham com
// "found multiple elements".
afterEach(cleanup)

const PEDIDO = {
  id: 7,
  numero: 'PED-001',
  cliente: 'Fábrica Alfa',
  tipo: 'Normal',
  status: 'Aberto',
  dataAbertura: '2026-08-06T09:30:00-03:00',
  criadoPorUsuarioId: 1,
}

describe('PedidosPage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra os pedidos que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson([PEDIDO]),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    // Texto exato, número antes do cliente: um `getByText(/PED-001/)` sozinho passaria mesmo com a
    // ordem trocada, porque casa por substring dentro do mesmo nó — ele já matou 0 mutações contra
    // a troca de ordem. A string inteira é o que prova a ordem. Acoplado ao markup de nó único de
    // propósito: a Task 8 deste plano re-diagrama esta tela para `ItemDeCadastro`, o que deve
    // separar número e cliente em nós distintos e quebrar esta asserção literal por mudança de
    // layout, não por regressão — quando isso acontecer, a propriedade a preservar é a ordem
    // (número antes do cliente), não esta string exata.
    expect(await screen.findByText('PED-001 — Fábrica Alfa')).toBeTruthy()
  })

  it('mostra a data de abertura no fuso que a API mandou, sem reconverter', async () => {
    // Offset +05:30 (Asia/Kolkata, fuso real — não um valor arbitrário): esta suíte roda em
    // -03:00, e é isso que fica provado aqui. Se alguém trocar `formatarDataHora` por
    // `new Date(...).toLocaleString()`, o horário reconverte para o fuso da máquina e deixa de
    // bater com 09:30 em qualquer máquina cujo fuso local não seja +05:30 — o que cobre esta
    // suíte, mas não é um absoluto universal (numa máquina em IST a mutação sobreviveria). Um
    // offset -03:00 na fixture coincidiria com o fuso local por acidente e deixaria a mutação
    // sobreviver aqui sem provar nada.
    const pedidoComFusoDistinto = { ...PEDIDO, dataAbertura: '2026-08-06T09:30:00+05:30' }
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson([pedidoComFusoDistinto]),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText(/06\/08\/2026 09:30/)).toBeTruthy()
  })

  it('mostra erro quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson({ erro: 'Falhou' }, 500),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText('Não foi possível carregar os pedidos.')).toBeTruthy()
  })

  it('limpa o formulário e recarrega a lista depois de abrir um pedido', async () => {
    let listagens = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => {
        listagens += 1
        return respostaJson(listagens === 1 ? [] : [PEDIDO])
      },
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)
    await screen.findByPlaceholderText('Código do pedido')

    fireEvent.change(screen.getByPlaceholderText('Código do pedido'), { target: { value: 'PED-001' } })
    fireEvent.change(screen.getByPlaceholderText('Cliente'), { target: { value: 'Fábrica Alfa' } })
    fireEvent.click(screen.getByText('Abrir pedido'))

    // O POST cai na MESMA rota da listagem: `fetchPorRota` casa por caminho, e o mock devolve a
    // lista nova. O que se prova aqui é o ramo de SUCESSO — campo limpo e lista recarregada —,
    // que é a metade que nenhum teste do projeto cobria antes desta task.
    expect(await screen.findByText(/PED-001/)).toBeTruthy()
    expect((screen.getByPlaceholderText('Código do pedido') as HTMLInputElement).value).toBe('')
  })
})
