// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation, useNavigationType } from 'react-router-dom'
import type { Selecao } from '../components/FiltroDeDemanda'
import { useSelecaoNaUrl } from './useSelecaoNaUrl'

afterEach(cleanup)

const CHAVES = ['material', 'pedido'] as const

function Sonda({ escrever }: { escrever?: Selecao }) {
  const { selecao, mudarSelecao, limpar } = useSelecaoNaUrl(CHAVES)
  const location = useLocation()
  const tipo = useNavigationType()
  return (
    <div>
      <output data-testid="selecao">{JSON.stringify(selecao)}</output>
      <output data-testid="busca">{location.search}</output>
      <output data-testid="navegacao">{tipo}</output>
      <button type="button" onClick={() => mudarSelecao(escrever ?? {})}>
        escrever
      </button>
      <button type="button" onClick={limpar}>
        limpar
      </button>
    </div>
  )
}

function montar(url: string, escrever?: Selecao) {
  render(
    <MemoryRouter initialEntries={[url]}>
      <Sonda escrever={escrever} />
    </MemoryRouter>,
  )
}

const selecaoLida = () => JSON.parse(screen.getByTestId('selecao').textContent ?? '')
const buscaDaUrl = () => new URLSearchParams(screen.getByTestId('busca').textContent ?? '')

describe('useSelecaoNaUrl', () => {
  it('le cada chave separada por virgula', () => {
    montar('/fila/1?material=3,5&pedido=42')

    expect(selecaoLida()).toEqual({ material: ['3', '5'], pedido: ['42'] })
  })

  it('ignora pedaco vazio, apara espaco e colapsa repetido', () => {
    montar('/fila/1?material=abc,,5,%205')

    expect(selecaoLida()).toEqual({ material: ['abc', '5'] })
  })

  it('escreve so as chaves com valor e preserva os outros parametros', () => {
    montar('/fila/1?pagina=2&material=3', { pedido: ['42'], material: [] })

    fireEvent.click(screen.getByRole('button', { name: 'escrever' }))

    const busca = buscaDaUrl()
    expect(busca.get('pagina')).toBe('2')
    expect(busca.get('pedido')).toBe('42')
    expect(busca.has('material')).toBe(false)
    expect(selecaoLida()).toEqual({ pedido: ['42'] })
  })

  it('escrever nao cria entrada nova no historico', () => {
    montar('/fila/1?material=3', { material: ['3', '5'] })
    expect(screen.getByTestId('navegacao').textContent).toBe('POP')

    fireEvent.click(screen.getByRole('button', { name: 'escrever' }))

    expect(screen.getByTestId('navegacao').textContent).toBe('REPLACE')
    expect(buscaDaUrl().get('material')).toBe('3,5')
  })

  it('limpar tira so as chaves do hook', () => {
    montar('/fila/1?pagina=2&material=3&pedido=42')

    fireEvent.click(screen.getByRole('button', { name: 'limpar' }))

    const busca = buscaDaUrl()
    expect(busca.get('pagina')).toBe('2')
    expect(busca.has('material')).toBe(false)
    expect(busca.has('pedido')).toBe(false)
    expect(selecaoLida()).toEqual({})
  })
})
