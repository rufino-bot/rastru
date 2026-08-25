/**
 * Erro de API que CARREGA o status. Antes desta classe o status vivia só dentro da string da
 * mensagem (`Falha ao listar setores (403).`), então a tela não tinha como distinguir "seu perfil
 * não pode" de "o servidor caiu" sem fazer parse de texto.
 *
 * Continua sendo `Error`: as funções de `cadastros.ts` não mudam de contrato, e todo
 * `rejects.toThrow()` que já existia segue valendo.
 */
export class ErroDeApi extends Error {
  readonly status: number

  /**
   * Mensagem que veio do SERVIDOR (o campo `erro` do corpo), quando quem chamou se deu ao trabalho
   * de lê-la. Opcional de propósito: quem não popula segue exatamente como antes.
   *
   * Existe porque a receita padrão tem um 400 cuja explicação só o servidor sabe dar — ele nomeia
   * o ciclo (`"Esta receita criaria um ciclo: MT-1010 -> MT-1000 -> MT-1010."`), e a spec §1.3 da
   * Fase 1C EXIGE que essa mensagem chegue ao usuário: a regra de ciclo é estrita, então alguém
   * pode ser barrado por um ciclo que não criou, e saber ONDE ele está é o que torna a regra
   * praticável. Sem isto, o backend cumpria e o front jogava a mensagem fora.
   *
   * **Só o cliente da receita popula.** É por isso que este campo é opcional em vez de o
   * `mensagemDeErro` passar a expor todo 400: os outros endpoints não escreveram os textos de
   * validação deles pensando em quem lê, e mostrá-los seria decidir por eles.
   */
  readonly detalhe?: string

  constructor(status: number, mensagem: string, detalhe?: string) {
    super(mensagem)
    this.status = status
    this.detalhe = detalhe
    this.name = 'ErroDeApi'
  }
}

/**
 * Traduz o que caiu no `catch` para uma frase que diga ao usuário o que fazer a seguir.
 *
 * `fallback` é o texto específico da tela ("Não foi possível carregar os setores.") e continua
 * sendo o destino de tudo que esta função não sabe explicar melhor — status de validação (400),
 * erro de programação, valor que nem erro é. A função nunca INVENTA explicação: ou reconhece o
 * caso, ou devolve o que a tela já diria.
 *
 * O 401 aqui é informativo, não corretivo: quem devolve o usuário ao login é o `onSessionLost` do
 * `client.ts`, depois de o refresh falhar. Esta mensagem cobre a janela em que a tela ainda está
 * montada.
 */
export function mensagemDeErro(e: unknown, fallback: string): string {
  if (e instanceof ErroDeApi) {
    // Mensagem do servidor ganha do texto da tela, e SÓ dela: `detalhe` não é inventado aqui nem
    // preenchido por acidente — quem o popula leu o corpo de propósito. Isto vem ANTES dos ramos
    // por status porque a explicação específica é melhor que a genérica sempre que existe.
    //
    // EXCETO no 401: hoje é inócuo (o backend só emite `{erro}` em 400/404/409, e o 401 vem do
    // middleware com corpo vazio, então `detalhe` nunca populado aqui), mas se algum dia um
    // endpoint responder 401 com corpo, esta guarda impede que ele apague "Sua sessão expirou.
    // Entre novamente." — a única mensagem acionável do conjunto (achado da review Tasks 10-12).
    if (e.status !== 401 && e.detalhe) return e.detalhe
    if (e.status === 401) return 'Sua sessão expirou. Entre novamente.'
    if (e.status === 403) return 'Seu perfil não tem permissão para esta ação.'
    if (e.status === 404) return 'Este registro não existe mais.'
    if (e.status >= 500) return 'O servidor não respondeu como esperado. Tente de novo em instantes.'
    return fallback
  }
  // `fetch` rejeita com TypeError quando a requisição nem sai (DNS, rede, CORS). É o único erro
  // de rede que chega aqui — o resto do caminho já virou Response.
  if (e instanceof TypeError) return 'Sem conexão com o servidor. Verifique a rede e tente de novo.'
  return fallback
}
