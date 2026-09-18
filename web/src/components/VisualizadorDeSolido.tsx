import { useEffect, useRef, useState } from 'react'
import type * as ThreeModulo from 'three'
import type { OrbitControls as OrbitControlsModulo } from 'three/examples/jsm/controls/OrbitControls.js'
import type { RoomEnvironment as RoomEnvironmentModulo } from 'three/examples/jsm/environments/RoomEnvironment.js'
import { apiFetch } from '../api/client'
import { caminhoDoSolido } from '../api/cadastros'
import { ErroDeApi, mensagemDeErro } from '../api/erros'
import {
  ABERTURA_VERTICAL_EM_GRAUS,
  MARGEM_DE_ENQUADRAMENTO,
  enquadramentoDoSolido,
  type EnquadramentoDoSolido,
} from './enquadramentoDoSolido'
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

/** `controls.maxDistance = distância inicial × este fator` — o usuário pode afastar até o triplo. */
export const FATOR_DE_ZOOM_MAXIMO = 3

/** Altura fixa do canvas, em pixels — a largura acompanha o container (ver `medirLarguraDoContainer`),
    mas a altura não muda com o redimensionamento da janela. */
export const ALTURA_DO_CANVAS_EM_PIXELS = 400

/**
 * Acabamento metálico igual para toda peça — decisão do usuário, independente do material da
 * receita. `metalness = 1` (metal genuíno): no workflow metalness/roughness do
 * `MeshStandardMaterial`, a parcela difusa cai a zero quando `metalness` é 1 — quase toda a
 * aparência do material passa a vir do reflexo do ambiente (ver `RoomEnvironment` em
 * `montarCena`), não da cor. Sem um ambiente de reflexo, metal genuíno ficaria quase preto fora do
 * ponto de brilho da luz direcional; é por isso que as duas mudanças desta peça (metal + ambiente)
 * vêm juntas.
 */
export const METALNESS_DO_ACABAMENTO = 1

/**
 * Nem espelhado nem fosco: rugoso o bastante para não virar espelho (que confundiria a leitura da
 * forma da peça com o reflexo do quarto do `RoomEnvironment`), liso o bastante para o reflexo do
 * ambiente continuar visível e ajudar a distinguir face de face — a mesma distinção que a normal
 * recalculada de `computeVertexNormals()` já produz na luz direcional.
 */
export const ROUGHNESS_DO_ACABAMENTO = 0.4

/** Cor base do acabamento — some quase inteira na tinta de reflexo com `metalness = 1`, mas dá o
    tom neutro (cinza-claro) do metal quando a luz é branca. A regra de tokens do `CLAUDE.md`
    ("cores só pelos tokens") vale para classe CSS; um hexadecimal passado direto ao three.js não é
    classe, mas a constante nomeada evita o literal solto no meio do código. */
const COR_DO_ACABAMENTO_METALICO = 0x9ca3af

/**
 * Intensidade da luz direcional, revisada para o acabamento metálico. O reflexo especular direto
 * de um material com `metalness = 1` é concentrado num lóbulo estreito ao redor da direção de
 * reflexão (diferente da difusão ampla de um material sem `metalness`), então a MESMA intensidade
 * calibrada para difusão estouraria o ponto de brilho da peça. Reduzida para complementar, não
 * substituir, o reflexo do mapa de ambiente (`RoomEnvironment`/`PMREMGenerator` — ver
 * `montarCena`): a luz direcional marca uma direção de luz sem ser a fonte principal — esse papel
 * é do mapa de ambiente.
 *
 * Não há `AmbientLight` nesta cena. Com `metalness = 1`, `material.diffuseContribution` (no shader
 * físico do three.js) é `diffuseColor * (1 - metalness)` — exatamente zero, não só pequeno — e a
 * irradiância de uma `AmbientLight` só alimenta essa parcela difusa em `MeshStandardMaterial`
 * (que define `STANDARD`): o especular indireto usa `getIBLIrradiance`, a irradiância do MAPA de
 * ambiente, uma variável separada, que só se soma à da `AmbientLight` para os defines
 * `LAMBERT`/`PHONG`, nunca para `STANDARD`. Ou seja: se existisse, uma `AmbientLight` não
 * contribuiria nada ao pixel final.
 */
