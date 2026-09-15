import { useEffect, useRef, useState } from 'react'
import type * as ThreeModulo from 'three'
import type { OrbitControls as OrbitControlsModulo } from 'three/examples/jsm/controls/OrbitControls.js'
import { apiFetch } from '../api/client'
import { caminhoDoSolido } from '../api/cadastros'
import { ErroDeApi, mensagemDeErro } from '../api/erros'
import { ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO, enquadramentoDoSolido } from './enquadramentoDoSolido'
import { BannerDeErro } from './BannerDeErro'
import { Botao } from './Botao'
import { EstadoCarregando } from './EstadoCarregando'

interface Props {
  componenteId: number
}

type Estado =
  | { tipo: 'inicial' }
  | { tipo: 'carregando' }
  | { tipo: 'erro'; erro: unknown }
  | { tipo: 'pronto' }

/** Nome acessível do canvas — um canvas sem `aria-label` é opaco para leitor de tela, e é por isso
    que o viewer não substitui a descrição textual do Componente. */
const ROTULO_DO_CANVAS = 'Visualização 3D do sólido'

/**
 * `controls.minDistance = distância inicial × este fator`. Derivado da distância que
 * `enquadramentoDoSolido` calcula (que por sua vez escala com o raio da esfera envolvente), nunca
 * um número fixo: um literal serviria a um tamanho de peça e atravessaria outro, o mesmo problema
 * que motivou a Parte 1. `0.5` mantém a câmera fora da esfera envolvente em qualquer tamanho de
 * sólido: como a distância inicial já é `raio / sin(abertura/2) × margem`, e `sin(abertura/2)` para
 * a abertura deste viewer vale ≈ 0.42, a distância inicial equivale a ≈ 2,7 raios — a metade disso
 * ainda deixa a câmera além do raio, nunca dentro da esfera que envolve o sólido.
 */
export const FATOR_DE_ZOOM_MINIMO = 0.5

/** `controls.maxDistance = distância inicial × este fator` — o usuário pode afastar até o triplo. */
export const FATOR_DE_ZOOM_MAXIMO = 3

/**
 * Viewer 3D do sólido STL de um Componente — Task 7 da Fase 2B (§2.3 e §7.2 da spec de desenho).
 *
 * Fica FORA da guarda `usePodeEscrever` da tela que o monta: o `GET /componentes/{id}/solido` é
 * de qualquer perfil autenticado, e quem não escreve enxerga o sólido por aqui — o
 * `UploadDeSolido` (Task 6) é só para quem escreve.
 *
 * `three`, o `STLLoader` e o `OrbitControls` entram por `import()` dinâmico DENTRO do clique de
 * "Visualizar", nunca no topo do módulo: o público que de fato abre o viewer é o desktop do
 * PCP/Administrador ao cadastrar (§2.3), e o operador no Android não deve pagar o bundle de uma
 * tela que nunca abre.
 *
 * A câmera se enquadra pelo tamanho real do sólido (`enquadramentoDoSolido`, calculado a partir do
 * raio da esfera envolvente), em vez de uma distância fixa que cortaria peça grande e deixaria
 * peça pequena minúscula no quadro — um STL não carrega unidade. O `OrbitControls` dá zoom (roda
 * do mouse, com limites derivados do mesmo enquadramento) e rotação por arraste; o sólido também
 * gira sozinho até a primeira interação do usuário, e para de vez a partir daí.
 *
 * O jsdom não implementa WebGL: nenhum teste deste componente prova que o sólido aparece girando
 * na tela — isso fica para a verificação manual em navegador. O que a suíte prova é o que o
 * componente CONTROLA: não buscar nem importar antes do clique, os três estados
 * (carregando/erro/pronto), o canvas acessível montado quando o sólido termina de carregar, os
 * parâmetros de câmera e de zoom calculados a partir do tamanho do sólido, e a rotação automática
 * que para na primeira interação e não retoma.
 */
