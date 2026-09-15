/**
 * Distância da câmera e planos `near`/`far` proporcionais ao tamanho do sólido, em vez dos
 * literais fixos (`0.1`/`10000` de near/far, `200` de distância) que cortavam peça grande e
 * deixavam peça pequena minúscula no quadro — um STL não carrega unidade, e uma peça exportada em
 * milímetros pode variar de poucos milímetros a vários metros de raio.
 */
export interface EnquadramentoDoSolido {
  distancia: number
  near: number
  far: number
}

/**
 * Abertura vertical da câmera, em graus — mesmo valor que a `PerspectiveCamera` já usava antes
 * deste enquadramento existir.
 */
export const ABERTURA_VERTICAL_EM_GRAUS = 50

/**
 * Folga sobre a distância mínima em que a esfera envolvente cabe inteira no campo de visão
 * vertical: 1 preencheria o quadro encostando a esfera nas bordas superior e inferior; a folga
 * abre um respiro ao redor do sólido.
 */
export const MARGEM_DE_ENQUADRAMENTO = 1.15

/**
 * Piso do raio de entrada: um STL degenerado (vazio, ou um único ponto) produz esfera envolvente
 * de raio 0, e `raio / seno` daria distância 0 — a câmera nasceria dentro do próprio sólido. Este
 * piso é pequeno o bastante para não distorcer o enquadramento de um sólido real.
 */
const RAIO_MINIMO_PARA_ENQUADRAR = 1

/** `near = distancia × este fator` — pequeno o bastante para não cortar o sólido ao aproximar. */
const FATOR_DE_FOLGA_PROXIMA = 1 / 100

/** `far = distancia × este fator` — grande o bastante para não cortar o sólido ao afastar. */
const FATOR_DE_FOLGA_DISTANTE = 50

/**
 * Distância de referência: câmera posicionada a `raio / sin(abertura / 2)` unidades do centro é a
 * distância mínima em que a esfera envolvente cabe inteira no campo de visão vertical —
 * trigonometria do triângulo retângulo formado pelo raio da esfera, o eixo da câmera e a metade
 * do ângulo de abertura. `margem` multiplica essa distância mínima para abrir folga ao redor.
 */
export function enquadramentoDoSolido(
  raioDaEsferaEnvolvente: number,
  aberturaVerticalEmGraus: number,
  margem: number,
): EnquadramentoDoSolido {
  const raio = Math.max(raioDaEsferaEnvolvente, RAIO_MINIMO_PARA_ENQUADRAR)
  const meioAnguloEmRadianos = (aberturaVerticalEmGraus * Math.PI) / 360
  const distancia = (raio / Math.sin(meioAnguloEmRadianos)) * margem

  return {
    distancia,
    near: distancia * FATOR_DE_FOLGA_PROXIMA,
    far: distancia * FATOR_DE_FOLGA_DISTANTE,
  }
}