const INTENSIDADE_DA_LUZ_DIRECIONAL = 0.6

/**
 * Largura do container, com piso: `getBoundingClientRect()` devolve 0 no jsdom (a suíte roda sem
 * layout de verdade) e devolveria 0 também num navegador real antes do primeiro layout do card. Um
 * `renderer.setSize(0, …)` produziria um canvas invisível sem erro nenhum — o piso evita isso caindo
 * na própria altura fixa do canvas, que dá uma proporção 1:1 como palpite neutro sem outra
 * informação disponível.
 */
function medirLarguraDoContainer(container: HTMLDivElement): number {
  const medida = container.getBoundingClientRect().width
  return medida > 0 ? medida : ALTURA_DO_CANVAS_EM_PIXELS
}

/**
 * Viewer 3D do sólido STL de um Componente — Task 7 da Fase 2B (§2.3 e §7.2 da spec de desenho).
 *
 * Fica FORA da guarda `usePodeEscrever` da tela que o monta: o `GET /componentes/{id}/solido` é
 * de qualquer perfil autenticado, e quem não escreve enxerga o sólido por aqui — o
 * `UploadDeSolido` (Task 6) é só para quem escreve.
 *
 * `three`, o `STLLoader`, o `OrbitControls` e o `RoomEnvironment` entram por `import()` dinâmico
 * DENTRO do clique de "Visualizar", nunca no topo do módulo: o público que de fato abre o viewer é
 * o desktop do PCP/Administrador ao cadastrar (§2.3), e o operador no Android não deve pagar o
 * bundle de uma tela que nunca abre.
 *
 * A câmera se enquadra pelo tamanho real do sólido e pela proporção do quadro
 * (`enquadramentoDoSolido`, a partir do raio no plano de giro e da meia altura), em vez de um
 * canvas quadrado com distância fixa — um STL não carrega unidade, e uma peça comprida deve
 * aproveitar a largura do quadro em vez de ser limitada pelo lado menor. O canvas ocupa a largura
 * do card (um `ResizeObserver` refaz o enquadramento quando ela muda) com altura fixa. O
 * `OrbitControls` dá zoom, pan e rotação por arraste, mais um botão "Recentralizar" que volta à
 * vista centralizada do tamanho atual do quadro; o sólido também gira sozinho até a primeira
 * interação do usuário (incluindo o próprio "Recentralizar"), e para de vez a partir daí.
 *
 * O jsdom não implementa WebGL: nenhum teste deste componente prova que o sólido aparece girando
 * na tela — isso fica para a verificação manual em navegador. O que a suíte prova é o que o
 * componente CONTROLA: não buscar nem importar antes do clique, os três estados
 * (carregando/erro/pronto), o canvas acessível montado quando o sólido termina de carregar, os
 * parâmetros de câmera e de zoom calculados a partir do tamanho do sólido e da proporção do quadro,
 * o redimensionamento e a rotação automática que para na primeira interação e não retoma.
 */
