// @vitest-environment jsdom
import { StrictMode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { VisualizadorDeSolido, FATOR_DE_ZOOM_MINIMO, FATOR_DE_ZOOM_MAXIMO } from './VisualizadorDeSolido'
import { ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO, enquadramentoDoSolido } from './enquadramentoDoSolido'
import { respostaBinaria } from '../testes/api'
import { inicializar, _resetParaTeste } from '../api/client'

// Contador de import: a fábrica de `vi.mock` só roda quando o módulo é importado pela primeira
// vez. Distingue import ESTÁTICO (a fábrica já rodou ao carregar este arquivo) de import DINÂMICO
// no clique (a fábrica só roda depois). Três contadores porque os três módulos são importados
// separadamente no componente (`Promise.all` com um `import()` para cada) e cada um pode
// regredir para estático de forma independente: um `STLLoader` estático, sozinho, já faz o
// `three` inteiro (que ele importa estaticamente por dentro) voltar ao bundle principal, mesmo
// com `import('three')` continuando dinâmico — por isso a asserção sobre `stlLoader` é
// indispensável e não redundante com a de `three`. O mesmo vale para `orbitControls`: nada no
// `STLLoader` nem no `three` importa `OrbitControls` por dentro, mas ele é um `import()` a mais no
// mesmo clique, e regride para estático de forma independente dos outros dois.
const importacoes = vi.hoisted(() => ({ three: 0, stlLoader: 0, orbitControls: 0 }))

// Guarda a última instância de `WebGLRendererFalso` criada — o componente instancia um renderer
// novo a cada `montarCena`, cada um com seu próprio `dispose = vi.fn()`; sem isto não haveria como
// o teste `cancela o quadro de animação e libera o renderer ao desmontar sob StrictMode` chegar ao
// dublê certo para checar se `dispose` foi chamado.
const rendererFalsos = vi.hoisted(() => ({ ultimo: null as null | { dispose: () => void } }))

// Guarda a última câmera falsa criada, com os argumentos recebidos no construtor (abertura, aspect,
// near, far) — é o que prova que `montarCena` passou a usar `enquadramentoDoSolido` em vez dos
// literais fixos que a câmera tinha antes.
const camerasFalsas = vi.hoisted(() => ({
  ultima: null as null | { args: unknown[]; position: { set: (...args: number[]) => void } },
}))

// Guarda a última instância de `OrbitControlsFalso`, com um `disparar` que simula o dublê
// invocando os ouvintes que o componente registrou via `addEventListener` — é o que permite ao
// teste da rotação automática simular a interação do usuário sem precisar de um mouse de verdade.
const controlsFalsos = vi.hoisted(() => ({
  ultimo: null as null | {
    autoRotate: boolean
    minDistance: number
    maxDistance: number
    update: () => void
    dispose: () => void
    disparar: (tipo: string) => void
  },
}))

/** Raio da esfera envolvente que o dublê do `STLLoader` devolve — usado pelos testes que
    conferem o enquadramento para reproduzir a MESMA conta que o componente faz, com
    `enquadramentoDoSolido` importado de verdade (não um valor redigitado à mão). */
const RAIO_DE_TESTE = 30

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

  // Herda de `Object3DFalso` (mesmo `position.set` espiável) e acrescenta só o que a câmera
  // precisa a mais: guardar os argumentos do construtor, para o teste do enquadramento conferir
  // `near`/`far` sem precisar de um espião separado por chamada.
  class PerspectiveCameraFalsa extends Object3DFalso {
    args: unknown[]
    constructor(...args: unknown[]) {
      super()
      this.args = args
      camerasFalsas.ultima = this
    }
  }

  class WebGLRendererFalso {
    domElement = document.createElement('canvas')
    setSize = vi.fn()
    render = vi.fn()
    dispose = vi.fn()
    constructor() {
      rendererFalsos.ultimo = this
    }
  }

  return {
    Scene: Object3DFalso,
    PerspectiveCamera: PerspectiveCameraFalsa,
    AmbientLight: Object3DFalso,
    DirectionalLight: Object3DFalso,
    Mesh: Object3DFalso,
    MeshStandardMaterial: class {},
    WebGLRenderer: WebGLRendererFalso,
  }
})

