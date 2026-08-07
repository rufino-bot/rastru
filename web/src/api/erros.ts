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

  constructor(status: number, mensagem: string) {
    super(mensagem)
    this.status = status
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
