import { useEffect, useRef, useState } from 'react'
import type * as ThreeModulo from 'three'
import { apiFetch } from '../api/client'
import { caminhoDoSolido } from '../api/cadastros'
import { ErroDeApi, mensagemDeErro } from '../api/erros'
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
 * Viewer 3D do sólido STL de um Componente — Task 7 da Fase 2B (§2.3 e §7.2 da spec de desenho).
 *
 * Fica FORA da guarda `usePodeEscrever` da tela que o monta: o `GET /componentes/{id}/solido` é
 * de qualquer perfil autenticado, e quem não escreve enxerga o sólido por aqui — o
 * `UploadDeSolido` (Task 6) é só para quem escreve.
 *
 * `three` e o `STLLoader` entram por `import()` dinâmico DENTRO do clique de "Visualizar", nunca
 * no topo do módulo: o público que de fato abre o viewer é o desktop do PCP/Administrador ao
 * cadastrar (§2.3), e o operador no Android não deve pagar o bundle de uma tela que nunca abre.
 *
 * O jsdom não implementa WebGL: nenhum teste deste componente prova que o sólido aparece girando
 * na tela — isso fica para a verificação manual em navegador (Task 9 da spec). O que a suíte prova
 * é o que o componente CONTROLA: não buscar nem importar antes do clique, os três estados
 * (carregando/erro/pronto) e o canvas acessível montado quando o sólido termina de carregar.
 */
export function VisualizadorDeSolido({ componenteId }: Props) {
  const [estado, setEstado] = useState<Estado>({ tipo: 'inicial' })
  const containerRef = useRef<HTMLDivElement | null>(null)
  const rendererRef = useRef<ThreeModulo.WebGLRenderer | null>(null)
  const quadroRef = useRef<number | null>(null)
  // Padrão `cancelado` que os `useEffect` de busca deste projeto já usam (ver
  // `ComponenteDetalhePage`), adaptado ao clique assíncrono: sem isto, a resposta do fetch ou do
  // import dinâmico chegando depois de desmontar a tela escreveria estado num componente morto.
  const desmontadoRef = useRef(false)

  useEffect(() => () => {
    desmontadoRef.current = true
    if (quadroRef.current !== null) cancelAnimationFrame(quadroRef.current)
    rendererRef.current?.dispose()
  }, [])

  function montarCena(THREE: typeof ThreeModulo, geometria: ThreeModulo.BufferGeometry) {
    const container = containerRef.current
    if (!container) return

    // Centraliza o sólido na origem: um STL exportado do CAD raramente nasce centrado, e sem isto
    // a peça apareceria fora do enquadramento da câmera fixa abaixo.
    geometria.computeBoundingBox()
    geometria.center()

    const cena = new THREE.Scene()
    const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 10000)
    camera.position.set(0, 0, 200)

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

    function animar() {
      malha.rotation.y += 0.01
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

      // Os dois módulos, num só Promise.all — nenhum dos dois é importado antes deste ponto.
      const [THREE, { STLLoader }] = await Promise.all([
        import('three'),
        import('three/examples/jsm/loaders/STLLoader.js'),
      ])
      if (desmontadoRef.current) return

      const geometria = new STLLoader().parse(binario)
      montarCena(THREE, geometria)
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
