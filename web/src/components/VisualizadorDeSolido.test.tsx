// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { VisualizadorDeSolido } from './VisualizadorDeSolido'
import { respostaBinaria } from '../testes/api'
import { inicializar, _resetParaTeste } from '../api/client'

// Contador de import: a fábrica de `vi.mock` só roda quando o módulo é importado pela primeira
// vez. Distingue import ESTÁTICO (a fábrica já rodou ao carregar este arquivo) de import DINÂMICO
// no clique (a fábrica só roda depois). Dois contadores porque os dois módulos são importados
// separadamente no componente (`Promise.all` com um `import()` para cada) e cada um pode
// regredir para estático de forma independente: um `STLLoader` estático, sozinho, já faz o
// `three` inteiro (que ele importa estaticamente por dentro) voltar ao bundle principal, mesmo
// com `import('three')` continuando dinâmico — por isso a asserção sobre `stlLoader` é
// indispensável e não redundante com a de `three`.
const importacoes = vi.hoisted(() => ({ three: 0, stlLoader: 0 }))

// `apiFetch` exige `inicializar()` — molde de `UploadDeSolido.test.tsx`.
beforeEach(() => {
  _resetParaTeste()
  inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

// O jsdom não implementa WebGL: nenhum teste aqui prova que o sólido APARECE. O que se prova é o
// que o componente controla — buscar, os três estados, e não carregar o three.js sem clique. O
// canvas renderizado de verdade é coberto pela verificação manual em navegador (Task 9 da spec).
//
// O dublê de `three` é o mínimo que `VisualizadorDeSolido.montarCena` usa: `Object3D`-like com
// `position.set`/`rotation`/`add`, e um `WebGLRenderer` cujo `domElement` é um `<canvas>` REAL do
// jsdom (não um objeto qualquer) — é nele que o componente escreve o `aria-label` que o teste do
// estado pronto procura.
vi.mock('three', () => {
  importacoes.three++

  class Object3DFalso {
    position = { set: vi.fn() }
    rotation = { y: 0 }
    add = vi.fn()
  }

  class WebGLRendererFalso {
    domElement = document.createElement('canvas')
    setSize = vi.fn()
    render = vi.fn()
    dispose = vi.fn()
  }

  return {
    Scene: Object3DFalso,
    PerspectiveCamera: Object3DFalso,
    AmbientLight: Object3DFalso,
    DirectionalLight: Object3DFalso,
    Mesh: Object3DFalso,
    MeshStandardMaterial: class {},
    WebGLRenderer: WebGLRendererFalso,
  }
})

// Dublê do STLLoader: `parse` devolve uma geometria falsa com os dois métodos que
// `montarCena` chama antes de montar a cena (`computeBoundingBox`/`center`), ambos no-op.
vi.mock('three/examples/jsm/loaders/STLLoader.js', () => {
  importacoes.stlLoader++

  return {
    STLLoader: class {
      parse() {
        return { computeBoundingBox: () => {}, center: () => {} }
      }
    },
  }
})

describe('VisualizadorDeSolido', () => {
  it('não busca o sólido nem carrega o three.js antes do clique', () => {
    const fetchMock = vi.fn((_url: string | URL, _init?: RequestInit) => Promise.resolve(new Response()))
    vi.stubGlobal('fetch', fetchMock)

    render(<VisualizadorDeSolido componenteId={7} />)

    expect(fetchMock).not.toHaveBeenCalled()
    // Mata o import ESTÁTICO de `three`: com `import ... from 'three'` no topo do módulo, a
    // fábrica já teria rodado quando este arquivo de teste importou `VisualizadorDeSolido`, antes
    // de qualquer clique.
    expect(importacoes.three).toBe(0)
    // Mata o import ESTÁTICO do `STLLoader`, isoladamente: um `STLLoader` estático, sozinho, traz
    // o `three` inteiro de volta ao bundle principal (ele o importa estaticamente por dentro),
    // mesmo com `import('three')` continuando dinâmico no componente — a asserção sobre
    // `importacoes.three`, sozinha, não pega essa regressão.
    expect(importacoes.stlLoader).toBe(0)
  })

  it('busca o binário quando o usuário pede para visualizar', async () => {
    const fetchMock = vi.fn((_url: string | URL, _init?: RequestInit) =>
      Promise.resolve(respostaBinaria(new Uint8Array(684))))
    vi.stubGlobal('fetch', fetchMock)

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/componentes/7/solido')
  })

  it('mostra carregando enquanto busca, e some o botão de Visualizar', async () => {
    // Promise que não resolve — o estado intermediário precisa ficar observável. Molde de
    // `UploadDeSolido.test.tsx` / `SeletorComBusca.test.tsx`.
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))

    expect(await screen.findByRole('status')).toBeTruthy()
    // Enquanto carrega não há botão para clicar de novo — mata se o botão continuar visível
    // junto do estado de carregando.
    expect(screen.queryByRole('button', { name: /visualizar/i })).toBeNull()
  })

  it('mostra erro quando a busca falha, e mantém o botão para tentar de novo', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(0), undefined, 404))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())
    // Texto pelo `mensagemDeErro`: 404 tem frase própria ("Este registro não existe mais."), que é
    // o que `mensagemDeErro` devolve para `ErroDeApi.status === 404` — a mesma tradução que
    // `BannerDeErro` já usa em toda outra tela deste projeto.
    expect(screen.getByRole('alert').textContent).toContain('Este registro não existe mais.')
    expect(screen.getByRole('button', { name: /visualizar/i })).toBeTruthy()
  })

  it('mostra o canvas rotulado quando o sólido carregou', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))

    // Estado PRONTO: com os dublês de `three` e do `STLLoader`, o `<canvas>` com `aria-label`
    // entra na tela. Não prova render — prova que o componente chegou ao estado pronto e montou
    // o canvas acessível (getByLabelText também acha `aria-label` fora de campo de formulário).
    expect(await screen.findByLabelText(/visualização 3d do sólido/i)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /visualizar/i })).toBeNull()
  })
})
