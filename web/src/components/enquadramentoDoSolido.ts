/**
 * Distância da câmera e planos `near`/`far` calculados a partir do tamanho REAL do sólido e da
 * proporção do quadro, em vez dos literais fixos (`0.1`/`10000` de near/far, `200` de distância,
 * quadro sempre quadrado) que cortavam peça grande e deixavam peça pequena minúscula no quadro —
 * um STL não carrega unidade, e uma peça exportada em milímetros pode variar de poucos milímetros
 * a vários metros de raio, comprida num eixo ou alta no outro.
 */
export interface EnquadramentoDoSolido {
  distancia: number
  near: number
  far: number
  distanciaMinima: number
}

/**
 * Abertura vertical da câmera, em graus — mesmo valor que a `PerspectiveCamera` já usava antes
 * deste enquadramento existir.
 */
export const ABERTURA_VERTICAL_EM_GRAUS = 50

/**
 * Folga sobre a distância mínima em que o sólido cabe inteiro no campo de visão: 1 preencheria o
 * quadro encostando o sólido nas bordas; a folga abre um respiro ao redor dele.
 */
export const MARGEM_DE_ENQUADRAMENTO = 1.15

/**
 * Piso para o raio no plano de giro OU a meia altura em Y: um STL degenerado (vazio, ou um único
 * ponto) produz as duas dimensões zero, e usá-las cruas em `enquadramentoDoSolido` daria distância
 * zero (a câmera nasceria dentro do próprio sólido inexistente) ou divisão por zero. Este piso é
 * pequeno o bastante para não distorcer o enquadramento de um sólido real.
 */
const DIMENSAO_MINIMA_PARA_ENQUADRAR = 1

/** `near = distancia × este fator` — pequeno o bastante para não cortar o sólido ao aproximar. */
const FATOR_DE_FOLGA_PROXIMA = 1 / 100

/** `far = distancia × este fator` — grande o bastante para não cortar o sólido ao afastar. */
const FATOR_DE_FOLGA_DISTANTE = 50

/**
 * `distanciaMinima = esfera do volume varrido (já com o piso de degenerescência em cada dimensão) ×
 * esta margem`. O `OrbitControls` deixa `minPolarAngle = 0` e `maxPolarAngle = Math.PI` (conferido
 * no fonte de `node_modules/three/examples/jsm/controls/OrbitControls.js`) — o usuário orbita até a
 * vista de cima ou de baixo, não só ao redor do eixo Y — então um piso que protegesse só
 * `raioNoPlanoDeGiro` deixaria a câmera atravessar uma peça alta ao orbitar por cima: uma barra de
 * 6 m em pé, por exemplo, tem `raioNoPlanoDeGiro` pequeno e `meiaAlturaEmY` grande, e um piso de
 * `raio × margem` ficaria bem MENOR que a própria peça. Como a geometria é centralizada
 * (`center()` sobre a caixa), todo ponto dela fica a no máximo `√(raio² + meiaAltura²)` do centro,
 * em QUALQUER direção de órbita — a esfera que envolve o volume, não o cilindro que ele varre só ao
 * redor do eixo Y. Ver os três testes cujo nome começa por "mantém distanciaMinima estritamente
 * maior que a esfera do volume", um para peça deitada, um para peça em pé e um para peça quase
 * cúbica — para QUALQUER proporção de quadro.
 *
 * `1,1 < MARGEM_DE_ENQUADRAMENTO` (1,15) continua de propósito, e o motivo agora exige a altura
 * também: `distanciaPelaAltura = meiaAltura / tan(meioAnguloVertical) + raio` NÃO depende da
 * proporção do quadro (só `distanciaPelaLargura` depende), então `distancia >= distanciaPelaAltura ×
 * MARGEM_DE_ENQUADRAMENTO` sempre, qualquer que seja o quadro. Com `ABERTURA_VERTICAL_EM_GRAUS` = 50
 * (meio ângulo vertical 25°, `tan(25°) ≈ 0,466308`), isso dá `distancia >= 1,15 × (raio + 2,144507 ×
 * meiaAltura)`. Escrevendo `t = meiaAltura / raio`, falta `1,1 × √(1+t²) < 1,15 + 2,466183 × t` para
 * todo `t >= 0` (dividindo os dois lados por `raio`): em `t = 0` a diferença é `0,05`, e a derivada
 * do lado direito menos o esquerdo é `2,466183 − 1,1 × t/√(1+t²)`, sempre `> 2,466183 − 1,1 =
 * 1,366183` (porque `t/√(1+t²) < 1` para todo `t` finito) — estritamente positiva, então a diferença
 * só cresce a partir de `0,05`. Vale para qualquer proporção entre raio e meia altura, sem depender
 * da proporção do quadro; conferido também por varredura numérica de `t` até `2.000` (menor
 * diferença encontrada: os mesmos `0,05` de `t = 0`).
 */
const MARGEM_DA_APROXIMACAO_MINIMA = 1.1

/**
 * Distância de referência que enquadra o sólido tanto na largura quanto na altura do quadro,
 * usando a MAIOR das duas distâncias — a menor cortaria a peça no eixo que ela satisfaz por pouco.
 *
 * A meia abertura horizontal não é igual à vertical quando o quadro não é quadrado:
 * `tan(meia abertura horizontal) = proporção × tan(meia abertura vertical)` — um quadro estreito
 * (proporção < 1) enxerga um ângulo horizontal MENOR que o vertical, e um quadro largo
 * (proporção > 1) enxerga um ângulo horizontal MAIOR.
 *
 * `distância pela largura` inscreve `raioNoPlanoDeGiro` no cone tangente à meia abertura
 * horizontal — trigonometria do triângulo retângulo formado pelo raio, o eixo da câmera e a meia
 * abertura horizontal, em vez de sempre a vertical.
 *
 * `distância pela altura` posiciona a câmera de modo que a borda superior do sólido
 * (`meiaAlturaEmY` acima do centro de giro) caiba na meia abertura vertical; a parcela
 * `+ raioNoPlanoDeGiro` existe porque a borda mais próxima da câmera não fica no centro de giro, e
 * sim a `raioNoPlanoDeGiro` de distância dele, na direção da câmera.
 */
export function enquadramentoDoSolido(
  raioNoPlanoDeGiro: number,
  meiaAlturaEmY: number,
  aberturaVerticalEmGraus: number,
  proporcaoDoQuadro: number,
  margem: number,
): EnquadramentoDoSolido {
  const raio = Math.max(raioNoPlanoDeGiro, DIMENSAO_MINIMA_PARA_ENQUADRAR)
  const meiaAltura = Math.max(meiaAlturaEmY, DIMENSAO_MINIMA_PARA_ENQUADRAR)
  const meioAnguloVerticalEmRadianos = (aberturaVerticalEmGraus * Math.PI) / 360
  const meioAnguloHorizontalEmRadianos = Math.atan(proporcaoDoQuadro * Math.tan(meioAnguloVerticalEmRadianos))

  const distanciaPelaLargura = raio / Math.sin(meioAnguloHorizontalEmRadianos)
  const distanciaPelaAltura = meiaAltura / Math.tan(meioAnguloVerticalEmRadianos) + raio

  const distancia = Math.max(distanciaPelaLargura, distanciaPelaAltura) * margem

  return {
    distancia,
    near: distancia * FATOR_DE_FOLGA_PROXIMA,
    far: distancia * FATOR_DE_FOLGA_DISTANTE,
    distanciaMinima: Math.sqrt(raio * raio + meiaAltura * meiaAltura) * MARGEM_DA_APROXIMACAO_MINIMA,
  }
}
