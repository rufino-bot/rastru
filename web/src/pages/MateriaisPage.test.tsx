// @vitest-environment jsdom
//
// O ambiente e declarado POR ARQUIVO, e nao em `vite.config.ts`, de proposito: os demais testes da
// suite rodam em ambiente `node` e usam `new Response(...)`; trocar o ambiente global para jsdom
// arriscaria mexer nos globals de que eles dependem, sem ganho nenhum. Teste de componente e a
// excecao, entao a excecao fica no arquivo.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { MateriaisPage } from './MateriaisPage'
import { inicializar, _resetParaTeste } from '../api/client'

// O auto-cleanup do RTL depende de um `afterEach` GLOBAL, que so existe com `globals: true` no
// Vitest — e este projeto importa `describe`/`it`/`expect` explicitamente, ou seja, globals off.
// Sem esta linha, o segundo teste do arquivo renderiza por cima do primeiro e os `getBy*` falham
// com "found multiple elements".
afterEach(cleanup)

describe('MateriaisPage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra os materiais que a API devolveu', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify([
          { id: 1, codigo: 'CH-001', descricao: 'Chapa de aco 3mm', unidadeMedida: 'KG', ativo: true },
        ]),
        { status: 200 },
      ),
    ))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
  })

  // I1 (achado da review de branch): `carregar` escreve `erro` no `catch` mas nunca o limpa no
  // caminho de sucesso. A carga inicial falha (mensagem na tela); marcar "Mostrar inativos" dispara
  // uma recarga que da certo — a lista nova tem que aparecer E a mensagem de erro tem que sumir.
  it('limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso', async () => {
    let chamadas = 0
    const fetchMock = vi.fn().mockImplementation(() => {
      chamadas += 1
      if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
      return Promise.resolve(new Response(
        JSON.stringify([
          { id: 1, codigo: 'CH-001', descricao: 'Chapa de aco 3mm', unidadeMedida: 'KG', ativo: true },
        ]),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await screen.findByText('Não foi possível carregar os materiais.')

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await screen.findByText('CH-001')
    expect(screen.queryByText('Não foi possível carregar os materiais.')).toBeNull()
  })
})