export function VisualizadorDeSolido({ componenteId }: Props) {
  const [estado, setEstado] = useState<Estado>({ tipo: 'inicial' })
  const containerRef = useRef<HTMLDivElement | null>(null)
  const rendererRef = useRef<ThreeModulo.WebGLRenderer | null>(null)
  const controlsRef = useRef<OrbitControlsModulo | null>(null)
  const resizeObserverRef = useRef<ResizeObserver | null>(null)
  const pmremGeneratorRef = useRef<ThreeModulo.PMREMGenerator | null>(null)
  const texturaDeAmbienteRef = useRef<ThreeModulo.Texture | null>(null)
  const roomEnvironmentRef = useRef<RoomEnvironmentModulo | null>(null)
  const geometriaRef = useRef<ThreeModulo.BufferGeometry | null>(null)
  const materialRef = useRef<ThreeModulo.MeshStandardMaterial | null>(null)
  const quadroRef = useRef<number | null>(null)
  // Guarda a MESMA função que o ouvinte de `start` usa para parar a rotação automática — o botão
  // "Recentralizar" reusa esta referência em vez de duplicar a lógica de parar, porque clicar nele
  // também conta como a primeira manipulação do usuário.
  const pararDeGirarSozinhoRef = useRef<(() => void) | null>(null)
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
      resizeObserverRef.current?.disconnect()
      controlsRef.current?.dispose()
      // Ordem importa, e é ao contrário do que pareceria natural: os `dispose` de
      // `texturaDeAmbienteRef`, `pmremGeneratorRef` e `roomEnvironmentRef` têm de rodar ANTES de
      // `rendererRef.current?.dispose()`, nunca depois. `WebGLRenderer.dispose()` chama
      // `properties.dispose()`, que troca o WeakMap inteiro de propriedades internas por um vazio
      // — e é NESSE mapa que tanto `releaseMaterialProgramReferences` (para os 8 materiais que o
      // `RoomEnvironment` cria) quanto `WebGLTextures.deallocateTexture` (para a textura e os
      // render targets internos do `PMREMGenerator`) leem o estado já alocado (`.programs`,
      // `.__webglInit`) para decidir o que liberar. Com o mapa já reciclado, cada `dispose()`
      // ainda dispara o evento, mas o ouvinte do renderer encontra um objeto novo e vazio e pula o
      // corpo inteiro — o programa GL compilado nunca chega a `programCache.releaseProgram`, que é
      // o único lugar que de fato chama `program.destroy()`. A geometria do `RoomEnvironment` não
      // sofre disso (usa o WeakMap próprio de `WebGLAttributes`, nunca reciclado por
      // `renderer.dispose()`), então na ordem antiga só ela era liberada de forma garantida.
      // `releaseProgram` decrementa uma contagem de referência (`usedTimes`) e só destrói o
      // programa quando ela chega a zero — para estes materiais, que não compartilham cacheKey com
      // nada fora desta cena isolada, o esperado é chegar a zero já na primeira liberação, mas a
      // garantia que chamar `dispose()` nesta ordem oferece é a de DISPARAR a liberação, não a de
      // destruir o programa incondicionalmente.
      //
      // A malha do sólido entra na mesma leva. O material dela tem a mesma restrição dos 8 materiais
      // do `RoomEnvironment` (o programa GL compilado vive no mapa que o renderer recicla). A
      // geometria não — os buffers de atributo dela vivem em `WebGLAttributes`, como os da geometria
      // do `RoomEnvironment` —, mas é o maior recurso de GPU do viewer e `renderer.dispose()` não a
      // libera; vai junto, antes, sem que a ordem dela importe.
      texturaDeAmbienteRef.current?.dispose()
      pmremGeneratorRef.current?.dispose()
      roomEnvironmentRef.current?.dispose()
      geometriaRef.current?.dispose()
      materialRef.current?.dispose()
      rendererRef.current?.dispose()
    }
  }, [])

  function montarCena(
    THREE: typeof ThreeModulo,
    geometria: ThreeModulo.BufferGeometry,
    OrbitControls: typeof OrbitControlsModulo,
    RoomEnvironment: typeof RoomEnvironmentModulo,
  ) {
    const container = containerRef.current
    if (!container) return

    // Centraliza o sólido na origem: um STL exportado do CAD raramente nasce centrado, e sem isto
    // a peça ficaria fora do quadro que `enquadramentoDoSolido` calcula a partir do tamanho dela.
    // `meioX`/`meioY`/`meioZ` (a diferença entre `max` e `min` de cada eixo) são invariantes a esta
    // translação — por isso podem ser lidas do bounding box de antes de centralizar.
    geometria.computeBoundingBox()
    const caixa = geometria.boundingBox
    // `Number.isFinite` cobre o caso de uma geometria sem vértice nenhum: `computeBoundingBox` faz
    // `boundingBox` existir mesmo assim, mas com `min`/`max` infinitos (caixa "vazia"), e
    // `max - min` desses daria `Infinity`, não passando pelo piso de `enquadramentoDoSolido` (que
    // só protege contra zero) — cai para 0 e deixa o piso da função pura assumir a partir daí.
    const meiaExtensao = (minimo: number, maximo: number) => {
      const valor = (maximo - minimo) / 2
      return Number.isFinite(valor) ? valor : 0
    }
    const meioX = caixa ? meiaExtensao(caixa.min.x, caixa.max.x) : 0
    const meioY = caixa ? meiaExtensao(caixa.min.y, caixa.max.y) : 0
    const meioZ = caixa ? meiaExtensao(caixa.min.z, caixa.max.z) : 0
    geometria.center()

    // Raio no plano XZ — o plano em que a rotação automática gira a câmera ao redor do eixo Y.
    // Diferente do raio da esfera envolvente usado antes, este não penaliza uma peça comprida só
    // porque ela também é alta: cada eixo entra na fórmula que lhe compete (largura ou altura).
    const raioNoPlanoDeGiro = Math.sqrt(meioX * meioX + meioZ * meioZ)
    const meiaAlturaEmY = meioY

    const cena = new THREE.Scene()
    // `1, 0.1, 1` são só o que o construtor exige — os valores reais entram via `aplicarEnquadramento`,
    // que também é o que o `ResizeObserver` chama de novo a cada mudança de largura.
    const camera = new THREE.PerspectiveCamera(ABERTURA_VERTICAL_EM_GRAUS, 1, 0.1, 1)

    function aplicarEnquadramento(proporcaoDoQuadro: number): EnquadramentoDoSolido {
      const enquadramento = enquadramentoDoSolido(
        raioNoPlanoDeGiro,
        meiaAlturaEmY,
        ABERTURA_VERTICAL_EM_GRAUS,
        proporcaoDoQuadro,
        MARGEM_DE_ENQUADRAMENTO,
      )
      camera.aspect = proporcaoDoQuadro
      camera.near = enquadramento.near
      camera.far = enquadramento.far
      camera.position.set(0, 0, enquadramento.distancia)
      camera.updateProjectionMatrix()
      return enquadramento
    }

    const larguraInicial = medirLarguraDoContainer(container)
    const enquadramentoInicial = aplicarEnquadramento(larguraInicial / ALTURA_DO_CANVAS_EM_PIXELS)

    const material = new THREE.MeshStandardMaterial({
      color: COR_DO_ACABAMENTO_METALICO,
      metalness: METALNESS_DO_ACABAMENTO,
      roughness: ROUGHNESS_DO_ACABAMENTO,
    })
    const malha = new THREE.Mesh(geometria, material)
    cena.add(malha)
    // Guardados para a limpeza do `useEffect` — ver o comentário da ordem dos `dispose` lá.
    geometriaRef.current = geometria
    materialRef.current = material
    const luz = new THREE.DirectionalLight(0xffffff, INTENSIDADE_DA_LUZ_DIRECIONAL)
    luz.position.set(1, 1, 1)
    cena.add(luz)

    const renderer = new THREE.WebGLRenderer({ antialias: true })
    renderer.setSize(larguraInicial, ALTURA_DO_CANVAS_EM_PIXELS)
    renderer.domElement.setAttribute('aria-label', ROTULO_DO_CANVAS)
    container.appendChild(renderer.domElement)
    rendererRef.current = renderer

    // Ambiente de reflexo: com `metalness = 1` a parcela difusa da malha é zero, então sem uma
    // fonte de luz vindo de toda direção a peça ficaria quase preta fora do ponto de brilho da
    // `DirectionalLight`. `RoomEnvironment` é uma salinha padrão do three.js (não depende de
    // nenhum asset externo); o `PMREMGenerator` prefiltra essa cena nos níveis de rugosidade que o
    // material físico precisa antes de virar `scene.environment`. A instância de `RoomEnvironment`
    // não precisa sobreviver além do `fromScene()` (a textura prefiltrada já basta para desenhar),
    // mas ela mesma cria 1 geometria e 8 materiais que só o `dispose()` próprio dela libera — sem
    // guardar a referência aqui, ninguém poderia chamá-lo na limpeza.
    const pmremGenerator = new THREE.PMREMGenerator(renderer)
    const ambiente = new RoomEnvironment()
    const texturaDeAmbiente = pmremGenerator.fromScene(ambiente).texture
    cena.environment = texturaDeAmbiente
    pmremGeneratorRef.current = pmremGenerator
    texturaDeAmbienteRef.current = texturaDeAmbiente
    roomEnvironmentRef.current = ambiente

    const controls = new OrbitControls(camera, renderer.domElement)
    // Limites de zoom derivados do MESMO enquadramento, nunca literais — ver `FATOR_DE_ZOOM_MAXIMO`.
    // `minDistance` vem pronto de `enquadramentoDoSolido` (`distanciaMinima`), que garante o
    // invariante "câmera nunca entra na esfera que envolve o volume varrido pela peça ao girar" —
    // o `OrbitControls` permite orbitar até a vista de cima ou de baixo, não só ao redor do eixo Y,
    // então a proteção tem de valer em QUALQUER direção de órbita, não só no plano de giro — para
    // qualquer proporção de quadro, diferente de multiplicar `distancia` (que varia com a proporção)
    // por um fator fixo aqui, o que deixava a garantia depender do quadro ser sempre quadrado.
    controls.minDistance = enquadramentoInicial.distanciaMinima
    controls.maxDistance = enquadramentoInicial.distancia * FATOR_DE_ZOOM_MAXIMO
    controlsRef.current = controls

    // Redimensionamento do card (ou da janela): refaz o tamanho do canvas, a proporção e o
    // enquadramento inteiro, pela MESMA `aplicarEnquadramento` que a montagem usou — nunca uma
    // segunda cópia da fórmula que pudesse divergir dela com o tempo.
    const resizeObserver = new ResizeObserver(() => {
      const containerAtual = containerRef.current
      if (!containerAtual) return
      const novaLargura = medirLarguraDoContainer(containerAtual)
      renderer.setSize(novaLargura, ALTURA_DO_CANVAS_EM_PIXELS)
      const novoEnquadramento = aplicarEnquadramento(novaLargura / ALTURA_DO_CANVAS_EM_PIXELS)
      controls.minDistance = novoEnquadramento.distanciaMinima
      controls.maxDistance = novoEnquadramento.distancia * FATOR_DE_ZOOM_MAXIMO
      // "Recentralizar" (`controls.reset()`) volta a `position0`, que o `OrbitControls` captura na
      // construção — sem reescrevê-la aqui, depois de um redimensionamento ele devolveria a câmera à
      // distância do tamanho ANTIGO. Só a posição é atualizada, e não `saveState()`: este copiaria
      // também o `target` atual, e um pan feito antes do redimensionamento deixaria de ser desfeito
      // por "Recentralizar". O `target0` continua sendo a origem, onde o sólido está centralizado.
      controls.position0.set(0, 0, novoEnquadramento.distancia)
    })
    resizeObserver.observe(container)
    resizeObserverRef.current = resizeObserver

    // Gira sozinho até a PRIMEIRA manipulação do usuário — decisão do usuário: um sólido que volta
    // a girar sozinho impede de parar na vista que ele quer olhar, então a rotação para DE VEZ na
    // primeira interação e não retoma nem ao soltar o mouse, nem depois de um tempo parado. O botão
    // "Recentralizar" também conta como manipulação (ver `aoClicarRecentralizar`), por isso a mesma
    // função é guardada em `pararDeGirarSozinhoRef` em vez de ficar presa só a este ouvinte.
    controls.autoRotate = true
    function pararDeGirarSozinho() {
      controls.autoRotate = false
      controls.removeEventListener('start', pararDeGirarSozinho)
    }
    controls.addEventListener('start', pararDeGirarSozinho)
    pararDeGirarSozinhoRef.current = pararDeGirarSozinho

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

      // Os quatro módulos, num só Promise.all — nenhum deles é importado antes deste ponto.
      const [THREE, { STLLoader }, { OrbitControls }, { RoomEnvironment }] = await Promise.all([
        import('three'),
        import('three/examples/jsm/loaders/STLLoader.js'),
        import('three/examples/jsm/controls/OrbitControls.js'),
        import('three/examples/jsm/environments/RoomEnvironment.js'),
      ])
      if (desmontadoRef.current) return

      const geometria = new STLLoader().parse(binario)
      // O `STLLoader` devolve geometria NÃO indexada (cada triângulo com os próprios três
      // vértices, sem compartilhar nenhum com a face vizinha) e copia a normal do arquivo como
      // está, sem recalcular nada — um exportador que grava normal zerada (ou errada) faz toda
      // iluminação que depende da orientação da face parar de distinguir uma face da outra: no
      // shader físico do three.js, `dot(normal, direção)` zera a `DirectionalLight`
      // (`RE_Direct_Physical`), e o reflexo do mapa de ambiente colapsa na própria direção de
      // incidência do olhar, e não na da face (`reflect(-viewDir, normal)` com normal nulo devolve
      // `-viewDir` — a continuação do raio câmera→ponto, não a volta para a câmera — em
      // `getIBLRadiance`) — a peça perde a distinção entre faces. `computeVertexNormals()`, numa
      // geometria sem índice, produz normal por face (cada "vértice" só pertence a UM triângulo),
      // que é o sombreamento facetado correto para peça mecânica — precisa rodar aqui, depois do
      // `parse` e antes de montar a malha, nunca depois: a malha guarda a MESMA geometria por
      // referência, mas o primeiro quadro já é desenhado dentro de `montarCena`.
      geometria.computeVertexNormals()
      montarCena(THREE, geometria, OrbitControls, RoomEnvironment)
      setEstado({ tipo: 'pronto' })
    } catch (erro) {
      if (!desmontadoRef.current) setEstado({ tipo: 'erro', erro })
    }
  }

  function aoClicarRecentralizar() {
    // `reset()` do `OrbitControls` real restaura ângulo e zoom à vista guardada em
    // `target0`/`position0` — capturada na construção dos controles e com `position0` refeita a cada
    // redimensionamento (ver o `ResizeObserver` em `montarCena`), então a vista é a do enquadramento
    // do tamanho ATUAL —, mas despacha só o evento `change`, nunca `start`. Sem a chamada a
    // `pararDeGirarSozinhoRef.current`, um clique aqui antes de qualquer outra interação devolveria
    // o sólido à vista centralizada e ele continuaria girando sozinho, contrariando a regra de que a
    // rotação para DE VEZ na primeira manipulação.
    controlsRef.current?.reset()
    pararDeGirarSozinhoRef.current?.()
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
      {estado.tipo === 'pronto' && (
        <Botao variante="secundario" onClick={aoClicarRecentralizar}>
          Recentralizar
        </Botao>
      )}
      <div
        ref={containerRef}
        data-testid="container-do-visualizador"
        className="w-full"
        style={{ height: ALTURA_DO_CANVAS_EM_PIXELS }}
      />
    </div>
  )
}
