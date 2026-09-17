// @vitest-environment jsdom
import { StrictMode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import {
  VisualizadorDeSolido,
  FATOR_DE_ZOOM_MAXIMO,
  ALTURA_DO_CANVAS_EM_PIXELS,
  METALNESS_DO_ACABAMENTO,
  ROUGHNESS_DO_ACABAMENTO,
} from './VisualizadorDeSolido'
import { ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO, enquadramentoDoSolido } from './enquadramentoDoSolido'
import { respostaBinaria } from '../testes/api'
import { inicializar, _resetParaTeste } from '../api/client'

// Contador de import: a fábrica de `vi.mock` só roda quando o módulo é importado pela primeira
// vez. Distingue import ESTÁTICO (a fábrica já rodou ao carregar este arquivo) de import DINÂMICO
// no clique (a fábrica só roda depois). Quatro contadores porque os quatro módulos são importados
// separadamente no componente (`Promise.all` com um `import()` para cada) e cada um pode
// regredir para estático de forma independente: um `STLLoader` estático, sozinho, já faz o
// `three` inteiro (que ele importa estaticamente por dentro) voltar ao bundle principal, mesmo
// com `import('three')` continuando dinâmico — por isso a asserção sobre `stlLoader` é
// indispensável e não redundante com a de `three`. O mesmo vale para `orbitControls` e
// `roomEnvironment`: nada nos outros três importa esses dois por dentro, mas cada um é um
// `import()` a mais no mesmo clique, e regride para estático de forma independente dos demais.
const importacoes = vi.hoisted(() => ({ three: 0, stlLoader: 0, orbitControls: 0, roomEnvironment: 0 }))

/** Sequência de eventos, na ordem em que acontecem — usado para provar ORDEM (não só presença) do
    recálculo de normais: `parse` (quando o STLLoader falso devolve a geometria), depois
    `computeVertexNormals` (quando o componente recalcula), depois `malhaConstruida` (quando o
    `Mesh` falso é construído com aquela geometria). Resetado a cada teste no `beforeEach`. */
const ordemDeChamadas = vi.hoisted(() => ({ eventos: [] as string[] }))

/** Última geometria falsa devolvida pelo `STLLoader.parse()` — permite ao teste inspecionar
    diretamente `computeVertexNormals` (um `vi.fn()`) sem precisar vasculhar o dublê do `Mesh`. */
const geometriasFalsas = vi.hoisted(() => ({
  ultima: null as null | { computeVertexNormals: () => void },
}))

/** Última malha falsa construída, com a geometria que RECEBEU no construtor — achado ao tentar
    burlar o teste do recálculo: sem isto, um `new THREE.Mesh()` com uma geometria qualquer no
    lugar da recalculada passava despercebido pelos outros 21 testes (nenhum inspecionava o
    argumento do construtor). */
const malhasFalsas = vi.hoisted(() => ({ ultima: null as null | { geometria: unknown } }))

/** Última cena falsa construída, com o que foi atribuído a `.environment` — permite ao teste do
    ambiente de reflexo provar que `scene.environment` recebeu a textura do PMREM, não só que ela
    foi gerada. */
const cenasFalsas = vi.hoisted(() => ({ ultima: null as null | { environment: unknown } }))

/** Último material padrão falso construído, com os parâmetros que recebeu — permite ao teste do
    acabamento inspecionar `metalness`/`roughness` sem vasculhar a malha. */
const materiaisFalsos = vi.hoisted(() => ({
  ultimo: null as null | { color?: number; metalness?: number; roughness?: number },
}))

/** Último `PMREMGenerator` falso construído — usado tanto pelo teste do ambiente de reflexo
    (`fromScene` devolve a textura capturada em `texturasDeAmbienteFalsas`) quanto pelo teste de
    limpeza (`dispose`). */
const pmremGeneratorsFalsos = vi.hoisted(() => ({ ultimo: null as null | { dispose: () => void } }))

/** Última textura de ambiente falsa devolvida por `PMREMGenerator.fromScene(...).texture` —
    guardada à parte do gerador porque a limpeza do componente libera os dois separadamente. */
const texturasDeAmbienteFalsas = vi.hoisted(() => ({ ultima: null as null | { dispose: () => void } }))

// Guarda a última instância de `WebGLRendererFalso` criada — o componente instancia um renderer
// novo a cada `montarCena`, cada um com seu próprio `dispose = vi.fn()`; sem isto não haveria como
// o teste `cancela o quadro de animação e libera o renderer ao desmontar sob StrictMode` chegar ao
// dublê certo para checar se `dispose` foi chamado.
const rendererFalsos = vi.hoisted(() => ({ ultimo: null as null | { dispose: () => void; setSize: (...args: number[]) => void } }))

// Guarda a última câmera falsa criada. `aspect`/`near`/`far` são propriedades graváveis (como na
// `PerspectiveCamera` real) em vez de argumentos de construtor: `aplicarEnquadramento`, no
// componente, constrói a câmera com literais de placeholder e escreve os valores reais depois via
// atribuição de propriedade — é assim que a MESMA função também consegue reaplicar o enquadramento
// quando o `ResizeObserver` dispara, sem reconstruir a câmera.
const camerasFalsas = vi.hoisted(() => ({
  ultima: null as null | {
    aspect: number
    near: number
    far: number
    updateProjectionMatrix: () => void
    position: { set: (...args: number[]) => void }
  },
}))

// Guarda a última instância de `OrbitControlsFalso`, com um `disparar` que simula o dublê
// invocando os ouvintes que o componente registrou via `addEventListener` — é o que permite ao
// teste da rotação automática simular a interação do usuário sem precisar de um mouse de verdade.
const controlsFalsos = vi.hoisted(() => ({
  ultimo: null as null | {
    autoRotate: boolean
    enablePan: boolean
    minDistance: number
    maxDistance: number
    update: () => void
    dispose: () => void
    reset: () => void
    disparar: (tipo: string) => void
    ouvintesPorTipo: Record<string, Array<() => void>>
  },
}))

/** Meias-extensões do sólido falso que o dublê do `STLLoader` devolve, usadas pelos testes de
    enquadramento para reproduzir a MESMA conta que o componente faz (nunca um número redigitado à
    mão). X e Z formam o plano em que a rotação automática gira a câmera (em torno do eixo Y); Y é
    a altura — os valores são bem diferentes entre si de propósito, para os testes que checam a
    escolha entre largura e altura terem uma peça assimétrica de verdade. */
const MEIA_EXTENSAO_X_DE_TESTE = 40
const MEIA_EXTENSAO_Y_DE_TESTE = 6
const MEIA_EXTENSAO_Z_DE_TESTE = 8
const RAIO_NO_PLANO_DE_GIRO_DE_TESTE = Math.sqrt(MEIA_EXTENSAO_X_DE_TESTE ** 2 + MEIA_EXTENSAO_Z_DE_TESTE ** 2)

type CaixaEnvolventeDeTeste = { min: { x: number; y: number; z: number }; max: { x: number; y: number; z: number } }

/** Caixa envolvente que o dublê do `STLLoader` devolve — mutável por teste (lida por referência a
    cada `computeBoundingBox`, então trocar o CONTEÚDO antes do clique em "Visualizar" já muda o
    que o componente recebe). Começa nas meias-extensões normais; resetada no `beforeEach`. Existe
    para o teste "não propaga Infinity..." simular uma geometria SEM vértice nenhum: o `Box3` real
    do Three.js fica com `min = (+Infinity, +Infinity, +Infinity)` e `max = (-Infinity, -Infinity,
    -Infinity)` (o estado "vazio" de `Box3.makeEmpty()`) quando não há ponto nenhum para expandi-lo
    — é esse par de valores que prova a guarda `Number.isFinite` em `meiaExtensao`. */
let caixaEnvolventeDeTeste: CaixaEnvolventeDeTeste = {
  min: { x: -MEIA_EXTENSAO_X_DE_TESTE, y: -MEIA_EXTENSAO_Y_DE_TESTE, z: -MEIA_EXTENSAO_Z_DE_TESTE },
  max: { x: MEIA_EXTENSAO_X_DE_TESTE, y: MEIA_EXTENSAO_Y_DE_TESTE, z: MEIA_EXTENSAO_Z_DE_TESTE },
}

// Guarda a última instância do dublê de `ResizeObserver` — `disparar()` simula o navegador
// invocando o callback que o componente passou ao `observe()`, sem precisar de um redimensionamento
// de verdade (que o jsdom não tem).
let ultimoResizeObserverFalso: ResizeObserverFalso | null = null

class ResizeObserverFalso {
  disconnect = vi.fn()
  observe = vi.fn()
  callback: () => void
  constructor(callback: () => void) {
    this.callback = callback
    ultimoResizeObserverFalso = this
  }
  disparar() {
    this.callback()
  }
}

// `apiFetch` exige `inicializar()` — molde de `UploadDeSolido.test.tsx`.
beforeEach(() => {
  _resetParaTeste()
  inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  ultimoResizeObserverFalso = null
  ordemDeChamadas.eventos = []
  geometriasFalsas.ultima = null
  malhasFalsas.ultima = null
  cenasFalsas.ultima = null
  materiaisFalsos.ultimo = null
  pmremGeneratorsFalsos.ultimo = null
  texturasDeAmbienteFalsas.ultima = null
  // `ResizeObserver` não existe no jsdom (só em navegador de verdade) — sem este stub, TODO teste
  // que chega a `montarCena` (ou seja, quase todos) lançaria `ReferenceError` ao clicar em
  // "Visualizar", não só os testes que testam redimensionamento.
  vi.stubGlobal('ResizeObserver', ResizeObserverFalso)
  // Reseta a caixa envolvente para as meias-extensões normais a cada teste — só o teste "não
  // propaga Infinity para a câmera quando o sólido não tem vértice nenhum" troca este valor, e sem
  // este reset explícito o `beforeEach` não garantiria isolamento entre execuções de teste.
  caixaEnvolventeDeTeste = {
    min: { x: -MEIA_EXTENSAO_X_DE_TESTE, y: -MEIA_EXTENSAO_Y_DE_TESTE, z: -MEIA_EXTENSAO_Z_DE_TESTE },
    max: { x: MEIA_EXTENSAO_X_DE_TESTE, y: MEIA_EXTENSAO_Y_DE_TESTE, z: MEIA_EXTENSAO_Z_DE_TESTE },
  }
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

  // Não herda de `Object3DFalso`: a câmera real não tem `rotation.y`/`add` relevantes aqui, e o que
  // o componente de fato usa nela (`position.set`, `aspect`, `near`, `far`,
  // `updateProjectionMatrix`) é tudo que este dublê precisa expor.
  class PerspectiveCameraFalsa {
    position = { set: vi.fn() }
    aspect: number
    near: number
    far: number
    updateProjectionMatrix = vi.fn()
    constructor(_fov: number, aspect: number, near: number, far: number) {
      this.aspect = aspect
      this.near = near
      this.far = far
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

  // Dublê do `Mesh`: só existe separado de `Object3DFalso` para registrar o instante em que a malha
  // é construída em `ordemDeChamadas` — é o marcador que prova que o recálculo de normais aconteceu
  // ANTES de a malha existir, não depois.
  class MeshFalso extends Object3DFalso {
    constructor(geometria: unknown) {
      super()
      ordemDeChamadas.eventos.push('malhaConstruida')
      malhasFalsas.ultima = { geometria }
    }
  }

  // Dublê da `Scene`: só existe separado de `Object3DFalso` para o teste do ambiente de reflexo
  // poder inspecionar o que foi atribuído a `.environment` (o real não tem esse campo nenhuma
  // outra classe deste dublê precisa).
  class SceneFalso extends Object3DFalso {
    environment: unknown = null
    constructor() {
      super()
      cenasFalsas.ultima = this
    }
  }

  // Dublê do material padrão: captura os parâmetros recebidos (`color`/`metalness`/`roughness`)
  // para o teste do acabamento metálico inspecionar sem vasculhar a malha.
  class MeshStandardMaterialFalso {
    color?: number
    metalness?: number
    roughness?: number
    constructor(parametros: { color?: number; metalness?: number; roughness?: number } = {}) {
      this.color = parametros.color
      this.metalness = parametros.metalness
      this.roughness = parametros.roughness
      materiaisFalsos.ultimo = this
    }
  }

  // Dublê do `PMREMGenerator`: `fromScene` devolve um `WebGLRenderTarget`-like mínimo (só o
  // `.texture` que o componente lê) e guarda a textura à parte, porque a limpeza do componente
  // libera o gerador e a textura separadamente.
  class PMREMGeneratorFalso {
    dispose = vi.fn()
    constructor() {
      pmremGeneratorsFalsos.ultimo = this
    }
    fromScene() {
      const textura = { dispose: vi.fn() }
      texturasDeAmbienteFalsas.ultima = textura
      return { texture: textura }
    }
  }

  return {
    Scene: SceneFalso,
    PerspectiveCamera: PerspectiveCameraFalsa,
    AmbientLight: Object3DFalso,
    DirectionalLight: Object3DFalso,
    Mesh: MeshFalso,
    MeshStandardMaterial: MeshStandardMaterialFalso,
    WebGLRenderer: WebGLRendererFalso,
    PMREMGenerator: PMREMGeneratorFalso,
  }
})

// Dublê do `RoomEnvironment`: só precisa existir como classe instanciável — o que importa para o
// teste é o `PMREMGeneratorFalso.fromScene` acima, não o conteúdo da cena de ambiente em si (o
// jsdom não renderiza WebGL de qualquer forma).
vi.mock('three/examples/jsm/environments/RoomEnvironment.js', () => {
  importacoes.roomEnvironment++

  return {
    RoomEnvironment: class {},
  }
})

// Dublê do STLLoader: `parse` devolve uma geometria falsa cujo `computeBoundingBox` grava
// `caixaEnvolventeDeTeste` (lida por referência, nunca copiada na definição do mock, para o teste
// da guarda `Number.isFinite` poder trocar o conteúdo antes do clique) — imita a `BufferGeometry`
// real o bastante para o componente derivar `raioNoPlanoDeGiro`/`meiaAlturaEmY` dela.
vi.mock('three/examples/jsm/loaders/STLLoader.js', () => {
  importacoes.stlLoader++

  return {
    STLLoader: class {
      parse() {
        ordemDeChamadas.eventos.push('parse')
        const geometria = {
          computeBoundingBox(this: { boundingBox?: CaixaEnvolventeDeTeste }) {
            this.boundingBox = caixaEnvolventeDeTeste
          },
          center: () => {},
          // `vi.fn()` real (não só uma função comum) para o teste poder inspecionar quantas vezes
          // foi chamado, além de registrar o instante em `ordemDeChamadas`.
          computeVertexNormals: vi.fn(() => {
            ordemDeChamadas.eventos.push('computeVertexNormals')
          }),
        }
        geometriasFalsas.ultima = geometria
        return geometria
      }
    },
  }
})

// Dublê do OrbitControls: expõe só o que `montarCena` usa (`autoRotate`, `enablePan`,
// `minDistance`, `maxDistance`, `update`, `dispose`, `reset`) e um
// `addEventListener`/`removeEventListener` mínimo o bastante para o componente se inscrever e
// cancelar a inscrição do evento `start` — `disparar` é o gancho de teste para simular o evento
// sem precisar de um `PointerEvent` de verdade.
vi.mock('three/examples/jsm/controls/OrbitControls.js', () => {
  importacoes.orbitControls++

  class OrbitControlsFalso {
    autoRotate = false
    // `true` por padrão, igual ao `OrbitControls` real (e ao que ele fica depois de o componente
    // parar de forçar `false`) — é o que faz o teste do pan ligado morrer se algum código voltar a
    // desligá-lo.
    enablePan = true
    minDistance = 0
    maxDistance = 0
    update = vi.fn()
    dispose = vi.fn()
    // `reset()` real não muda `autoRotate` nem dispara `start` (só `change`) — o dublê reflete
    // isso não fazendo nada além de registrar a chamada, para o teste do botão "Recentralizar"
    // provar que É O COMPONENTE, e não o `OrbitControls`, quem para a rotação automática.
    reset = vi.fn()
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
    // Mata o import ESTÁTICO do `RoomEnvironment`, pela mesma razão: ele entra no mesmo
    // `Promise.all` do clique, e nada nos outros três módulos o importa por dentro.
    expect(importacoes.roomEnvironment).toBe(0)
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

  it('libera o renderer, os controles, o observador de redimensionamento e os recursos do ambiente ao desmontar sob StrictMode', async () => {
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
    const desconectarObservador = ultimoResizeObserverFalso?.disconnect
    const disposeDoGerador = pmremGeneratorsFalsos.ultimo?.dispose
    const disposeDaTextura = texturasDeAmbienteFalsas.ultima?.dispose
    expect(dispose).not.toHaveBeenCalled()
    expect(disposeDosControles).not.toHaveBeenCalled()
    expect(desconectarObservador).not.toHaveBeenCalled()
    expect(cancelarQuadro).not.toHaveBeenCalled()
    expect(disposeDoGerador).not.toHaveBeenCalled()
    expect(disposeDaTextura).not.toHaveBeenCalled()

    unmount()

    expect(cancelarQuadro).toHaveBeenCalled()
    // `toHaveBeenCalledTimes(1)`, não só `toHaveBeenCalled()`: sob StrictMode o componente passa
    // por um ciclo extra de monta/desmonta/remonta antes deste desmonte real — o mesmo ciclo que,
    // sem `desmontadoRef.current = false` na montagem, prendia o viewer em "Carregando…" para
    // sempre (é o que o teste `mostra o canvas rotulado quando o sólido carregou, mesmo sob
    // StrictMode` prova). Uma chamada a mais aqui seria a mesma classe de regressão, desta vez no
    // dispose/disconnect em vez do estado preso.
    expect(dispose).toHaveBeenCalledTimes(1)
    expect(disposeDosControles).toHaveBeenCalledTimes(1)
    expect(desconectarObservador).toHaveBeenCalledTimes(1)
    // Mata a mutação de deixar o `PMREMGenerator` ou a textura de ambiente fora da limpeza: os
    // dois seguram recurso de GPU (render targets internos do PMREM, a textura prefiltrada) que
    // vazariam a cada vez que o viewer fosse aberto e fechado.
    expect(disposeDoGerador).toHaveBeenCalledTimes(1)
    expect(disposeDaTextura).toHaveBeenCalledTimes(1)

    cancelarQuadro.mockRestore()
  })

  it('gera o ambiente de reflexo do RoomEnvironment via PMREMGenerator e atribui a textura a scene.environment', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    // Mata a mutação de não atribuir `scene.environment`: sem a atribuição, o campo fica no
    // `null` inicial do dublê da `Scene`, nunca na textura que `PMREMGenerator.fromScene(...)`
    // devolveu.
    expect(cenasFalsas.ultima?.environment).not.toBeNull()
    expect(cenasFalsas.ultima?.environment).toBe(texturasDeAmbienteFalsas.ultima)
  })

  it('usa acabamento metálico (metalness e roughness) igual para toda peça', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    // Mata a mutação de `metalness` voltar a 0: a peça deixaria de ler como metal (metal genuíno
    // não tem parcela difusa — depende do reflexo do ambiente, não da cor).
    expect(materiaisFalsos.ultimo?.metalness).toBe(METALNESS_DO_ACABAMENTO)
    expect(materiaisFalsos.ultimo?.metalness).toBeGreaterThan(0)
    expect(materiaisFalsos.ultimo?.roughness).toBe(ROUGHNESS_DO_ACABAMENTO)
  })

  it('recalcula as normais da geometria depois do parse do STLLoader e antes de montar a malha', async () => {
    // Os STL de teste desta suíte têm normal zerada, e o `STLLoader` copia a normal do arquivo sem
    // recalcular nada — sem este recálculo, a parcela difusa da luz direcional fica zero em toda
    // face e só a luz ambiente (uniforme) sobra, apagando as arestas.
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    // Mata a mutação de tirar o `computeVertexNormals()`: sem ele, o dublê nunca é chamado.
    expect(geometriasFalsas.ultima?.computeVertexNormals).toHaveBeenCalledTimes(1)

    const indiceDoParse = ordemDeChamadas.eventos.indexOf('parse')
    const indiceDoRecalculo = ordemDeChamadas.eventos.indexOf('computeVertexNormals')
    const indiceDaMalha = ordemDeChamadas.eventos.indexOf('malhaConstruida')

    // Mata a mutação de recalcular ANTES do parse (índice teria de ser menor que o de 'parse') e a
    // de recalcular DEPOIS de a malha já estar montada (índice teria de ser maior que o de
    // 'malhaConstruida') — a ordem certa é parse, recálculo, malha, nesta sequência.
    expect(indiceDoRecalculo).toBeGreaterThan(indiceDoParse)
    expect(indiceDoRecalculo).toBeLessThan(indiceDaMalha)

    // Achado ao tentar burlar este próprio teste: sem esta linha, trocar a geometria passada ao
    // `Mesh` por qualquer outro objeto (em vez da que teve as normais recalculadas) passava pelos
    // outros 21 testes do arquivo sem quebrar nenhum. A malha tem de guardar a MESMA instância que
    // `computeVertexNormals()` mutou, não uma cópia nem uma geometria nova.
    expect(malhasFalsas.ultima?.geometria).toBe(geometriasFalsas.ultima)
  })

  it('enquadra a câmera pelo tamanho e formato do sólido em vez da distância fixa antiga', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    // jsdom não mede layout: `getBoundingClientRect()` do container devolve 0, e o componente cai
    // no piso (a própria altura fixa), o que dá proporção 1:1 — a MESMA conta que `esperado`
    // reproduz com a função `enquadramentoDoSolido` de produção, nunca um número redigitado à mão.
    const esperado = enquadramentoDoSolido(
      RAIO_NO_PLANO_DE_GIRO_DE_TESTE,
      MEIA_EXTENSAO_Y_DE_TESTE,
      ABERTURA_VERTICAL_EM_GRAUS,
      1,
      MARGEM_DE_ENQUADRAMENTO,
    )

    // Mata a mutação de voltar ao `50, 1, 0.1, 1000` fixo (aspect sempre 1, near/far sem depender
    // do tamanho do sólido).
    expect(camerasFalsas.ultima?.aspect).toBe(1)
    expect(camerasFalsas.ultima?.near).toBeCloseTo(esperado.near, 10)
    expect(camerasFalsas.ultima?.far).toBeCloseTo(esperado.far, 10)
    // Mata a mutação de voltar a `camera.position.set(0, 0, 200)`.
    expect(camerasFalsas.ultima?.position.set).toHaveBeenCalledWith(0, 0, esperado.distancia)
  })

  it('não propaga Infinity para a câmera quando o sólido não tem vértice nenhum', async () => {
    // Um STL vazio (ou sem geometria válida) deixa o `Box3` real do Three.js no estado "vazio" de
    // `Box3.makeEmpty()`: `min = (+Infinity, +Infinity, +Infinity)`, `max = (-Infinity, -Infinity,
    // -Infinity)` — nunca expandido por nenhum ponto. Sem a guarda `Number.isFinite` em
    // `meiaExtensao`, `max - min` desse par (`-Infinity - Infinity = -Infinity`) propagaria
    // `Infinity`/`-Infinity` para `raioNoPlanoDeGiro` e `meiaAlturaEmY`, e dali para
    // `enquadramentoDoSolido`, que devolveria `distancia`/`near`/`far`/`distanciaMinima` todos
    // `Infinity` — bem diferente do valor com o piso de degenerescência (`enquadramentoDoSolido(0,
    // 0, ...)`, o mesmo cenário que a suíte de `enquadramentoDoSolido` já prova para a função pura)
    // que a variável `esperado` deste teste calcula.
    caixaEnvolventeDeTeste = {
      min: { x: Infinity, y: Infinity, z: Infinity },
      max: { x: -Infinity, y: -Infinity, z: -Infinity },
    }
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    // jsdom não mede layout — mesmo piso de largura (proporção 1:1) que "enquadra a câmera pelo
    // tamanho e formato do sólido em vez da distância fixa antiga" usa.
    const esperado = enquadramentoDoSolido(0, 0, ABERTURA_VERTICAL_EM_GRAUS, 1, MARGEM_DE_ENQUADRAMENTO)

    expect(camerasFalsas.ultima?.near).toBeCloseTo(esperado.near, 10)
    expect(camerasFalsas.ultima?.far).toBeCloseTo(esperado.far, 10)
    expect(camerasFalsas.ultima?.position.set).toHaveBeenCalledWith(0, 0, esperado.distancia)
    expect(controlsFalsos.ultimo?.minDistance).toBeCloseTo(esperado.distanciaMinima, 10)
    expect(Number.isFinite(camerasFalsas.ultima?.near)).toBe(true)
    expect(Number.isFinite(camerasFalsas.ultima?.far)).toBe(true)
  })

  it('limita o zoom com base no tamanho do sólido, não em números fixos', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    const esperado = enquadramentoDoSolido(
      RAIO_NO_PLANO_DE_GIRO_DE_TESTE,
      MEIA_EXTENSAO_Y_DE_TESTE,
      ABERTURA_VERTICAL_EM_GRAUS,
      1,
      MARGEM_DE_ENQUADRAMENTO,
    )

    // Mata a mutação de trocar `minDistance`/`maxDistance` por literais: uma peça de teste
    // diferente teria de produzir limites diferentes dos de qualquer outra, e um literal fixo não
    // acompanharia essa mudança.
    expect(controlsFalsos.ultimo?.minDistance).toBeCloseTo(esperado.distanciaMinima)
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

  it('remove o próprio ouvinte de start depois de disparado uma vez', async () => {
    // O teste `gira sozinho...` não pega a ausência de `removeEventListener`: reatribuir
    // `autoRotate = false` numa segunda invocação do mesmo ouvinte é inócuo (já está `false`), então
    // nenhuma asserção sobre `autoRotate` morre se a chamada a `removeEventListener` for removida.
    // Este teste inspeciona o dublê diretamente: `ouvintesPorTipo.start` só esvazia se
    // `removeEventListener` for chamado com o MESMO tipo e a MESMA referência de função que
    // `addEventListener` recebeu — o dublê filtra por identidade (`o !== ouvinte`), então uma
    // chamada ausente, ou com outra função, deixaria o ouvinte parado no array.
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    const controles = controlsFalsos.ultimo
    expect(controles?.ouvintesPorTipo.start).toHaveLength(1)

    controles?.disparar('start')

    expect(controles?.ouvintesPorTipo.start).toHaveLength(0)
  })

  it('mantém o pan ligado (padrão do OrbitControls), agora que o botão Recentralizar existe', async () => {
    // Com o botão "Recentralizar" desfazendo qualquer arrasto que afaste o sólido do quadro, o pan
    // volta ao padrão ligado do `OrbitControls` (`enablePan = true` no dublê, nunca sobrescrito pelo
    // componente). Mata a mutação de o componente forçar `enablePan = false`.
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    expect(controlsFalsos.ultimo?.enablePan).toBe(true)
    expect(screen.getByRole('button', { name: /recentralizar/i })).toBeTruthy()
  })

  it('mostra o botão Recentralizar só depois que o sólido carrega', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    expect(screen.queryByRole('button', { name: /recentralizar/i })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    expect(screen.queryByRole('button', { name: /recentralizar/i })).toBeNull()

    await screen.findByLabelText(/visualização 3d do sólido/i)
    expect(screen.getByRole('button', { name: /recentralizar/i })).toBeTruthy()
  })

  it('recentraliza a vista e para a rotação automática de vez ao clicar em Recentralizar', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    const controles = controlsFalsos.ultimo
    expect(controles?.autoRotate).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: /recentralizar/i }))

    // Mata a mutação de remover `controls.reset()` do botão.
    expect(controles?.reset).toHaveBeenCalledTimes(1)
    // Mata a mutação de a rotação automática NÃO parar ao clicar em Recentralizar: o `reset()` do
    // `OrbitControls` real dispara só o evento `change`, nunca `start` (conferido no código-fonte
    // do `OrbitControls` antes de escrever este teste) — sem o componente parar a rotação por
    // conta própria, a peça voltaria à vista inicial e continuaria girando sozinho.
    expect(controles?.autoRotate).toBe(false)

    // O clique também conta como a PRIMEIRA interação: disparar `start` depois não deveria religar
    // nada — mesma regra de sempre, a rotação para de vez.
    controles?.disparar('start')
    expect(controles?.autoRotate).toBe(false)
  })

  it('mede a largura do container e a usa para o tamanho do canvas e a proporção da câmera', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    const { getByTestId } = render(<VisualizadorDeSolido componenteId={7} />)
    vi.spyOn(getByTestId('container-do-visualizador'), 'getBoundingClientRect').mockReturnValue({
      width: 700,
    } as DOMRect)

    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    expect(rendererFalsos.ultimo?.setSize).toHaveBeenCalledWith(700, ALTURA_DO_CANVAS_EM_PIXELS)
    expect(camerasFalsas.ultima?.aspect).toBeCloseTo(700 / ALTURA_DO_CANVAS_EM_PIXELS, 10)
  })

  it('observa o container (não o canvas) para refazer o enquadramento quando ele muda de tamanho', async () => {
    // O elemento observado tem de ser o CONTAINER (`container-do-visualizador`, que ocupa `w-full` e
    // cujo tamanho o layout CSS do card determina), não o `<canvas>` que o Three.js cria dentro dele
    // — o tamanho do canvas é escrito programaticamente por `renderer.setSize()`, então observá-lo
    // mediria o próprio efeito colateral do componente, não o redimensionamento real do card. Sem
    // esta asserção, trocar o alvo do `observe` não derrubava nenhum teste: o dublê grava a chamada,
    // mas nenhum teste inspecionava o argumento recebido.
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    const { getByTestId } = render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    expect(ultimoResizeObserverFalso?.observe).toHaveBeenCalledWith(getByTestId('container-do-visualizador'))
  })

  it('nunca chama setSize com largura zero quando o container ainda não tem layout', async () => {
    // jsdom não mede layout: `getBoundingClientRect()` do container devolve 0 sem mock nenhum. Um
    // `setSize(0, ALTURA_DO_CANVAS_EM_PIXELS)` produziria um canvas invisível sem erro nenhum — só
    // afirmar a LARGURA passada (não só que `setSize` foi chamado) pega essa regressão.
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    render(<VisualizadorDeSolido componenteId={7} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)

    expect(rendererFalsos.ultimo?.setSize).toHaveBeenCalledWith(ALTURA_DO_CANVAS_EM_PIXELS, ALTURA_DO_CANVAS_EM_PIXELS)
  })

  it('refaz o tamanho, a proporção e o enquadramento quando o observador de redimensionamento dispara', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaBinaria(new Uint8Array(684)))))

    const { getByTestId } = render(<VisualizadorDeSolido componenteId={7} />)
    const container = getByTestId('container-do-visualizador')
    const medidaDeLargura = vi.spyOn(container, 'getBoundingClientRect')
    medidaDeLargura.mockReturnValue({ width: 700 } as DOMRect)

    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await screen.findByLabelText(/visualização 3d do sólido/i)
    expect(rendererFalsos.ultimo?.setSize).toHaveBeenCalledWith(700, ALTURA_DO_CANVAS_EM_PIXELS)

    const chamadasDeUpdateAntes = (camerasFalsas.ultima?.updateProjectionMatrix as ReturnType<typeof vi.fn>).mock
      .calls.length

    medidaDeLargura.mockReturnValue({ width: 350 } as DOMRect)
    ultimoResizeObserverFalso?.disparar()

    const esperado = enquadramentoDoSolido(
      RAIO_NO_PLANO_DE_GIRO_DE_TESTE,
      MEIA_EXTENSAO_Y_DE_TESTE,
      ABERTURA_VERTICAL_EM_GRAUS,
      350 / ALTURA_DO_CANVAS_EM_PIXELS,
      MARGEM_DE_ENQUADRAMENTO,
    )

    // Mata a mutação de trocar `setSize`/aspect por um valor que não acompanha a nova largura.
    expect(rendererFalsos.ultimo?.setSize).toHaveBeenLastCalledWith(350, ALTURA_DO_CANVAS_EM_PIXELS)
    expect(camerasFalsas.ultima?.aspect).toBeCloseTo(350 / ALTURA_DO_CANVAS_EM_PIXELS, 10)
    // Mata a mutação de tirar `updateProjectionMatrix()` do callback de redimensionamento
    // especificamente: a câmera já chama esse método uma vez na montagem, então só
    // `toHaveBeenCalled()` não provaria nada sobre o callback — o teste precisa de uma chamada A
    // MAIS depois do disparo.
    expect(
      (camerasFalsas.ultima?.updateProjectionMatrix as ReturnType<typeof vi.fn>).mock.calls.length,
    ).toBeGreaterThan(chamadasDeUpdateAntes)
    expect(controlsFalsos.ultimo?.minDistance).toBeCloseTo(esperado.distanciaMinima, 6)
    expect(controlsFalsos.ultimo?.maxDistance).toBeCloseTo(esperado.distancia * FATOR_DE_ZOOM_MAXIMO, 6)
  })
})