export function VisualizadorDeSolido({ componenteId }: Props) {
  const [estado, setEstado] = useState<Estado>({ tipo: 'inicial' })
  const containerRef = useRef<HTMLDivElement | null>(null)
  const rendererRef = useRef<ThreeModulo.WebGLRenderer | null>(null)
  const controlsRef = useRef<OrbitControlsModulo | null>(null)
  const quadroRef = useRef<number | null>(null)
  // Padrão `cancelado` que os `useEffect` de busca deste projeto já usam (ver
  // `ComponenteDetalhePage`), adaptado ao clique assíncrono: sem isto, a resposta do fetch ou do
  // import dinâmico chegando depois de desmontar a tela escreveria estado num componente morto.
  const desmontadoRef = useRef(false)

  useEffect(() => {
    // StrictMode remonta este componente em dev (monta, desmonta, monta de novo) para expor efeito
    // que não aguenta o ciclo. Sem esta linha, a remontagem herdava `desmontadoRef.current === true`
    // já deixado `true` pela limpeza que a primeira montagem rodou, e as guardas de
    // `aoClicarVisualizar` descartavam o estado PRONTO e o de ERRO para sempre — o viewer ficava
    // preso em "Carregando…".
    desmontadoRef.current = false
    return () => {
      desmontadoRef.current = true
      if (quadroRef.current !== null) cancelAnimationFrame(quadroRef.current)
      controlsRef.current?.dispose()
      rendererRef.current?.dispose()
    }
  }, [])

  function montarCena(
    THREE: typeof ThreeModulo,
    geometria: ThreeModulo.BufferGeometry,
    OrbitControls: typeof OrbitControlsModulo,
  ) {
    const container = containerRef.current
    if (!container) return

    // Centraliza o sólido na origem: um STL exportado do CAD raramente nasce centrado, e sem isto
    // a peça ficaria fora do quadro que `enquadramentoDoSolido` calcula a partir do tamanho dela.
    geometria.computeBoundingBox()
    geometria.center()

    // Raio da esfera que envolve o sólido — entrada da função pura de enquadramento. Um STL não
    // carrega unidade: uma peça de poucos milímetros e uma de vários metros precisam da mesma
    // fórmula, só variando este raio.
    geometria.computeBoundingSphere()
    const raioDaEsfera = geometria.boundingSphere?.radius ?? 0
    const enquadramento = enquadramentoDoSolido(raioDaEsfera, ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO)

    const cena = new THREE.Scene()
    const camera = new THREE.PerspectiveCamera(
      ABERTURA_VERTICAL_EM_GRAUS,
      1,
      enquadramento.near,
      enquadramento.far,
    )
    camera.position.set(0, 0, enquadramento.distancia)

    const malha = new THREE.Mesh(geometria, new THREE.MeshStandardMaterial({ color: 0x9ca3af }))
    cena.add(malha)
    cena.add(new THREE.AmbientLight(0xffffff, 0.6))
    const luz = new THREE.DirectionalLight(0xffffff, 0.8)
    luz.position.set(1, 1, 1)
    cena.add(luz)

    const renderer = new THREE.WebGLRenderer({ antialias: true })
    renderer.setSize(320, 320)
    renderer.domElement.setAttribute('aria-label', ROTULO_DO_CANVAS)
    container.appendChild(renderer.domElement)
    rendererRef.current = renderer

    const controls = new OrbitControls(camera, renderer.domElement)
    // Limites de zoom derivados do MESMO enquadramento, nunca literais — ver `FATOR_DE_ZOOM_MINIMO`
    // e `FATOR_DE_ZOOM_MAXIMO`.
    controls.minDistance = enquadramento.distancia * FATOR_DE_ZOOM_MINIMO
    controls.maxDistance = enquadramento.distancia * FATOR_DE_ZOOM_MAXIMO

    // Gira sozinho até a PRIMEIRA manipulação do usuário — decisão do usuário: um sólido que volta
    // a girar sozinho impede de parar na vista que ele quer olhar, então a rotação para DE VEZ na
    // primeira interação e não retoma nem ao soltar o mouse, nem depois de um tempo parado.
    controls.autoRotate = true
    function pararDeGirarSozinho() {
      controls.autoRotate = false
      controls.removeEventListener('start', pararDeGirarSozinho)
    }
    controls.addEventListener('start', pararDeGirarSozinho)
    controlsRef.current = controls

    function animar() {
      // `controls.update()` é o que o `autoRotate` exige a cada quadro — sem ele a rotação
      // automática fica declarada mas parada. A rotação manual da malha saiu: com o `autoRotate`
      // do `OrbitControls` girando a câmera ao redor do sólido, girar a malha também produziria
      // dois mecanismos de rotação ao mesmo tempo.
      controls.update()
      renderer.render(cena, camera)
      quadroRef.current = requestAnimationFrame(animar)
    }
    animar()
  }

  async function aoClicarVisualizar() {
    setEstado({ tipo: 'carregando' })
    try {
      const resp = await apiFetch(caminhoDoSolido(componenteId))
      if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao carregar o sólido (${resp.status}).`)
      const binario = await resp.arrayBuffer()

      // Os três módulos, num só Promise.all — nenhum deles é importado antes deste ponto.
      const [THREE, { STLLoader }, { OrbitControls }] = await Promise.all([
        import('three'),
        import('three/examples/jsm/loaders/STLLoader.js'),
        import('three/examples/jsm/controls/OrbitControls.js'),
      ])
      if (desmontadoRef.current) return

      const geometria = new STLLoader().parse(binario)
      montarCena(THREE, geometria, OrbitControls)
      if (desmontadoRef.current) return
      setEstado({ tipo: 'pronto' })
    } catch (erro) {
      if (!desmontadoRef.current) setEstado({ tipo: 'erro', erro })
    }
  }

  return (
    <div className="flex flex-col gap-3 rounded-lg border border-borda bg-superficie p-4">
      {(estado.tipo === 'inicial' || estado.tipo === 'erro') && (
        <Botao variante="secundario" onClick={aoClicarVisualizar}>
          Visualizar
        </Botao>
      )}
      {estado.tipo === 'carregando' && <EstadoCarregando />}
      {estado.tipo === 'erro' && (
        <BannerDeErro mensagem={mensagemDeErro(estado.erro, 'Não foi possível carregar o sólido.')} />
      )}
      <div ref={containerRef} />
    </div>
  )
}
