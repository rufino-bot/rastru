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
 * `distanciaMinima = raio (já com o piso de degenerescência) × esta margem`. Diferente de `distancia`
 * — que soma a parcela pela largura OU pela altura, dependendo de qual domina, e por isso varia com
 * a proporção do quadro — `distanciaMinima` multiplica só o raio no plano de giro, nunca a distância
 * final: é isso que garante o invariante (câmera nunca entra no cilindro que a peça varre ao girar) —
 * ver os dois testes cujo nome começa por "mantém distanciaMinima estritamente maior que
 * raioNoPlanoDeGiro", um para peça dominada pela largura e outro pela altura — para QUALQUER
 * proporção de quadro, em vez de só para quadro quadrado.
 *
 * `1,1 < MARGEM_DE_ENQUADRAMENTO` (1,15) de propósito: `distanciaPelaLargura = raio /
 * sin(meioAnguloHorizontal)` é sempre `>= raio` (porque `sin <= 1`), então `distancia >= raio ×
 * MARGEM_DE_ENQUADRAMENTO` sempre — inclusive no limite de um quadro infinitamente largo, onde
 * `sin(meioAnguloHorizontal) → 1` e a desigualdade vira igualdade. Manter `distanciaMinima` abaixo
 * dessa margem garante `distanciaMinima < distancia` (zoom mínimo sempre mais perto que a vista
 * inicial) mesmo nesse limite, sem depender da proporção real do quadro.
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
    distanciaMinima: raio * MARGEM_DA_APROXIMACAO_MINIMA,
  }
}