// Dublê do STLLoader: `parse` devolve uma geometria falsa com os métodos que `montarCena` chama
// antes de montar a cena. `computeBoundingSphere` imita o comportamento real (grava
// `boundingSphere` na própria geometria) para o componente conseguir ler `boundingSphere.radius`
// exatamente como leria de uma `BufferGeometry` de verdade.
vi.mock('three/examples/jsm/loaders/STLLoader.js', () => {
  importacoes.stlLoader++

  return {
    STLLoader: class {
      parse() {
        return {
          computeBoundingBox: () => {},
          center: () => {},
          computeBoundingSphere(this: { boundingSphere?: { radius: number } }) {
            this.boundingSphere = { radius: RAIO_DE_TESTE }
          },
        }
      }
    },
  }
})

// Dublê do OrbitControls: expõe só o que `montarCena` usa (`autoRotate`, `minDistance`,
// `maxDistance`, `update`, `dispose`) e um `addEventListener`/`removeEventListener` mínimo o
// bastante para o componente se inscrever e cancelar a inscrição do evento `start` — `disparar` é
// o gancho de teste para simular o evento sem precisar de um `PointerEvent` de verdade.
vi.mock('three/examples/jsm/controls/OrbitControls.js', () => {
  importacoes.orbitControls++

  class OrbitControlsFalso {
    autoRotate = false
    minDistance = 0
    maxDistance = 0
    update = vi.fn()
    dispose = vi.fn()
    ouvintesPorTipo: Record<string, Array<() => void>> = {}

    constructor() {
      controlsFalsos.ultimo = this
    }

    addEventListener(tipo: string, ouvinte: () => void) {
      ;(this.ouvintesPorTipo[tipo] ??= []).push(ouvinte)
    }

    removeEventListener(tipo: string, ouvinte: () => void) {
      this.ouvintesPorTipo[tipo] = (this.ouvintesPorTipo[tipo] ?? []).filter((o) => o !== ouvinte)
    }

    disparar(tipo: string) {
      ;(this.ouvintesPorTipo[tipo] ?? []).forEach((ouvinte) => ouvinte())
    }
  }

  return { OrbitControls: OrbitControlsFalso }
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
    // Mata o import ESTÁTICO do `OrbitControls`, pela mesma razão: nenhum dos outros dois
    // contadores pega uma regressão isolada dele.
    expect(importacoes.orbitControls).toBe(0)
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

  // `web/src/main.tsx` monta o app em `<StrictMode>`: em dev o React monta o componente, roda a
  // limpeza do `useEffect` e remonta, para forçar que efeitos aguentem esse ciclo. O teste
  // `mostra o canvas rotulado quando o sólido carregou`, sem `StrictMode`, não passa por isso — só
  // este, envolto em `StrictMode`, prova que `desmontadoRef` volta a `false` na montagem em vez de
  // ficar `true` para sempre depois da limpeza da primeira passada.
  it('mostra o canvas rotulado quando o sólido carregou, mesmo sob StrictMode', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(
      <StrictMode>
        <VisualizadorDeSolido componenteId={7} />
      </StrictMode>,
    )
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))

    expect(await screen.findByLabelText(/visualização 3d do sólido/i)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /visualizar/i })).toBeNull()
  })

  it('mostra erro quando a busca falha sob StrictMode, e mantém o botão para tentar de novo', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(0), undefined, 404))))

    render(
      <StrictMode>
        <VisualizadorDeSolido componenteId={7} />
      </StrictMode>,
    )
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())
    expect(screen.getByRole('alert').textContent).toContain('Este registro não existe mais.')
    expect(screen.getByRole('button', { name: /visualizar/i })).toBeTruthy()
  })

  it('cancela o quadro de animação e libera o renderer e os controles ao desmontar sob StrictMode', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))
    const cancelarQuadro = vi.spyOn(globalThis, 'cancelAnimationFrame')

    const { unmount } = render(
      <StrictMode>
        <VisualizadorDeSolido componenteId={7} />
      </StrictMode>,
    )
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    const dispose = rendererFalsos.ultimo?.dispose
    const disposeDosControles = controlsFalsos.ultimo?.dispose
    expect(dispose).not.toHaveBeenCalled()
    expect(disposeDosControles).not.toHaveBeenCalled()
    expect(cancelarQuadro).not.toHaveBeenCalled()

    unmount()

    expect(cancelarQuadro).toHaveBeenCalled()
    // `toHaveBeenCalledTimes(1)`, não só `toHaveBeenCalled()`: sob StrictMode o componente passa
    // por um ciclo extra de monta/desmonta/remonta antes deste desmonte real — o mesmo ciclo que,
    // sem `desmontadoRef.current = false` na montagem, prendia o viewer em "Carregando…" para
    // sempre (é o que o teste `mostra o canvas rotulado quando o sólido carregou, mesmo sob
    // StrictMode` prova). Uma chamada a mais aqui seria a mesma classe de regressão, desta vez no
    // dispose em vez do estado preso.
    expect(dispose).toHaveBeenCalledTimes(1)
    expect(disposeDosControles).toHaveBeenCalledTimes(1)

    cancelarQuadro.mockRestore()
  })

  it('enquadra a câmera pelo tamanho do sólido em vez da distância fixa antiga', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    // Mesma conta que `montarCena` faz, com a função de verdade — nunca um número redigitado à
    // mão, que divergiria em silêncio se a fórmula ou as constantes mudassem de novo.
    const esperado = enquadramentoDoSolido(RAIO_DE_TESTE, ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO)

    // Mata a mutação de voltar ao `50, 1, 0.1, 10000` fixo: com `RAIO_DE_TESTE = 30`, a distância
    // e o near/far esperados são bem diferentes dos literais antigos.
    expect(camerasFalsas.ultima?.args).toEqual([ABERTURA_VERTICAL_EM_GRAUS, 1, esperado.near, esperado.far])
    // Mata a mutação de voltar a `camera.position.set(0, 0, 200)`.
    expect(camerasFalsas.ultima?.position.set).toHaveBeenCalledWith(0, 0, esperado.distancia)
  })

  it('limita o zoom com base no tamanho do sólido, não em números fixos', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    const esperado = enquadramentoDoSolido(RAIO_DE_TESTE, ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO)

    // Mata a mutação de trocar `minDistance`/`maxDistance` por literais: um `RAIO_DE_TESTE`
    // diferente (30, aqui) teria de produzir limites diferentes dos de qualquer outro sólido, e um
    // literal fixo não acompanharia essa mudança.
    expect(controlsFalsos.ultimo?.minDistance).toBeCloseTo(esperado.distancia * FATOR_DE_ZOOM_MINIMO)
    expect(controlsFalsos.ultimo?.maxDistance).toBeCloseTo(esperado.distancia * FATOR_DE_ZOOM_MAXIMO)
  })

  it('atualiza os controles a cada quadro do loop de animação', async () => {
    // Achado por mutação: tirar `controls.update()` do loop não derrubava nenhum teste antes desta
    // asserção — o `autoRotate` ficaria declarado, mas parado, sem ninguém perceber. `update` é
    // chamado de forma síncrona na primeira passada de `animar()`, antes do primeiro
    // `requestAnimationFrame` agendar a próxima — não é preciso avançar temporizador nenhum.
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    expect(controlsFalsos.ultimo?.update).toHaveBeenCalled()
  })

  it('gira sozinho até a primeira interação do usuário nos controles, e não retoma depois', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    const controles = controlsFalsos.ultimo
    // (a) Antes de qualquer interação, a rotação automática está ligada.
    expect(controles?.autoRotate).toBe(true)

    // (b) Mata a mutação de não desligar no `start`: sem o `autoRotate = false` no ouvinte, esta
    // asserção continuaria vendo `true`.
    controles?.disparar('start')
    expect(controles?.autoRotate).toBe(false)

    // (c) Mata a mutação de religar no `end` — a decisão do usuário foi que a rotação para DE VEZ
    // na primeira manipulação, e não retoma quando o usuário solta o mouse.
    controles?.disparar('end')
    expect(controles?.autoRotate).toBe(false)
  })
})
