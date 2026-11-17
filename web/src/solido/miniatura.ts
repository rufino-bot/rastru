import type * as ThreeModulo from 'three'

/** C1: PNG de 320 × 240 (4:3), fundo transparente. */
export const LARGURA_DA_MINIATURA = 320
export const ALTURA_DA_MINIATURA = 240

/** C2: folga sobre a esfera envolvente, para nenhuma Peça encostar na borda. */
export const FOLGA_DO_ENQUADRAMENTO = 1.08
/** Distância da câmera ao centro, em raios; só precisa ficar fora do sólido (a câmera é ortográfica). */
export const DISTANCIA_EM_RAIOS = 4

/** C3: o estilo, em números. */
export const COR_DA_FACE = 0xc9d3d1
export const COR_DA_ARESTA = 0x2f3a38
export const INTENSIDADE_DA_LUZ_AMBIENTE = 0.55
export const INTENSIDADE_DA_LUZ_DIRECIONAL = 0.6
/** A luz direcional vem de cima e da frente (relativa ao centro, em raios). */
export const DIRECAO_DA_LUZ_DIRECIONAL: [number, number, number] = [0.3, 1, 0.8]
export const LIMIAR_DA_ARESTA_EM_GRAUS = 20
export const POLYGON_OFFSET_FACTOR = 1
export const POLYGON_OFFSET_UNITS = 1

export interface CameraIsometrica {
  posicao: [number, number, number]
  meiaAltura: number
  meiaLargura: number
  perto: number
  longe: number
}

/** Câmera ortográfica olhando de (1,1,1) para o centro (pura: o desenho a aplica). */
export function cameraIsometrica(centro: [number, number, number], raio: number): CameraIsometrica {
  const k = (raio * DISTANCIA_EM_RAIOS) / Math.sqrt(3)
  const meiaAltura = raio * FOLGA_DO_ENQUADRAMENTO
  return {
    posicao: [centro[0] + k, centro[1] + k, centro[2] + k],
    meiaAltura,
    meiaLargura: (meiaAltura * LARGURA_DA_MINIATURA) / ALTURA_DA_MINIATURA,
    perto: raio * (DISTANCIA_EM_RAIOS - 2),
    longe: raio * (DISTANCIA_EM_RAIOS + 2),
  }
}

/**
 * Desenha o STL em PNG transparente. Qualquer falha (sem WebGL, STL ruim, `toBlob` nulo) vira
 * `null` (C13): a miniatura é acessório e nunca trava o envio do sólido.
 */
export async function desenharMiniatura(stl: ArrayBuffer): Promise<Blob | null> {
  let renderer: ThreeModulo.WebGLRenderer | null = null
  const descartaveis: { dispose(): void }[] = []
  try {
    const [THREE, { STLLoader }] = await Promise.all([
      import('three'),
      import('three/examples/jsm/loaders/STLLoader.js'),
    ])
    const geometria = new STLLoader().parse(stl)
    descartaveis.push(geometria)
    geometria.computeVertexNormals()
    geometria.computeBoundingSphere()
    const esfera = geometria.boundingSphere
    if (!esfera || !(esfera.radius > 0)) return null

    renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true, preserveDrawingBuffer: true })
    renderer.setPixelRatio(1)
    renderer.setSize(LARGURA_DA_MINIATURA, ALTURA_DA_MINIATURA)
    renderer.setClearColor(0x000000, 0)

    const material = new THREE.MeshLambertMaterial({
      color: COR_DA_FACE,
      polygonOffset: true,
      polygonOffsetFactor: POLYGON_OFFSET_FACTOR,
      polygonOffsetUnits: POLYGON_OFFSET_UNITS,
    })
    const arestas = new THREE.EdgesGeometry(geometria, LIMIAR_DA_ARESTA_EM_GRAUS)
    const materialDaAresta = new THREE.LineBasicMaterial({ color: COR_DA_ARESTA })
    descartaveis.push(material, arestas, materialDaAresta)

    const cena = new THREE.Scene()
    cena.add(new THREE.Mesh(geometria, material))
    cena.add(new THREE.LineSegments(arestas, materialDaAresta))
    cena.add(new THREE.AmbientLight(0xffffff, INTENSIDADE_DA_LUZ_AMBIENTE))
    const centro: [number, number, number] = [esfera.center.x, esfera.center.y, esfera.center.z]
    const raio = esfera.radius
    const luz = new THREE.DirectionalLight(0xffffff, INTENSIDADE_DA_LUZ_DIRECIONAL)
    luz.position.set(
      centro[0] + DIRECAO_DA_LUZ_DIRECIONAL[0] * raio * 4,
      centro[1] + DIRECAO_DA_LUZ_DIRECIONAL[1] * raio * 4,
      centro[2] + DIRECAO_DA_LUZ_DIRECIONAL[2] * raio * 4,
    )
    luz.target.position.set(...centro)
    cena.add(luz, luz.target)

    const c = cameraIsometrica(centro, raio)
    const camera = new THREE.OrthographicCamera(-c.meiaLargura, c.meiaLargura, c.meiaAltura, -c.meiaAltura, c.perto, c.longe)
    camera.position.set(...c.posicao)
    camera.up.set(0, 1, 0)
    camera.lookAt(...centro)
    camera.updateProjectionMatrix()

    renderer.render(cena, camera)
    const canvas = renderer.domElement
    return await new Promise<Blob | null>((resolver) => canvas.toBlob((b) => resolver(b), 'image/png'))
  } catch {
    return null
  } finally {
    for (const d of descartaveis) d.dispose()
    renderer?.dispose()
  }
}
