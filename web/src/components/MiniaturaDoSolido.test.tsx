// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { MiniaturaDoSolido, limparCacheDeMiniaturas } from './MiniaturaDoSolido'
import { inicializar, _resetParaTeste } from '../api/client'

const png = () => new Response(new Uint8Array([137, 80, 78, 71]), { status: 200, headers: { 'Content-Type': 'image/png' } })

describe('MiniaturaDoSolido', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    inicializar({ getToken: () => 'tok', setToken: () => {}, onSessionLost: () => {} })
    limparCacheDeMiniaturas()
    fetchMock = vi.fn(async () => png())
    vi.stubGlobal('fetch', fetchMock)
    let n = 0
    URL.createObjectURL = vi.fn(() => `blob:miniatura-${++n}`)
    URL.revokeObjectURL = vi.fn()
  })

  afterEach(() => {
    cleanup()
    _resetParaTeste()
    vi.unstubAllGlobals()
  })

  it('sem versao mostra o espaco neutro com o nome', () => {
    render(<MiniaturaDoSolido caminho="/componentes/1/miniatura" versao={null} alt="Miniatura de P-1" />)
    const el = screen.getByRole('img', { name: 'Miniatura de P-1' })
    expect(el.tagName).not.toBe('IMG')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('com versao mostra a imagem', async () => {
    render(<MiniaturaDoSolido caminho="/componentes/1/miniatura" versao="a" alt="Miniatura de P-1" />)
    await waitFor(() => expect(screen.getByRole('img', { name: 'Miniatura de P-1' }).getAttribute('src')).toMatch(/^blob:/))
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/componentes/1/miniatura')
  })

  it('nao busca de novo a mesma versao', async () => {
    const p = { caminho: '/componentes/1/miniatura', versao: 'a', alt: 'M' }
    const { unmount } = render(<MiniaturaDoSolido {...p} />)
    await waitFor(() => expect(screen.getByRole('img').getAttribute('src')).toMatch(/^blob:/))
    unmount()
    render(<MiniaturaDoSolido {...p} />)
    await waitFor(() => expect(screen.getByRole('img').getAttribute('src')).toMatch(/^blob:/))
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('versao nova busca de novo', async () => {
    const { rerender } = render(<MiniaturaDoSolido caminho="/componentes/1/miniatura" versao="a" alt="M" />)
    await waitFor(() => expect(screen.getByRole('img').getAttribute('src')).toBe('blob:miniatura-1'))
    rerender(<MiniaturaDoSolido caminho="/componentes/1/miniatura" versao="b" alt="M" />)
    await waitFor(() => expect(screen.getByRole('img').getAttribute('src')).toBe('blob:miniatura-2'))
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('falha na busca mostra o espaco neutro', async () => {
    fetchMock.mockResolvedValue(new Response('x', { status: 404 }))
    render(<MiniaturaDoSolido caminho="/componentes/1/miniatura" versao="a" alt="M" />)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    await new Promise((r) => setTimeout(r, 0))
    expect(screen.getByRole('img', { name: 'M' }).tagName).not.toBe('IMG')
  })
})
